using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Uses the real attached UI Toolkit panel and picking, without a game World or profile.
public sealed class RaidInventoryPanelChecks : EditorWindow
{
    private ContainerInventoryView m_View;
    private InventoryGraph m_Graph;
    private int m_Commands;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play mode.");
        var window = GetWindow<RaidInventoryPanelChecks>();
        window.titleContent = new GUIContent("Raid inventory checks");
        window.position = new Rect(100, 100, 1280, 800);
        window.Show();
        window.m_View?.Dispose(); window.rootVisualElement.Clear(); window.m_Commands = 0;
        window.m_Graph = InventoryGraph.Create(false);
        window.m_Graph.AddSupply("medkit", 1);
        window.m_View = new ContainerInventoryView(window.rootVisualElement, false, command =>
        {
            var error = window.m_Graph.TryApply(command);
            if (error != InventoryError.None) throw new InvalidOperationException("Invalid panel command: " + error);
            window.m_Commands++; window.m_View.Present(window.m_Graph);
        });
        Unity.MP_FPS.MoonkovAudio.BindUI(window.rootVisualElement);
        window.m_View.Present(window.m_Graph);
        window.rootVisualElement.schedule.Execute(window.Check).StartingIn(300);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pointer(VisualElement target, EventType type, Vector2 point)
    {
        var source = new Event { type = type, mousePosition = point, button = 0, clickCount = 1 };
        if (type == EventType.MouseDown) { using var evt = PointerDownEvent.GetPooled(source); target.SendEvent(evt); }
        else if (type == EventType.MouseUp) { using var evt = PointerUpEvent.GetPooled(source); target.SendEvent(evt); }
        else { using var evt = PointerMoveEvent.GetPooled(source); typeof(PointerMoveEvent).GetProperty("pressedButtons").SetValue(evt, 1); target.SendEvent(evt); }
    }
    private void Check()
    {
        try
        {
            var item = m_Graph.Items.First(i => i.Code == "medkit");
            var tile = m_View.Element.Q<Button>("inventoryItem" + item.Id);
            var picked = tile.panel.Pick(tile.worldBound.center);
            Require(picked == tile || tile.Contains(picked), "Item covered by: " + picked?.name + " / " + picked?.GetType().Name);
            for (int i = 0; i < 3; i++)
            {
                Pointer(picked, EventType.MouseDown, tile.worldBound.center);
                Pointer(tile, EventType.MouseUp, tile.worldBound.center);
                Require((string)typeof(ContainerInventoryView).GetField("m_SelectedItemId", Private).GetValue(m_View) == item.Id, "Panel click did not select item.");
                Require(typeof(ContainerInventoryView).GetField("m_Drag", Private).GetValue(m_View) == null, "Released click left a gesture active.");
            }
            Require(m_Commands == 0, "Click unexpectedly moved inventory.");
            // A pending server reply blocks mutation, not selection and inspection.
            typeof(ContainerInventoryView).GetField("m_Busy", Private).SetValue(m_View, true);
            typeof(ContainerInventoryView).GetField("m_SelectedItemId", Private).SetValue(m_View, null);
            Pointer(tile, EventType.MouseDown, tile.worldBound.center);
            Pointer(tile, EventType.MouseUp, tile.worldBound.center);
            Require((string)typeof(ContainerInventoryView).GetField("m_SelectedItemId", Private).GetValue(m_View) == item.Id, "Pending reply blocked even item inspection.");
            m_View.Present(m_Graph);
            var start = tile.worldBound.center;
            Pointer(tile, EventType.MouseDown, start);
            Pointer(tile, EventType.MouseDrag, start + Vector2.right * 12);
            Require(m_View.Element.Q("inventoryGhost").resolvedStyle.display != DisplayStyle.None || m_View.Element.Q("inventoryGhost").style.display.value == DisplayStyle.Flex, "Held gesture did not start drag.");
            Pointer(tile, EventType.MouseDown, start + Vector2.right * 12);
            Require(m_View.Element.Q("inventoryGhost").style.display.value == DisplayStyle.Flex, "Repeated down restarted the held drag.");
            m_View.CancelDrag();
            foreach (var slot in m_View.Element.Query<Button>(className: "inventory-slot").ToList())
            {
                var hit = slot.panel.Pick(slot.worldBound.center);
                Require(hit == slot || slot.Contains(hit), "Equipment covered: " + slot.name + " by " + hit?.name);
            }
            File.WriteAllText("Temp/raid-panel-check.txt", "PASS: actual panel picking and repeated clicks; pending reply permits inspection; repeated down preserves held drag; no phantom command or capture; eight equipment targets.");
        }
        catch (Exception error) { File.WriteAllText("Temp/raid-panel-check.txt", error.ToString()); Debug.LogException(error); }
        finally { Unity.MP_FPS.MoonkovAudio.UnbindUI(rootVisualElement); m_View?.Dispose(); m_View = null; Close(); }
    }
    private void OnDisable() { m_View?.Dispose(); m_View = null; }
}
