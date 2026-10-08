using System;
using System.IO;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SniperHaloSetup
{
    private const string Prefab = "Assets/DollSinger/Prefabs/SniperHalo.prefab";
    [MenuItem("Tools/Doll Singer/Install Sniper Optic Halo")]
    public static void Install()
    {
        var root = new GameObject("Sniper Optic Halo");
        try
        {
            var visual = root.AddComponent<SniperHaloVisual>();
            visual.lineMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/DollSinger/Art/Materials/RifleHaloGlow.mat");
            if (!visual.lineMaterial) throw new Exception("Install rifle halo material first");
            visual.BuildGeometry(); PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        const string weaponPath = "Assets/DollSinger/SniperWeapon.asset";
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(weaponPath);
        if (!weapon)
        {
            weapon = UnityEngine.Object.Instantiate(registry.GetWeaponData(DollSingerWeapons.Halo));
            weapon.name = "SniperWeapon"; weapon.WeaponName = "Halo Sniper";
            weapon.MagazineSize = SniperHaloVisual.Capacity; weapon.Damage = 110;
            weapon.CooldownInMs = 1.4f; weapon.Automatic = false; weapon.AutoReloadWhenEmpty = true;
            weapon.ReloadTime = 3; weapon.EnergyPerRound = 18; weapon.PelletCount = 1; weapon.SpreadDegrees = 0;
            weapon.ProjectileSpeed = 800; weapon.ProjectileLifetime = 4; weapon.ProjectileGravity = 1.62f;
            AssetDatabase.CreateAsset(weapon, weaponPath);
        }
        while (registry.Weapons.Count <= DollSingerWeapons.Sniper) registry.Weapons.Add(null);
        registry.Weapons[(int)DollSingerWeapons.Sniper] = weapon; EditorUtility.SetDirty(registry);
        foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab", "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab" })
        {
            var player = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var halo = player.GetComponentInChildren<DollSingerHaloAim>(true);
                if (!halo.sniperVisual)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), halo.haloVisual);
                    halo.sniperVisual = child.GetComponent<SniperHaloVisual>();
                }
                halo.sniperVisual.SetEquipped(false, 5); EditorUtility.SetDirty(halo);
                PrefabUtility.SaveAsPrefabAsset(player, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }
        AssetDatabase.SaveAssets(); HaloWeaponSetup.GenerateSniperIcon();
    }
    public static void Validate()
    {
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var weapon = registry.GetWeaponData(DollSingerWeapons.Sniper);
        Require(weapon && weapon.MagazineSize == 5 && weapon.EnergyPerRound == 18 && !weapon.Automatic && weapon.ProjectileGhostPrefab != null, "Sniper registry and projectile");
        Require(DollSingerWeapons.WeaponId("sniper") == DollSingerWeapons.Sniper && DollSingerWeapons.IsHalo(5), "Sniper mapping");
        var definition = InventoryCatalog.Get("sniper");
        Require(definition != null && definition.Width * definition.Height == 4 && definition.Kind == ItemKind.LongGun, "Sniper footprint/slots");
        foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab", "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab" })
            Require(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<DollSingerHaloAim>(true).sniperVisual, "Sniper binding: " + path);
        var graph = InventoryGraph.Create(starter: false); graph.AddSupply("dust", 90, "stash");
        Require(ShopRules.Apply(graph, new ShopCommand { RequestId = Guid.NewGuid().ToString(), Operation = ShopOperation.Buy,
            Code = "sniper", Quantity = 1, ExpectedVersion = graph.Version }, out var bought) == null, "Sniper purchase");
        var item = bought.Items.Find(i => i.Code == "sniper");
        Require(!ShopCatalog.CanClaim(bought), "Stored sniper prevents emergency kit");
        Require(bought.TryApply(new InventoryCommand { ExpectedVersion = bought.Version, ItemId = item.Id,
            Parent = "equipment", Region = "Primary" }) == InventoryError.None, "Equip sniper");
        var state = new PredictedPlayerGhost { CurrentHealth = 100, EquippedWeaponID = DollSingerWeapons.None };
        DollSingerWeapons.SyncEquipment(ref state, bought, registry);
        Require(state.EquippedWeaponID == 5 && state.CurrentAmmo == 5, "Initial ammo");
        state.CurrentAmmo = 0; DollSingerWeapons.SaveActiveAmmo(ref state, bought);
        bought.AddSupply("cells", 1, "pockets", cellCharge: 40);
        Require(bought.TryRecharge(0, 5, weapon.EnergyPerRound, out int target) && target == 2 && bought.CellEnergy() == 4, "Partial sniper charge uses 36 energy");
        state.ReloadTargetAmmo = target; DollSingerWeapons.CompleteReload(ref state, weapon); DollSingerWeapons.SaveActiveAmmo(ref state, bought);
        Require(bought.TryApply(new InventoryCommand { ExpectedVersion = bought.Version, ItemId = item.Id,
            Parent = "stash", Region = "main" }) == InventoryError.None && bought.Clone().Find(item.Id).LoadedAmmo == 2, "Store exact ammo");
        DollSingerWeapons.SyncEquipment(ref state, bought, registry);
        Require(state.EquippedWeaponID == DollSingerWeapons.None && state.CurrentAmmo == 0, "Backpack/storage weapons cannot fire");
        Require(ShopRules.Apply(bought, new ShopCommand { RequestId = Guid.NewGuid().ToString(), Operation = ShopOperation.Sell,
            Code = "sniper", ItemId = item.Id, Quantity = 1, ExpectedVersion = bought.Version }, out var sold) == null && sold.Count("dust") == 45, "Sniper sale");
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        try
        {
            var visual = root.GetComponent<SniperHaloVisual>(); visual.SetEquipped(false, 5); visual.SetEquipped(true, 5);
            visual.SetNetworkState(5, false, 0, 100, 0, 5);
            visual.SetNetworkState(4, false, 0, 101, 0, 5); visual.SetAimBlend(1); visual.Step(.3f);
            Vector3 sight = visual.sight.GetPosition(0);
            visual.SetNetworkState(4, false, 0, 101, 0, 5); visual.Step(0);
            Require(visual.VisibleAmmo == 4 && visual.sight.GetPosition(0) == sight && sight.magnitude < .003f, "Replay/tiny stable sight");
            visual.SetNetworkState(0, true, .5f, 101, 110, 2); float p = visual.ReloadProgress;
            visual.SetNetworkState(0, true, .2f, 101, 110, 2);
            Require(visual.ReloadProgress == p && visual.VisibleAmmo < 2, "Reload progress monotonic");
            visual.SetNetworkState(2, false, 1, 101, 110, 2); visual.SetNetworkState(0, true, .2f, 101, 110, 2);
            Require(visual.VisibleAmmo == 2 && !visual.IsReloading, "Completed partial reload replay");
            visual.SetEquipped(false, 0); visual.SetEquipped(true, 0); visual.Step(2);
            Require(visual.VisibleAmmo == 0 && !visual.IsReloading, "Empty reset");
            var view = root.AddComponent<DollSingerView>();
            var serialized = new SerializedObject(view); serialized.FindProperty("firstPerson").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
            view.SetAimBlend(1); view.SetWeaponAimMagnification(visual.scopeMagnification);
            Require(Mathf.Approximately(view.CurrentAimMagnification, 6), "Six times first person scope");
            view.SetWeaponAimMagnification(0);
            Require(Mathf.Approximately(view.CurrentAimMagnification, view.aimMagnification), "Other weapon zoom restores");
            // Projection must keep the optic within the viewport even when base FOV changes.
            foreach (float baseFov in new[] { 40f, 70f, 100f })
            {
                float scopeFov = 2 * Mathf.Atan(Mathf.Tan(baseFov * .5f * Mathf.Deg2Rad) / visual.scopeMagnification) * Mathf.Rad2Deg;
                float scale = visual.ScopeScale(.45f, scopeFov);
                float viewportFraction = visual.AimedRadius * scale / (.45f * Mathf.Tan(scopeFov * .5f * Mathf.Deg2Rad));
                Require(Mathf.Abs(viewportFraction - visual.scopeViewportFraction) < .001f, "Scope fits configured FOV");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Require(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/HaloIcons/Sniper.png"), "Original-shape icon");
        Debug.Log("Sniper checks passed: registration, two prefabs, purchase/equip/store/sell, partial energy, replay and scope zoom/reset.");
    }
    public static void RenderPreview()
    {
        string folder = Path.GetFullPath("LocalData/SniperHaloPreview"); Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)); SceneManager.MoveGameObjectToScene(root, scene);
        var cameraObject = new GameObject("Sniper preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = .42f;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position = new Vector3(0, 0, -2); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.013f, .018f, .03f); camera.nearClipPlane = .1f; camera.farClipPlane = 5;
        var target = new RenderTexture(640, 640, 24); camera.targetTexture = target;
        var pixels = new Texture2D(640, 640, TextureFormat.RGB24, false);
        try
        {
            var visual = root.GetComponent<SniperHaloVisual>(); visual.SetEquipped(false, 5); visual.SetEquipped(true, 5);
            for (int frame = 0; frame < 180; frame++)
            {
                visual.SetAimBlend(Mathf.Clamp01(frame / 29f));
                if (frame == 35) visual.PlayShot();
                if (frame < 60) visual.SetLocalState(frame < 35 ? 5 : 4, false, 0);
                else if (frame < 150) visual.SetLocalState(4, true, (frame - 60) / 89f);
                else visual.SetLocalState(5, false, 1);
                visual.Step(1f / 30); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, $"frame-{frame:D3}.png"), pixels.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log("Sniper native preview rendered: " + folder);
    }
}
