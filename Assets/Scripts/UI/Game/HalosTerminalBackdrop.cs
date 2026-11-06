using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Skipping changes presentation only, never GameSettings or LoadingData.
    public sealed class HalosTerminalBackdrop : IDisposable
    {
        public const string ReducedMotionPreference = "Moonkov.AccessReducedMotion";
        private const double IntroDuration = 3.2, CycleDuration = 4.8;
        private readonly VisualElement m_Host, m_Root, m_Stage, m_Signal, m_Intro;
        private readonly TerminalWindow[] m_Windows = new TerminalWindow[10];
        private readonly IVisualElementScheduledItem m_Tick;
        private readonly bool m_Loading;
        private readonly Action m_IntroFinished;
        private bool m_Visible, m_ReducedMotion, m_Skipped, m_IntroPlayed, m_Disposed;
        private double m_Started;

        private sealed class TerminalWindow
        {
            public VisualElement Element;
            public Label Text;
            public string Content;
            public int Characters = -1;
            public float Delay;
        }

        public static bool PrefersReducedMotion => PlayerPrefs.GetInt(ReducedMotionPreference,
            OnlineSquadStage.SystemReducedMotion ? 1 : 0) != 0;

        public HalosTerminalBackdrop(VisualElement host, bool loading, Action introFinished = null)
        {
            m_Host = host ?? throw new ArgumentNullException(nameof(host));
            m_Loading = loading; m_IntroFinished = introFinished;
            m_Root = new VisualElement { name = "halosBackdrop", pickingMode = PickingMode.Ignore };
            m_Root.AddToClassList("halos-backdrop");
            AddDecoration(m_Root, "halos-noise");
            m_Stage = AddDecoration(m_Root, "halos-stage");
            m_Signal = AddDecoration(m_Root, "halos-signal");
            host.Insert(0, m_Root);
            var frames = new[]
            {
                new Rect(0, 0, 1920, 1080), new Rect(300, 800, 768, 432),
                new Rect(-400, 200, 768, 432), new Rect(400, -400, 768, 432),
                new Rect(600, 900, 768, 432), new Rect(200, 100, 768, 432),
                new Rect(-500, -300, 768, 432), new Rect(700, 500, 768, 432),
                new Rect(1200, 200, 768, 432), new Rect(1300, 1000, 768, 432)
            };
            float delay = 0;
            for (int i = 0; i < m_Windows.Length; i++)
            {
                var frame = frames[i]; var window = AddDecoration(m_Stage, "halos-terminal");
                window.AddToClassList(i == 0 ? "halos-terminal-main" : "halos-terminal-small");
                window.style.left = frame.x; window.style.top = frame.y;
                window.style.width = frame.width; window.style.height = frame.height;
                window.style.display = DisplayStyle.None;
                var text = new Label { pickingMode = PickingMode.Ignore };
                text.AddToClassList("halos-terminal-text"); text.AddToClassList("moonkov-style-font");
                window.Add(text);
                m_Windows[i] = new TerminalWindow { Element = window, Text = text, Content = TerminalText(i), Delay = delay };
                delay += Mathf.Max(.04f, (60 - i * 8) / 60f * .35f);
            }
            if (!loading)
            {
                m_Intro = new VisualElement { name = "halosIntro", focusable = true };
                m_Intro.AddToClassList("halos-intro");
                AddDecoration(m_Intro, "halos-emblem"); AddDecoration(m_Intro, "halos-wordmark");
                m_Intro.RegisterCallback<PointerDownEvent>(SkipIntroPointer);
                m_Intro.RegisterCallback<KeyDownEvent>(SkipIntroKey);
                host.Add(m_Intro); m_Intro.style.display = DisplayStyle.None;
            }
            else
            {
                host.RegisterCallback<PointerDownEvent>(SkipLoadingPointer, TrickleDown.TrickleDown);
            }
            m_Tick = m_Root.schedule.Execute(Tick).Every(33); m_Tick.Pause();
            m_Root.style.display = DisplayStyle.None;
            m_Root.RegisterCallback<GeometryChangedEvent>(Resize);
            m_Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_Root.RegisterCallback<AttachToPanelEvent>(Attached);
        }

        private static VisualElement AddDecoration(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className); parent.Add(element); return element;
        }

        private static string TerminalText(int seed)
        {
            const string letters = "1234567890qwertyuiopasdfghjklzxcvbnm";
            int[] lineLengths = { 6, 12, 18, 35, 13, 26, 3 };
            var random = new System.Random(901 + seed); var text = new StringBuilder(640);
            while (text.Length < 600)
            {
                int length = lineLengths[random.Next(lineLengths.Length)];
                for (int i = 0; i < length; i++) text.Append(letters[random.Next(letters.Length)]);
                text.Append('\n');
            }
            return text.ToString();
        }

        public void Present(bool visible, bool reducedMotion)
        {
            if (m_Disposed) return;
            bool changed = m_Visible != visible || m_ReducedMotion != reducedMotion;
            m_ReducedMotion = reducedMotion;
            m_Host.EnableInClassList("halos-reduced-motion", reducedMotion);
            if (!changed) return;
            m_Visible = visible;
            m_Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            m_Tick.Pause();
            if (!visible) { FinishIntro(); return; }
            SettleSignal();
            m_Started = TerminalMotion.Now; m_Skipped = reducedMotion;
            m_Stage.style.display = reducedMotion || (!m_Loading && m_IntroPlayed) ? DisplayStyle.None : DisplayStyle.Flex;
            if (m_Intro != null && !m_IntroPlayed && !reducedMotion)
            {
                m_Host.AddToClassList("halos-intro-active"); m_Intro.style.display = DisplayStyle.Flex;
                m_Intro.style.opacity = 0; m_Intro.Focus();
            }
            if (reducedMotion) FinishIntro();
            else if (m_Loading || !m_IntroPlayed) { Tick(); m_Tick.Resume(); }
        }

        public void Skip()
        {
            if (!m_Visible || m_Disposed) return;
            m_Skipped = true; m_Stage.style.display = DisplayStyle.None;
            SettleSignal();
            m_Tick.Pause(); FinishIntro();
        }
        private void SkipIntroPointer(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            Skip(); evt.StopImmediatePropagation();
        }
        private void SkipIntroKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape && evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
            Skip(); evt.StopImmediatePropagation();
        }
        private void SkipLoadingPointer(PointerDownEvent evt) { if (evt.button == 0) Skip(); }

        private void Tick()
        {
            if (!m_Visible || m_ReducedMotion || m_Skipped) { m_Tick?.Pause(); return; }
            double elapsed = TerminalMotion.Now - m_Started;
            if (!m_Loading && elapsed >= IntroDuration)
            {
                m_Stage.style.display = DisplayStyle.None; SettleSignal(); m_Tick.Pause(); FinishIntro(); return;
            }
            double cycle = m_Loading ? elapsed % CycleDuration : elapsed;
            for (int i = 0; i < m_Windows.Length; i++)
            {
                var window = m_Windows[i];
                float age = (float)cycle - window.Delay;
                float lifetime = i == 0 ? 2.4f : (300 - i * 20) / 60f * .4f;
                bool show = age >= 0 && age < lifetime;
                window.Element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                window.Element.style.opacity = .72f * Mathf.Min(TerminalMotion.OutCubic(age / .2f), Mathf.Clamp01((lifetime - age) / .4f));
                int characters = Mathf.Min(window.Content.Length, (int)(age * 60));
                if (window.Characters == characters) continue;
                window.Characters = characters; window.Text.text = window.Content.Substring(0, characters);
            }
            float interference = Mathf.Sin((float)elapsed * 4.1f);
            m_Signal.style.opacity = .18f + .08f * interference;
            m_Signal.style.translate = new Translate(interference * 7, Mathf.Sin((float)elapsed * 2.7f) * 2);
            if (m_Intro != null) m_Intro.style.opacity = Mathf.Min(TerminalMotion.OutCubic(Mathf.Clamp01(((float)elapsed - 1.5f) / .45f)), Mathf.Clamp01((float)(IntroDuration - elapsed) / .2f));
        }

        private void SettleSignal()
        {
            m_Signal.style.opacity = .12f; m_Signal.style.translate = new Translate(0, 0);
        }

        private void FinishIntro()
        {
            if (m_Intro == null) return;
            bool wasActive = m_Host.ClassListContains("halos-intro-active");
            m_Host.RemoveFromClassList("halos-intro-active"); m_Intro.style.display = DisplayStyle.None;
            if (wasActive || m_ReducedMotion) m_IntroPlayed = true;
            if (wasActive && m_Visible) m_IntroFinished?.Invoke();
        }
        private void Resize(GeometryChangedEvent evt)
        {
            float scale = Mathf.Max(evt.newRect.width / 1920f, evt.newRect.height / 1080f);
            m_Stage.style.left = (evt.newRect.width - 1920) * .5f; m_Stage.style.top = (evt.newRect.height - 1080) * .5f;
            m_Stage.style.scale = new Scale(Vector3.one * scale);
        }
        private void Detached(DetachFromPanelEvent evt) => m_Tick.Pause();
        private void Attached(AttachToPanelEvent evt)
        {
            if (m_Visible && !m_ReducedMotion && !m_Skipped && (m_Loading || !m_IntroPlayed)) m_Tick.Resume();
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Visible = false; m_Tick.Pause(); FinishIntro();
            m_Root.UnregisterCallback<GeometryChangedEvent>(Resize);
            m_Root.UnregisterCallback<DetachFromPanelEvent>(Detached);
            m_Root.UnregisterCallback<AttachToPanelEvent>(Attached);
            if (m_Loading) m_Host.UnregisterCallback<PointerDownEvent>(SkipLoadingPointer, TrickleDown.TrickleDown);
            if (m_Intro != null)
            {
                m_Intro.UnregisterCallback<PointerDownEvent>(SkipIntroPointer); m_Intro.UnregisterCallback<KeyDownEvent>(SkipIntroKey);
                m_Intro.RemoveFromHierarchy();
            }
            m_Root.RemoveFromHierarchy();
        }
    }
}
