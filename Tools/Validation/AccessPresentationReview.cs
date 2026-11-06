using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Run outside Assets with the Editor command runner. No Play, account access or reflection.
public sealed class AccessPresentationReview : EditorWindow
{
    private const string Output = "LocalData/AccessPresentationReview/checks.txt";
    private readonly List<string> m_Checks = new List<string>();
    private HalosTerminalBackdrop m_Backdrop;

    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Review requires an idle Editor.");
        var window = CreateInstance<AccessPresentationReview>();
        window.titleContent = new GUIContent("Moonkov / Access presentation review");
        window.position = new Rect(40, 50, 1280, 750);
        window.ShowUtility();
        window.Run();
    }

    private void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        m_Checks.Add("PASS " + message);
    }

    private Task Wait(int milliseconds)
    {
        var done = new TaskCompletionSource<bool>();
        rootVisualElement.schedule.Execute(() => done.SetResult(true)).StartingIn(milliseconds);
        return done.Task;
    }

    private async void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        try
        {
            var host = new VisualElement();
            host.AddToClassList("halos-ui"); host.AddToClassList("halos-access");
            host.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/Moonkov/UI/HalosAccessStyles.uss"));
            rootVisualElement.Add(host);
            int completed = 0;
            m_Backdrop = new HalosTerminalBackdrop(host, false, () => completed++);
            m_Backdrop.Present(true, false); m_Backdrop.Present(false, false);
            Require(m_Backdrop.IntroPending && completed == 0, "hiding before presentation does not complete the intro");
            host.style.display = DisplayStyle.None;
            m_Backdrop.Present(true, false);
            await Wait(3600);
            Require(m_Backdrop.IntroPending && completed == 0, "hidden startup time does not consume the intro");
            host.style.display = DisplayStyle.Flex;
            await Wait(350);
            Require(m_Backdrop.IntroPending && host.ClassListContains("halos-intro-active"), "intro is still pending after its first visible frames");
            double deadline = EditorApplication.timeSinceStartup + 15;
            while (m_Backdrop.IntroPending && EditorApplication.timeSinceStartup < deadline) await Wait(150);
            Require(!m_Backdrop.IntroPending && completed == 1, "visible intro completes exactly once");
            var signal = host.Q(className: "halos-signal");
            float opacity = signal.style.opacity.value;
            await Wait(270);
            Require(Math.Abs(opacity - signal.style.opacity.value) > .0001f, "login background continues after the intro");
            m_Backdrop.Skip(); opacity = signal.style.opacity.value;
            await Wait(270);
            Require(completed == 1 && Math.Abs(opacity - signal.style.opacity.value) > .0001f, "skip does not stop the login background or repeat completion");
            m_Backdrop.Present(false, false); opacity = signal.style.opacity.value;
            await Wait(270);
            Require(opacity == signal.style.opacity.value, "hidden background stops updating");
            m_Backdrop.Present(true, true);
            Require(host.Q(className: "halos-stage").style.display == DisplayStyle.None, "reduced motion disables decorative windows");
            m_Backdrop.Dispose();
            Require(host.Q("halosBackdrop") == null && host.Q("halosIntro") == null, "dispose removes the animation elements");

            m_Backdrop = new HalosTerminalBackdrop(host, false, () => { completed++; m_Backdrop.Present(false, false); });
            m_Backdrop.Present(true, false); m_Backdrop.Skip();
            Require(completed == 2 && !m_Backdrop.IntroPending && host.Q("halosBackdrop").style.display == DisplayStyle.None,
                "skip can immediately hand off to the authenticated menu");
            m_Backdrop.Dispose(); rootVisualElement.Clear();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/Loading.uxml").CloneTree();
            tree.style.flexGrow = 1; rootVisualElement.Add(tree);
            var canvas = tree.Q("Canvas");
            tree.Q<Label>("LoadingStatus").text = "正在加载战局…";
            var progress = tree.Q<ProgressBar>(); progress.value = .42f;
            m_Backdrop = new HalosTerminalBackdrop(canvas, true); m_Backdrop.Present(true, false);
            await Wait(200);
            CheckLayout(canvas);
            position = new Rect(40, 50, 800, 630);
            await Wait(250);
            CheckLayout(canvas);
            m_Backdrop.Skip();
            Require(progress.value == .42f, "skipping decoration preserves the real progress value");
            File.WriteAllText(Output, string.Join("\n", m_Checks));
            Debug.Log("Access presentation review: " + m_Checks.Count + " checks passed.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Output, string.Join("\n", m_Checks) + "\nFAIL " + exception);
            Debug.LogException(exception);
        }
    }

    private void CheckLayout(VisualElement canvas)
    {
        var content = canvas.Q(className: "halos-loading-content").worldBound;
        var emblem = canvas.Q(className: "halos-emblem").worldBound;
        var wordmark = canvas.Q(className: "halos-wordmark").worldBound;
        var progress = canvas.Q<ProgressBar>().worldBound;
        var bounds = canvas.worldBound;
        Require(Vector2.Distance(content.center, bounds.center) < 1 && content.width > 0 &&
            content.yMin >= bounds.yMin && content.yMax <= bounds.yMax,
            "loading stack is centered and fits " + bounds.size);
        Require(emblem.height > 0 && wordmark.height > 0 && progress.height > 0 &&
            emblem.yMax <= wordmark.yMin && wordmark.yMax < progress.yMin &&
            Math.Abs(emblem.center.x - progress.center.x) < 1 && Math.Abs(wordmark.center.x - progress.center.x) < 1,
            "logo, game name and progress share a centered vertical stack");
    }

    private void OnDisable() { m_Backdrop?.Dispose(); rootVisualElement.Clear(); }
}
