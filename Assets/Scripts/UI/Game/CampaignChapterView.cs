using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS.Client
{
    // The journal reads confirmed/run state. Its only mutation is the display preference.
    internal sealed class CampaignChapterView : IDisposable
    {
        private readonly Func<CampaignProgress> m_Read;
        private readonly Label m_Saved, m_Title, m_Status, m_Briefing, m_Communications, m_Feedback;
        private readonly VisualElement m_Nodes;
        private readonly Button m_Track;
        private readonly Dictionary<string, Button> m_Rows = new Dictionary<string, Button>();
        private readonly Dictionary<string, Label> m_States = new Dictionary<string, Label>();
        private readonly IVisualElementScheduledItem m_Timer;
        private string m_Selected, m_Signature, m_Error;
        private bool m_Busy, m_Disposed;
#if UNITY_EDITOR
        private bool m_ReviewOnly;
        public void SetReviewPresentation(bool busy)
        {
            m_ReviewOnly = true; m_Busy = busy; m_Error = null; m_Signature = null; Refresh();
        }
#endif
        public VisualElement Element { get; }

        public CampaignChapterView(VisualElement host, Func<CampaignProgress> read, Action continueAction, string continueText)
        {
            m_Read = read ?? throw new ArgumentNullException(nameof(read));
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/CampaignChapterOne");
            if (!tree) throw new InvalidOperationException("Missing first chapter journal layout.");
            var instance = tree.CloneTree();
            Element = instance.Q("campaignChapterView");
            Element.RemoveFromHierarchy(); host.Add(Element);
            var stylesheet = Resources.Load<StyleSheet>("Moonkov/UI/CampaignChapterOne");
            if (stylesheet != null) Element.styleSheets.Add(stylesheet);
            m_Saved = Element.Q<Label>("campaignChapterSaved");
            m_Title = Element.Q<Label>("campaignSelectedTitle"); m_Status = Element.Q<Label>("campaignSelectedStatus");
            m_Briefing = Element.Q<Label>("campaignSelectedBriefing"); m_Nodes = Element.Q("campaignSelectedNodes");
            m_Communications = Element.Q<Label>("campaignSelectedCommunications"); m_Feedback = Element.Q<Label>("campaignTrackingStatus");
            m_Track = Element.Q<Button>("campaignTrack"); m_Track.clicked += ToggleTracking;
            var next = Element.Q<Button>("campaignContinue"); next.text = continueText;
            if (continueAction != null) next.clicked += continueAction; else next.AddToClassList("campaign-hidden");
            foreach (var task in CampaignCatalog.Tasks)
            {
                string id = task.Id;
                var button = new Button(() => { m_Selected = id; m_Signature = null; Refresh(); });
                button.AddToClassList("campaign-task-row");
                var name = new Label(task.Title); name.AddToClassList("campaign-task-name"); button.Add(name);
                var status = new Label(); status.AddToClassList("campaign-task-state"); button.Add(status);
                Element.Q("campaignChapterTasks").Add(button); m_Rows.Add(id, button); m_States.Add(id, status);
            }
            Element.RegisterCallback<DetachFromPanelEvent>(Detached);
            MoonkovLocalization.Bind(Element); MoonkovAudio.BindUI(Element);
            Refresh(); m_Timer = Element.schedule.Execute(Refresh).Every(250);
        }

        private void Detached(DetachFromPanelEvent evt) => Dispose();

        public void Select(string taskId)
        { if (CampaignCatalog.Find(taskId) == null) return; m_Selected = taskId; m_Signature = null; Refresh(); }

        public void SetVisible(bool visible)
        {
            if (m_Disposed) return;
            if (visible) { m_Timer?.Resume(); m_Signature = null; Refresh(); }
            else m_Timer?.Pause();
        }

        public void Refresh()
        {
            if (m_Disposed) return;
            var progress = m_Read();
            if (progress == null) { m_Feedback.text = "单人档案尚未载入"; m_Track.SetEnabled(false); return; }
            if (m_Selected == null)
                m_Selected = CampaignRules.Available(progress, progress.PrimaryTracked) ? progress.PrimaryTracked : CampaignRules.CurrentMain(progress) ?? "CM03";
            string signature = progress.Revision + "/" + progress.PreferenceRevision + "/" + m_Selected + "/" + m_Busy + "/" + m_Error;
            foreach (var task in CampaignCatalog.Tasks) signature += "/" + CampaignRules.Mask(progress, task.Id) + "/" + progress.CompletedTasks.Contains(task.Id);
            if (signature == m_Signature) return;
            m_Signature = signature;
            int complete = 0;
            foreach (var task in CampaignCatalog.Tasks)
            {
                var state = CampaignRules.Status(progress, task.Id);
                bool completed = state == CampaignTaskStatus.Completed;
                if (completed) complete++;
                m_States[task.Id].text = StateText(state);
                m_Rows[task.Id].SetEnabled(state != CampaignTaskStatus.Locked);
                m_Rows[task.Id].EnableInClassList("campaign-selected", task.Id == m_Selected);
                m_Rows[task.Id].EnableInClassList("campaign-complete", completed);
            }
            m_Saved.text = complete == 3 ? "第一章已完成" : "已保存 " + complete + "/3";
            var selected = CampaignCatalog.Find(m_Selected);
            var selectedStatus = CampaignRules.Status(progress, selected.Id);
            m_Title.text = selected.Title; m_Status.text = StateText(selectedStatus); m_Briefing.text = selected.Briefing;
            m_Nodes.Clear();
            var current = CampaignRules.CurrentNode(progress, selected.Id);
            for (int i = 0; i < selected.Nodes.Length; i++)
            {
                var node = selected.Nodes[i]; bool done = CampaignRules.NodeCompleted(progress, selected.Id, i);
                var label = new Label((done ? "已完成  " : "") + node.Action); label.AddToClassList("campaign-node");
                label.EnableInClassList("campaign-complete", done); label.EnableInClassList("campaign-current", node == current);
                m_Nodes.Add(label);
            }
            m_Communications.text = Communications(progress, selected);
            bool tracked = !progress.TrackingSuppressed && progress.PrimaryTracked == selected.Id;
            m_Track.text = m_Busy ? "正在保存" : tracked ? "取消追踪" : "追踪目标";
            m_Track.SetEnabled(!m_Busy && selectedStatus != CampaignTaskStatus.Locked && selectedStatus != CampaignTaskStatus.Completed);
            m_Feedback.text = m_Error ?? (m_Busy ? "正在保存追踪选择" : "");
            MoonkovLocalization.Bind(m_Nodes);
        }

        public static string StateText(CampaignTaskStatus state)
        {
            switch (state)
            {
                case CampaignTaskStatus.Completed: return "已保存";
                case CampaignTaskStatus.AwaitingExtraction: return "带回后保存";
                case CampaignTaskStatus.InProgress: return "进行中";
                case CampaignTaskStatus.Locked: return "待前置完成";
                default: return "可推进";
            }
        }

        private static string Communications(CampaignProgress progress, TaskDefinition task)
        {
            var text = new StringBuilder();
            if (task.Id == "CM01") text.Append(string.Join("\n\n", TutorialRules.Introduction)).Append("\n\n");
            var current = CampaignRules.CurrentNode(progress, task.Id);
            for (int i = 0; i < task.Nodes.Length; i++)
            {
                var node = task.Nodes[i];
                if (string.IsNullOrEmpty(node.Briefing) || !CampaignRules.NodeCompleted(progress, task.Id, i) && node != current) continue;
                if (text.Length > 0) text.Append("\n\n"); text.Append(node.Briefing);
            }
            if (CampaignRules.Status(progress, task.Id) == CampaignTaskStatus.Completed)
            { if (text.Length > 0) text.Append("\n\n"); text.Append(task.CompletionText); }
            return text.Length == 0 ? "当前没有新的通讯记录。" : text.ToString();
        }

        private async void ToggleTracking()
        {
#if UNITY_EDITOR
            if (m_ReviewOnly) return;
#endif
            if (m_Busy || m_Disposed) return;
            var progress = m_Read(); if (progress == null || m_Selected == null) return;
            bool remove = progress.PrimaryTracked == m_Selected && !progress.TrackingSuppressed;
            m_Busy = true; m_Error = null; m_Signature = null; Refresh();
            try { await PlayerProfileClient.SetCampaignTrackingAsync(m_Selected, remove: remove); }
            catch (Exception) { m_Error = "追踪选择未保存，请重试。"; }
            finally { m_Busy = false; if (!m_Disposed) { m_Signature = null; Refresh(); } }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Timer?.Pause(); m_Track.clicked -= ToggleTracking;
            Element.UnregisterCallback<DetachFromPanelEvent>(Detached); MoonkovAudio.UnbindUI(Element);
        }
    }
}
