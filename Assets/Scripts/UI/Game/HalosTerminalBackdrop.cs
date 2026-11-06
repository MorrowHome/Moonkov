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
        // Halos runs at 60 Hz. Only its initial 260-frame black wait is omitted.
        // Last window: frame 257; shutter: 493; login reveal: 506; settled: 523.
        private const int SourceRate = 60, ShutterFrame = 493, RevealFrame = 506, IntroFrames = 523;
        private readonly VisualElement m_Host, m_Root, m_Stage, m_Noise, m_Signal, m_Intro, m_Content;
        private readonly VisualElement m_ShutterTop, m_ShutterBottom, m_LightTop, m_LightBottom;
        private readonly TerminalWindow[] m_Windows = new TerminalWindow[10];
        private readonly IVisualElementScheduledItem m_Tick;
        private readonly bool m_Loading;
        private readonly Action m_IntroFinished;
        private bool m_Visible, m_ReducedMotion, m_Skipped, m_IntroPlayed, m_Disposed;
        private double m_Elapsed, m_IntroElapsed, m_LastTick = double.NaN;
        private readonly System.Random m_SignalRandom = new System.Random(1701);
        private int m_SignalFrame = -1, m_FlickerRemaining = 30;
        private float m_SignalLevel = 1;

        // Authentication may finish first; presentation still owns the opening sequence.
        public bool IntroPending => !m_Loading && !m_IntroPlayed && !m_ReducedMotion && !m_Disposed;

        private sealed class TerminalWindow
        {
            public VisualElement Element;
            public Label Text;
            public string Content;
            public int[] TextEnds;
            public int Characters = -1;
            public int Delay, Lifetime;
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
            host.Insert(0, m_Root);
            var frames = new[]
            {
                new Rect(0, 0, 1920, 1080), new Rect(300, 800, 768, 432),
                new Rect(-400, 200, 768, 432), new Rect(400, -400, 768, 432),
                new Rect(600, 900, 768, 432), new Rect(200, 100, 768, 432),
                new Rect(-500, -300, 768, 432), new Rect(700, 500, 768, 432),
                new Rect(1200, 200, 768, 432), new Rect(1300, 1000, 768, 432)
            };
            int delay = 0;
            for (int i = 0; i < m_Windows.Length; i++)
            {
                var frame = frames[i]; var window = AddDecoration(m_Stage, "halos-terminal");
                window.name = "halosTerminal" + i;
                window.AddToClassList(i == 0 ? "halos-terminal-main" : "halos-terminal-small");
                window.style.left = frame.x; window.style.top = frame.y;
                window.style.width = frame.width; window.style.height = frame.height;
                window.style.display = DisplayStyle.None;
                var text = new Label { pickingMode = PickingMode.Ignore };
                text.AddToClassList("halos-terminal-text"); text.AddToClassList("moonkov-style-font");
                window.Add(text);
                string content = TerminalText(i, out var textEnds);
                m_Windows[i] = new TerminalWindow { Element = window, Text = text, Content = content, TextEnds = textEnds,
                    Delay = delay, Lifetime = i == 0 ? 350 : 300 - i * 20 };
                delay += Math.Max(1, 60 - i * 8);
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

        private static VisualElement AddDecoration(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className); parent.Add(element); return element;
        }

        private static string TerminalText(int seed, out int[] textEnds)
        {
            const string letters = "1234567890qwertyuiopasdfghjklzxcvbnm";
            int[] lineLengths = { 6, 12, 18, 35, 13, 26, 3 };
            var random = new System.Random(901 + seed); var text = new StringBuilder(512);
            textEnds = new int[365];
            int line = 0, target = lineLengths[random.Next(lineLengths.Length)];
            for (int frame = 1; frame < textEnds.Length; frame++)
            {
                text.Append(letters[random.Next(letters.Length)]);
                if (++line == target) { text.Append('\n'); line = 0; target = lineLengths[random.Next(lineLengths.Length)]; }
                textEnds[frame] = text.Length;
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
            int frame = (int)((m_Loading ? m_Elapsed : m_IntroElapsed) * SourceRate);
            if (m_Loading) frame %= IntroFrames;
            if (m_Loading || IntroPending) PresentTerminals(frame);
            if (IntroPending) PresentIntro(frame);
            PresentSignal((int)(m_Elapsed * SourceRate));
        }

        private void PresentTerminals(int frame)
        {
            for (int i = 0; i < m_Windows.Length; i++)
            {
                var window = m_Windows[i];
                int age = frame - window.Delay;
                float opacity = (float)Math.Pow(5.0 / 6, Math.Max(0, age - window.Lifetime + 1));
                bool show = age >= 0 && opacity >= .1f;
                window.Element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                // Original windows pop in, hold, then decay by one sixth per source frame.
                window.Element.style.opacity = opacity;
                int characters = window.TextEnds[Math.Min(age + 1, window.TextEnds.Length - 1)];
                if (window.Characters == characters) continue;
                window.Characters = characters; window.Text.text = window.Content.Substring(0, characters);
            }
        }

        private void PresentIntro(int frame)
        {
            int opening = Math.Clamp(frame - ShutterFrame + 1, 0, 14);
            float height = m_Root.layout.height > 0 ? m_Root.layout.height : 1080f;
            float distance = opening * 50f * height / 1080f;
            m_ShutterTop.style.display = m_ShutterBottom.style.display = DisplayStyle.Flex;
            m_ShutterTop.style.translate = new Translate(0, -distance);
            m_ShutterBottom.style.translate = new Translate(0, distance);
            m_LightTop.style.opacity = m_LightBottom.style.opacity = 1 - (float)Math.Pow(.75, opening);
            if (m_Content != null)
            {
                m_Content.style.visibility = frame >= RevealFrame ? Visibility.Visible : Visibility.Hidden;
                m_Content.style.opacity = 1 - (float)Math.Pow(5.0 / 6, Math.Max(0, frame - RevealFrame + 1));
            }
        }

        private void PresentSignal(int frame)
        {
            // Two opposed noise layers step on alternate source frames; no sinusoidal drift.
            if (frame / 2 == m_SignalFrame / 2 && m_SignalFrame >= 0) return;
            m_FlickerRemaining -= m_SignalFrame < 0 ? 0 : frame - m_SignalFrame;
            m_SignalFrame = frame;
            if (m_FlickerRemaining < 0)
            {
                m_SignalLevel = .6f + (float)m_SignalRandom.NextDouble() * .3f;
                m_FlickerRemaining = m_SignalRandom.Next(15, 31);
            }
            int shake = m_SignalRandom.Next(m_FlickerRemaining < 10 ? -20 : -5, m_FlickerRemaining < 10 ? 21 : 6);
            float offset = shake * m_Root.layout.width / 1920f;
            m_Signal.style.opacity = .36f * (m_SignalLevel - .5f);
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
            m_SignalFrame = -1; m_FlickerRemaining = 30; m_SignalLevel = 1;
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
            float scale = Mathf.Max(evt.newRect.width / 1920f, evt.newRect.height / 1080f);
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
