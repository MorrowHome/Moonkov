using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Editor-only native preview. Never enters Play or connects to an account.
public sealed class HalosMainRoomReview : EditorWindow
{
    private StashScreen m_Screen;
    private HalosMainRoom m_Isolated;
    private readonly List<string> m_Checks = new List<string>();
    private const string Output = "LocalData/HalosHomeReview/checks.txt";

    [MenuItem("Tools/Moonkov/Halos Original Room Review")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before previewing.");
        var window = GetWindow<HalosMainRoomReview>();
        window.titleContent = new GUIContent("Moonkov / Halos original room");
        window.position = new Rect(70, 70, 1280, 720); window.ShowUtility();
        window.CreatePreview();
    }
    public static void Check()
    {
        Open(); GetWindow<HalosMainRoomReview>().RunChecks();
    }
    public void CreateGUI()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) CreatePreview();
    }
    private void CreatePreview()
    {
        m_Isolated?.Dispose(); m_Isolated = null;
        m_Screen?.Dispose(); rootVisualElement.Clear();
        var host = new VisualElement(); host.style.flexGrow = 1;
        host.AddToClassList("archive-access");
        host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/AccessTheme"));
        AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/StashScreen.uxml").CloneTree(host);
        rootVisualElement.Add(host);
        host.Q("ReconnectPanel").style.display = DisplayStyle.None;
        m_Screen = new StashScreen(host, () => { }, () => { }, () => { }, preview: true);
        m_Screen.Present("PREVIEW", 2, 128, 36, 12, true, false);
    }
    private void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        m_Checks.Add("PASS " + message);
    }
    private Task Wait(int ms)
    {
        var done = new TaskCompletionSource<bool>();
        rootVisualElement.schedule.Execute(() => done.TrySetResult(true)).StartingIn(ms);
        return done.Task;
    }
    private async Task Until(Func<bool> condition)
    {
        double timeout = EditorApplication.timeSinceStartup + 5;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Room transition did not finish.");
            await Wait(40);
        }
    }
    public static void Press(Button button)
    {
        if (button == null) throw new InvalidOperationException("Missing room button.");
        using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
    }
    private async void RunChecks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output)); m_Checks.Clear();
        try
        {
            await Wait(400);
            var stash = rootVisualElement.Q("stashScreen");
            var room = stash.Q("halosOriginalHome");
            Require(room.resolvedStyle.display == DisplayStyle.Flex && room.worldBound.width > 1000, "original room occupies the real terminal home");
            Require(room.resolvedStyle.position == Position.Absolute && room.Q<Image>("halosArtRoom1").resolvedStyle.height == 1080,
                "source sprites use the original absolute coordinates, not a flex fallback");
            var viewport = room.Q("halosRoomViewport").worldBound;
            Require(Mathf.Abs(viewport.width / viewport.height - 1920f / 1080) < .001f &&
                Mathf.Abs(room.Q<Image>("halosArtRoom1").worldBound.width - viewport.width) < 1,
                "room artwork fills the viewport at its original aspect ratio");
            Require(stash.Q("terminalNav").resolvedStyle.display == DisplayStyle.None, "old navigation cards are absent on the original room");
            Require(room.Q<Image>("halosArtRoom1").image != null && room.Query<Image>().ToList().Count == 18, "all original room layers and eight monitor icons are present");
            Require(stash.Q("stashMenuActions").worldBound.width > 0, "account actions remain available");
            Press(room.Q<Button>("halosRoomPage4")); await Wait(200);
            Require(stash.ClassListContains("archive-page-4") && room.resolvedStyle.display == DisplayStyle.None, "center monitor opens the actual operation page");
            m_Screen.NavigatePage(0); await Wait(100);
            Press(room.Q<Button>("halosRoomPage1")); await Wait(200);
            Require(stash.ClassListContains("terminal-is-loadout") && stash.Q(className: "stash-body").resolvedStyle.display == DisplayStyle.Flex, "other center monitor opens the actual equipment page");
            m_Screen.NavigatePage(0);

            var host = new VisualElement(); host.style.position = Position.Absolute;
            host.style.left = host.style.right = host.style.top = host.style.bottom = 0;
            host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/HalosMainRoomStyles"));
            host.Add(new VisualElement()); rootVisualElement.Add(host);
            var routed = new List<int>();
            m_Isolated = new HalosMainRoom(host, routed.Add); m_Isolated.Show(false);
            m_Isolated.MoveRoom(-1);
            foreach (int page in new[] { 2, 3, 6 }) Press(m_Isolated.Root.Q<Button>("halosRoomPage" + page));
            m_Isolated.MoveRoom(1);
            foreach (int page in new[] { 4, 1 }) Press(m_Isolated.Root.Q<Button>("halosRoomPage" + page));
            m_Isolated.MoveRoom(1);
            foreach (int page in new[] { 5, 7, 8 }) Press(m_Isolated.Root.Q<Button>("halosRoomPage" + page));
            Require(routed.Count == 8 && new HashSet<int>(routed).Count == 8, "all eight original monitor hit areas route to distinct existing features");
            m_Isolated.Present(true); m_Isolated.Open(8);
            Require(routed.Count == 8, "busy state blocks duplicate operations"); m_Isolated.Present(false);
            m_Isolated.MoveRoom(-1);
            host.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
            if (!HalosTerminalBackdrop.PrefersReducedMotion)
            {
                m_Isolated.Show(true); await Wait(200);
                Require(m_Isolated.TabletState > 75, "original full-size tablet hold precedes its lowering");
                await Until(() => m_Isolated.TabletState == 0);
                m_Isolated.Open(4); await Wait(250);
                Require(m_Isolated.IsOpening && routed.Count == 8 && m_Isolated.TabletState > 0, "tablet rises before switching the functional page");
                m_Isolated.Show(false); await Until(() => m_Isolated.TabletState == 0);
                Require(routed.Count == 8, "return cancels the queued navigation");
                m_Isolated.Open(4); host.style.display = DisplayStyle.None; await Wait(250);
                host.style.display = DisplayStyle.Flex; await Wait(150);
                Require(!m_Isolated.IsOpening && routed.Count == 8, "hiding the menu cancels a pending page switch");
                m_Isolated.Open(4); await Until(() => routed.Count == 9); await Wait(100);
                Require(routed.Count == 9 && !m_Isolated.IsOpening, "opening completes exactly once");
            }
            m_Isolated.Dispose(); m_Isolated = null; host.RemoveFromHierarchy();
            Require(host.childCount == 1, "room disposal removes its visual tree and scheduler owner");
            m_Screen.NavigatePage(0);
            File.WriteAllLines(Output, m_Checks); Debug.Log("HALOS_ROOM_REVIEW_PASS " + m_Checks.Count);
        }
        catch (Exception error)
        {
            m_Checks.Add("FAIL " + error); File.WriteAllLines(Output, m_Checks); Debug.LogException(error);
        }
    }
    private void OnDisable() { m_Isolated?.Dispose(); m_Screen?.Dispose(); m_Isolated = null; m_Screen = null; }
}
