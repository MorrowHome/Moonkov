// 终端页面的背景元素。切页面时 MoonkovTerminal 设置模式。要看月球时再创建 MenuMoonView，关闭预览时随窗口释放。
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public sealed class LunarBackdrop : VisualElement, IDisposable {
        public MenuMoonView Moon { get; }
        public bool IsObserving => m_Overlay != null;
        private readonly VisualElement m_Owner;
        private readonly Button m_Inspect;
        private VisualElement m_Overlay;
        private IVisualElementScheduledItem m_Telemetry;
        private BackdropMode m_Mode;
        private bool m_Disposed;

        public BackdropMode Mode {
            get => m_Mode;
            set {
                // 进入普通终端页先退出观察模式，再隐藏背景月球。这样独立预览不会挡住目标页。
                if (value == BackdropMode.Page) CloseObserver();
                m_Mode = value;
                EnableInClassList("lunar-backdrop-page", value == BackdropMode.Page);
                Moon.Element.style.display = m_Inspect.style.display =
                    value == BackdropMode.Page ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public LunarBackdrop(BackdropMode mode, VisualElement owner = null) {
            pickingMode = PickingMode.Ignore;
            AddToClassList("lunar-backdrop");
            m_Owner = owner ?? this;
            Moon = new MenuMoonView(OpenObserver);
            Add(Moon.Element);
            m_Inspect = new Button(OpenObserver) { text = "OBSERVE MOON  ↗" };
            m_Inspect.AddToClassList("moon-observe-link");
            Add(m_Inspect);
            // Register before the terminal's Esc/back handler so the observer closes first.
            m_Owner.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            Mode = mode;
        }

        public void OpenObserver() {
            if (m_Disposed || m_Overlay != null) return;
            m_Overlay = new VisualElement { focusable = true };
            m_Overlay.AddToClassList("moon-observer");
            var header = new VisualElement();
            header.AddToClassList("moon-observer-header");
            m_Overlay.Add(header);
            var title = new Label("LUNAR OBSERVATORY");
            title.AddToClassList("moon-observer-title");
            header.Add(title);
            var close = new Button(CloseObserver) { text = "BACK  /  ESC" };
            close.AddToClassList("moon-observer-button");
            header.Add(close);
            // 背景与观察窗复用同一个月球视图。切换时移动元素并换样式，观察窗可以使用更大纹理和交互。
            Moon.Observing = true;
            Moon.Element.RemoveFromHierarchy();
            Moon.Element.RemoveFromClassList("moon-backdrop-view");
            Moon.Element.AddToClassList("moon-observer-view");
            m_Overlay.Add(Moon.Element);
            var controls = new VisualElement();
            controls.AddToClassList("moon-observer-controls");
            m_Overlay.Add(controls);
            controls.Add(new Label("DRAG TO ORBIT  /  SCROLL TO ZOOM"));
            var reset = new Button(Moon.ResetView) { text = "RESET VIEW" };
            reset.AddToClassList("moon-observer-button");
            controls.Add(reset);
            Button pause = null;
            pause = new Button(() => {
                Moon.Paused = !Moon.Paused;
                MoonkovLocalization.Set(pause, Moon.Paused ? "RESUME ORBIT" : "PAUSE ORBIT");
            }) { text = "PAUSE ORBIT" };
            pause.AddToClassList("moon-observer-button");
            controls.Add(pause);
            var telemetry = new Label();
            telemetry.AddToClassList("moon-observer-telemetry");
            m_Overlay.Add(telemetry);
            // 观察读数定时从预览的日照方向和天数计算，不需要每帧改文字。
            m_Telemetry = telemetry.schedule.Execute(() => MoonkovLocalization.Set(telemetry,
                "ORBITAL DAY " + Moon.Days.ToString("F2") + "  /  ACCELERATED TIME  /  SUNLIT " +
                ((1 + Vector3.Dot(Moon.SunDirection, Vector3.back)) * 50).ToString("F0") + "%")).Every(500);
            m_Overlay.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            header.BringToFront();
            m_Owner.Add(m_Overlay);
            m_Overlay.BringToFront();
            m_Overlay.Focus();
        }

        public void CloseObserver() {
            if (m_Overlay == null) return;
            m_Telemetry?.Pause();
            m_Telemetry = null;
            // 关窗时把月球放回背景，清掉暂停、旋转与缩放。下次打开会从统一状态开始。
            Moon.Element.RemoveFromHierarchy();
            Moon.Observing = false;
            Moon.Paused = false;
            Moon.ResetView();
            Moon.Element.RemoveFromClassList("moon-observer-view");
            Moon.Element.AddToClassList("moon-backdrop-view");
            Insert(0, Moon.Element);
            m_Overlay.RemoveFromHierarchy();
            m_Overlay = null;
            m_Inspect.Focus();
        }

        private void OnKey(KeyDownEvent e) {
            if (m_Overlay == null || e.keyCode != KeyCode.Escape) return;
            CloseObserver();
            e.StopImmediatePropagation();
        }

        public void Dispose() {
            if (m_Disposed) return;
            m_Disposed = true;
            CloseObserver();
            Moon.Dispose();
            m_Owner.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }
    }
}