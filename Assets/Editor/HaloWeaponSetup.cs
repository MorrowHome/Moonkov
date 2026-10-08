using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HaloWeaponSetup
{
    private const string Art = "Assets/DollSinger/Art/Shotgun/";
    private const string ShotgunPrefab = "Assets/DollSinger/Prefabs/ShotgunHalo.prefab";
    [MenuItem("Tools/Doll Singer/Import Shotgun And Weapon Icons")]
    public static void Run()
    {
        AssetDatabase.Refresh();
        var frames = Enumerable.Range(0, 5).Select(i => ImportSprite(Art + "ShotShell" + i + ".png")).ToArray();
        var dot = ImportSprite(Art + "ShotDot.png");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "ShotgunGlow.mat");
        if (!material)
        {
            material = new Material(Shader.Find("DollSinger/HaloLine")) { name = "ShotgunGlow" };
            material.SetFloat("_Gain", 1.5f); AssetDatabase.CreateAsset(material, Art + "ShotgunGlow.mat");
        }
        var root = new GameObject("Shotgun Halo");
        try
        {
            var visual = root.AddComponent<ShotgunHaloVisual>();
            visual.shellFrames = frames;
            visual.shell = Sprite("Shell", root.transform, frames[0], material);
            visual.dots = Enumerable.Range(0, 4).Select(i => Sprite("Stroke " + i, root.transform, dot, material)).ToArray();
            visual.ApplyLayout();
            PrefabUtility.SaveAsPrefabAsset(root, ShotgunPrefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var shotgun = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/ShotgunWeapon.asset");
        if (!shotgun)
        {
            shotgun = UnityEngine.Object.Instantiate(registry.GetWeaponData(DollSingerWeapons.Halo));
            shotgun.name = "ShotgunWeapon"; shotgun.WeaponName = "Halo Shotgun";
            shotgun.MagazineSize = 4; shotgun.CooldownInMs = .8f; shotgun.ReloadTime = 2f;
            shotgun.EnergyPerRound = 12;
            shotgun.Automatic = false; shotgun.AutoReloadWhenEmpty = true;
            shotgun.Damage = 10; shotgun.PelletCount = 8; shotgun.SpreadDegrees = 5f;
            shotgun.ProjectileSpeed = 240f; shotgun.ProjectileLifetime = 1f;
            var template = registry.GetWeaponData(1);
            shotgun.WeaponFireSfx = template.WeaponFireSfx; shotgun.WeaponReloadSfx = template.WeaponReloadSfx;
            AssetDatabase.CreateAsset(shotgun, "Assets/DollSinger/ShotgunWeapon.asset");
        }
        while (registry.Weapons.Count <= DollSingerWeapons.Shotgun) registry.Weapons.Add(null);
        registry.Weapons[(int)DollSingerWeapons.Shotgun] = shotgun; EditorUtility.SetDirty(registry);
        foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab", "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab" })
        {
            var player = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var halo = player.GetComponentInChildren<DollSingerHaloAim>(true);
                if (!halo.shotgunVisual)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunPrefab), halo.haloVisual);
                    halo.shotgunVisual = child.GetComponent<ShotgunHaloVisual>(); child.SetActive(false);
                }
                PrefabUtility.SaveAsPrefabAsset(player, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }
        AssetDatabase.SaveAssets();
        GenerateIcons();
        Validate();
        Debug.Log("HALO_WEAPON_SETUP_OK: shotgun import, registry, two prefabs, original-shape icons and equipment checks passed.");
    }
    private static Sprite ImportSprite(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 1000f; importer.spritePivot = new Vector2(.5f, .5f);
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center; settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    private static SpriteRenderer Sprite(string name, Transform parent, Sprite sprite, Material material)
    {
        var child = new GameObject(name); child.transform.SetParent(parent, false);
        var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return renderer;
    }
    public static void GenerateRifleIcon() => GenerateIcons(DollSingerHaloAim.HaloWeapon.Rifle);
    private static void GenerateIcons(DollSingerHaloAim.HaloWeapon? only = null)
    {
        const string folder = "Assets/Resources/HaloIcons"; Directory.CreateDirectory(folder);
        var player = PrefabUtility.LoadPrefabContents("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab");
        try
        {
            var halo = player.GetComponentInChildren<DollSingerHaloAim>(true);
            foreach (var weapon in new[] { DollSingerHaloAim.HaloWeapon.Rifle, DollSingerHaloAim.HaloWeapon.Revolver, DollSingerHaloAim.HaloWeapon.Shotgun })
            {
                if (only.HasValue && weapon != only.Value) continue;
                var scene = EditorSceneManager.NewPreviewScene();
                Texture2D pixels = null;
                try
                {
                    var copy = UnityEngine.Object.Instantiate(halo.haloVisual.gameObject);
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    copy.SetActive(true); copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); copy.transform.localScale = Vector3.one;
                    var rifle = copy.GetComponentInChildren<RifleHaloVisual>(true);
                    foreach (var line in copy.GetComponentsInChildren<LineRenderer>(true)) line.enabled = weapon == DollSingerHaloAim.HaloWeapon.Rifle && (!rifle || line.GetComponentInParent<RifleHaloVisual>(true));
                    if (rifle) { rifle.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Rifle, 30); rifle.ApplyLayout(); }
                    foreach (var light in copy.GetComponentsInChildren<Light>(true)) light.enabled = false;
                    var revolver = copy.GetComponentInChildren<RevolverHaloVisual>(true);
                    revolver.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Revolver, 6);
                    if (weapon == DollSingerHaloAim.HaloWeapon.Revolver)
                        for (int i = 0; i < revolver.chambers.Length; i++)
                        {
                            float rotation = (i + 2) * 60f;
                            float angle = (rotation + 60f) * Mathf.Deg2Rad;
                            revolver.chambers[i].transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * revolver.restingRadius;
                            revolver.chambers[i].transform.localRotation = Quaternion.Euler(0, 0, rotation);
                        }
                    var shotgun = copy.GetComponentInChildren<ShotgunHaloVisual>(true);
                    shotgun.SetEquipped(weapon == DollSingerHaloAim.HaloWeapon.Shotgun, 4); shotgun.ApplyLayout();
                    var renderers = copy.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                    var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    pixels = Rasterize(renderers, bounds);
                    if (!pixels.GetPixels32().Any(c => c.a > 10)) throw new Exception("Empty icon: " + weapon + " renderers=" + renderers.Length + " bounds=" + bounds);
                    File.WriteAllBytes(folder + "/" + weapon + ".png", pixels.EncodeToPNG());
                }
                finally
                {
                    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.Refresh();
        foreach (string name in new[] { "Rifle", "Revolver", "Shotgun" })
        {
            if (only.HasValue && name != only.Value.ToString()) continue;
            var importer = (TextureImporter)AssetImporter.GetAtPath(folder + "/" + name + ".png");
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
    }
    // Copy the actual sprite pixels and line vertices. This is independent of the
    // Editor's render loop, so the original-shape icons can also be rebuilt in batch mode.
    private static Texture2D Rasterize(Renderer[] renderers, Bounds bounds)
    {
        const int size = 256;
        float extent = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.12f;
        float unit = extent * 2f / size;
        var colours = new Color[size * size];
        var textures = new System.Collections.Generic.Dictionary<string, Texture2D>();
        Vector2 Pixel(Vector3 point) => new Vector2((point.x - bounds.center.x + extent) / unit, (point.y - bounds.center.y + extent) / unit);
        void Blend(int x, int y, Color source)
        {
            if (x < 0 || y < 0 || x >= size || y >= size || source.a <= 0) return;
            int index = y * size + x; var destination = colours[index];
            float alpha = source.a + destination.a * (1f - source.a);
            var colour = (source * source.a + destination * destination.a * (1f - source.a)) / alpha;
            colour.a = alpha; colours[index] = colour;
        }
        try
        {
            foreach (var renderer in renderers)
            {
                if (renderer is LineRenderer line)
                {
                    int segments = line.loop ? line.positionCount : line.positionCount - 1;
                    for (int segment = 0; segment < segments; segment++)
                    {
                        Vector3 World(int index) => line.useWorldSpace ? line.GetPosition(index) : line.transform.TransformPoint(line.GetPosition(index));
                        Vector2 a = Pixel(World(segment)), b = Pixel(World((segment + 1) % line.positionCount));
                        float scale = Mathf.Max(line.transform.lossyScale.x, line.transform.lossyScale.y);
                        float width = line.widthMultiplier * line.widthCurve.Evaluate((segment + .5f) / Mathf.Max(1, segments)) * scale / unit;
                        float radius = Mathf.Max(.4f, width * .5f);
                        Vector2 delta = b - a;
                        var colour = Color.Lerp(line.startColor, line.endColor, (segment + .5f) / Mathf.Max(1, segments));
                        if (line.sharedMaterial && line.sharedMaterial.HasProperty("_Color")) colour *= line.sharedMaterial.GetColor("_Color");
                        for (int y = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1)); y < Mathf.Min(size, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1)); y++)
                            for (int x = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1)); x < Mathf.Min(size, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1)); x++)
                            {
                                Vector2 point = new Vector2(x + .5f, y + .5f);
                                float t = delta.sqrMagnitude > .00001f ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
                                var sample = colour; sample.a *= Mathf.Clamp01(radius + .5f - Vector2.Distance(point, a + delta * t)); Blend(x, y, sample);
                            }
                    }
                }
                else if (renderer is SpriteRenderer sprite && sprite.sprite)
                {
                    string path = AssetDatabase.GetAssetPath(sprite.sprite.texture);
                    if (!textures.TryGetValue(path, out var texture))
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); texture.LoadImage(File.ReadAllBytes(path)); textures.Add(path, texture);
                    }
                    var rect = sprite.sprite.rect; var pivot = sprite.sprite.pivot; float ppu = sprite.sprite.pixelsPerUnit;
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    {
                        var local = sprite.transform.InverseTransformPoint(new Vector3(bounds.center.x - extent + (x + .5f) * unit, bounds.center.y - extent + (y + .5f) * unit, sprite.transform.position.z));
                        float sx = local.x * ppu + pivot.x, sy = local.y * ppu + pivot.y;
                        if (sx < 0 || sy < 0 || sx >= rect.width || sy >= rect.height) continue;
                        var sample = texture.GetPixelBilinear((rect.x + sx) / texture.width, (rect.y + sy) / texture.height) * sprite.color;
                        Blend(x, y, sample);
                    }
                }
            }
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false); result.SetPixels(colours); result.Apply(); return result;
        }
        finally { foreach (var texture in textures.Values) UnityEngine.Object.DestroyImmediate(texture); }
    }
    public static void Validate()
    {
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var shotgun = registry.GetWeaponData(DollSingerWeapons.Shotgun);
        Require(shotgun != null && shotgun.MagazineSize == 4 && shotgun.PelletCount == 8 && shotgun.ProjectileGhostPrefab != null, "Shotgun registry/config");
        foreach (var pair in new[] { (Code:"rifle", Cells:4), (Code:"pistol", Cells:1), (Code:"shotgun", Cells:4) })
        { var definition = InventoryCatalog.Get(pair.Code); Require(definition.Width * definition.Height == pair.Cells, "Footprint " + pair.Code); }
        var graph = InventoryGraph.Create(false);
        var state = new PredictedPlayerGhost { CurrentHealth = 100, EquippedWeaponID = DollSingerWeapons.None };
        DollSingerWeapons.SyncEquipment(ref state, graph, registry);
        Require(state.EquippedWeaponID == DollSingerWeapons.Halo, "Primary equip");
        state.CurrentAmmo = 7; DollSingerWeapons.SaveActiveAmmo(ref state, graph);
        var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.EquipRevolver, true);
        Require(DollSingerWeapons.TryEquip(ref state, input, registry), "Secondary switch");
        DollSingerWeapons.SyncEquipment(ref state, graph, registry);
        Require(state.EquippedWeaponID == DollSingerWeapons.Shotgun && state.CurrentAmmo == 4 && graph.Equipped("Primary").LoadedAmmo == 7, "Per-item ammo");
        state.CurrentAmmo = 2; DollSingerWeapons.SaveActiveAmmo(ref state, graph);
        var pack = graph.Equipped("Backpack"); var item = graph.Equipped("Secondary");
        Require(graph.TryApply(new InventoryCommand { ExpectedVersion = graph.Version, ItemId = item.Id, Parent = pack.Id, Region = "main", X = 0, Y = 0 }) == InventoryError.None, "Weapon storage");
        DollSingerWeapons.SyncEquipment(ref state, graph, registry);
        Require(!DollSingerWeapons.TryEquip(ref state, input, registry) && graph.Find(item.Id).LoadedAmmo == 2, "Backpack possession/saved ammo");
        graph.Items.RemoveAll(i => i.Parent == "equipment" && InventoryCatalog.Get(i.Code).Kind != ItemKind.Rig && InventoryCatalog.Get(i.Code).Kind != ItemKind.Backpack);
        DollSingerWeapons.SyncEquipment(ref state, graph, registry);
        Require(state.EquippedWeaponID == DollSingerWeapons.None && state.CurrentAmmo == 0, "Unarmed state");
        var legacy = InventoryGraph.Create(starter: false); legacy.AddStarterWeapons(); int count = legacy.Items.Count; legacy.AddStarterWeapons();
        Require(legacy.Items.Count == count && legacy.Clone().WeaponKitVersion == 1 && legacy.Validate() == InventoryError.None, "Starter migration is one-time and stored");
        for (int pellet = 0; pellet < 8; pellet++)
        {
            Vector3 direction = WeaponSpread.Direction(Vector3.forward, shotgun, pellet, 123);
            Require(Vector3.Angle(Vector3.forward, direction) <= shotgun.SpreadDegrees + .01f &&
                direction == WeaponSpread.Direction(Vector3.forward, shotgun, pellet, 123), "Deterministic spread");
        }
        var visualObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunPrefab));
        try
        {
            var visual = visualObject.GetComponent<ShotgunHaloVisual>(); visual.ApplyLayout();
            Require(visual.dots[0].transform.localPosition.x > 0 && visual.dots[0].transform.localPosition.y < 0, "Source lengthdir conversion");
            visual.SetNetworkState(0, true, .5f, 10, 20); var half = visual.shell.sprite;
            visual.SetNetworkState(0, true, .1f, 10, 20);
            Require(visual.shell.sprite == half, "Reload progress cannot rewind");
            visual.SetNetworkState(4, false, 1, 10, 20); visual.SetNetworkState(0, true, .4f, 10, 20);
            Require(visual.shell.sprite == visual.shellFrames[0], "Completed reload does not replay empty shell");
        }
        finally { UnityEngine.Object.DestroyImmediate(visualObject); }
    }
}
