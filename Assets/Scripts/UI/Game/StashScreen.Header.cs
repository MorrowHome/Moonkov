using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashScreen
    {
        private VisualElement m_HeaderActions;
        private IVisualElementScheduledItem m_HeaderSizing;
        private readonly List<Button> m_HeaderButtons = new List<Button>();
        private float m_HeaderScale;

        private void InitializeHeaderActions()
        {
            m_HeaderActions = m_Root.Q("stashMenuActions");
            m_HeaderSizing = m_Root.schedule.Execute(FitHeaderActions).Every(250);
            FitHeaderActions();
        }
        private void FitHeaderActions()
        {
            for (int i = m_HeaderButtons.Count - 1; i >= 0; i--)
                if (m_HeaderButtons[i].parent != m_HeaderActions)
                { SizeHeaderButton(m_HeaderButtons[i], null); m_HeaderButtons.RemoveAt(i); }
            if (m_Host.worldBound.width <= 0 || Screen.width <= 0) return;
            float scale = Mathf.Max(1, m_Host.worldBound.width / Screen.width);
            bool changed = !Mathf.Approximately(scale, m_HeaderScale) || m_HeaderButtons.Count != m_HeaderActions.childCount;
            if (!changed) return;
            foreach (var button in m_HeaderButtons) SizeHeaderButton(button, null);
            m_HeaderButtons.Clear(); m_HeaderScale = scale;
            foreach (var child in m_HeaderActions.Children())
                if (child is Button button) { m_HeaderButtons.Add(button); SizeHeaderButton(button, scale); }
        }
        private static void SizeHeaderButton(Button button, float? scale)
        {
            // Size in layout units so scaling never paints one button over another.
            button.style.fontSize = scale.HasValue ? new StyleLength(12 * scale.Value) : StyleKeyword.Null;
            button.style.height = button.style.minHeight = scale.HasValue ? new StyleLength(36 * scale.Value) : StyleKeyword.Null;
            button.style.paddingLeft = button.style.paddingRight = scale.HasValue ? new StyleLength(12 * scale.Value) : StyleKeyword.Null;
            button.style.marginLeft = scale.HasValue ? new StyleLength(8 * scale.Value) : StyleKeyword.Null;
        }
        private void DisposeHeaderActions()
        {
            m_HeaderSizing?.Pause();
            foreach (var button in m_HeaderButtons) SizeHeaderButton(button, null);
            m_HeaderButtons.Clear();
        }
    }
}
