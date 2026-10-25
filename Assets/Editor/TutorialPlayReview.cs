using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

// Drives native UI and real inventory transactions against a unique review profile.
public static class TutorialPlayReview
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string ProfilePath { get; private set; }
    private static bool s_HadPlayerName, s_PreferencesCaptured;
    private static string s_PlayerName;
    public static string Open()
    {
        if (!EditorApplication.isPlaying || PlayerProfileClient.IsOffline) throw new InvalidOperationException("Use fresh menu Play with no account open.");
        s_HadPlayerName = PlayerPrefs.HasKey("PlayerName"); s_PlayerName = PlayerPrefs.GetString("PlayerName"); s_PreferencesCaptured = true;
        EditorApplication.playModeStateChanged -= PlayChanged; EditorApplication.playModeStateChanged += PlayChanged;
        ProfilePath = Path.GetFullPath("Library/BuildValidation/Tutorial/Play-" + Guid.NewGuid().ToString("N") + "/profile.json");
        typeof(PlayerProfileClient).GetField("s_Offline", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new OfflineProfileStore(ProfilePath));
        PlayerProfileClient.ValidateAsync("", CancellationToken.None).GetAwaiter().GetResult();
        var menu = UnityEngine.Object.FindFirstObjectByType<Unity.MP_FPS.Client.MainMenu>();
        typeof(Unity.MP_FPS.Client.MainMenu).GetField("m_AccountVerified", PrivateInstance).SetValue(menu, true);
        typeof(Unity.MP_FPS.Client.MainMenu).GetMethod("UpdateAccountPanel", PrivateInstance).Invoke(menu, null);
        return ProfilePath;
    }
    private static void PlayChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !s_PreferencesCaptured) return;
        if (s_HadPlayerName) PlayerPrefs.SetString("PlayerName", s_PlayerName); else PlayerPrefs.DeleteKey("PlayerName");
        PlayerPrefs.Save(); s_PreferencesCaptured = false; EditorApplication.playModeStateChanged -= PlayChanged;
    }
    public static string Submit(string text)
    {
        foreach (var document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            var buttons = document.rootVisualElement.Query<Button>().ToList();
            var button = buttons.FirstOrDefault(b => (b.name == text || b.text == text || b.Query<Label>().ToList().Any(l => l.text == text)) && b.enabledInHierarchy && b.resolvedStyle.display != DisplayStyle.None && b.worldBound.width > 0);
            if (button == null) continue;
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
            return "Submitted: " + text;
        }
        return "No visible button: " + text;
    }
    public static string Equip()
    {
        var graph = PlayerProfileClient.Inventory;
        var weapon = graph.Items.First(i => i.Code == "rifle");
        Move(weapon.Id, "equipment", "Primary");
        graph = PlayerProfileClient.Inventory;
        var cells = graph.Items.First(i => i.Code == "cells");
        Move(cells.Id, "pockets", "1");
        graph = PlayerProfileClient.Inventory;
        var med = graph.Items.First(i => i.Code == "medkit");
        Move(med.Id, "pockets", "2");
        return "Prepared through actual inventory API; mask=" + Unity.MP_FPS.Campaign.TutorialRules.Preparation(PlayerProfileClient.Inventory);
    }
    private static void Move(string id, string parent, string region)
    {
        PlayerProfileClient.OfflineStore.Move(new InventoryCommand { Operation = InventoryOperation.Move,
            ExpectedVersion = PlayerProfileClient.Inventory.Version, ItemId = id, Parent = parent, Region = region });
        PlayerProfileClient.ValidateAsync("", CancellationToken.None).GetAwaiter().GetResult();
    }
    public static string UI()
    {
        return string.Join("\n", UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
            .SelectMany(d => d.rootVisualElement.Query<Label>().ToList())
            .Where(e => e.worldBound.width > 0 && e.resolvedStyle.display != DisplayStyle.None && !string.IsNullOrEmpty(e.text))
            .Select(e => e.name + ": " + e.text).TakeLast(50));
    }
}
