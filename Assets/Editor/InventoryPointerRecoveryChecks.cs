using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        Require(pending.GetValue(view) == null, "Missing PointerUp left a captured press alive.");
        press();
        typeof(ContainerInventoryView).GetMethod("FocusChanged", Private).Invoke(view, new object[] { false });
        Require(pending.GetValue(view) == null, "Focus loss left an inventory drag alive.");
        press(); view.Suspend();
        Require(pending.GetValue(view) == null, "Closing inventory left a gesture alive.");
        Debug.Log("Equivalent inventory snapshots and missed release, focus loss, close recovery passed.");
    }
}
