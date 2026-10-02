using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;

public sealed partial class MoonkovUIPreview
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/Moonkov/Validate Loadout")]
    public static void ValidateLoadout()
    {
        var grid = new StashLayout(10, 24); var gear = new StashLoadout(grid);
        void Add(string id, EquipmentKind kind, int w, int h, int x, int y)
        { grid.Add(id, w, h, x, y); gear.Register(id, kind); }
        Add("helmet", EquipmentKind.Helmet, 2, 2, 0, 0);
        Add("rifle", EquipmentKind.LongGun, 4, 2, 2, 0);
        Add("compact", EquipmentKind.LongGun, 3, 2, 6, 0);
        Require(!gear.TryEquip("helmet", LoadoutSlot.Primary) && grid.Items["helmet"].InStorage, "Incompatible equipment changed state.");
        Require(gear.TryEquip("helmet", LoadoutSlot.Helmet) && !grid.Items["helmet"].InStorage, "Helmet equip failed.");
        Require(!gear.TryStore("helmet", 2, 0, false) && gear.At(LoadoutSlot.Helmet) == "helmet", "Colliding return removed equipped item.");
        Require(gear.TryStore("helmet", 4, 12, true) && gear.At(LoadoutSlot.Helmet) == null, "Arbitrary rotated storage return failed.");
        Require(gear.TryEquip("rifle", LoadoutSlot.Primary) && gear.TryEquip("compact", LoadoutSlot.Secondary), "Weapon equip failed.");
        Require(gear.TryEquip("rifle", LoadoutSlot.Secondary) && gear.At(LoadoutSlot.Primary) == "compact" && gear.At(LoadoutSlot.Secondary) == "rifle", "Weapon slot swap duplicated or lost equipment.");
        grid.Items["rifle"].Locked = true;
        Require(!gear.TryUnequip(LoadoutSlot.Secondary), "Locked equipped item moved.");
        var full = new StashLayout(3, 2); var crowded = new StashLoadout(full);
        full.Add("old", 2, 2, 0, 0); crowded.Register("old", EquipmentKind.Helmet);
        full.Add("new", 1, 1, 2, 0); crowded.Register("new", EquipmentKind.Helmet);
        Require(crowded.TryEquip("old", LoadoutSlot.Helmet), "Full-storage fixture setup failed.");
        full.Add("block", 2, 2, 0, 0); full.Add("block2", 1, 1, 2, 1);
        Require(!crowded.TryEquip("new", LoadoutSlot.Helmet) && crowded.At(LoadoutSlot.Helmet) == "old"
            && full.Items["new"].InStorage && !full.Items["old"].InStorage, "Full-storage replacement failed to roll back.");

        OpenLoadout(); var window = GetWindow<MoonkovUIPreview>();
        EditorApplication.delayCall += () => window.CheckLoadoutBindings();
    }

    private static Button Tile(VisualElement root, string shortName)
    {
        foreach (var tile in root.Query<Button>(className: "stash-item").ToList())
            if (tile.Q<Label>(className: "stash-item-name").text == shortName) return tile;
        throw new Exception("Missing item tile: " + shortName);
    }
    private static void Down(VisualElement target, Vector2 point)
    {
        using (var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = point, clickCount = 1 })) target.SendEvent(evt);
    }
    private static void Move(VisualElement target, Vector2 point)
    {
        using (var evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = point })) target.SendEvent(evt);
    }
    private static void Up(VisualElement target, Vector2 point)
    {
        using (var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = point })) target.SendEvent(evt);
    }
    private static void Drag(VisualElement target, Vector2 end) { Down(target, target.worldBound.center); Move(target, end); Up(target, end); }

    private void CheckLoadoutBindings()
    {
        var root = rootVisualElement.Q<VisualElement>("stashScreen");
        var stage = root.Q<VisualElement>("stashCharacterStage");
        var slots = root.Query<Button>(className: "stash-loadout-slot").ToList();
        Require(slots.Count == 6, "Expected six equipment slots.");
        foreach (var slot in slots) Require(slot.worldBound.width > 60 && slot.worldBound.height > 60 && stage.worldBound.Contains(slot.worldBound.center), "Equipment slot has invalid layout.");
        Require(root.Query<UnityEngine.UIElements.Image>().ToList().Count == 1, "Loadout page must have one character image.");
        var helmet = root.Q<Button>("loadoutHelmet"); var pistol = root.Q<Button>("loadoutPistol");
        var tile = Tile(root, "L-01");
        Drag(tile, helmet.worldBound.center);
        Require(helmet.Q<Label>("slotCaption").text == "L-01" && tile.style.display.value == DisplayStyle.None, "Pointer drag did not equip helmet exactly once: " + helmet.Q<Label>("slotCaption").text + " / " + tile.style.display.value);
        Down(helmet, helmet.worldBound.center); Up(helmet, helmet.worldBound.center);
        Require(root.Query<VisualElement>(className: "terminal-context").ToList().Count == 1, "An occupied slot click did not open equipment choices.");
        using (var close = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) root.SendEvent(close);
        Drag(helmet, pistol.worldBound.center);
        Require(helmet.Q<Label>("slotCaption").text == "L-01" && pistol.Q<Label>("slotCaption").text == "EMPTY", "Incompatible pointer drop changed loadout.");
        var storage = root.Q<VisualElement>(className: "stash-grid");
        float cell = storage.resolvedStyle.width / 10;
        var destination = storage.LocalToWorld(new Vector2(8 * cell, 6 * cell));
        Require(root.Q<ScrollView>("stashScroll").contentViewport.worldBound.Contains(destination), "Pointer return fixture outside visible grid.");
        Drag(helmet, destination);
        Require(helmet.Q<Label>("slotCaption").text == "EMPTY" && tile.style.display.value == DisplayStyle.Flex, "Pointer return did not restore storage item.");
        Vector2 before = tile.layout.position;
        Down(tile, tile.worldBound.center); Move(tile, helmet.worldBound.center);
        using (var key = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) root.SendEvent(key);
        Up(tile, helmet.worldBound.center);
        Require(helmet.Q<Label>("slotCaption").text == "EMPTY" && tile.layout.position == before, "Escape did not cancel drag.");
        Require(root.Q<VisualElement>(className: "stash-drag-ghost").style.display.value == DisplayStyle.None, "Drag ghost survived cancellation.");
        m_Screen.NavigatePage(0); m_Screen.NavigatePage(1);
        Require(root.Query<UnityEngine.UIElements.Image>().ToList().Count == 1, "Navigation accumulated character images.");
        Debug.Log("Moonkov loadout checks passed: compatibility, collision rollback, arbitrary rotated placement, weapon swap, locking, full-storage rollback; six-slot layout, pointer equip/invalid/return, Escape cancellation, one character image after navigation.");
    }
}
