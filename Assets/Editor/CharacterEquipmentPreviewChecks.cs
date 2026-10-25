using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Isolated item trees and private preview scenes. Never reads or writes a player profile.
public static class CharacterEquipmentPreviewChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int[] Characters = { GameSettings.DollSingerCharacterIndex,
        GameSettings.HatsuneMikuCharacterIndex, GameSettings.ShianCharacterIndex };
    private static readonly string[] Weapons = { "rifle", "compact", "pistol", "shotgun", "sniper", "cicada",
        "trine", "aegis", "vesper", "thorn", "sundown", "rondo", "tempest", "nocturne" };
    private static int s_Checks;

    private static void Require(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException("Character equipment preview: " + message);
    }

    private static T Field<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, Private);
        if (field == null) throw new InvalidOperationException("Missing check field " + name);
        return (T)field.GetValue(target);
    }

    private static InventoryItem Equip(InventoryGraph graph, string code, string slot, int ammo = -1)
    {
        var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code,
            Parent = InventoryCatalog.Equipment, Region = slot, LoadedAmmo = ammo };
        graph.Items.Add(item);
        return item;
    }

    private static void Module(InventoryGraph graph, InventoryItem weapon, string code)
    {
        graph.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code,
            Parent = weapon.Id, Region = HaloModules.Slot(HaloModules.FromCode(code)) });
    }

    private static InventoryGraph Loadout()
    {
        var graph = InventoryGraph.Create(false, false);
        var rifle = Equip(graph, "rifle", "Primary", 11);
        var shotgun = Equip(graph, "shotgun", "Secondary", 2);
        Equip(graph, "pistol", "Pistol", 3);
        Equip(graph, "energy_sword", "Melee");
        Equip(graph, EnergyShieldEquipment.HeadDomeCode, "Helmet");
        Equip(graph, EnergyShieldEquipment.ShieldCode, "Shield");
        Equip(graph, "cargo_pack", "Backpack");
        Module(graph, rifle, "halo_capacitor");
        Module(graph, rifle, "halo_pulse_controller");
        Module(graph, rifle, "halo_hit_marker");
        Module(graph, shotgun, "halo_charge_bridge");
        Module(graph, shotgun, "halo_focus_lens");
        graph.HaloResearchLevel = 1; graph.HaloResearchBranches = 7;
        return graph;
    }

    [MenuItem("Tools/Moonkov/Check Character Equipment Previews")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run outside Play mode.");
        s_Checks = 0;
        int scenes = EditorSceneManager.previewSceneCount;
        foreach (int character in Characters)
        {
            CheckCharacter(character);
            CheckCorpse(character);
        }
        Require(EditorSceneManager.previewSceneCount == scenes, "private preview scenes leaked");
        Debug.Log("Character equipment preview checks passed: " + s_Checks +
            " assertions; three characters, every halo, item modules/ammo, live snapshots, protection gear, corpse transfers and cleanup.");
    }

    private static void CheckCharacter(int character)
    {
        var settings = Resources.Load<MenuCharacterSettings>(GameSettings.MenuCharacterSettingsPath(character));
        Require(settings && settings.HaloSource && settings.WeaponRegistry, "missing formal sources for character " + character);
        using var view = new MenuCharacterView(idleOnly: true, characterIndex: character);
        view.SetEquipmentInventory(InventoryGraph.Create(false, false)); view.Advance(0);
        Require(!view.HaloVisible, "empty loadout fabricated a halo");
        CheckBodyAssets(view, settings);
        CheckHaloAssets(view, settings.HaloSource);
        var stage = view.DisplayScene;

        foreach (string code in Weapons)
        {
            var graph = InventoryGraph.Create(false, false);
            var weapon = Equip(graph, code, code == "pistol" ? "Pistol" : "Primary", 2);
            string before = JsonUtility.ToJson(graph);
            view.SetEquipmentInventory(graph); view.Advance(0);
            uint id = DollSingerWeapons.WeaponId(code);
            Require(view.PreviewWeaponId == id && (uint)view.EquippedPreviewWeapon == id - 1, "wrong halo for " + code);
            Require(view.HaloVisible && view.PreviewWeaponItemId == weapon.Id, "wrong item instance for " + code);
            Require(view.PreviewLoadedAmmo == 2 && view.PreviewModules == 0, "wrong unmodified item state for " + code);
            Require(view.PreviewMagazineCapacity == settings.WeaponRegistry.GetWeaponData(id).MagazineSize, "wrong capacity for " + code);
            CheckSelectedHalo(view, code);
            Require(JsonUtility.ToJson(graph) == before, "preview changed source inventory for " + code);
        }

        var loadout = Loadout();
        string unchanged = JsonUtility.ToJson(loadout);
        var primary = loadout.Equipped("Primary"); var secondary = loadout.Equipped("Secondary");
        view.SetEquipmentInventory(loadout); view.Advance(0);
        Require(view.PreviewWeaponItemId == primary.Id, "deployment did not select primary");
        CheckModules(view, loadout, primary, settings.WeaponRegistry);
        view.SetEquipmentInventory(loadout, secondary.Id); view.Advance(0);
        Require(view.PreviewWeaponId == DollSingerWeapons.Shotgun && view.PreviewLoadedAmmo == 2, "secondary selection ignored");
        CheckModules(view, loadout, secondary, settings.WeaponRegistry);
        view.SetEquipmentInventory(loadout, loadout.Equipped("Melee").Id); view.Advance(0);
        Require(!view.HaloVisible && view.PreviewMeleeWeapon == MeleeWeaponKind.EnergySword, "equipped sword was not presented");
        Require(Field<DollSingerMeleePresentation>(view, "m_EquipmentMelee").PoseBlend > .999f,
            "static sword preview did not sample the complete holding pose");
        loadout.Equipped("Melee").Code = MeleeInventory.DaggerCode;
        view.SetEquipmentInventory(loadout, loadout.Equipped("Melee").Id); view.Advance(0);
        Require(!view.HaloVisible && view.PreviewMeleeWeapon == MeleeWeaponKind.Dagger, "equipped dagger was not presented");
        loadout.Equipped("Melee").Code = MeleeInventory.SwordCode;
        view.SetEquipmentInventory(loadout, ""); view.Advance(0);
        Require(!view.HaloVisible && view.PreviewMeleeWeapon == MeleeWeaponKind.Fists, "explicit empty hands selected primary");

        var fallback = loadout.Clone(); fallback.Items.RemoveAll(item => item.Id == primary.Id || item.Parent == primary.Id);
        view.SetEquipmentInventory(fallback); view.Advance(0);
        Require(view.PreviewWeaponItemId == secondary.Id, "missing primary did not fall back to secondary");
        fallback.Items.RemoveAll(item => item.Id == secondary.Id || item.Parent == secondary.Id);
        view.SetEquipmentInventory(fallback); view.Advance(0);
        Require(view.PreviewWeaponItemId == loadout.Equipped("Pistol").Id, "missing long guns did not fall back to pistol");
        fallback.Items.RemoveAll(item => item.Region == "Pistol");
        view.SetEquipmentInventory(fallback); view.Advance(0);
        Require(!view.HaloVisible && view.PreviewMeleeWeapon == MeleeWeaponKind.EnergySword, "missing firearms did not fall back to melee");

        var fresh = InventoryGraph.Create(false, false); var newRifle = Equip(fresh, "rifle", "Primary");
        Module(fresh, newRifle, "halo_capacitor");
        view.SetEquipmentInventory(fresh); view.Advance(0);
        Require(newRifle.LoadedAmmo == -1 && view.PreviewLoadedAmmo == settings.WeaponRegistry.GetWeaponData(DollSingerWeapons.Halo).MagazineSize,
            "new weapon preview changed stored rounds or invented module capacity rounds");

        view.SetEquipmentInventory(loadout); view.Advance(0);
        CheckProtection(view, false, 3);
        loadout.Equipped("Helmet").Code = EnergyShieldEquipment.StandardHeadDomeCode;
        view.SetEquipmentInventory(loadout); view.Advance(0);
        CheckProtection(view, true, 3);
        foreach (string code in new[] { "sling_pack", "backpack", "cargo_pack", "small_pack" })
        {
            loadout.Equipped("Backpack").Code = code;
            view.SetEquipmentInventory(loadout); view.Advance(0);
            CheckProtection(view, true, EnergyShieldEquipment.BackpackStyle(code));
        }
        loadout.Equipped("Helmet").Code = EnergyShieldEquipment.HeadDomeCode;
        loadout.Equipped("Backpack").Code = "cargo_pack";
        Require(JsonUtility.ToJson(loadout) == unchanged, "equipment presentation mutated resources or attachments");
        CheckSnapshot(view, loadout, settings.WeaponRegistry);
        Require(JsonUtility.ToJson(loadout) == unchanged, "live preview wrote ghost state into inventory");

        view.SetEquipmentInventory(InventoryGraph.Create(false, false)); view.Advance(0);
        var protection = Field<EnergyShieldPresentation>(view, "m_Protection");
        Require(!view.HaloVisible && !Field<bool>(protection, "m_HeadEquipped") && Field<byte>(protection, "m_BackpackStyle") == 0,
            "removed equipment stayed visible");
        view.Dispose();
        Require(!stage.IsValid(), "character stage survived disposal");
    }

    private static void CheckModules(MenuCharacterView view, InventoryGraph graph, InventoryItem item, WeaponRegistry registry)
    {
        ushort modules = (ushort)HaloModules.Mask(graph, item);
        Require(view.PreviewModules == modules, "module mask differs from the equipped item's tree");
        Require(view.PreviewMagazineCapacity == registry.GetWeaponData(DollSingerWeapons.WeaponId(item.Code), modules).MagazineSize,
            "modules did not change the formal capacity");
        var halo = Field<Transform>(view, "m_Halo");
        var modulesVisual = halo.GetComponentsInChildren<HaloModuleVisual>(true).FirstOrDefault(visual => visual.gameObject.activeInHierarchy);
        Require(modulesVisual, "formal module presentation missing");
        Require((ushort)Field<HaloModule>(modulesVisual, "modules") == modules,
            "formal module presentation has a different mask");
        int expected = graph.Children(item.Id).Count(child => HaloModules.FromCode(child.Code) != HaloModule.None) * 2;
        var sprites = modulesVisual.GetComponentsInChildren<SpriteRenderer>(true);
        if (sprites.Length > 0)
            Require(sprites.Count(renderer => renderer.gameObject.activeInHierarchy) == expected,
                "installed module artwork does not match the item tree");
        else
            Require(modulesVisual.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                "formal procedural module geometry is empty");
    }

    private static void CheckSnapshot(MenuCharacterView view, InventoryGraph graph, WeaponRegistry registry)
    {
        var secondary = graph.Equipped("Secondary");
        ushort modules = (ushort)(HaloModule.Capacitor | HaloModule.FocusLens | HaloModule.HitMarker);
        var weapon = registry.GetWeaponData(DollSingerWeapons.Shotgun, modules);
        var snapshot = new PredictedPlayerGhost { CurrentHealth = 100, EquippedWeaponID = DollSingerWeapons.Shotgun,
            EquippedWeaponItem = new FixedString64Bytes(secondary.Id), CurrentAmmo = 1, EquippedModules = modules,
            ReloadTargetAmmo = weapon.MagazineSize, ReloadTimer = weapon.ReloadTime * .5f,
            LastShotTick = 10, LastReloadTick = 11, ShieldEquipped = true, HeadDomeEquipped = true,
            HeadDomeStandard = true, SubspaceBackpack = 4, ShieldCharge = 47, ShieldEnergy = 63 };
        snapshot.ControllerState.IsReloadingState = true;
        view.SetEquipmentSnapshot(in snapshot); view.Advance(0);
        Require(view.PreviewWeaponId == snapshot.EquippedWeaponID && view.PreviewWeaponItemId == secondary.Id,
            "live snapshot did not override inventory's default weapon");
        Require(view.PreviewModules == modules && view.PreviewLoadedAmmo == 1 && view.PreviewMagazineCapacity == weapon.MagazineSize,
            "live modules, rounds or capacity ignored");
        var shotgun = Field<Transform>(view, "m_Halo").GetComponentInChildren<ShotgunHaloVisual>(true);
        Require(shotgun && shotgun.IsReloading && Mathf.Abs(shotgun.ReloadProgress - .5f) < .001f,
            "live charging state ignored");
        CheckProtection(view, true, 4);
        snapshot.ControllerState.IsReloadingState = false;
        snapshot.EquippedWeaponID = DollSingerWeapons.None; snapshot.EquippedWeaponItem = default;
        snapshot.CurrentAmmo = 0; snapshot.EquippedModules = 0;
        snapshot.EquippedWeaponSlot = MeleeInventory.EquipmentIndex;
        snapshot.MeleeWeapon = (byte)MeleeWeaponKind.Dagger;
        view.SetEquipmentSnapshot(in snapshot); view.Advance(0);
        Require(!view.HaloVisible && view.PreviewMeleeWeapon == MeleeWeaponKind.Dagger, "live dagger selection ignored");
        snapshot.MeleeWeapon = (byte)MeleeWeaponKind.Fists;
        view.SetEquipmentSnapshot(in snapshot); view.Advance(0);
        Require(!view.HaloVisible && view.PreviewMeleeWeapon == MeleeWeaponKind.Fists, "live empty hands retained a weapon");
    }

    private static void CheckProtection(MenuCharacterView view, bool standard, byte backpack)
    {
        var protection = Field<EnergyShieldPresentation>(view, "m_Protection");
        Require(protection && Field<bool>(protection, "m_HeadEquipped") && Field<bool>(protection, "m_ShieldEquipped"),
            "formal collar or shield generator missing");
        Require(!protection.PresentationAudioEnabled, "equipment preview may duplicate gameplay audio");
        Require(Field<bool>(protection, "m_StandardCollar") == standard && Field<byte>(protection, "m_BackpackStyle") == backpack,
            "collar or backpack variant differs from equipment");
        Require(Field<Transform>(protection, "m_Collar").gameObject.activeInHierarchy,
            "equipped collar geometry hidden");
        Require(Field<Transform>(protection, "m_Subspace").gameObject.activeInHierarchy, "equipped backpack geometry hidden");
    }

    private static void CheckBodyAssets(MenuCharacterView view, MenuCharacterSettings settings)
    {
        var model = Field<GameObject>(view, "m_Model");
        var formal = settings.HaloSource.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var preview = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Require(preview.Length > 0 && preview.Length == formal.Length && preview.All(renderer => formal.Any(source =>
                renderer.sharedMesh == source.sharedMesh && renderer.sharedMaterials.SequenceEqual(source.sharedMaterials))),
            "menu body meshes or materials differ from the formal character");
        Require(model.GetComponentsInChildren<Collider>(true).Length == 0 && model.GetComponentsInChildren<AudioSource>(true).Length == 0,
            "preview retained gameplay collision or audio");
    }

    private static void CheckHaloAssets(MenuCharacterView view, DollSingerHaloAim source)
    {
        var halo = Field<Transform>(view, "m_Halo");
        Require(halo, "formal halo tree was not instantiated");
        foreach (var actual in halo.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var type = actual.GetType();
            if (type != typeof(RifleHaloVisual) && type != typeof(RevolverHaloVisual) && type != typeof(ShotgunHaloVisual) &&
                type != typeof(SniperHaloVisual) && type != typeof(ArsenalHaloVisual)) continue;
            var original = source.haloVisual.GetComponentsInChildren(type, true).Cast<MonoBehaviour>().FirstOrDefault(candidate =>
                !(actual is ArsenalHaloVisual arsenal) || ((ArsenalHaloVisual)candidate).weapon == arsenal.weapon);
            Require(original, "formal halo component absent: " + type.Name);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType == typeof(Sprite) || field.FieldType == typeof(Material))
                    Require((Object)field.GetValue(actual) == (Object)field.GetValue(original), "asset reference replaced: " + type.Name + "." + field.Name);
                else if (field.FieldType == typeof(Sprite[]) || field.FieldType == typeof(Material[]))
                {
                    var a = (Array)field.GetValue(actual); var b = (Array)field.GetValue(original);
                    Require(a == null && b == null || a != null && b != null && a.Cast<Object>().SequenceEqual(b.Cast<Object>()),
                        "asset array replaced: " + type.Name + "." + field.Name);
                }
            }
        }
    }

    private static void CheckSelectedHalo(MenuCharacterView view, string code)
    {
        var halo = Field<Transform>(view, "m_Halo");
        Type type = code == "rifle" || code == "compact" ? typeof(RifleHaloVisual) : code == "pistol" ? typeof(RevolverHaloVisual) :
            code == "shotgun" ? typeof(ShotgunHaloVisual) : code == "sniper" ? typeof(SniperHaloVisual) : typeof(ArsenalHaloVisual);
        var selected = halo.GetComponentsInChildren(type, true).Cast<MonoBehaviour>().Where(visual => visual.gameObject.activeInHierarchy).ToArray();
        Require(selected.Length == 1, "wrong visible formal halo count for " + code);
        if (selected[0] is ArsenalHaloVisual arsenal)
            Require(arsenal.weapon == view.EquippedPreviewWeapon, "wrong arsenal design for " + code);
    }

    private static void CheckCorpse(int character)
    {
        var owner = InventoryGraph.Create(false, false); Equip(owner, "rifle", "Primary", 9);
        owner.HaloResearchLevel = 1; owner.HaloResearchBranches = 7;
        var fallen = Loadout();
        var slots = fallen.Children(InventoryCatalog.Equipment).ToDictionary(item => item.Region, item => item.Id);
        var cache = LootInventoryExchange.DropOnDeath(fallen);
        var joined = LootInventoryExchange.Snapshot(owner, cache);
        string before = JsonUtility.ToJson(joined);
        using var inventory = new ContainerInventoryView(new VisualElement(), false, _ => { });
        inventory.Present(joined, lootId: RaidLootContainers.FirstDeathBagId, corpseCharacterIndex: character, corpseEquipmentSlots: slots);
        inventory.Show();
        var left = Field<MenuCharacterView>(inventory, "m_Character");
        var right = Field<MenuCharacterView>(inventory, "m_CorpseCharacter");
        Require(left != null && right != null && right.CharacterIndex == character, "missing live player/corpse portrait");
        left.Advance(0); right.Advance(0);
        Require(left.PreviewWeaponItemId == owner.Equipped("Primary").Id && right.PreviewWeaponItemId == slots["Primary"],
            "corpse and player equipment were mixed");
        var method = typeof(ContainerInventoryView).GetMethod("CorpsePortraitInventory", Private);
        var projection = (InventoryGraph)method.Invoke(inventory, null);
        Require(projection.HaloResearchLevel == 0 && projection.HaloResearchBranches == 0, "corpse borrowed viewer's research");
        Require(projection.Children(slots["Primary"]).Count() == 3, "corpse lost nested halo modules");
        Require(JsonUtility.ToJson(joined) == before, "corpse portrait mutated the shared loot snapshot");
        Scene oldStage = right.DisplayScene;
        var next = joined.Clone();
        next.Items.RemoveAll(item => item.Id == slots["Helmet"]);
        next.Version++;
        inventory.Present(next, lootId: RaidLootContainers.FirstDeathBagId, corpseCharacterIndex: character, corpseEquipmentSlots: slots);
        right = Field<MenuCharacterView>(inventory, "m_CorpseCharacter"); right.Advance(0);
        Require(!oldStage.IsValid(), "replaced corpse stage leaked");
        var protection = Field<EnergyShieldPresentation>(right, "m_Protection");
        Require(!Field<bool>(protection, "m_HeadEquipped"), "looted collar stayed on corpse");
        var corpseStage = right.DisplayScene; var playerStage = left.DisplayScene;
        inventory.Present(owner);
        Require(Field<MenuCharacterView>(inventory, "m_CorpseCharacter") == null && !corpseStage.IsValid(), "closed loot retained corpse rendering");
        inventory.Suspend();
        Require(!playerStage.IsValid() && Field<MenuCharacterView>(inventory, "m_Character") == null,
            "hidden inventory retained player rendering");
    }

    [MenuItem("Tools/Moonkov/Capture Character Equipment Previews")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture outside Play mode.");
        const string directory = "LocalData/CharacterEquipmentPreview";
        Directory.CreateDirectory(directory);
        foreach (var capture in new[] {
            (character: GameSettings.DollSingerCharacterIndex, selection: "Primary"),
            (character: GameSettings.DollSingerCharacterIndex, selection: "Secondary"),
            (character: GameSettings.DollSingerCharacterIndex, selection: "Melee"),
            (character: GameSettings.HatsuneMikuCharacterIndex, selection: "Secondary"),
            (character: GameSettings.ShianCharacterIndex, selection: "tempest"),
            (character: GameSettings.ShianCharacterIndex, selection: "empty") })
        {
            using var view = new MenuCharacterView(idleOnly: true, characterIndex: capture.character);
            var graph = Loadout();
            string selected = capture.selection == "empty" ? "" : capture.selection == "tempest" ? graph.Equipped("Primary").Id : graph.Equipped(capture.selection).Id;
            if (capture.selection == "tempest") graph.Equipped("Primary").Code = "tempest";
            view.SetEquipmentInventory(graph, selected); view.Advance(0);
            view.Render(new Vector2(400, 640));
            var previous = RenderTexture.active;
            var image = new Texture2D(view.Target.width, view.Target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = view.Target;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                File.WriteAllBytes(directory + "/character-" + capture.character + "-" + capture.selection.ToLowerInvariant() + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        Debug.Log("Saved six actual character/equipment preview renders to " + directory);
    }
}
