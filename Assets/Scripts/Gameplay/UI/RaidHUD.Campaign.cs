using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private VisualElement m_CampaignOverlay, m_CampaignHud, m_CampaignJournalModal;
        private Label m_CampaignTitle, m_CampaignAction, m_CampaignLocation, m_CampaignHint, m_CampaignMarker, m_CampaignResult;
        private Client.CampaignChapterView m_CampaignJournal;
        private bool m_CampaignJournalVisible, m_CampaignHasDestination;
        private float m_CampaignNextPresentation, m_CampaignNextRead;
        private CampaignProgress m_CampaignProgress;
        private Vector3 m_CampaignDestination;
        private string m_CampaignDestinationName;
        private readonly HashSet<string> m_CampaignCommunicationsShown = new HashSet<string>(StringComparer.Ordinal);

        private bool CampaignJournalAvailable
        {
            get
            {
                if (!PlayerProfileClient.IsOffline || m_ClashMode || m_World == null || !m_World.IsCreated || m_StateQuery.IsEmptyIgnoreFilter) return false;
                var entity = m_StateQuery.GetSingletonEntity();
                return m_World.EntityManager.HasComponent<RaidMissionClientState>(entity) &&
                    m_World.EntityManager.GetComponentData<RaidMissionClientState>(entity).Snapshot.CampaignEnabled;
            }
        }

        private void InitializeCampaignHUD()
        {
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/CampaignChapterHUD");
            if (!tree) return;
            tree.CloneTree(m_Root);
            m_CampaignOverlay = m_Root.Q("campaignOverlay"); m_CampaignHud = m_Root.Q("campaignHud");
            m_CampaignTitle = m_Root.Q<Label>("campaignHudTitle"); m_CampaignAction = m_Root.Q<Label>("campaignHudAction");
            m_CampaignLocation = m_Root.Q<Label>("campaignHudLocation"); m_CampaignHint = m_Root.Q<Label>("campaignHudHint");
            m_CampaignMarker = m_Root.Q<Label>("campaignMarker"); m_CampaignJournalModal = m_Root.Q("campaignJournalModal");
            var rail = m_Root.Q<ScrollView>("raidRightRail"); if (rail != null) rail.Add(m_CampaignHud);
            m_CampaignHud.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            var style = Resources.Load<StyleSheet>("Moonkov/UI/CampaignChapterOne");
            if (style != null && !m_Root.styleSheets.Contains(style)) m_Root.styleSheets.Add(style);
            m_CampaignJournal = new Client.CampaignChapterView(m_Root.Q("campaignJournalHost"),
                () => m_World != null && m_World.IsCreated ? CampaignScene.ReadProgress(m_World) : null,
                () => SetCampaignJournal(false), "返回战局");
            m_CampaignJournal.SetVisible(false);
            m_CampaignResult = new Label(); m_CampaignResult.AddToClassList("campaign-copy"); m_CampaignResult.AddToClassList("campaign-hidden");
            m_ResultText.parent.Insert(m_ResultText.parent.IndexOf(m_ResultText) + 1, m_CampaignResult);
            m_CampaignJournalVisible = m_CampaignHasDestination = false; m_CampaignNextPresentation = m_CampaignNextRead = 0; m_CampaignProgress = null;
            MoonkovLocalization.Bind(m_CampaignOverlay); MoonkovLocalization.Bind(m_CampaignResult);
        }

        private void SetCampaignJournal(bool open)
        {
            if (m_CampaignJournalModal == null || open == m_CampaignJournalVisible) return;
            if (open)
            {
                if (!CampaignJournalAvailable || m_InventoryVisible || GameSettings.Instance.IsPauseMenuOpen) return;
                SetRaidMapOpen(false); SetMissionDetails(false); CancelMissionHold();
                m_CampaignJournal.Refresh();
            }
            m_CampaignJournalVisible = open;
            m_CampaignJournalModal.EnableInClassList("campaign-hidden", !open);
            m_CampaignJournal.SetVisible(open);
            Utils.SetCursorVisible(open || m_InventoryVisible || m_MissionDetailsVisible || m_MapOpen || m_WasSettled || GameSettings.Instance.IsPauseMenuOpen);
            if (open) m_CampaignJournal.Element.Q<Button>("campaignContinue")?.Focus();
        }

        private void UpdateCampaignHUD(bool live)
        {
            if (m_CampaignHud == null) return;
            bool available = CampaignJournalAvailable;
            if (!available) m_CampaignProgress = null;
            else if (m_CampaignProgress == null || Time.unscaledTime >= m_CampaignNextRead)
            { m_CampaignProgress = CampaignScene.ReadProgress(m_World); m_CampaignNextRead = Time.unscaledTime + .25f; }
            var progress = m_CampaignProgress;
            m_CampaignOverlay.EnableInClassList("campaign-compact", m_Root.resolvedStyle.width < 1400);
            if (!live || progress == null)
            {
                m_CampaignHud.AddToClassList("campaign-hidden"); m_CampaignMarker.AddToClassList("campaign-hidden");
                SetCampaignJournal(false);
                PresentCampaignResult(progress);
                return;
            }
            m_CampaignResult.AddToClassList("campaign-hidden");
            PresentCampaignCommunication(progress);
            if (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && !m_InventoryVisible &&
                !MapInputBlocked && !GameSettings.Instance.IsPauseMenuOpen)
                SetCampaignJournal(!m_CampaignJournalVisible);
            bool tracked = !progress.TrackingSuppressed && progress.PrimaryTracked != null;
            string id = tracked ? EffectiveCampaignTask(progress) : null;
            bool show = id != null && !m_InventoryVisible && !m_CampaignJournalVisible && !m_MissionDetailsVisible &&
                !m_MapOpen && !GameSettings.Instance.IsPauseMenuOpen;
            m_CampaignHud.EnableInClassList("campaign-hidden", !show);
            // The old event remains interactive; only its duplicate tracking card is replaced.
            m_MissionHud.style.display = DisplayStyle.None; m_MissionMarker.style.display = DisplayStyle.None;
            if (!show) { m_CampaignMarker.AddToClassList("campaign-hidden"); return; }
            if (Time.unscaledTime >= m_CampaignNextPresentation)
            {
                m_CampaignNextPresentation = Time.unscaledTime + .25f;
                var task = CampaignCatalog.Find(id); var node = CampaignRules.CurrentNode(progress, id);
                m_CampaignTitle.text = task.Title;
                m_CampaignAction.text = CampaignActionText(node);
                m_CampaignHint.text = Time.unscaledTime < m_MissionNoticeUntil ? m_MissionNotice : CampaignTeachingHint(node);
                m_CampaignHasDestination = CampaignScene.TryDestination(m_World, progress, id, out m_CampaignDestination, out m_CampaignDestinationName);
                if (m_CampaignHasDestination && m_PlayerQuery.HasSingleton<LocalTransform>())
                {
                    Vector3 from = m_PlayerQuery.GetSingleton<LocalTransform>().Position;
                    float height = m_CampaignDestination.y - from.y;
                    string floor = height > 3 ? "楼上" : height < -3 ? "楼下" : "当前层附近";
                    m_CampaignLocation.text = m_CampaignDestinationName + "\n" + floor + "  直线 " + Vector3.Distance(from, m_CampaignDestination).ToString("0") + " 米";
                }
                else m_CampaignLocation.text = node?.Location ?? "返回作业船查看记录";
            }
            UpdateCampaignMarker(show && m_CampaignHasDestination);
        }

        private static string EffectiveCampaignTask(CampaignProgress progress)
        {
            string current = CampaignRules.CurrentMain(progress);
            string tracked = progress.PrimaryTracked;
            if (!CampaignRules.Available(progress, tracked) || CampaignRules.Status(progress, tracked) == CampaignTaskStatus.AwaitingExtraction && current != null)
                return current;
            return CampaignRules.Status(progress, tracked) == CampaignTaskStatus.Completed ? current : tracked;
        }

        private void PresentCampaignCommunication(CampaignProgress progress)
        {
            // Follow actual chapter progress, not the currently selected journal row.
            string current = CampaignRules.CurrentMain(progress);
            var node = current == null ? null : CampaignRules.CurrentNode(progress, current);
            if (node == null || string.IsNullOrEmpty(node.Briefing) || !m_CampaignCommunicationsShown.Add(current + "/" + node.Id)) return;
            MissionNotice(node.Briefing);
            m_CampaignNextPresentation = 0;
        }

        private string CampaignActionText(NodeDefinition node)
        {
            if (node == null) return "第一章调查记录已保存";
            if (node.Kind != CampaignObjectiveKind.Process || node.Seconds <= 0 || string.IsNullOrEmpty(node.ExistingObjectiveId)) return node.Action;
            var site = RaidMissionSite.Active;
            int index = m_MissionSnapshot.DefinitionIndex;
            if (site && index >= 0 && index < site.Definitions.Length)
            {
                var objectives = site.Definitions[index].Objectives;
                for (int i = 0; i < objectives.Length; i++)
                    if (objectives[i].Id == node.ExistingObjectiveId)
                    {
                        float fraction = i < 4 ? m_MissionSnapshot.ProgressA[i] : m_MissionSnapshot.ProgressB[i - 4];
                        return "切割进行中 " + (Mathf.Clamp01(fraction) * node.Seconds).ToString("0") + "/" + node.Seconds.ToString("0") + " 秒";
                    }
            }
            return node.Action;
        }

        private string CampaignTeachingHint(NodeDefinition node)
        {
            if (node == null) return "沈禾：实验场信号仍在维持。";
            if (node.Kind == CampaignObjectiveKind.Extract) return "携带物尚未入库，成功撤离后保存调查记录。";
            if (node.Kind == CampaignObjectiveKind.RecoverItem) return "打开输出槽取走碎片，预留至少 1×1 空位。";
            if (node.Kind == CampaignObjectiveKind.ReachArea) return "移动并观察 MC00 入口，留意来时路线。";
            if (!m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
                if (inventory.Graph != null && inventory.RaidId == m_Snapshot.RaidId && inventory.Graph.CellEnergy(accessibleOnly: true) == 0)
                    return "没有可用电池，留意已充能弹量与回程余量。";
            }
            if (node.Kind == CampaignObjectiveKind.Process) return "设备自动运行，可以移动观察，也可以提前撤离。";
            if (node.Kind == CampaignObjectiveKind.Configure) return "看向近处面板，核对故障说明后选择线路。";
            return "靠近设备并看向面板，按交互提示完成操作。";
        }

        private void PresentCampaignResult(CampaignProgress progress)
        {
            bool complete = progress != null && progress.CompletedTasks.Contains("CM03") &&
                m_Snapshot.Phase == RaidPhase.Extracted && m_Snapshot.SaveState == RaidSaveState.Saved;
            m_CampaignResult.EnableInClassList("campaign-hidden", !complete);
            if (complete) m_CampaignResult.text = "第一章 静默着陆 已完成\n沈禾：样本接收结果显示，实验场信号仍在维持。事故已经过去，为什么它还在运行？";
        }

        private void UpdateCampaignMarker(bool visible)
        {
            var camera = Camera.main;
            if (!visible || !camera || Vector3.Distance(camera.transform.position, m_CampaignDestination) < 5 ||
                UnityEngine.Physics.Linecast(camera.transform.position, m_CampaignDestination, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
            { m_CampaignMarker.AddToClassList("campaign-hidden"); return; }
            var point = camera.WorldToViewportPoint(m_CampaignDestination);
            bool behind = point.z <= 0;
            if (behind) { point.x = camera.transform.InverseTransformPoint(m_CampaignDestination).x >= 0 ? .9f : .1f; point.y = .35f; }
            m_CampaignMarker.text = (behind ? "身后  " : "") + m_CampaignDestinationName;
            m_CampaignMarker.style.left = Mathf.Clamp(point.x, .09f, .91f) * m_Root.resolvedStyle.width - 90;
            m_CampaignMarker.style.top = (1 - Mathf.Clamp(point.y, .15f, .82f)) * m_Root.resolvedStyle.height;
            m_CampaignMarker.RemoveFromClassList("campaign-hidden");
        }

        private void DisposeCampaignHUD()
        {
            m_CampaignJournal?.Dispose(); m_CampaignJournal = null;
            if (m_CampaignJournalVisible) Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
            m_CampaignJournalVisible = m_CampaignHasDestination = false;
            m_CampaignProgress = null;
        }
    }
}
