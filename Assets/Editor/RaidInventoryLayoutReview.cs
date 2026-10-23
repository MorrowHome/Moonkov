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
        if (mode != "corpse" && mode != "cache" && mode != "empty") throw new ArgumentException("Unknown inventory review case.");
        var window = CreateInstance<RaidInventoryLayoutReview>();
        window.m_Case = mode; window.m_Folder = Path.GetFullPath(folder);
        window.titleContent = new GUIContent("背包与搜刮 / 原生预览");
        window.minSize = window.maxSize = new Vector2(width, height);
        window.position = new Rect(30, 30, width, height); window.ShowUtility(); window.Build();
        window.rootVisualElement.schedule.Execute(window.Review).StartingIn(700);
    }
    private void Build()
    {
        var root = rootVisualElement;
        Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(root);
        var presentation = root.Q("raidPresentation"); presentation.style.flexGrow = 1;
        foreach (var child in presentation.Children()) child.style.display = child.name == "raidInventory" ? DisplayStyle.Flex : DisplayStyle.None;
        root.Q("raidHealthHost").style.display = DisplayStyle.None;
        root.Q<Label>(className: "raid-inventory-title").text = "随身装备与搜刮 · 示例库存预览";
        m_Carried = InventoryGraph.Create(false, m_Case != "empty");
        if (m_Case != "empty")
        {
            m_Carried.AddSupply("medkit", 2); m_Carried.AddSupply("cells", 3);
            m_Carried.AddSupply("water", 1, m_Carried.Equipped("Backpack").Id);
            m_Carried.AddSupply("ration", 1, m_Carried.Equipped("ChestRig").Id);
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
        m_View = new ContainerInventoryView(root.Q("raidInventoryHost"), false, command =>
        {
            var error = m_Loot == null ? m_Carried.TryApply(command) : LootInventoryExchange.TryApply(m_Carried, m_Loot, command, m_Loot.Version);
            Require(error == InventoryError.None, "Invalid real transaction: " + error); m_Commands++;
            Present();
        });
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
    { m_Bindings.InitializeWeapons(m_Joined); m_Quick.Present(m_Bindings, m_Joined, false, true, null); }
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
            foreach (var slot in new[] { "Helmet", "Shield", "Primary", "Secondary", "Pistol", "Melee", "ChestRig", "Backpack" })
                Require(m_View.Element.Q<Button>("inventorySlot" + slot) != null, "Player equipment slot missing: " + slot);
            var quick = rootVisualElement.Q("quickSlot9");
            Require(quick.worldBound.yMax <= m_View.Element.worldBound.yMax + 1, "Quickbar leaves inventory bounds.");
            Require(quick.panel.Pick(quick.worldBound.center) == quick, "Quickbar is covered by another panel.");
            if (m_Case == "empty") Require(m_View.Element.Q("containerPane").Q("inventorySlotBackpack") != null && m_View.Element.Q("containerPane").Q("inventorySlotChestRig") != null, "Empty container drop slots missing.");
            Capture(Path.Combine(m_Folder, m_Case + "-" + Mathf.RoundToInt(rootVisualElement.resolvedStyle.width) + ".png"));
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
            Directory.CreateDirectory(m_Folder);
            File.WriteAllText(Path.Combine(m_Folder, m_Case + "-checks.txt"), "PASS: native column order, eight equipment slots, visible quickbar; " + (m_Case == "empty" ? "empty chest-rig/backpack targets." : "drag and click binding; " + (m_Case == "corpse" ? "corpse-to-equipment authoritative graph exchange." : "supply cache.")));
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
