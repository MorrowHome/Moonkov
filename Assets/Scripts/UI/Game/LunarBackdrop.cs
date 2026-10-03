using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class LunarBackdrop : VisualElement, IDisposable
    {
        public MenuMoonView Moon { get; }
        public bool IsObserving => m_Overlay != null;
        private readonly VisualElement m_Owner;
        private readonly Button m_Inspect;
        private VisualElement m_Overlay;
        private IVisualElementScheduledItem m_Telemetry;
        private BackdropMode m_Mode;
        private bool m_Disposed;
        public BackdropMode Mode
        {
            get => m_Mode;
            set
            {
                if (value == BackdropMode.Page) CloseObserver();
                m_Mode = value; EnableInClassList("lunar-backdrop-page", value == BackdropMode.Page);
                Moon.Element.style.display = m_Inspect.style.display = value == BackdropMode.Page ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
        public LunarBackdrop(BackdropMode mode, VisualElement owner = null)
        {
            pickingMode = PickingMode.Ignore; AddToClassList("lunar-backdrop");
            m_Owner = owner ?? this;
            Moon = new MenuMoonView(OpenObserver); Add(Moon.Element);
            m_Inspect = new Button(OpenObserver) { text = "OBSERVE MOON  ↗" }; m_Inspect.AddToClassList("moon-observe-link"); Add(m_Inspect);
            // Register before the terminal's Esc/back handler so the observer closes first.
            m_Owner.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            Mode = mode;
        }
        public void OpenObserver()
        {
            if (m_Disposed || m_Overlay != null) return;
            m_Overlay = new VisualElement { focusable = true }; m_Overlay.AddToClassList("moon-observer");
            var header = new VisualElement(); header.AddToClassList("moon-observer-header"); m_Overlay.Add(header);
            var title = new Label("LUNAR OBSERVATORY"); title.AddToClassList("moon-observer-title"); header.Add(title);
            var close = new Button(CloseObserver) { text = "BACK  /  ESC" }; close.AddToClassList("moon-observer-button"); header.Add(close);
            Moon.Observing = true; Moon.Element.RemoveFromHierarchy();
            Moon.Element.RemoveFromClassList("moon-backdrop-view"); Moon.Element.AddToClassList("moon-observer-view"); m_Overlay.Add(Moon.Element);
            var controls = new VisualElement(); controls.AddToClassList("moon-observer-controls"); m_Overlay.Add(controls);
            controls.Add(new Label("DRAG TO ORBIT  /  SCROLL TO ZOOM"));
            var reset = new Button(Moon.ResetView) { text = "RESET VIEW" }; reset.AddToClassList("moon-observer-button"); controls.Add(reset);
            Button pause = null;
            pause = new Button(() => { Moon.Paused = !Moon.Paused; pause.text = Moon.Paused ? "RESUME ORBIT" : "PAUSE ORBIT"; }) { text = "PAUSE ORBIT" };
            pause.AddToClassList("moon-observer-button"); controls.Add(pause);
            var telemetry = new Label(); telemetry.AddToClassList("moon-observer-telemetry"); m_Overlay.Add(telemetry);
            m_Telemetry = telemetry.schedule.Execute(() => telemetry.text = "ORBITAL DAY " + Moon.Days.ToString("F2") + "  /  ACCELERATED TIME  /  SUNLIT " + ((1 + Vector3.Dot(Moon.SunDirection, Vector3.back)) * 50).ToString("F0") + "%").Every(500);
            m_Overlay.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            header.BringToFront();
            m_Owner.Add(m_Overlay); m_Overlay.BringToFront(); m_Overlay.Focus();
        }
        public void CloseObserver()
        {
            if (m_Overlay == null) return;
            m_Telemetry?.Pause(); m_Telemetry = null;
            Moon.Element.RemoveFromHierarchy(); Moon.Observing = false; Moon.Paused = false; Moon.ResetView();
            Moon.Element.RemoveFromClassList("moon-observer-view"); Moon.Element.AddToClassList("moon-backdrop-view"); Insert(0, Moon.Element);
            m_Overlay.RemoveFromHierarchy(); m_Overlay = null;
            m_Inspect.Focus();
        }
        private void OnKey(KeyDownEvent e)
        {
            if (m_Overlay == null || e.keyCode != KeyCode.Escape) return;
            CloseObserver(); e.StopImmediatePropagation();
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; CloseObserver(); Moon.Dispose();
            m_Owner.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }
    }
}
