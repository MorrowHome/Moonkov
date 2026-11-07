using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Open(); var window = GetWindow<HalosMainRoomReview>();
        window.rootVisualElement.RemoveFromClassList(TerminalMotion.LiveEditorPreviewClass); window.RunChecks();
    }
    [MenuItem("Tools/Moonkov/Check Halos Subject Previews")]
    public static void CheckSubjects()
    {
        Open(); var window = GetWindow<HalosMainRoomReview>();
        window.rootVisualElement.RemoveFromClassList(TerminalMotion.LiveEditorPreviewClass); window.RunSubjectChecks();
    }
    [MenuItem("Tools/Moonkov/Check Halos Room Sliding")]
    public static void CheckSliding()
    {
        Open(); var window = GetWindow<HalosMainRoomReview>();
        window.rootVisualElement.RemoveFromClassList(TerminalMotion.LiveEditorPreviewClass); window.RunSlideChecks();
    }
    [MenuItem("Tools/Moonkov/Check Halos Mouse Parallax")]
    public static void CheckParallax()
    {
        Open(); var window = GetWindow<HalosMainRoomReview>();
        window.rootVisualElement.RemoveFromClassList(TerminalMotion.LiveEditorPreviewClass); window.RunParallaxChecks();
    }
    public void CreateGUI()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) CreatePreview();
    }
    private void CreatePreview()
    {
        m_Isolated?.Dispose(); m_Isolated = null;
        m_Screen?.Dispose(); rootVisualElement.Clear();
        rootVisualElement.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
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
            Require(room.Q<Image>("halosArtRoom1").image != null && room.Query<Image>().ToList().Count == 17, "all original room layers, eight monitor icons and home portrait are present");
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
    private async void RunSubjectChecks()
    {
        const string output = "LocalData/HalosHomeReview/subject-checks.txt";
        VisualElement host = null; m_Checks.Clear();
        try
        {
            m_Screen.NavigatePage(7); await Wait(150);
            var previous = new HashSet<Camera>(Resources.FindObjectsOfTypeAll<Camera>());
            Camera CameraFor(string name) => Resources.FindObjectsOfTypeAll<Camera>().Single(c => c.name == name && !previous.Contains(c));
            host = new VisualElement(); host.style.position = Position.Absolute;
            host.style.left = host.style.right = host.style.top = host.style.bottom = 0;
            host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/HalosMainRoomStyles"));
            host.Add(new VisualElement()); rootVisualElement.Add(host);
            m_Isolated = new HalosMainRoom(host, _ => { }); m_Isolated.Show(false);
            var root = m_Isolated.Root;
            var home = root.Q<Image>("halosRoomPortrait");
            await Until(() => home.image != null);
            var homeCamera = CameraFor("Menu portrait camera");
            var homeScene = homeCamera.scene;
            var model = homeScene.GetRootGameObjects().Single().GetComponentInChildren<Animator>().transform;
            Require(model.forward.z < -.7f && model.forward.x > .3f, "home actor faces away and left toward the Moon");
            Require(home.worldBound.center.x > root.worldBound.center.x && home.worldBound.height > root.worldBound.height,
                "home actor uses the enlarged right foreground framing");
            var hips = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            var hipPoint = homeCamera.WorldToViewportPoint(hips.position);
            float hipY = home.LocalToWorld(new Vector2(hipPoint.x * home.contentRect.width, (1 - hipPoint.y) * home.contentRect.height)).y;
            Require(hipY >= root.worldBound.yMax, "lower body is cropped below the screen, not standing on the desk");
            float modelTop = model.GetComponentsInChildren<SkinnedMeshRenderer>().Max(r => r.bounds.max.y);
            Require(homeCamera.WorldToViewportPoint(new Vector3(0, modelTop, 0)).y < 1,
                "upper-body framing keeps the head inside the render target");
            Require(((RenderTexture)home.image).height > 1024 && ((RenderTexture)home.image).height <= 1536,
                "home render target uses high resolution within 1536 pixels");
            Require(root.panel.Pick(root.Q<Button>("halosRoomPage4").worldBound.center) == root.Q<Button>("halosRoomPage4"),
                "actor and moon hit areas preserve the original monitor controls");
            Press(root.Q<Button>("halosRoomInspectCharacter"));
            await Until(() => root.Q<Image>("halosSubjectArt")?.image != null);
            Require(m_Isolated.IsPreviewOpen && home.image == null && !homeScene.isLoaded,
                "character click opens its own view and releases the home stage");
            var art = root.Q<Image>("halosSubjectArt");
            var camera = CameraFor("Menu portrait camera"); var scene = camera.scene;
            model = scene.GetRootGameObjects().Single().GetComponentInChildren<Animator>().transform;
            var rotation = model.rotation; float fov = camera.fieldOfView;
            var grabbed = Quaternion.Inverse(rotation) * -camera.transform.forward;
            Drag(art); await Wait(100);
            Require(Vector3.Dot(model.rotation * grabbed, camera.transform.right) > .05f &&
                !art.HasPointerCapture(PointerId.mousePointerId), "rightward character drag follows the mouse and releases capture");
            Wheel(art); await Wait(100);
            Require(camera.fieldOfView < fov, "character scroll zooms the rendered camera");
            Press(root.Q<Button>("halosSubjectReset")); await Wait(100);
            Require(Mathf.Abs(camera.fieldOfView - fov) < .01f && Quaternion.Angle(rotation, model.rotation) < .1f,
                "character reset restores orientation and zoom");
            m_Isolated.MoveRoom(1);
            Require(m_Isolated.Room == 1, "preview blocks room navigation");
            using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                root.Q<Button>("halosSubjectBack").SendEvent(key);
            await Until(() => home.image != null);
            Require(!m_Isolated.IsPreviewOpen && !scene.isLoaded && m_Isolated.Room == 1, "Escape returns to the same room and releases the preview");
            Press(root.Q<Button>("halosRoomInspectMoon"));
            await Until(() => root.Q<Image>("halosSubjectArt")?.image != null);
            art = root.Q<Image>("halosSubjectArt"); camera = CameraFor("Lunar display camera"); scene = camera.scene;
            var moon = scene.GetRootGameObjects().Single().GetComponentInChildren<MeshFilter>().transform;
            rotation = moon.rotation; float size = camera.orthographicSize;
            grabbed = Quaternion.Inverse(rotation) * -camera.transform.forward;
            Drag(art); Wheel(art); await Wait(100);
            Require(Vector3.Dot(moon.rotation * grabbed, camera.transform.right) > .05f && camera.orthographicSize < size,
                "rightward Moon drag follows the mouse and scroll zooms the real model");
            Press(root.Q<Button>("halosSubjectReset")); await Wait(100);
            Require(Quaternion.Angle(rotation, moon.rotation) < .1f && Mathf.Abs(size - camera.orthographicSize) < .01f,
                "Moon reset restores its initial view");
            Press(root.Q<Button>("halosSubjectBack")); await Until(() => home.image != null);
            Require(!scene.isLoaded && !m_Isolated.IsPreviewOpen, "back button releases the lunar stage");
            m_Isolated.Present(true); m_Isolated.OpenMoonPreview();
            Require(!m_Isolated.IsPreviewOpen, "busy state blocks a new preview"); m_Isolated.Present(false);
            m_Isolated.OpenCharacterPreview(); await Until(() => root.Q<Image>("halosSubjectArt")?.image != null);
            scene = CameraFor("Menu portrait camera").scene; m_Isolated.Hide();
            Require(!scene.isLoaded && home.image == null && !m_Isolated.IsPreviewOpen, "hiding while inspecting releases both stages");
            m_Isolated.Show(false); await Until(() => home.image != null);
            scene = CameraFor("Menu portrait camera").scene; m_Isolated.Dispose(); m_Isolated = null;
            Require(!scene.isLoaded, "disposal releases the reopened home stage");
            File.WriteAllLines(output, m_Checks); Debug.Log("HALOS_SUBJECT_REVIEW_PASS " + m_Checks.Count);
        }
        catch (Exception error) { m_Checks.Add("FAIL " + error); File.WriteAllLines(output, m_Checks); Debug.LogException(error); }
        finally
        {
            m_Isolated?.Dispose(); m_Isolated = null; host?.RemoveFromHierarchy(); m_Screen.NavigatePage(0);
            rootVisualElement.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
        }
    }
    private async void RunSlideChecks()
    {
        const string output = "LocalData/HalosHomeReview/slide-checks.txt";
        VisualElement host = null; m_Checks.Clear();
        try
        {
            m_Screen.NavigatePage(7); await Wait(100);
            host = new VisualElement(); host.style.position = Position.Absolute;
            host.style.left = host.style.right = host.style.top = host.style.bottom = 0;
            host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/HalosMainRoomStyles"));
            host.Add(new VisualElement()); rootVisualElement.Add(host);
            int routed = 0;
            m_Isolated = new HalosMainRoom(host, _ => routed++); m_Isolated.Show(false);
            await Until(() => m_Isolated.Root.Q<Image>("halosRoomPortrait").image != null);
            var world = m_Isolated.Root.Q("halosRoomWorld");
            float X() => world.style.translate.value.x.value;
            Require(!HalosTerminalBackdrop.PrefersReducedMotion, "animated preview uses enabled motion preference");
            host.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
            float start = X(); m_Isolated.MoveRoom(1);
            Require(m_Isolated.IsMoving && X() == start, "edge turn starts without an immediate position jump");
            await Wait(160);
            Require(m_Isolated.IsMoving && X() < start - 1 && X() > -3839, "room travels through intermediate positions");
            m_Isolated.Open(8); m_Isolated.OpenMoonPreview(); m_Isolated.MoveRoom(-1);
            Require(routed == 0 && !m_Isolated.IsPreviewOpen && m_Isolated.Room == 2, "movement blocks navigation and direction interruption");
            await Until(() => !m_Isolated.IsMoving);
            Require(Mathf.Abs(X() + 3840) < .1f, "slide finishes exactly on the next room");
            await Until(() => { m_Isolated.MoveRoom(-1); return m_Isolated.IsMoving; }); await Wait(160);
            Require(m_Isolated.IsMoving && X() > -3839 && X() < -1921, "reverse slide also moves through intermediate positions");
            m_Isolated.Hide(); m_Isolated.Show(false);
            Require(!m_Isolated.IsMoving && Mathf.Abs(X() + 1920) < .1f, "reopening settles a cancelled slide at its chosen room");
            host.RemoveFromClassList(TerminalMotion.LiveEditorPreviewClass); m_Isolated.MoveRoom(-1);
            Require(!m_Isolated.IsMoving && X() == 0, "nonanimated presentation keeps immediate navigation");
            File.WriteAllLines(output, m_Checks); Debug.Log("HALOS_SLIDE_REVIEW_PASS " + m_Checks.Count);
        }
        catch (Exception error) { m_Checks.Add("FAIL " + error); File.WriteAllLines(output, m_Checks); Debug.LogException(error); }
        finally
        {
            m_Isolated?.Dispose(); m_Isolated = null; host?.RemoveFromHierarchy(); m_Screen.NavigatePage(0);
            rootVisualElement.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
        }
    }
    private async void RunParallaxChecks()
    {
        const string output = "LocalData/HalosHomeReview/parallax-checks.txt";
        VisualElement host = null; m_Checks.Clear();
        try
        {
            m_Screen.NavigatePage(7); await Wait(100);
            host = new VisualElement(); host.style.position = Position.Absolute;
            host.style.left = host.style.right = host.style.top = host.style.bottom = 0;
            host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/HalosMainRoomStyles"));
            host.Add(new VisualElement()); rootVisualElement.Add(host);
            m_Isolated = new HalosMainRoom(host, _ => { }); m_Isolated.Show(false);
            var root = m_Isolated.Root;
            await Until(() => root.Q<Image>("halosRoomPortrait").image != null);
            var layer = root.Q("halosRoomParallax"); var viewport = root.Q("halosRoomViewport");
            var edge = root.Q("halosRoomRight").worldBound;
            float X() => layer.style.translate.value.x.value;
            void Point()
            {
                var at = viewport.worldBound.center + new Vector2(viewport.worldBound.width * .3f, -viewport.worldBound.height * .25f);
                using var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = at });
                root.SendEvent(e);
            }
            Require(!HalosTerminalBackdrop.PrefersReducedMotion, "parallax check uses enabled motion preference");
            host.AddToClassList(TerminalMotion.LiveEditorPreviewClass); Point();
            Require(X() == 0, "pointer move does not instantly snap the room");
            await Wait(100);
            Require(X() > 0 && X() < 12, "room follows with smooth damping");
            await Wait(650);
            float moon = root.Q("halosArtMoon").style.translate.value.x.value;
            float actor = root.Q("halosRoomCharacter").style.translate.value.x.value;
            Require(X() > 10 && moon < 0 && X() + moon > 0 && actor > 0, "Moon travels least and foreground actor travels most in the pointer direction");
            Require(root.Q("halosRoomRight").worldBound == edge, "edge navigation zones remain fixed");
            var hit = root.Q<Button>("halosRoomInspectCharacter");
            Require(root.panel.Pick(hit.worldBound.center) == hit, "character hit area follows its visible layer");
            var bounds = layer.worldBound; var visible = viewport.worldBound;
            Require(bounds.xMin <= visible.xMin && bounds.yMin <= visible.yMin && bounds.xMax >= visible.xMax && bounds.yMax >= visible.yMax,
                "overscan covers the viewport during diagonal motion");
            using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = root; root.SendEvent(leave); }
            await Wait(700);
            Require(Mathf.Abs(X()) < .3f, "leaving the room smoothly returns it to center");
            Point(); await Wait(200); m_Isolated.OpenMoonPreview(); m_Isolated.ClosePreview();
            Require(X() == 0, "returning from inspection clears prior mouse drift");
            Point(); await Wait(100); host.RemoveFromClassList(TerminalMotion.LiveEditorPreviewClass); await Wait(100);
            Require(X() == 0 && layer.style.scale.value.value.x == 1, "nonanimated presentation removes drift and overscan");
            File.WriteAllLines(output, m_Checks); Debug.Log("HALOS_PARALLAX_REVIEW_PASS " + m_Checks.Count);
        }
        catch (Exception error) { m_Checks.Add("FAIL " + error); File.WriteAllLines(output, m_Checks); Debug.LogException(error); }
        finally
        {
            m_Isolated?.Dispose(); m_Isolated = null; host?.RemoveFromHierarchy(); m_Screen.NavigatePage(0);
            rootVisualElement.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
        }
    }
    private static void Drag(VisualElement target)
    {
        var from = target.worldBound.center; var to = from + Vector2.right * 120;
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = from })) target.SendEvent(e);
        using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = to })) target.SendEvent(e);
        using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = to })) target.SendEvent(e);
    }
    private static void Wheel(VisualElement target)
    {
        using var e = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = target.worldBound.center, delta = new Vector2(0, -2) });
        target.SendEvent(e);
    }
    private void OnDisable() { m_Isolated?.Dispose(); m_Screen?.Dispose(); m_Isolated = null; m_Screen = null; }
}
