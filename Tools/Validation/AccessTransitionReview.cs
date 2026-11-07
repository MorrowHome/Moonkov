using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Native presentation review. Authentication is simulated; no account, save or Play state is changed.
public sealed class AccessTransitionReview : EditorWindow
{
    private const string Output = "LocalData/AccessTransitionReview";
    private readonly List<string> m_Checks = new List<string>();
    private VisualElement m_Host, m_Form, m_HomeHost;
    private HalosMainRoom m_Home;
    private HalosTerminalBackdrop m_Backdrop;
    private HalosAccessTransition m_Transition;
    private bool m_Authenticated, m_IntroFinished = true, m_Visible = true, m_Reduced;
    private int m_Changes, m_CoveredChanges;
    private bool m_ReloadLocked;
    private double m_UnlockAt;

    public static void Open() => Create(true);
    public static void Preview() => Create(false);

    private static void Create(bool runChecks)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Review requires an idle Editor.");
        foreach (var previous in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (previous.titleContent.text == "Moonkov / Access transition review") previous.Close();
        var window = CreateInstance<AccessTransitionReview>();
        window.titleContent = new GUIContent("Moonkov / Access transition review");
        window.position = new Rect(70, 70, 1280, 750); window.ShowUtility();
        EditorApplication.LockReloadAssemblies(); window.m_ReloadLocked = true;
        window.m_UnlockAt = EditorApplication.timeSinceStartup + 45;
        EditorApplication.update += window.UnlockOnTimeout;
        try { window.Initialize(runChecks); } catch { window.Close(); throw; }
    }

    private void Initialize(bool runChecks)
    {
        Directory.CreateDirectory(Output);
        rootVisualElement.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
        var menu = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/MainMenu.uxml").CloneTree();
        menu.style.flexGrow = 1; rootVisualElement.Add(menu);
        m_Host = menu.Q("Canvas");
        m_Host.Q("HidingBackground").style.display = DisplayStyle.None;
        m_Host.Q("stashScreenHost").style.display = DisplayStyle.None;
        var connection = menu.Q("ConnectionPanel"); connection.style.display = DisplayStyle.None;
        var account = Resources.Load<VisualTreeAsset>("Moonkov/UI/AccountAccess").CloneTree().Q("AccountPanel");
        connection.parent.Insert(0, account);
        account.Q("accountSecondaryActions").Insert(0, menu.Q("ProfileMode"));
        m_Form = menu.Q("Container");
        m_Host.AddToClassList("archive-login-plates");
        m_Backdrop = new HalosTerminalBackdrop(m_Host, false);
        m_Backdrop.Present(true, false); m_Backdrop.Skip();
        m_HomeHost = new VisualElement(); m_HomeHost.style.position = Position.Absolute;
        m_HomeHost.style.left = m_HomeHost.style.right = m_HomeHost.style.top = m_HomeHost.style.bottom = 0;
        m_HomeHost.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/HalosMainRoomStyles"));
        m_HomeHost.Add(new VisualElement { pickingMode = PickingMode.Ignore });
        m_Host.Add(m_HomeHost);
        m_Home = new HalosMainRoom(m_HomeHost, _ => { }); m_Home.Hide();
        m_Transition = new HalosAccessTransition(m_Host, Changed);
        account.Q<Button>("accountLogin").clicked += PreviewSuccess;
        menu.Q<Button>("ProfileMode").clicked += PreviewSuccess;
        m_Transition.Present(false, true, true, false); Present();
        if (runChecks) Run();
        else
        {
            Focus();
            rootVisualElement.schedule.Execute(() =>
            {
                File.WriteAllText(Output + "/visual-start2.txt", DateTime.UtcNow.ToString("O"));
                PreviewSuccess();
            }).StartingIn(1500);
            rootVisualElement.schedule.Execute(ReleaseReload).StartingIn(4000);
        }
    }

    private void Changed()
    {
        m_Changes++;
        if (m_Transition.ContentVisible && m_Transition.IsRunning)
        {
            var overlay = m_Host.Q("halosAccessTransition");
            Require(overlay.Q(className: "halos-shutter-top").style.translate.value.y.value == 0 &&
                overlay.Q(className: "halos-shutter-bottom").style.translate.value.y.value == 0,
                "destination becomes available only behind fully closed shutters");
            m_CoveredChanges++;
        }
        Present();
    }

    private void Present()
    {
        bool home = m_Transition.Present(m_Authenticated, m_IntroFinished, m_Visible, m_Reduced);
        bool wasHome = m_HomeHost.style.display == DisplayStyle.Flex;
        m_Host.EnableInClassList("archive-access-account", !home);
        m_Backdrop.Present(!home && m_Visible, m_Reduced);
        m_Form.style.display = home ? DisplayStyle.None : DisplayStyle.Flex;
        m_Form.SetEnabled(!m_Transition.IsRunning);
        m_HomeHost.style.display = home ? DisplayStyle.Flex : DisplayStyle.None;
        m_HomeHost.SetEnabled(!m_Transition.IsRunning);
        if (home && !wasHome) m_Home.Show(false);
        if (!home) m_Home.Hide();
        m_Home.Present(m_Transition.IsRunning);
    }

    private void PreviewSuccess()
    {
        m_Authenticated = true;
        m_Host.Q<Label>("accountMessage").text = "已就绪";
        Present();
    }

    private async void Run()
    {
        try
        {
            await Wait(120);
            var overlay = m_Host.Q("halosAccessTransition");
            var top = overlay.Q(className: "halos-shutter-top");
            Require(!m_Transition.IsRunning && m_Form.resolvedStyle.display != DisplayStyle.None,
                "pending or failed authentication leaves the login form available");
            m_Authenticated = true; m_IntroFinished = false; Present();
            await Wait(150);
            Require(!m_Transition.ContentVisible && !m_Transition.IsRunning,
                "fast automatic login still waits for the opening intro");
            m_IntroFinished = true; Present();
            Require(m_Transition.IsRunning && !m_Transition.ContentVisible && !m_Form.enabledInHierarchy,
                "login success starts confirmation and blocks duplicate input without jumping home");
            await Until(() => top.style.translate.value.y.value > -overlay.layout.height * .5f);
            Require(!m_Transition.ContentVisible && top.style.translate.value.y.value < 0,
                "shutters visibly close before the destination is shown");
            Require(overlay.panel.Pick(overlay.worldBound.center) == overlay,
                "transition intercepts pointer input over the underlying controls");
            using (var key = KeyDownEvent.GetPooled('\t', KeyCode.Tab, EventModifiers.None))
            {
                key.target = overlay; overlay.SendEvent(key);
                Require(key.isPropagationStopped, "keyboard navigation cannot escape the transition");
            }
            await Until(() => m_Transition.ContentVisible);
            Require(m_HomeHost.style.display == DisplayStyle.Flex && !m_HomeHost.enabledInHierarchy && m_CoveredChanges == 1,
                "home prepares under cover while its controls remain disabled");
            await Until(() => top.style.translate.value.y.value < -10);
            Require(m_Transition.IsRunning && top.style.translate.value.y.value > -overlay.layout.height * .6f,
                "home appears through a moving reveal instead of a hard cut");
            await Until(() => !m_Transition.IsRunning);
            Require(m_Transition.ContentVisible && m_Changes == 2 && m_HomeHost.enabledInHierarchy &&
                overlay.style.display == DisplayStyle.None, "completion releases the overlay and home input exactly once");
            Present(); await Wait(100);
            Require(m_Changes == 2 && !m_Transition.IsRunning, "inventory refresh does not replay the entry animation");

            m_Authenticated = false; Present();
            Require(!m_Transition.ContentVisible, "logout resets presentation for the next profile");
            m_Authenticated = true; Present(); await Wait(100);
            m_Authenticated = false; Present(); await Wait(850);
            Require(m_Changes == 2 && !m_Transition.ContentVisible && !m_Transition.IsRunning,
                "cancelled authentication cannot reveal home later");

            // The single-player path uses the same verified-profile presentation contract.
            m_Authenticated = true; Present();
            m_Host.style.display = DisplayStyle.None; await Wait(900);
            Require(!m_Transition.ContentVisible && m_Changes == 2, "hidden time cannot consume the single-player transition");
            m_Host.style.display = DisplayStyle.Flex;
            await Until(() => !m_Transition.IsRunning);
            Require(m_Transition.ContentVisible && m_Changes == 4 && m_CoveredChanges == 2,
                "single-player entry follows the same complete close-and-reveal sequence");

            m_Authenticated = false; Present(); m_Authenticated = true; Present();
            m_Reduced = true; Present();
            Require(m_Transition.ContentVisible && !m_Transition.IsRunning && overlay.style.display == DisplayStyle.None,
                "enabling reduced motion during entry immediately releases the destination");
            m_Authenticated = false; m_Reduced = false; Present(); m_Authenticated = true; Present();
            int changes = m_Changes; m_Transition.Dispose(); await Wait(850);
            Require(m_Host.Q("halosAccessTransition") == null && m_Changes == changes,
                "disposal removes the overlay and cancels all delayed navigation");
            File.WriteAllText(Output + "/checks.txt", string.Join("\n", m_Checks));
            // Leave a functional preview at the login screen for visual inspection.
            m_Transition = new HalosAccessTransition(m_Host, Changed); m_Authenticated = false; Present();
            Debug.Log("Access transition review: " + m_Checks.Count + " checks passed.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Output + "/checks.txt", string.Join("\n", m_Checks) + "\nFAIL " + exception);
            Debug.LogException(exception);
        }
        finally { ReleaseReload(); }
    }

    private void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        m_Checks.Add("PASS " + message);
    }
    private Task Wait(int milliseconds)
    {
        var done = new TaskCompletionSource<bool>();
        rootVisualElement.schedule.Execute(() => done.TrySetResult(true)).StartingIn(milliseconds); return done.Task;
    }
    private async Task Until(Func<bool> condition)
    {
        double deadline = EditorApplication.timeSinceStartup + 12;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Entry transition did not reach its next phase.");
            await Wait(16);
        }
    }
    private void UnlockOnTimeout() { if (EditorApplication.timeSinceStartup >= m_UnlockAt) ReleaseReload(); }
    private void ReleaseReload()
    {
        if (!m_ReloadLocked) return;
        m_ReloadLocked = false; EditorApplication.update -= UnlockOnTimeout; EditorApplication.UnlockReloadAssemblies();
    }
    private void OnDisable() { m_Transition?.Dispose(); m_Backdrop?.Dispose(); m_Home?.Dispose(); ReleaseReload(); }
}
