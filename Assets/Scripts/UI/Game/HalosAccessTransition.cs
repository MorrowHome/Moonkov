using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Shared by online authentication and opening a single-player profile.
    // Only presentation is gated; this never changes authentication or save data.
    public sealed class HalosAccessTransition : IDisposable
    {
        private enum Phase { Waiting, Closing, Revealing, Complete }
        // loginControl holds 30 frames, closes for 15, then mainRoomControl opens
        // at 50 design pixels per frame. Keep the same 60 Hz visual cadence.
        private const float ConfirmSeconds = .5f, CloseSeconds = .25f;
        private const float CoveredSeconds = .1f, OpenSeconds = 13f / 60f, SettleSeconds = .18f;
        private readonly VisualElement m_Root, m_Shade, m_Top, m_Bottom, m_TopLight, m_BottomLight;
        private readonly Action m_Changed;
        private readonly IVisualElementScheduledItem m_Tick;
        private readonly HalosAccessAudio m_Audio;
        private Phase m_Phase;
        private float m_Age;
        private double m_LastTick = double.NaN;
        private bool m_Disposed, m_CloseSoundPlayed, m_OpenSoundPlayed;

        public bool IsRunning => m_Phase == Phase.Closing || m_Phase == Phase.Revealing;
        public bool ContentVisible => m_Phase == Phase.Revealing || m_Phase == Phase.Complete;

        public HalosAccessTransition(VisualElement host, Action changed, HalosAccessAudio audio = null)
        {
            m_Changed = changed;
            m_Audio = audio ?? new HalosAccessAudio();
            m_Root = new VisualElement { name = "halosAccessTransition", focusable = true, tabIndex = -1 };
            m_Root.AddToClassList("halos-access-transition");
            m_Shade = Decoration(m_Root, "halos-transition-shade");
            m_Top = Decoration(m_Root, "halos-shutter", "halos-shutter-top");
            m_Bottom = Decoration(m_Root, "halos-shutter", "halos-shutter-bottom");
            m_TopLight = Decoration(m_Top, "halos-shutter-light");
            m_BottomLight = Decoration(m_Bottom, "halos-shutter-light");
            m_Root.style.display = DisplayStyle.None;
            host.Add(m_Root);
            m_Root.RegisterCallback<KeyDownEvent>(BlockKey);
            m_Root.RegisterCallback<NavigationMoveEvent>(BlockNavigation);
            m_Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_Root.RegisterCallback<AttachToPanelEvent>(Attached);
            m_Tick = m_Root.schedule.Execute(Tick).Every(16); m_Tick.Pause();
        }

        private static VisualElement Decoration(VisualElement parent, string style, string extra = null)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(style);
            if (extra != null) element.AddToClassList(extra);
            parent.Add(element); return element;
        }

        // The destination can be shown only after the shutters fully cover it.
        // The opening intro and menu readiness remain independent prerequisites.
        public bool Present(bool authenticated, bool introFinished, bool visible, bool reducedMotion)
        {
            if (m_Disposed) return false;
            if (!authenticated)
            {
                m_Phase = Phase.Waiting; m_Age = 0;
                m_CloseSoundPlayed = m_OpenSoundPlayed = false; Hide(); return false;
            }
            if (reducedMotion)
            {
                m_Phase = Phase.Complete; Hide(); return true;
            }
            if (m_Phase == Phase.Complete) { if (!visible) m_Audio.Suspend(); return true; }
            if (!introFinished || !visible) { Hide(); return ContentVisible; }
            if (m_Phase == Phase.Waiting) { m_Phase = Phase.Closing; m_Age = 0; }
            bool showing = m_Root.style.display == DisplayStyle.Flex;
            m_Root.style.display = DisplayStyle.Flex; m_Root.BringToFront();
            Paint();
            if (!showing) { m_LastTick = double.NaN; m_Root.Focus(); }
            m_Tick.Resume();
            return ContentVisible;
        }

        private void Tick()
        {
            if (!IsRunning || m_Disposed) { m_Tick.Pause(); return; }
            if (!OnScreen()) { m_Audio.Suspend(); m_LastTick = double.NaN; return; }
            double now = TerminalMotion.Now;
            // Preparing the home model can take a frame. Never skip the reveal
            // because its creation or an unfocused/hidden window stalled a tick.
            m_Age += double.IsNaN(m_LastTick) ? 0 : (float)Math.Max(0, Math.Min(.05, now - m_LastTick));
            m_LastTick = now;
            if (m_Phase == Phase.Closing && m_Age >= ConfirmSeconds + CloseSeconds)
            {
                m_Audio.StopShutter();
                m_Phase = Phase.Revealing; m_Age = 0; Paint();
                m_Changed?.Invoke();
                m_LastTick = double.NaN;
                return;
            }
            if (m_Phase == Phase.Revealing && m_Age >= CoveredSeconds + OpenSeconds + SettleSeconds)
            {
                m_Phase = Phase.Complete; Hide(true); m_Changed?.Invoke(); return;
            }
            Paint();
            // The rise begins with the dimming/confirmation and peaks at full closure.
            if (m_Phase == Phase.Closing && !m_CloseSoundPlayed)
            { m_CloseSoundPlayed = true; m_Audio.Shutter(true); }
            if (m_Phase == Phase.Revealing && m_Age > CoveredSeconds && !m_OpenSoundPlayed)
            { m_OpenSoundPlayed = true; m_Audio.Shutter(false); }
        }

        private void Paint()
        {
            float opening = m_Phase == Phase.Closing
                ? 1 - Mathf.Clamp01((m_Age - ConfirmSeconds) / CloseSeconds)
                : Mathf.Clamp01((m_Age - CoveredSeconds) / OpenSeconds);
            float height = m_Root.layout.height > 0 ? m_Root.layout.height : 1080;
            float distance = opening * 650 * height / 1080;
            m_Top.style.translate = new Translate(0, -distance);
            m_Bottom.style.translate = new Translate(0, distance);
            float light = m_Phase == Phase.Closing
                ? Mathf.Clamp01((m_Age - ConfirmSeconds) * 60 / 4)
                : 1 - TerminalMotion.OutCubic((m_Age - CoveredSeconds - OpenSeconds) / SettleSeconds);
            m_TopLight.style.opacity = m_BottomLight.style.opacity = light;
            m_Shade.style.opacity = .38f * (m_Phase == Phase.Closing
                ? TerminalMotion.OutCubic(m_Age / ConfirmSeconds)
                : 1 - TerminalMotion.OutCubic((m_Age - CoveredSeconds) / (OpenSeconds + SettleSeconds)));
        }

        private bool OnScreen()
        {
            if (m_Root.panel == null || !(m_Root.layout.width > 0) || !(m_Root.layout.height > 0)) return false;
            for (var element = m_Root; element != null; element = element.parent)
                if (element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility == Visibility.Hidden ||
                    element.resolvedStyle.opacity <= .001f) return false;
            return true;
        }

        private void Hide(bool preserveImpactTail = false)
        {
            if (!preserveImpactTail) m_Audio.Suspend();
            m_Tick.Pause(); m_LastTick = double.NaN; m_Root.style.display = DisplayStyle.None;
        }
        private static void BlockKey(KeyDownEvent evt) { evt.StopImmediatePropagation(); evt.PreventDefault(); }
        private static void BlockNavigation(NavigationMoveEvent evt) { evt.StopImmediatePropagation(); evt.PreventDefault(); }
        private void Detached(DetachFromPanelEvent evt) { m_Audio.Suspend(); m_Tick.Pause(); m_LastTick = double.NaN; }
        private void Attached(AttachToPanelEvent evt)
        {
            m_LastTick = double.NaN;
            if (IsRunning && m_Root.style.display == DisplayStyle.Flex) m_Tick.Resume();
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Phase = Phase.Waiting; Hide();
            m_Audio.Dispose();
            m_Root.UnregisterCallback<KeyDownEvent>(BlockKey);
            m_Root.UnregisterCallback<NavigationMoveEvent>(BlockNavigation);
            m_Root.UnregisterCallback<DetachFromPanelEvent>(Detached);
            m_Root.UnregisterCallback<AttachToPanelEvent>(Attached);
            m_Root.RemoveFromHierarchy();
        }
    }
}
