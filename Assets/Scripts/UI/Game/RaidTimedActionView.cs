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
        private readonly HaloProgressRing m_Ring;
        private readonly Button m_Cancel;
        private string m_LastTitle;
        private int m_LastTenth = -1;
        public RaidTimedActionView(Action cancel)
        {
            name="raidTimedAction";AddToClassList("raid-timed-action");pickingMode=PickingMode.Ignore;
            m_Ring=new HaloProgressRing();m_Ring.AddToClassList("raid-action-ring");Add(m_Ring);
            m_Title=new Label {name="raidActionTitle",pickingMode=PickingMode.Ignore};m_Title.AddToClassList("raid-action-title");Add(m_Title);
            var row=new VisualElement {pickingMode=PickingMode.Ignore};row.AddToClassList("raid-action-row");Add(row);
            m_Time=new Label {name="raidActionTime",pickingMode=PickingMode.Ignore};m_Time.AddToClassList("raid-action-time");row.Add(m_Time);
            m_Cancel=new Button(cancel) {name="raidActionCancel",text="X"};row.Add(m_Cancel);
            style.display=DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_=>MoonkovLocalization.Bind(this));
        }
        public void Present(bool active,bool accepted,bool cancelling,bool medical,float remaining,float duration,
            string receipt,bool failed,bool visible,bool showCancel=true)
        {
            var state=new RaidActionReadout(accepted,cancelling,remaining,duration);
            PresentProgress(active ? medical ? "TREATING" : "SEARCHING" : receipt,
                Mathf.Min(state.Progress,.97f),state.Remaining,active,
                active && state.Stage==RaidActionStage.Running,active && showCancel,cancelling,failed,visible);
        }
        public void PresentProgress(string title,float progress,float remaining,bool active,bool showTime,
            bool cancellable,bool cancelling,bool failed,bool visible)
        {
            style.display=visible && (active || !string.IsNullOrEmpty(title)) ? DisplayStyle.Flex : DisplayStyle.None;
            if(!visible)return;
            if(m_LastTitle!=title) {m_LastTitle=title;MoonkovLocalization.Set(m_Title,title ?? "");}
            m_Ring.style.display=active ? DisplayStyle.Flex : DisplayStyle.None;m_Ring.SetProgress(progress);
            m_Time.style.display=active && showTime ? DisplayStyle.Flex : DisplayStyle.None;
            int tenth=active && showTime && float.IsFinite(remaining) ? Mathf.CeilToInt(Mathf.Max(0,remaining)*10) : -1;
            if(tenth>=0 && tenth!=m_LastTenth)MoonkovLocalization.Set(m_Time,"{0:0.0}s",tenth*.1f);
            m_LastTenth=tenth;
            m_Cancel.style.display=active && cancellable ? DisplayStyle.Flex : DisplayStyle.None;
            m_Cancel.SetEnabled(active && cancellable && !cancelling);
            EnableInClassList("is-error",!active && failed);
        }
    }
}
