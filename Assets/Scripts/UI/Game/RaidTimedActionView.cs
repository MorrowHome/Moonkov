using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public enum RaidActionStage { Requesting, Running, Awaiting, Cancelling }

    public readonly struct RaidActionReadout
    {
        public readonly RaidActionStage Stage;
        public readonly float Remaining, Progress;
        public RaidActionReadout(bool accepted, bool cancelling, float remaining, float duration)
        {
            Remaining = float.IsFinite(remaining) ? Mathf.Max(0,remaining) : 0;
            Stage = cancelling ? RaidActionStage.Cancelling : !accepted ? RaidActionStage.Requesting :
                Remaining > 0 ? RaidActionStage.Running : RaidActionStage.Awaiting;
            Progress = !accepted || !float.IsFinite(duration) || duration <= 0 ? 0 : Mathf.Clamp01(1-Remaining/duration);
        }
        public static string Receipt(string source, bool error)
        {
            if (source == "治疗完成。" && !error) return "TREATMENT COMPLETE";
            if (source == "搜索完成。" && !error) return "SEARCH COMPLETE";
            if (source != null && source.Contains("已取消")) return "ACTION CANCELLED";
            if (source != null && source.Contains("移动超过")) return "INTERRUPTED / MOVED";
            if (source != null && source.Contains("受到攻击")) return "INTERRUPTED / HIT";
            if (source != null && source.Contains("开火或投掷")) return "INTERRUPTED / FIRED";
            if (source != null && source.Contains("换弹或切换")) return "INTERRUPTED / WEAPON CHANGED";
            if (source != null && source.Contains("库存已变化")) return "INTERRUPTED / INVENTORY CHANGED";
            return "ACTION INTERRUPTED";
        }
    }

    // Read-only progress. Reaching zero never confirms a treatment or unlocks loot.
    public sealed class RaidTimedActionView : VisualElement
    {
        private readonly Label m_Title, m_Time;
        private readonly VisualElement m_Track, m_Fill;
        private readonly Button m_Cancel;
        private string m_LastTitle;
        private int m_LastTenth = -1;
        public RaidTimedActionView(Action cancel)
        {
            name = "raidTimedAction"; AddToClassList("raid-timed-action"); pickingMode = PickingMode.Ignore;
            var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("raid-action-row"); Add(row);
            m_Title = new Label { name = "raidActionTitle", pickingMode = PickingMode.Ignore }; m_Title.AddToClassList("raid-action-title"); row.Add(m_Title);
            m_Time = new Label { name = "raidActionTime", pickingMode = PickingMode.Ignore }; m_Time.AddToClassList("raid-action-time"); row.Add(m_Time);
            m_Cancel = new Button(cancel) { name = "raidActionCancel", text = "X / CANCEL" }; row.Add(m_Cancel);
            m_Track = new VisualElement { pickingMode = PickingMode.Ignore }; m_Track.AddToClassList("raid-action-track"); Add(m_Track);
            m_Fill = new VisualElement { pickingMode = PickingMode.Ignore }; m_Fill.AddToClassList("raid-action-fill"); m_Track.Add(m_Fill);
            style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ => MoonkovLocalization.Bind(this));
        }
        public void Present(bool active, bool accepted, bool cancelling, bool medical, float remaining, float duration,
            string receipt, bool failed, bool visible)
        {
            style.display = visible && (active || !string.IsNullOrEmpty(receipt)) ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            var state = new RaidActionReadout(accepted,cancelling,remaining,duration);
            bool running = active && state.Stage == RaidActionStage.Running;
            string title = !active ? receipt : medical ? "TREATING" : "SEARCHING";
            if (m_LastTitle != title) { m_LastTitle=title; MoonkovLocalization.Set(m_Title,title ?? ""); }
            m_Time.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            int tenth=running ? Mathf.CeilToInt(state.Remaining*10) : -1;
            if (running && tenth!=m_LastTenth) MoonkovLocalization.Set(m_Time,"{0:0.0}s",tenth*.1f);
            m_LastTenth=tenth;
            m_Cancel.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            m_Cancel.SetEnabled(active && !cancelling);
            m_Track.style.display = active && accepted ? DisplayStyle.Flex : DisplayStyle.None;
            m_Fill.style.width = Length.Percent(Mathf.Min(state.Progress,.97f)*100);
            EnableInClassList("is-error",!active && failed);
        }
    }
}
