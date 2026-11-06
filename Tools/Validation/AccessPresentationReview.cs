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
            var menu = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/MainMenu.uxml").CloneTree();
            menu.style.flexGrow = 1; rootVisualElement.Add(menu);
            var host = menu.Q("Canvas");
            host.AddToClassList("archive-access-account"); host.AddToClassList("halos-compact");
            host.Q("HidingBackground").style.display = DisplayStyle.None;
            menu.Q("stashScreenHost").style.display = DisplayStyle.None;
            var connection = menu.Q("ConnectionPanel"); connection.style.display = DisplayStyle.None;
            var account = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/Moonkov/UI/AccountAccess.uxml").CloneTree().Q("AccountPanel");
            connection.parent.Insert(0, account);
            account.Q("accountSecondaryActions").Insert(0, menu.Q("ProfileMode"));
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
            Require(VisibleTerminals(host) == 1 && host.Q("halosTerminal0").style.opacity.value == 1,
                "first terminal pops in at full opacity and holds alone");
            Require(host.Q("Container").resolvedStyle.visibility == Visibility.Hidden,
                "unrevealed login controls remain hidden from keyboard focus");
            await Until(() => VisibleTerminals(host) == 33);
            await Wait(50);
            Require(VisibleTerminals(host) == 33, "buildup retains all thirty-three windows at its peak");
            bool inside = true;
            for (int i = 1; i < 33; i++)
            {
                var bounds = host.Q("halosTerminal" + i).worldBound;
                inside &= bounds.width > 0 && host.worldBound.Contains(bounds.min) && host.worldBound.Contains(bounds.max);
            }
            Require(inside, "stacked windows stay inside the visible canvas instead of building offscreen");
            var terminalText = host.Q("halosTerminal0").Q<Label>();
            string retainedText = terminalText.text;
            var newestText = host.Q("halosTerminal32").Q<Label>();
            string liveText = newestText.text;
            await Wait(100);
            Require(terminalText.text == retainedText && newestText.text != liveText,
                "covered text is retained while front windows continue scrolling");
            Require(terminalText.text.Length > 200 && !terminalText.enableRichText && terminalText.text.IndexOfAny("[]{}<>/_:=+-#$".ToCharArray()) >= 0,
                "fast terminal stream includes literal corruption symbols without markup parsing");
            await Until(() => VisibleTerminals(host) == 0);
            Require(VisibleTerminals(host) == 0 && m_Backdrop.IntroPending && host.Q("Container").style.opacity.value == 0,
                "windows clear before the quiet beat and login reveal");
            await Until(() => host.Q(className: "halos-shutter-top").style.translate.value.y.value < 0);
            Require(m_Backdrop.IntroPending && host.Q("Container").style.opacity.value == 0,
                "quick shutter starts from black before the login reveal");
            await Until(() => host.Q("Container").style.opacity.value > 0);
            Require(-host.Q(className: "halos-shutter-top").style.translate.value.y.value >= host.worldBound.height * .5f,
                "shutter has fully cleared the center before the login appears");
            await Wait(60);
            Require(m_Backdrop.IntroPending && host.Q("Container").style.opacity.value < 1,
                "exponential login reveal remains visible before the irregular flicker");
            double deadline = EditorApplication.timeSinceStartup + 15;
            while (m_Backdrop.IntroPending && EditorApplication.timeSinceStartup < deadline) await Wait(150);
            Require(!m_Backdrop.IntroPending && completed == 1, "visible intro completes exactly once");
            Require(host.Q("Container").resolvedStyle.opacity >= .6f, "the actual login form is revealed without a replacement-logo jump");
            Require(await SignalMoves(host.Q("Container")), "login brightness continues the original irregular flicker");
            var signal = host.Q(className: "halos-signal");
            Require(await SignalMoves(signal), "login background continues flickering after the intro");
            var noise = host.Q(className: "halos-noise");
            var echo = host.Q(className: "halos-noise-echo");
            Require(echo.style.opacity.value > 0 && echo.style.translate.value.x.value == -2 * noise.style.translate.value.x.value,
                "two static layers move in opposite directions");
            float opacity;
            m_Backdrop.Skip(); opacity = signal.style.opacity.value;
            Require(await SignalMoves(signal) && completed == 1 && host.Q(className: "halos-stage").style.display == DisplayStyle.None,
                "skip preserves login noise without replaying terminal windows");
            await Wait(1800);
            Require(VisibleTerminals(host) == 0, "login stays quiet instead of restarting the terminal sequence");
            m_Backdrop.Present(false, false); opacity = signal.style.opacity.value;
            await Wait(270);
            Require(opacity == signal.style.opacity.value, "hidden background stops updating");
            m_Backdrop.Present(true, true);
            Require(host.Q(className: "halos-stage").style.display == DisplayStyle.None, "reduced motion disables decorative windows");
            Require(host.Q("Container").style.opacity.keyword == StyleKeyword.Null && echo.style.opacity.value == 0,
                "reduced motion restores steady login brightness and removes the extra static layer");
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

    private static int VisibleTerminals(VisualElement host)
    {
        int count = 0;
        host.Query(className: "halos-terminal").ForEach(element => { if (element.style.display == DisplayStyle.Flex) count++; });
        return count;
    }

    private async Task Until(Func<bool> predicate)
    {
        double deadline = EditorApplication.timeSinceStartup + 8;
        while (!predicate() && EditorApplication.timeSinceStartup < deadline) await Wait(50);
        Require(predicate(), "animation reaches the next observable beat");
    }

    private async Task<bool> SignalMoves(VisualElement signal)
    {
        var position = signal.style.translate.value;
        float opacity = signal.style.opacity.value;
        for (int i = 0; i < 6; i++)
        {
            await Wait(100);
            if (!signal.style.translate.value.Equals(position) || signal.style.opacity.value != opacity) return true;
        }
        return false;
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
