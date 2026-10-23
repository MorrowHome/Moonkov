using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Native UXML/presenter review. No desktop pixels, account, game World or network.
public sealed class RaidInventoryLayoutReview : EditorWindow
{
    private ContainerInventoryView m_View;
    private QuickSlotView m_Quick;
    private QuickSlotBindings m_Bindings;
    private InventoryGraph m_Carried, m_Loot, m_Joined;
    private string m_Case, m_Folder;
    private int m_LootId, m_Commands;
    private sealed class Memory : IQuickSlotStore
    { public QuickSlotSave Load(string scope) => null; public void Save(string scope, QuickSlotSave value) { } }

    public static void Open(string mode, int width, int height, string folder)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play mode.");
        if (mode != "corpse" && mode != "cache" && mode != "empty" && mode != "warehouse" && mode != "secure") throw new ArgumentException("Unknown inventory review case.");
        var window = CreateInstance<RaidInventoryLayoutReview>();
        window.m_Case = mode; window.m_Folder = Path.GetFullPath(folder);
        window.titleContent = new GUIContent("背包与搜刮 / 原生预览");
        window.minSize = window.maxSize = new Vector2(width, height);
        window.position = new Rect(30, 30, width, height); window.ShowUtility(); window.Build();
        window.rootVisualElement.schedule.Execute(() =>
        {
            if (mode != "secure") window.Review();
            else
            {
                window.m_View.Element.Q<ScrollView>("containerPane").ScrollTo(window.m_View.Element.Q("inventorySlotSecureContainer").parent.parent);
                window.rootVisualElement.schedule.Execute(window.Review).StartingIn(150);
            }
        }).StartingIn(700);
    }
    private void Build()
    {
        var root = rootVisualElement;
        Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(root);
        var presentation = root.Q("raidPresentation"); presentation.style.flexGrow = 1;
        foreach (var child in presentation.Children()) child.style.display = child.name == "raidInventory" ? DisplayStyle.Flex : DisplayStyle.None;
        root.Q("raidHealthHost").style.display = DisplayStyle.None;
        root.Q<Label>(className: "raid-inventory-title").text = "随身装备与搜刮 · 示例库存预览";
        bool warehouse = m_Case == "warehouse";
        m_Carried = InventoryGraph.Create(warehouse, m_Case != "empty");
        if (m_Case != "empty")
        {
            m_Carried.AddSupply("medkit", 2); m_Carried.AddSupply("cells", 3);
            m_Carried.AddSupply("water", 1, m_Carried.Equipped("Backpack").Id);
            m_Carried.AddSupply("ration", 1, m_Carried.Equipped("ChestRig").Id);
            m_Carried.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "survey_compass", Parent = "equipment", Region = "Special1" });
            m_Carried.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "laser_rangefinder", Parent = "equipment", Region = "Special2" });
            var box = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "secure_case", Parent = "equipment", Region = "SecureContainer" };
            m_Carried.Items.Add(box); Require(m_Carried.AddSupply("data_drive", 1, box.Id, true) == InventoryError.None, "Cannot stock secure case fixture.");
        }
        if (warehouse)
        {
            foreach (var code in new[] { "dust", "alloy", "optical_sensor", "cells", "medkit", "water", "circuit_board", "oxygen_reserve", "field_rig", "small_pack", "halo_focus_lens", "energy_head_dome", "energy_shield_generator", "ration", "data_drive", "ceramic_plate", "quantum_core" })
            {
                if (!InventoryCatalog.Get(code).Container) Require(m_Carried.AddSupply(code, 1, "stash") == InventoryError.None, "Cannot stock warehouse fixture: " + code);
                else
                {
                    var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code, Parent = "stash" };
                    Require(m_Carried.FindSpace(item, "stash", out var region, out var x, out var y), "Cannot place fixture container.");
                    item.Region = region; item.X = x; item.Y = y; m_Carried.Items.Add(item);
                }
            }
            root.Q<Label>(className: "raid-inventory-title").text = "人物装备与仓库 · 示例库存预览";
        }
        m_LootId = -1;
        if (m_Case == "corpse")
        {
            var dead = InventoryGraph.Create(false); dead.AddSupply("helmet", 1, "equipment"); dead.AddSupply("energy_shield_generator", 1, "equipment");
            dead.AddSupply("med_gel", 2, dead.Equipped("ChestRig").Id);
            dead.AddSupply("water", 1, dead.Equipped("Backpack").Id); dead.AddSupply("dust", 3, dead.Equipped("Backpack").Id);
            dead.AddSupply("cells", 2); m_Loot = LootInventoryExchange.DropOnDeath(dead); m_LootId = RaidLootContainers.FirstDeathBagId;
        }
        else if (m_Case == "cache") { m_Loot = LootInventoryExchange.CreateCache(0); m_LootId = 0; }
        m_Joined = m_Loot == null ? m_Carried : LootInventoryExchange.Snapshot(m_Carried, m_Loot);
        Require(m_Joined.Validate() == InventoryError.None, "Review fixture graph invalid.");
        m_View = new ContainerInventoryView(root.Q("raidInventoryHost"), warehouse, command =>
        {
            var error = m_Loot == null ? m_Carried.TryApply(command) : LootInventoryExchange.TryApply(m_Carried, m_Loot, command, m_Loot.Version);
            Require(error == InventoryError.None, "Invalid real transaction: " + error); m_Commands++;
            Present();
        });
        if (warehouse) { Present(); MoonkovLocalization.Bind(root); MoonkovAudio.BindUI(root); return; }
        m_Bindings = new QuickSlotBindings(new Memory()); m_Bindings.UseProfile("native-layout-review");
        m_Quick = new QuickSlotView(root, slot => { m_Bindings.Clear(slot); PresentQuick(); }, slot =>
        { m_Bindings.Bind(slot, m_View.QuickSlotCandidate, m_Joined); PresentQuick(); });
        m_Quick.SetInventoryHost(m_View.Element.Q("inventoryQuickbarDock"));
        m_View.QuickSlotDrop = (item, point) =>
        {
            int slot = m_Quick.Hit(point); if (slot < 0) return false;
            m_Bindings.Bind(slot, item, m_Joined); PresentQuick(); return true;
        };
        m_View.QuickSlotDragPreview = (item, point) => m_Quick.PreviewDrop(item == null ? -1 : m_Quick.Hit(point), QuickSlotBindings.CanBind(m_Joined, item));
        Present(); MoonkovLocalization.Bind(root); MoonkovAudio.BindUI(root);
    }
    private void Present()
    {
        m_Joined = m_Loot == null ? m_Carried : LootInventoryExchange.Snapshot(m_Carried, m_Loot);
        m_View.Present(m_Joined, lootId: m_LootId); PresentQuick();
    }
    private void PresentQuick()
    { if (m_Quick == null) return; m_Bindings.InitializeWeapons(m_Joined); m_Quick.Present(m_Bindings, m_Joined, false, true, null); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pointer(VisualElement target, EventType type, Vector2 point)
    {
        var source = new Event { type = type, mousePosition = point, button = 0, clickCount = 1 };
        if (type == EventType.MouseDown) { using var evt = PointerDownEvent.GetPooled(source); target.SendEvent(evt); }
        else if (type == EventType.MouseUp) { using var evt = PointerUpEvent.GetPooled(source); target.SendEvent(evt); }
        else { using var evt = PointerMoveEvent.GetPooled(source); typeof(PointerMoveEvent).GetProperty("pressedButtons").SetValue(evt, 1); target.SendEvent(evt); }
    }
    private static void Drag(VisualElement source, Vector2 point)
    {
        var hit = source.panel.Pick(source.worldBound.center);
        Require(hit == source || source.Contains(hit), "Source covered: " + source.name + " by " + hit?.name);
        Pointer(source, EventType.MouseDown, source.worldBound.center);
        Pointer(source, EventType.MouseDrag, point); Pointer(source, EventType.MouseUp, point);
    }
    private void Review()
    {
        try
        {
            var left = m_View.Element.Q("characterPane").worldBound;
            var middle = m_View.Element.Q("containerPane").worldBound;
            var right = m_View.Element.Q("lootPane").worldBound;
            Require(left.xMax <= middle.xMin && middle.xMax <= right.xMin, "Three-column order or clipping incorrect.");
            Require(middle.width >= 260 && right.width >= 260, "Container columns too narrow.");
            foreach (var slot in new[] { "Helmet", "Shield", "Primary", "Secondary", "Pistol", "Melee", "ChestRig", "Backpack", "SecureContainer", "Special1", "Special2", "Special3" })
                Require(m_View.Element.Q<Button>("inventorySlot" + slot) != null, "Player equipment slot missing: " + slot);
            if (m_Case == "warehouse")
            {
                Capture(Path.Combine(m_Folder, "warehouse-" + Mathf.RoundToInt(rootVisualElement.resolvedStyle.width) + ".png"));
                var supply = m_Joined.Items.First(i => i.Code == "optical_sensor");
                var tile = m_View.Element.Q<Button>("inventoryItem" + supply.Id);
                var pocket = m_View.Element.Q("inventoryGridpockets-3");
                Drag(tile, pocket.worldBound.center);
                Require(m_Commands == 1 && m_Carried.Find(supply.Id).Parent == "pockets", "Warehouse-to-pockets move failed.");
                Directory.CreateDirectory(m_Folder); File.WriteAllText(Path.Combine(m_Folder, "warehouse-checks.txt"), "PASS: native warehouse columns, static portrait, twelve equipment targets, warehouse-to-pocket authoritative transaction.");
                return;
            }
            var quick = rootVisualElement.Q("quickSlot9");
            Require(quick.worldBound.yMax <= m_View.Element.worldBound.yMax + 1, "Quickbar leaves inventory bounds.");
            Require(quick.panel.Pick(quick.worldBound.center) == quick, "Quickbar is covered by another panel.");
            if (m_Case == "empty") Require(m_View.Element.Q("containerPane").Q("inventorySlotBackpack") != null && m_View.Element.Q("containerPane").Q("inventorySlotChestRig") != null, "Empty container drop slots missing.");
            Capture(Path.Combine(m_Folder, m_Case + "-" + Mathf.RoundToInt(rootVisualElement.resolvedStyle.width) + ".png"));
            if (m_Case == "secure")
            {
                var box = m_Carried.Equipped("SecureContainer");
                var grid = m_View.Element.Q("inventoryGrid" + box.Id + "-main");
                var viewport = m_View.Element.Q<ScrollView>("containerPane").contentViewport.worldBound;
                Require(grid != null && grid.worldBound.yMin >= viewport.yMin && grid.worldBound.yMax <= viewport.yMax + 1, "Secure-case contents not fully visible in review.");
                File.WriteAllText(Path.Combine(m_Folder, "secure-checks.txt"), "PASS: dedicated secure-case target and actual content grid."); return;
            }
            if (m_Case != "empty")
            {
                var medicine = m_Joined.Items.First(i => i.Code == "medkit"); var tile = m_View.Element.Q<Button>("inventoryItem" + medicine.Id);
                Drag(tile, quick.worldBound.center);
                Require(m_Bindings[8] == medicine.Id, "Docked quickbar rejected drag binding.");
                Pointer(tile, EventType.MouseDown, tile.worldBound.center); Pointer(tile, EventType.MouseUp, tile.worldBound.center);
                var clicked = rootVisualElement.Q<Button>("quickSlot8");
                Pointer(clicked, EventType.MouseDown, clicked.worldBound.center); Pointer(clicked, EventType.MouseUp, clicked.worldBound.center);
                Require(m_Bindings[7] == medicine.Id, "Select-item then click-slot binding failed.");
            }
            if (m_Case == "corpse")
            {
                var source = m_View.Element.Q<Button>("corpseSlotHelmet"); var helmet = m_Loot.Children("loot").First(i => InventoryCatalog.Get(i.Code).Kind == ItemKind.Helmet);
                Drag(source, m_View.Element.Q("inventorySlotHelmet").worldBound.center);
                Require(m_Commands == 1 && m_Carried.Equipped("Helmet")?.Id == helmet.Id, "Corpse-to-equipment transaction failed.");
            }
            else if (m_Case == "cache")
            {
                string tool = m_Carried.Equipped("Special1").Id;
                Drag(m_View.Element.Q<Button>("inventorySlotSpecial1"), m_View.Element.Q("inventorySlotSpecial3").worldBound.center);
                Require(m_Commands == 1 && m_Carried.Equipped("Special3")?.Id == tool && m_Carried.Equipped("Special1") == null, "Special-tool slot transfer failed.");
            }
            Directory.CreateDirectory(m_Folder);
            File.WriteAllText(Path.Combine(m_Folder, m_Case + "-checks.txt"), "PASS: native column order, twelve equipment slots, visible quickbar; " + (m_Case == "empty" ? "empty chest-rig/backpack/case/tool targets." : "drag and click binding; " + (m_Case == "corpse" ? "corpse-to-equipment authoritative graph exchange." : "supply cache and special-tool slot transfer.")));
        }
        catch (Exception error) { Directory.CreateDirectory(m_Folder); File.WriteAllText(Path.Combine(m_Folder, m_Case + "-checks.txt"), error.ToString()); Debug.LogException(error); }
        finally { Close(); }
    }
    private void Capture(string path)
    {
        var root = rootVisualElement; int width = Mathf.RoundToInt(root.resolvedStyle.width), height = Mathf.RoundToInt(root.resolvedStyle.height);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // The utility panel retains its own monitor scale; the global EditorGUI
        // value may belong to another window after a camera preview repaints.
        float dpi = Convert.ToSingle(root.panel.GetType().GetProperty("scaledPixelsPerPoint", flags).GetValue(root.panel));
        var target = new RenderTexture(Mathf.RoundToInt(width * dpi), Mathf.RoundToInt(height * dpi), 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create();
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; Texture2D image = null;
        try
        {
            RenderTexture.active = target; GL.Viewport(new Rect(0, 0, target.width, target.height)); GL.sRGBWrite = false; GL.Clear(true, true, Color.black); GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, width, height, 0);
                root.panel.GetType().GetMethod("Repaint", flags, null, new[] { typeof(Event) }, null).Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                root.panel.GetType().GetMethod("Render", flags).Invoke(root.panel, null);
            }
            finally { GL.PopMatrix(); }
            image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; if (image) DestroyImmediate(image); target.Release(); DestroyImmediate(target); }
    }
    private void OnDisable() { MoonkovAudio.UnbindUI(rootVisualElement); m_Quick?.Dispose(); m_View?.Dispose(); m_Quick = null; m_View = null; }
}
