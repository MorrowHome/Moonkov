using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class InventoryPointerRecoveryChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Inventory Pointer Recovery")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play mode.");
        var root = new VisualElement();
        using var view = new ContainerInventoryView(root, false, _ => { });
        var graph = InventoryGraph.Create(false); graph.AddSupply("cells", 2);
        view.Present(graph);
        var tilesField = typeof(ContainerInventoryView).GetField("m_Tiles", Private);
        var tiles = (Dictionary<string, VisualElement>)tilesField.GetValue(view);
        var tile = tiles.Values.First();
        view.Present(graph.Clone());
        Require(ReferenceEquals(tile, ((Dictionary<string, VisualElement>)tilesField.GetValue(view)).Values.First()), "Equivalent snapshot destroyed the interactive tile.");
        var dragType = typeof(ContainerInventoryView).GetNestedType("Drag", BindingFlags.NonPublic);
        var drag = (IManipulator)Activator.CreateInstance(dragType, BindingFlags.Instance | BindingFlags.Public, null,
            new object[] { view, new Func<InventoryItem>(() => graph.Items.First(i => i.Code == "cells")), new Action(() => { }), new Action(() => { }) }, null);
        tile.AddManipulator(drag);
        var pending = typeof(ContainerInventoryView).GetField("m_Drag", Private);
        Action press = () =>
        {
            using var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 });
            dragType.GetMethod("Down", Private).Invoke(drag, new object[] { down });
            Require(pending.GetValue(view) != null, "Synthetic primary press did not start a gesture.");
        };
        press();
        using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove }))
        {
            typeof(PointerMoveEvent).GetProperty("pressedButtons").SetValue(move, 0);
            dragType.GetMethod("Move", Private).Invoke(drag, new object[] { move });
        }
        Require(pending.GetValue(view) != null, "An unpressed move discarded the queued PointerUp.");
        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
            dragType.GetMethod("Up", Private).Invoke(drag, new object[] { up });
        Require(pending.GetValue(view) == null, "PointerUp left a captured press alive.");
        press();
        typeof(ContainerInventoryView).GetMethod("FocusChanged", Private).Invoke(view, new object[] { false });
        Require(pending.GetValue(view) == null, "Focus loss left an inventory drag alive.");
        press(); view.Suspend();
        Require(pending.GetValue(view) == null, "Closing inventory left a gesture alive.");
        Debug.Log("Equivalent inventory snapshots, queued release, focus loss and close recovery passed.");
    }

    [MenuItem("Tools/Moonkov/Check Inventory Drop Release")]
    public static async void CheckDropRelease() => Debug.Log(await RunDropRelease());

    // An attached panel is required: missed-release recovery runs in its scheduler.
    public static async Task<string> RunDropRelease()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play mode.");
        var window = ScriptableObject.CreateInstance<EditorWindow>();
        window.titleContent = new GUIContent("Inventory drop release checks");
        window.position = new Rect(50, 50, 1600, 1000); window.ShowUtility();
        int cases = 0;
        try
        {
            foreach (string surface in new[] { "warehouse", "carried", "loot" })
            foreach (string scenario in new[] { "normal", "queued-up", "burst", "missed-up", "continue-held",
                "new-press", "focus-loss", "close", "detach", "right-cancel", "invalid", "snapshot" })
            {
                bool stash = surface == "warehouse", loot = surface == "loot";
                var graph = InventoryGraph.Create(stash, false);
                if (loot) graph.Items.Add(new InventoryItem { Id = "loot", Code = "loot" });
                string source = stash ? "stash" : loot ? "loot" : "pockets";
                Require(graph.AddSupply("medkit", 1, source) == InventoryError.None, "Fixture supply failed.");
                var item = graph.Items.First(i => i.Code == "medkit");
                if (scenario == "invalid") graph.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"),
                    Code = "ration", Parent = stash ? "stash" : "pockets", Region = stash ? "main" : "3", X = stash ? 4 : 0, Y = stash ? 2 : 0 });
                int commands = 0;
                ContainerInventoryView view = null;
                view = new ContainerInventoryView(window.rootVisualElement, stash, command =>
                {
                    Require(graph.TryApply(command) == InventoryError.None, "Drop sent an invalid command.");
                    commands++; view.Present(graph, lootId: loot ? 0 : -1);
                });
                using (view)
                {
                    view.OperationAudioEnabled = false;
                    view.Present(graph, lootId: loot ? 0 : -1);
                    await Task.Delay(120);
                    var tile = view.Element.Q<Button>("inventoryItem" + item.Id);
                    var grid = view.Element.Q(stash ? "inventoryGridstash-main" : "inventoryGridpockets-3");
                    Require(tile != null && tile.worldBound.width > 10 && grid.worldBound.width > 10, "Panel did not lay out.");
                    var start = tile.worldBound.center;
                    var end = grid.LocalToWorld(stash ? new Vector2(4.5f * 64, 2.5f * 64) : new Vector2(32, 32));
                    var picked = tile.panel.Pick(start);
                    Require(picked == tile || tile.Contains(picked), surface + ": source is covered.");
                    SendPointer(picked, EventType.MouseDown, start);
                    SendPointer(tile, EventType.MouseDrag, end);
                    bool valid = scenario != "invalid";
                    Require(view.Element.Query(className: valid ? "inventory-drop-valid" : "inventory-drop-invalid").ToList().Count > 0,
                        surface + "/" + scenario + ": unexpected drop preview.");

                    bool completes = scenario == "normal" || scenario == "queued-up" || scenario == "burst" ||
                        scenario == "continue-held" || scenario == "new-press" || scenario == "snapshot";
                    if (scenario != "normal") SendPointer(tile, EventType.MouseMove, end, 0);
                    if (scenario == "burst")
                    {
                        // A high polling-rate batch can put several unpressed moves before
                        // PointerUp and older held moves after it. Submit exactly once.
                        for (int i = 0; i < 24; i++) SendPointer(tile, EventType.MouseMove, end, 0);
                    }
                    if (scenario == "snapshot")
                    {
                        graph = graph.Clone(); graph.Find(item.Id).Quantity = 2;
                        view.Present(graph, lootId: loot ? 0 : -1);
                    }
                    if (scenario == "continue-held" || scenario == "new-press")
                    {
                        if (scenario == "new-press") SendPointer(tile, EventType.MouseDown, start);
                        SendPointer(tile, EventType.MouseDrag, end);
                        await Task.Delay(80);
                        Require(Held(view), "Recovery cancelled a continued/new press.");
                    }
                    if (scenario == "focus-loss") typeof(ContainerInventoryView).GetMethod("FocusChanged", Private).Invoke(view, new object[] { false });
                    if (scenario == "close") view.Suspend();
                    if (scenario == "detach") tile.RemoveFromHierarchy();
                    if (scenario == "right-cancel") SendPointer(tile, EventType.MouseDown, end, 0, 1);
                    if (scenario != "missed-up") SendPointer(tile, EventType.MouseUp, end);
                    if (scenario == "burst")
                        for (int i = 0; i < 24; i++) SendPointer(window.rootVisualElement, EventType.MouseMove, end);
                    await Task.Delay(80);
                    string context = surface + "/" + scenario;
                    Require(commands == (completes ? 1 : 0), context + ": wrong command count " + commands);
                    Require(!Held(view) && !tile.HasPointerCapture(0), context + ": gesture/capture was not cleared.");
                    Require(view.Element.Q("inventoryGhost").style.display.value == DisplayStyle.None, context + ": ghost remains visible.");
                    var current = graph.Find(item.Id);
                    Require(current != null && graph.Count("medkit") == (scenario == "snapshot" ? 2 : 1), context + ": item count changed.");
                    Require(current.Parent == (completes ? stash ? "stash" : "pockets" : source), context + ": wrong destination.");
                    Require(current.X == (completes && stash ? 4 : 0) && current.Y == (completes && stash ? 2 : 0) &&
                        (stash || current.Region == (completes ? "3" : loot ? "main" : "1")), context + ": wrong grid placement.");
                    cases++;
                }
            }
            return $"PASS {cases} attached-panel drop cases: warehouse, carried inventory and loot; queued releases, burst input, real missed releases, cancellation, invalid placement and live snapshots.";
        }
        finally { window.Close(); }
    }

    private static bool Held(ContainerInventoryView view) => typeof(ContainerInventoryView).GetField("m_Drag", Private).GetValue(view) != null;

    private static void SendPointer(VisualElement target, EventType type, Vector2 point, int buttons = 1, int button = 0)
    {
        var raw = new Event { type = type, button = button, mousePosition = point, clickCount = 1 };
        if (type == EventType.MouseDown) { using var evt = PointerDownEvent.GetPooled(raw); target.SendEvent(evt); }
        else if (type == EventType.MouseUp) { using var evt = PointerUpEvent.GetPooled(raw); target.SendEvent(evt); }
        else
        {
            using var evt = PointerMoveEvent.GetPooled(raw);
            typeof(PointerMoveEvent).GetProperty("pressedButtons").SetValue(evt, buttons);
            target.SendEvent(evt);
        }
    }
}
