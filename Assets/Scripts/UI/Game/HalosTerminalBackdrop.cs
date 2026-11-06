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
        // Four accelerating subdivisions accumulate a wall of windows before the release.
        private const int SourceRate = 60, BuildCompleteFrame = 264, ImpactFrame = 282, ClearFrame = 294;
        private const int ShutterFrame = 330, ShutterFrames = 42, RevealFrame = 372, RevealFrames = 42;
        private const int IntroFrames = 432, LoadingLoopFrames = 360, LiveTextWindows = 12;
        private readonly VisualElement m_Host, m_Root, m_Stage, m_Noise, m_Signal, m_Impact, m_Intro, m_Content;
        private readonly VisualElement m_ShutterTop, m_ShutterBottom, m_LightTop, m_LightBottom;
        private readonly TerminalWindow[] m_Windows = new TerminalWindow[33];
        private readonly IVisualElementScheduledItem m_Tick;
        private readonly bool m_Loading;
        private readonly Action m_IntroFinished;
        private bool m_Visible, m_ReducedMotion, m_Skipped, m_IntroPlayed, m_Disposed;
        private double m_Elapsed, m_IntroElapsed, m_LastTick = double.NaN;
        private readonly System.Random m_SignalRandom = new System.Random(1701);
        private int m_SignalFrame = -1, m_NextGlitchFrame, m_GlitchEndFrame;

        // Authentication may finish first; presentation still owns the opening sequence.
        public bool IntroPending => !m_Loading && !m_IntroPlayed && !m_ReducedMotion && !m_Disposed;

        private sealed class TerminalWindow
        {
            public VisualElement Element;
            public Label Text;
            public string Content;
            public readonly char[] Scratch = new char[420];
            public int TextStep = -1;
            public int Delay;
        }

        public static bool PrefersReducedMotion => PlayerPrefs.GetInt(ReducedMotionPreference,
            OnlineSquadStage.SystemReducedMotion ? 1 : 0) != 0;

        public HalosTerminalBackdrop(VisualElement host, bool loading, Action introFinished = null)
        {
            m_Host = host ?? throw new ArgumentNullException(nameof(host));
            m_Loading = loading; m_IntroFinished = introFinished;
            m_Root = new VisualElement { name = "halosBackdrop", pickingMode = PickingMode.Ignore };
            m_Root.AddToClassList("halos-backdrop");
            m_Noise = AddDecoration(m_Root, "halos-noise");
            m_Signal = AddDecoration(m_Root, "halos-signal");
            if (!loading)
            {
                m_Content = host.Q("Container");
                m_ShutterTop = AddDecoration(m_Root, "halos-shutter"); m_ShutterTop.AddToClassList("halos-shutter-top");
                m_ShutterBottom = AddDecoration(m_Root, "halos-shutter"); m_ShutterBottom.AddToClassList("halos-shutter-bottom");
                m_LightTop = AddDecoration(m_ShutterTop, "halos-shutter-light");
                m_LightBottom = AddDecoration(m_ShutterBottom, "halos-shutter-light");
            }
            m_Stage = AddDecoration(m_Root, "halos-stage");
            m_Impact = AddDecoration(m_Root, "halos-impact");
            host.Insert(0, m_Root);
            int delay = 0;
            for (int i = 0; i < m_Windows.Length; i++)
            {
                if (i > 0) delay += i <= 4 ? 24 : i <= 12 ? 12 : i <= 20 ? 6 : 2;
                var frame = TerminalFrame(i); var window = AddDecoration(m_Stage, "halos-terminal");
                window.name = "halosTerminal" + i;
                window.AddToClassList(i == 0 ? "halos-terminal-main" : "halos-terminal-small");
                window.style.left = frame.x; window.style.top = frame.y;
                window.style.width = frame.width; window.style.height = frame.height;
                window.style.display = DisplayStyle.None;
                var text = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                text.AddToClassList("halos-terminal-text"); text.AddToClassList("moonkov-style-font");
                window.Add(text);
                m_Windows[i] = new TerminalWindow { Element = window, Text = text, Content = TerminalText(i),
                    Delay = delay };
            }
            if (!loading)
            {
                m_Intro = new VisualElement { name = "halosIntro", focusable = true };
                m_Intro.AddToClassList("halos-intro");
                m_Intro.RegisterCallback<PointerDownEvent>(SkipIntroPointer);
                m_Intro.RegisterCallback<KeyDownEvent>(SkipIntroKey);
                host.Add(m_Intro); m_Intro.style.display = DisplayStyle.None;
            }
            else
            {
                host.RegisterCallback<PointerDownEvent>(SkipLoadingPointer, TrickleDown.TrickleDown);
            }
            m_Tick = m_Root.schedule.Execute(Tick).Every(16); m_Tick.Pause();
            m_Root.style.display = DisplayStyle.None;
            m_Root.RegisterCallback<GeometryChangedEvent>(Resize);
            m_Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_Root.RegisterCallback<AttachToPanelEvent>(Attached);
        }

        private static Rect TerminalFrame(int index)
        {
            if (index == 0) return new Rect(0, 0, 1920, 1080);
            int group = (index - 1) % 4, layer = (index - 1) / 4;
            var frame = group switch
            {
                0 => new Rect(70, 80, 680, 382),
                1 => new Rect(1100, 60, 640, 360),
                2 => new Rect(120, 500, 620, 340),
                _ => new Rect(1110, 510, 620, 340)
            };
            // Later windows sit below earlier title bars, so accumulation stays visible.
            frame.x += layer * (group % 2 == 0 ? 22 : -22);
            frame.y += layer * 24;
            frame.width += layer * 6; frame.height += layer * 3;
            return frame;
        }

        private static VisualElement AddDecoration(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className); parent.Add(element); return element;
        }

        private static string TerminalText(int seed)
        {
            const string letters = "0123456789ABCDEF0123456789abcdef[]{}<>/_:=+-#$";
            int[] lineLengths = { 6, 12, 18, 35, 13, 26, 3 };
            var random = new System.Random(901 + seed); var text = new StringBuilder(1024);
            int line = 0, target = lineLengths[random.Next(lineLengths.Length)];
            while (text.Length < 1024)
            {
                text.Append(letters[random.Next(letters.Length)]);
                if (++line == target) { text.Append('\n'); line = 0; target = lineLengths[random.Next(lineLengths.Length)]; }
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
            m_LastTick = double.NaN;
            if (!visible) { HideIntro(); return; }
            SettleSignal();
            if (m_Loading) m_Elapsed = 0;
            m_Skipped = false;
            m_Stage.style.display = !reducedMotion && (m_Loading || IntroPending) ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_Intro != null && !m_IntroPlayed && !reducedMotion)
            {
                m_Host.AddToClassList("halos-intro-active"); m_Intro.style.display = DisplayStyle.Flex;
                PresentIntro((int)(m_IntroElapsed * SourceRate)); m_Intro.Focus();
            }
            if (reducedMotion) FinishIntro();
            else m_Tick.Resume();
        }

        public void Skip()
        {
            if (!m_Visible || m_Disposed) return;
            if (m_Loading)
            {
                m_Skipped = true; m_Stage.style.display = DisplayStyle.None;
                SettleSignal(); m_Tick.Pause();
            }
            else FinishIntro();
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
            if (!IsOnScreen()) { m_LastTick = double.NaN; return; }
            double now = TerminalMotion.Now;
            // Hidden panels and a blocked startup frame must not consume the whole intro.
            double step = double.IsNaN(m_LastTick) ? 0 : Math.Max(0, Math.Min(.1, now - m_LastTick));
            m_LastTick = now;
            m_Elapsed += step;
            if (IntroPending)
            {
                m_IntroElapsed += step;
                if (m_IntroElapsed * SourceRate >= IntroFrames)
                {
                    FinishIntro();
                    if (!m_Visible || m_Disposed) return;
                }
            }
            int frame = m_Loading ? (int)(m_Elapsed * SourceRate) % LoadingLoopFrames : (int)(m_IntroElapsed * SourceRate);
            if (m_Loading || IntroPending) PresentTerminals(frame);
            if (IntroPending) PresentIntro(frame);
            PresentSignal((int)(m_Elapsed * SourceRate));
        }

        private void PresentTerminals(int frame)
        {
            int newest = 0;
            while (newest + 1 < m_Windows.Length && frame >= m_Windows[newest + 1].Delay) newest++;
            float intensity = Mathf.Clamp01(frame / (float)BuildCompleteFrame);
            int beatFrames = frame < 96 ? 24 : frame < 192 ? 12 : frame < 240 ? 6 : 2;
            bool onBeat = frame < ClearFrame && frame % beatFrames < 2;
            m_Stage.style.translate = new Translate(onBeat ? ((frame / beatFrames) % 2 == 0 ? -1 : 1) * intensity * intensity * 6 : 0, 0);
            float release = frame < ImpactFrame ? 1 : frame < ImpactFrame + 3 ? .35f :
                frame < ImpactFrame + 6 ? 1 : Mathf.Clamp01((ClearFrame - frame) / 6f);
            m_Impact.style.opacity = frame >= ImpactFrame && frame < ClearFrame ? .1f * (ClearFrame - frame) / (ClearFrame - ImpactFrame) :
                onBeat ? .035f * intensity * intensity : 0;
            for (int i = 0; i < m_Windows.Length; i++)
            {
                var window = m_Windows[i];
                int age = frame - window.Delay;
                bool show = age >= 0 && frame < ClearFrame;
                window.Element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                int textStep = age / 4; // Chunked text updates at 15 Hz, independent of the 60 Hz reveal.
                int key = textStep * 17 + i * 13;
                bool dropout = age > 6 && key % 11 == 0;
                window.Element.style.opacity = release * (dropout ? .65f : 1);
                window.Element.style.translate = new Translate(dropout ? (i % 2 == 0 ? 7 : -7) : 0, 0);
                // Covered layers retain their last text; only the newest twelve keep scrolling.
                if (i <= newest - LiveTextWindows) continue;
                if (window.TextStep == textStep) continue;
                window.TextStep = textStep;
                int end = Math.Min(window.Content.Length, 24 + textStep * (24 + (int)(intensity * 32)));
                int length = Math.Min(end, window.Scratch.Length);
                window.Content.CopyTo(end - length, window.Scratch, 0, length);
                if (key % 7 == 0 || age < 6)
                {
                    const string corruption = "#%/<>[]{}_=01";
                    for (int j = 0; j < length; j++)
                        if (window.Scratch[j] != '\n' && (j + key) % 9 == 0)
                            window.Scratch[j] = corruption[(j * 7 + key) % corruption.Length];
                }
                window.Text.text = new string(window.Scratch, 0, length);
            }
        }

        private void PresentIntro(int frame)
        {
            float opening = Mathf.Clamp01((frame - ShutterFrame) / (float)ShutterFrames);
            float height = m_Root.layout.height > 0 ? m_Root.layout.height : 1080f;
            float distance = Mathf.SmoothStep(0, 1, opening) * height * .55f;
            m_ShutterTop.style.display = m_ShutterBottom.style.display = DisplayStyle.Flex;
            m_ShutterTop.style.translate = new Translate(0, -distance);
            m_ShutterBottom.style.translate = new Translate(0, distance);
            m_LightTop.style.opacity = m_LightBottom.style.opacity = .65f * Mathf.Sin(opening * Mathf.PI);
            if (m_Content != null)
            {
                m_Content.style.visibility = frame >= RevealFrame ? Visibility.Visible : Visibility.Hidden;
                m_Content.style.opacity = Mathf.SmoothStep(0, 1, Mathf.Clamp01((frame - RevealFrame) / (float)RevealFrames));
            }
        }

        private void PresentSignal(int frame)
        {
            // Gentle brightness breathing, interrupted by brief, separated signal faults.
            if (frame / 2 == m_SignalFrame / 2 && m_SignalFrame >= 0) return;
            if (m_SignalFrame < 0) m_NextGlitchFrame = frame + 90;
            m_SignalFrame = frame;
            if (frame >= m_NextGlitchFrame)
            {
                m_GlitchEndFrame = frame + 6;
                m_NextGlitchFrame = frame + m_SignalRandom.Next(90, 181);
            }
            bool glitch = frame < m_GlitchEndFrame;
            float breath = .5f - .5f * Mathf.Cos(frame * Mathf.PI * 2 / 216f);
            int shake = glitch ? m_SignalRandom.Next(-10, 11) : 0;
            float offset = shake * m_Root.layout.width / 1920f;
            m_Signal.style.opacity = .07f + .05f * breath + (glitch ? .045f : 0);
            m_Noise.style.opacity = .035f + .018f * breath + (glitch ? .02f : 0);
            m_Signal.style.translate = new Translate(offset, 0);
            m_Noise.style.translate = new Translate(-2 * offset, 0);
        }

        private bool IsOnScreen()
        {
            if (m_Root.panel == null || !(m_Root.layout.width > 0) || !(m_Root.layout.height > 0)) return false;
            for (var element = m_Root; element != null; element = element.parent)
                if (element.resolvedStyle.display == DisplayStyle.None ||
                    element.resolvedStyle.visibility == Visibility.Hidden || element.resolvedStyle.opacity <= .001f) return false;
            return true;
        }

        private void SettleSignal()
        {
            m_Signal.style.opacity = .12f; m_Signal.style.translate = new Translate(0, 0);
            m_Noise.style.translate = new Translate(0, 0);
            m_Noise.style.opacity = .045f;
            m_Impact.style.opacity = 0; m_Stage.style.translate = new Translate(0, 0);
            m_SignalFrame = -1; m_GlitchEndFrame = 0;
        }

        private void FinishIntro()
        {
            if (m_Intro == null || m_IntroPlayed) return;
            m_IntroPlayed = true;
            m_Stage.style.display = DisplayStyle.None;
            HideIntro();
            if (m_Visible && !m_ReducedMotion) m_IntroFinished?.Invoke();
        }
        private void HideIntro()
        {
            m_Host.RemoveFromClassList("halos-intro-active");
            m_Impact.style.opacity = 0; m_Stage.style.translate = new Translate(0, 0);
            if (m_Intro != null) m_Intro.style.display = DisplayStyle.None;
            if (m_Content != null)
            {
                m_Content.style.opacity = StyleKeyword.Null;
                m_Content.style.visibility = StyleKeyword.Null;
            }
            if (m_ShutterTop != null) m_ShutterTop.style.display = m_ShutterBottom.style.display = DisplayStyle.None;
        }
        private void Resize(GeometryChangedEvent evt)
        {
            float scale = Mathf.Min(evt.newRect.width / 1920f, evt.newRect.height / 1080f);
            m_Stage.style.left = (evt.newRect.width - 1920) * .5f; m_Stage.style.top = (evt.newRect.height - 1080) * .5f;
            m_Stage.style.scale = new Scale(Vector3.one * scale);
        }
        private void Detached(DetachFromPanelEvent evt) { m_Tick.Pause(); m_LastTick = double.NaN; }
        private void Attached(AttachToPanelEvent evt)
        {
            m_LastTick = double.NaN;
            if (m_Visible && !m_ReducedMotion && !m_Skipped) m_Tick.Resume();
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Visible = false; m_Tick.Pause(); HideIntro();
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
