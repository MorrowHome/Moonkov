// 排仓库右上方的账号与退出等入口。按面板和屏幕换算按钮尺寸，在窗口变窄时把按钮换到下一行。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public sealed partial class StashScreen {
        private VisualElement m_HeaderActions;
        private IVisualElementScheduledItem m_HeaderSizing;
        private readonly List<Button> m_HeaderButtons = new();
        private float m_HeaderScale;

        private void InitializeHeaderActions() {
            m_HeaderActions = m_Root.Q("stashMenuActions");
            m_HeaderSizing = m_Root.schedule.Execute(FitHeaderActions).Every(250);
            FitHeaderActions();
        }

        private void FitHeaderActions() {
            for (int i = m_HeaderButtons.Count - 1; i >= 0; i--)
                // 账号和连接入口可能移到其他区域。移出去时解除对按钮的临时尺寸设置，不让新父层继承旧仓库宽度。
                if (m_HeaderButtons[i].parent != m_HeaderActions) {
                    SizeHeaderButton(m_HeaderButtons[i], null);
                    m_HeaderButtons.RemoveAt(i);
                }
            if (m_Host.worldBound.width <= 0 || Screen.width <= 0) return;
            float scale = m_Preview ? 1 : Mathf.Max(1, m_Host.worldBound.width / Screen.width);
            // 面板比例和子控件数量都相同时不刷新。窗口缩放或账号入口换位置才重新布置。
            bool changed = !Mathf.Approximately(scale, m_HeaderScale) ||
                           m_HeaderButtons.Count != m_HeaderActions.childCount;
            if (!changed) return;
            foreach (var button in m_HeaderButtons) SizeHeaderButton(button, null);
            m_HeaderButtons.Clear();
            m_HeaderScale = scale;
            foreach (var child in m_HeaderActions.Children())
                if (child is Button button) {
                    m_HeaderButtons.Add(button);
                    SizeHeaderButton(button, scale);
                }
        }

        // 这里把最小点击尺寸按面板缩放换成对应的值。离开这个页时清除这些临时样式。
        private static void SizeHeaderButton(Button button, float? scale) {
            // Size in layout units so scaling never paints one button over another.
            button.style.fontSize = scale.HasValue ? new StyleLength(12 * scale.Value) : StyleKeyword.Null;
            button.style.height = button.style.minHeight =
                scale.HasValue ? new StyleLength(36 * scale.Value) : StyleKeyword.Null;
            button.style.paddingLeft = button.style.paddingRight =
                scale.HasValue ? new StyleLength(12 * scale.Value) : StyleKeyword.Null;
            button.style.marginLeft = scale.HasValue ? new StyleLength(8 * scale.Value) : StyleKeyword.Null;
        }

        private void DisposeHeaderActions() {
            m_HeaderSizing?.Pause();
            foreach (var button in m_HeaderButtons) SizeHeaderButton(button, null);
            m_HeaderButtons.Clear();
        }
    }
}