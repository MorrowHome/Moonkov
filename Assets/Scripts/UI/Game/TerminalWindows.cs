using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class TerminalWindows : IDisposable
    {
        private readonly VisualElement m_Root, m_Layer;
        private readonly List<VisualElement> m_Windows = new List<VisualElement>();
        private readonly Dictionary<VisualElement, VisualElement> m_ReturnFocus = new Dictionary<VisualElement, VisualElement>();
        private readonly Action m_Back;
        private IVisualElementScheduledItem m_ToastTimer;
        private readonly Label m_Toast;

        public TerminalWindows(VisualElement root, Action back)
        {
            m_Root = root; m_Back = back;
            m_Layer = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Layer.AddToClassList("terminal-window-layer"); root.Add(m_Layer);
            m_Toast = new Label { pickingMode = PickingMode.Ignore };
            m_Toast.AddToClassList("terminal-toast"); m_Toast.style.display = DisplayStyle.None; root.Add(m_Toast);
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }

        public VisualElement Open(string title, Vector2? position = null, bool compact = false)
        {
            var window = new VisualElement { focusable = true };
            window.AddToClassList(compact ? "terminal-context" : "terminal-window");
            float offset = (m_Windows.Count % 5) * 24;
            window.style.left = position.HasValue ? Mathf.Max(0, position.Value.x) : 70 + offset;
            window.style.top = position.HasValue ? Mathf.Max(0, position.Value.y) : 70 + offset;
            var header = new VisualElement(); header.AddToClassList("terminal-window-header");
            var label = new Label(title); label.AddToClassList("terminal-heading"); header.Add(label);
            var close = new Button(() => Close(window)) { text = "×", tooltip = "Close / Esc" };
            close.AddToClassList("terminal-window-close"); header.Add(close); window.Add(header);
            header.AddManipulator(new WindowDrag(window));
            m_ReturnFocus[window] = m_Root.panel?.focusController.focusedElement as VisualElement;
            m_Windows.Add(window); m_Layer.Add(window); window.Focus();
            window.RegisterCallback<PointerDownEvent>(_ => BringToFront(window), TrickleDown.TrickleDown);
            return window;
        }
        private void BringToFront(VisualElement window)
        {
            m_Windows.Remove(window); m_Windows.Add(window); window.BringToFront();
        }
        public void Close(VisualElement window)
        {
            m_Windows.Remove(window); window.RemoveFromHierarchy();
            if (m_ReturnFocus.TryGetValue(window, out var focus) && focus?.panel != null) focus.Focus();
            m_ReturnFocus.Remove(window);
        }
        public void CloseAll()
        {
            while (m_Windows.Count > 0) Close(m_Windows[m_Windows.Count - 1]);
        }
        private void OnKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            if (m_Windows.Count > 0) Close(m_Windows[m_Windows.Count - 1]); else m_Back();
            evt.StopImmediatePropagation();
        }
        public void Toast(string message)
        {
            m_ToastTimer?.Pause(); m_Toast.text = message; m_Toast.style.display = DisplayStyle.Flex;
            m_ToastTimer = m_Root.schedule.Execute(() => m_Toast.style.display = DisplayStyle.None).StartingIn(3500);
        }
        public void Dispose()
        {
            CloseAll(); m_ToastTimer?.Pause(); m_Root.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            m_Layer.RemoveFromHierarchy(); m_Toast.RemoveFromHierarchy();
        }

        private sealed class WindowDrag : PointerManipulator
        {
            private readonly VisualElement m_Window;
            private Vector2 m_StartPointer, m_Start;
            private int m_Pointer = -1;
            public WindowDrag(VisualElement window) { m_Window = window; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            private void Down(PointerDownEvent e)
            {
                if (e.button != 0 || e.target is Button) return;
                m_Pointer = e.pointerId; m_StartPointer = e.position;
                m_Start = new Vector2(m_Window.resolvedStyle.left, m_Window.resolvedStyle.top);
                target.CapturePointer(m_Pointer); e.StopPropagation();
            }
            private void Move(PointerMoveEvent e)
            {
                if (m_Pointer != e.pointerId) return;
                var delta = (Vector2)e.position - m_StartPointer;
                var bounds = m_Window.parent.contentRect;
                m_Window.style.left = Mathf.Clamp(m_Start.x + delta.x, 0, Mathf.Max(0, bounds.width - m_Window.resolvedStyle.width));
                m_Window.style.top = Mathf.Clamp(m_Start.y + delta.y, 0, Mathf.Max(0, bounds.height - 45));
            }
            private void Up(PointerUpEvent e) { if (m_Pointer != e.pointerId) return; target.ReleasePointer(m_Pointer); m_Pointer = -1; }
            private void Lost(PointerCaptureOutEvent e) { m_Pointer = -1; }
        }
    }
}
