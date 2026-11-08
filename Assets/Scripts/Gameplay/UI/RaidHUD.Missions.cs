using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Unity.MP_FPS.Missions;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private VisualElement m_MissionHud, m_MissionDetails, m_MissionInteraction, m_MissionNodes, m_MissionFill;
        private Label m_MissionTitle, m_MissionStep, m_MissionLocation, m_MissionRisk, m_MissionAction, m_MissionActionNote;
        private RaidMissionSnapshotRpc m_MissionSnapshot;
        private uint m_MissionSequence, m_MissionRequest;
        private readonly MoonkovActionAudio m_MissionAudio = new MoonkovActionAudio();
        private int m_MissionHolding = -1;
        private float m_MissionNextRenew;
        private string m_MissionOperation;
        private bool m_MissionPinned = true, m_MissionDetailsVisible, m_MissionHasDestination;
        private bool m_MissionConfiguring;
        private bool m_MissionReleaseRequired;
        private RaidMissionSnapshotRpc m_MissionHoldSnapshot;
        private uint m_MissionFeedbackRequest;
        private string m_MissionRequestedAction;
        private Label m_MissionKey, m_MissionInspectKey, m_MissionObject;
        private VisualElement m_MissionHoldTrack, m_MissionHoldFill;
        private string m_MissionInspectAnchor;
        private readonly System.Collections.Generic.HashSet<string> m_MissionInspected = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        private string m_MissionNotice;
        private float m_MissionNoticeUntil, m_MissionNextPresentation;
        private readonly System.Collections.Generic.List<Label> m_MissionNodeLabels = new System.Collections.Generic.List<Label>();
        private Client.StashItemArt m_MissionIcon;
        private Label m_MissionResult;
        private Label m_MissionMarker;
        private Vector3 m_MissionTarget;
        private string m_MissionTargetLabel;
        private static readonly string[] MissionCardinals = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };

        private void InitializeMissions()
        {
            var layout = Resources.Load<VisualTreeAsset>("Moonkov/UI/MissionHUD");
            if (!layout) return;
            layout.CloneTree(m_Root);
            m_MissionHud = m_Root.Q("missionHud"); m_MissionDetails = m_Root.Q("missionDetails");
            var rightRail = m_Root.Q<ScrollView>("raidRightRail");
            if (rightRail != null) rightRail.Add(m_MissionHud);
            m_MissionInteraction = m_Root.Q("missionInteraction"); m_MissionNodes = m_Root.Q("missionNodes");
            m_MissionFill = m_Root.Q("missionFill"); m_MissionTitle = m_Root.Q<Label>("missionTitle");
            m_MissionStep = m_Root.Q<Label>("missionStep"); m_MissionLocation = m_Root.Q<Label>("missionLocation");
            m_MissionRisk = m_Root.Q<Label>("missionRisk"); m_MissionAction = m_Root.Q<Label>("missionAction");
            m_MissionIcon = new Client.StashItemArt();
            m_MissionIcon.style.position = Position.Absolute; m_MissionIcon.style.right = 12; m_MissionIcon.style.top = 12;
            m_MissionIcon.style.width = m_MissionIcon.style.height = 42; m_MissionHud.Add(m_MissionIcon);
            m_MissionTitle.style.paddingRight = 40;
            m_MissionResult = new Label(); m_MissionResult.AddToClassList("mission-step");
            m_MissionResult.pickingMode = PickingMode.Ignore;
            m_MissionResult.style.display = DisplayStyle.None;
            m_ResultText.parent.Insert(m_ResultText.parent.IndexOf(m_ResultText) + 1, m_MissionResult);
            m_MissionMarker = new Label(); m_MissionMarker.AddToClassList("mission-world-marker"); m_MissionMarker.pickingMode = PickingMode.Ignore;
            m_MissionMarker.style.display = DisplayStyle.None; m_Root.Add(m_MissionMarker);
            m_MissionActionNote = m_Root.Q<Label>("missionActionNote");
            m_MissionKey = m_Root.Q<Label>("missionKey"); m_MissionInspectKey = m_Root.Q<Label>("missionInspectKey");
            m_MissionObject = m_Root.Q<Label>("missionObject");
            m_MissionHoldTrack = m_Root.Q("missionHoldTrack"); m_MissionHoldFill = m_Root.Q("missionHoldFill");
            m_Root.Q<Button>("missionPlateTab").clicked += () => ShowMissionInspectionTab(false);
            m_Root.Q<Button>("missionCircuitTab").clicked += () => ShowMissionInspectionTab(true);
            m_Root.Q<Button>("missionClose").clicked += () => SetMissionDetails(false);
            m_Root.Q<Button>("missionPin").clicked += () =>
            {
                m_MissionPinned = !m_MissionPinned;
                m_Root.Q<Button>("missionPin").text = m_MissionPinned ? "取消追踪" : "固定追踪";
                m_MissionHud.style.display = m_MissionPinned ? DisplayStyle.Flex : DisplayStyle.None;
            };
            m_MissionAudio.Reset(); m_MissionSequence = m_MissionRequest = 0; m_MissionOperation = null; m_MissionHolding = -1;
            m_MissionDetailsVisible = false; m_MissionConfiguring = false; m_MissionPinned = true;
            m_MissionReleaseRequired = false; m_MissionInspectAnchor = null; m_MissionInspected.Clear();
            m_MissionFeedbackRequest = 0; m_MissionRequestedAction = null; m_MissionHoldSnapshot = default;
            m_MissionNotice = null; m_MissionNoticeUntil = m_MissionNextPresentation = 0;
            m_MissionNodeLabels.Clear();
            m_MissionHud.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            m_MissionInteraction.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            MoonkovLocalization.Bind(m_MissionDetails); MoonkovLocalization.Bind(m_MissionHud);
            MoonkovAudio.BindUI(m_MissionDetails);
        }

        private void SetMissionDetails(bool visible)
        {
            if (m_MissionDetails == null || visible && m_FieldRecordOpen) return;
            if (visible) SetRaidMapOpen(false);
            CancelMissionHold();
            m_MissionDetailsVisible = visible;
            m_Root.Q<Button>("missionPin")?.EnableInClassList("campaign-hidden", CampaignJournalAvailable);
            if (!visible)
            {
                m_Root.Q("missionControls")?.Clear(); m_MissionConfiguring = false; m_MissionInspectAnchor = null;
                m_MissionDetails.RemoveFromClassList("mission-inspecting");
            }
            m_MissionDetails.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            Utils.SetCursorVisible(visible || m_MapOpen || m_InventoryVisible || m_WasSettled || GameSettings.Instance.IsPauseMenuOpen);
            if (visible) m_Root.Q<Button>("missionClose").Focus();
        }

        // Returns ownership of the shared F/gamepad interaction for this frame.
        private bool UpdateMissions(bool live)
        {
            if (m_MissionHud == null) return false;
            if (m_InteractionBindings != null && !m_InteractionBindings.Operate.IsPressed()) m_MissionReleaseRequired = false;
            m_Root.EnableInClassList("mission-compact", m_Root.resolvedStyle.width < 1400);
            var site = RaidMissionSite.Active;
            var entity = m_StateQuery.GetSingletonEntity();
            if (!site || !m_World.EntityManager.HasComponent<RaidMissionClientState>(entity)) return HideMissions();
            var snapshot = m_World.EntityManager.GetComponentData<RaidMissionClientState>(entity).Snapshot;
            site.Present(snapshot, live);
            RenderMissionResult(snapshot);
            bool known = snapshot.RaidId == m_Snapshot.RaidId && snapshot.DefinitionIndex >= 0 && snapshot.DefinitionIndex < site.Definitions.Length && snapshot.OperationId.Length > 0;
            if (!known)
            {
                CancelMissionHold(); m_MissionHud.style.display = m_MissionInteraction.style.display = DisplayStyle.None;
                m_MissionMarker.style.display = DisplayStyle.None;
                if (m_MissionOperation != null)
                { SetMissionDetails(false); m_MissionOperation = null; m_MissionSequence = 0; }
                if (!ChatInputBlocked && live && !CampaignJournalAvailable && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
                {
                    m_Root.Q<Label>("missionDetailTitle").text = "设施记录";
                    m_MissionNodes.Clear(); m_MissionNodeLabels.Clear();
                    m_Root.Q<Label>("missionClue").text = "尚未发现附近事件。留意设备信号、故障灯和残留记录；直接操作有效设备也会记录成果。";
                    m_Root.Q<Label>("missionOutcome").text = snapshot.BackendSupported ? "任务在局内随机出现，无需事先接取。" : "当前会话版本不支持设施调查。可继续普通搜刮，或返回准备台检查会话版本。";
                    SetMissionDetails(!m_MissionDetailsVisible);
                }
                if (!live && m_MissionDetailsVisible) SetMissionDetails(false);
                return false;
            }
            var definition = site.Definitions[snapshot.DefinitionIndex];
            if (m_MissionOperation != snapshot.OperationId.ToString())
            {
                SetMissionDetails(false); m_MissionOperation = snapshot.OperationId.ToString(); m_MissionPinned = true;
                if (!snapshot.CampaignEnabled)
                    MissionNotice("发现 MC00 设施信号  无需接取，直接操作有效。去向与流程可按 J 查看。");
                m_Root.Q<Button>("missionPin").text = "取消追踪";
                if (live) MoonkovAudio.Play(MoonkovAudio.Library?.Notification, Vector3.zero, .65f);
            }
            if (live)
            {
                bool active = snapshot.Result == MissionActionResult.Started || snapshot.Result == MissionActionResult.Running;
                bool success = snapshot.Result == MissionActionResult.Completed || snapshot.Result == MissionActionResult.AlreadyCompleted;
                MoonkovAudio.PlayWeaponLayer(m_MissionAudio.Observe(snapshot.RaidId, snapshot.RequestId,
                    active, !active && !success, MoonkovAudio.Library?.Error), Vector3.zero, 1, 0);
            }
            else m_MissionAudio.Reset();
            if (m_MissionSnapshot.RaidId != snapshot.RaidId) m_MissionInspected.Clear();
            m_MissionSnapshot = snapshot;
            m_MissionRequest = System.Math.Max(m_MissionRequest, snapshot.RequestId);
            if (snapshot.RequestId != 0 && snapshot.RequestId == m_MissionRequest && snapshot.RequestId != m_MissionFeedbackRequest &&
                snapshot.Result != MissionActionResult.Started && snapshot.Result != MissionActionResult.Running)
            {
                m_MissionFeedbackRequest = snapshot.RequestId;
                bool success = snapshot.Result == MissionActionResult.Completed || snapshot.Result == MissionActionResult.AlreadyCompleted;
                MissionNotice(success ? (m_MissionRequestedAction ?? "设备操作") + "已完成" : MissionFailureCopy(snapshot.Result));
            }
            if (snapshot.Sequence != m_MissionSequence || Time.unscaledTime >= m_MissionNextPresentation)
            {
                m_MissionSequence = snapshot.Sequence;
                m_MissionNextPresentation = Time.unscaledTime + .25f;
                RenderMission(definition, snapshot, live);
            }
            m_MissionHud.style.display = live && m_MissionPinned && !m_InventoryVisible ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateMissionMarker(false);
            if (!ChatInputBlocked && !CampaignJournalAvailable && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && live && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
                SetMissionDetails(!m_MissionDetailsVisible);
            if (!live || ChatInputBlocked || MapInputBlocked || m_InventoryVisible || m_InventoryRequestPending || m_MissionDetailsVisible || m_CampaignJournalVisible || m_FieldRecordOpen || GameSettings.Instance.IsPauseMenuOpen || DarkSightRequested || !Application.isFocused || UnityEngine.Cursor.lockState != CursorLockMode.Locked)
            {
                CancelMissionHold(); m_MissionInteraction.style.display = DisplayStyle.None;
                if (!live && m_MissionDetailsVisible) SetMissionDetails(false);
                return false;
            }
            var local = m_PlayerQuery.GetSingleton<LocalTransform>();
            if (m_ResearchInputPriority)
            {
                CancelMissionHold();
                m_MissionInteraction.style.display = DisplayStyle.None;
                return false;
            }
            var view = Camera.main;
            bool pressed = m_InteractionBindings.Operate.WasPressedThisFrame();
            bool held = m_InteractionBindings.Operate.IsPressed();
            if (!held) m_MissionReleaseRequired = false;
            bool terminal = snapshot.Phase == MissionPhase.Expired || snapshot.Phase == MissionPhase.Resolved;
            if (m_MissionHolding >= 0 && snapshot.RequestId == m_MissionRequest &&
                snapshot.Result != MissionActionResult.Started && snapshot.Result != MissionActionResult.Running)
            {
                m_MissionHolding = -1;
            }
            int target = -1, targetPriority = -1;
            float best = -1, distance = 0;
            for (int i = 0; i < definition.Objectives.Length; i++)
            {
                var node = definition.Objectives[i]; var anchor = site.Anchor(node.Anchor);
                if (anchor == null) continue;
                float score = RaidInteractionAim.Score(view, local.Position, anchor.Position, 3, RaidInteractionAim.MissionSurface(node.Anchor));
                if (score < 0) continue;
                bool completed = (snapshot.CompletedMask & (1u << i)) != 0;
                bool ready = node.RecordBeforePrerequisites || (snapshot.CompletedMask & node.Prerequisites) == node.Prerequisites;
                int priority = completed ? 0 : !ready ? 1 : node.Kind == MissionObjectiveKind.Process ? 2 : 3;
                if (target >= 0 && (score < best - .05f || Mathf.Abs(score - best) <= .05f && priority <= targetPriority)) continue;
                best = score; target = i; targetPriority = priority; distance = Vector3.Distance(local.Position, anchor.Position);
            }
            bool output = false;
            if (snapshot.Phase == MissionPhase.OutputReady && snapshot.OutputLootId >= 0)
            {
                var anchor = site.Anchor(definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive");
                if (anchor != null)
                {
                    float score = RaidInteractionAim.Score(view, local.Position, anchor.Position, 3, RaidInteractionAim.MissionSurface(anchor.Id));
                    if (score >= 0 && score >= best - .05f)
                    { best = score; target = -1; output = true; distance = Vector3.Distance(local.Position, anchor.Position); }
                }
            }
            if (m_MissionHolding >= 0)
            {
                // The press captures an operation epoch and node. Assisted selection never transfers a live hold.
                int captured = m_MissionHolding;
                var anchor = site.Anchor(definition.Objectives[captured].Anchor);
                bool valid = !terminal && held && snapshot.RaidId == m_MissionHoldSnapshot.RaidId &&
                    snapshot.Epoch == m_MissionHoldSnapshot.Epoch && snapshot.OperationId.Equals(m_MissionHoldSnapshot.OperationId) &&
                    anchor != null && RaidInteractionAim.Score(view, local.Position, anchor.Position, 3,
                        RaidInteractionAim.MissionSurface(anchor.Id)) >= 0;
                if (!valid)
                {
                    CancelMissionHold(); MissionNotice("操作已中断。已完成步骤保留，重新按下后继续。");
                }
                else { target = captured; output = false; best = float.MaxValue; }
            }
            bool selected = (target >= 0 || output) && !terminal && (best >= m_WorldFocusScore || m_MissionHolding >= 0);
            m_MissionInteraction.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            if (!selected) return m_MissionReleaseRequired && held;
            string anchorId = output ? definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive" : definition.Objectives[target].Anchor;
            var selectedAnchor = site.Anchor(anchorId);
            m_MissionObject.text = selectedAnchor.Label;
            m_MissionKey.text = m_InteractionBindings.Label(m_InteractionBindings.Operate);
            m_MissionInspectKey.text = m_InteractionBindings.Label(m_InteractionBindings.Inspect) + "  查看铭牌";
            m_MissionHoldTrack.style.display = DisplayStyle.None;
            if (m_InteractionBindings.Inspect.WasPressedThisFrame())
            { OpenMissionInspection(selectedAnchor, output ? null : definition.Objectives[target], target, false); return true; }
            if (output)
            {
                m_MissionAction.text = "打开输出槽"; m_MissionActionNote.text = "取走产物后撤离，尚未入库";
                if (pressed && !m_MissionReleaseRequired) { m_MissionReleaseRequired = true; OpenMissionOutput(snapshot.OutputLootId); }
                return true;
            }
            var objective = definition.Objectives[target];
            bool done = (snapshot.CompletedMask & (1u << target)) != 0;
            bool prerequisite = objective.RecordBeforePrerequisites || (snapshot.CompletedMask & objective.Prerequisites) == objective.Prerequisites;
            bool process = objective.Kind == MissionObjectiveKind.Process;
            bool configuration = objective.Kind == MissionObjectiveKind.Configure;
            float progress = target < 4 ? snapshot.ProgressA[target] : snapshot.ProgressB[target - 4];
            m_MissionAction.text = done ? "已完成 " + objective.Name : process ? objective.Name : configuration ? "检查配电线路" : "按住  " + objective.Name;
            m_MissionActionNote.text = !prerequisite ? "先完成前置步骤" : done ? "设备状态已确认" :
                process ? $"自动运行 {progress * 100:0}%  可以离开设备" : configuration ? "隔离烧损支路，保留备用母线" :
                m_MissionHolding == target ? "松开取消" : $"{objective.Seconds:0.#} 秒  保持站位与视线";
            if (Time.unscaledTime < m_MissionNoticeUntil) m_MissionActionNote.text = m_MissionNotice;
            if (m_MissionHolding == target && snapshot.RequestId != m_MissionRequest) m_MissionActionNote.text = "等待设备确认，松开取消";
            if (snapshot.RequestId == m_MissionRequest && snapshot.Result == MissionActionResult.Busy)
                m_MissionActionNote.text = MissionFailureCopy(snapshot.Result);
            m_MissionKey.style.display = done || process || !prerequisite ? DisplayStyle.None : DisplayStyle.Flex;
            m_MissionHoldTrack.style.display = m_MissionHolding == target ? DisplayStyle.Flex : DisplayStyle.None;
            m_MissionHoldFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
            if (!done && !process && prerequisite && pressed && !m_MissionReleaseRequired)
            {
                m_MissionReleaseRequired = true; m_MissionNoticeUntil = 0;
                if (configuration) { OpenMissionInspection(selectedAnchor, objective, target, m_MissionInspected.Contains(anchorId)); return true; }
                CancelMissionHold(); m_MissionRequest = NextManualActionRequest(); m_MissionHolding = target;
                m_MissionRequestedAction = objective.Name;
                m_MissionHoldSnapshot = snapshot;
                SendMission(MissionIntent.Begin, target); m_MissionNextRenew = Time.unscaledTime + .25f;
            }
            if (m_MissionHolding == target && held && Time.unscaledTime >= m_MissionNextRenew)
            { SendMission(MissionIntent.Renew, target); m_MissionNextRenew = Time.unscaledTime + .25f; }
            return true;
        }

        private static string MissionFailureCopy(MissionActionResult result) => result switch
        {
            MissionActionResult.Busy => "其他人正在操作设备，稍后重试。",
            MissionActionResult.Prerequisite => "前置步骤尚未完成，查看任务记录。",
            MissionActionResult.Stale => "设备状态已变化，松开后重新操作。",
            MissionActionResult.Expired => "本次设备窗口已结束。可以正常撤离。",
            _ => "操作未完成。检查线路、站位和视线后重试。"
        };

        private bool HideMissions()
        {
            RaidMissionSite.Active?.StopPresentation();
            CancelMissionHold(); m_MissionHud.style.display = m_MissionInteraction.style.display = DisplayStyle.None;
            m_MissionMarker.style.display = DisplayStyle.None;
            if (m_MissionDetailsVisible) SetMissionDetails(false);
            return false;
        }
        private void CancelMissionHold()
        {
            if (m_MissionHolding < 0) return;
            SendMission(MissionIntent.Cancel, m_MissionHolding); m_MissionHolding = -1;
        }
        private void SendMission(MissionIntent intent, int node)
        {
            if (m_World == null || !m_World.IsCreated || m_MissionSnapshot.OperationId.Length == 0) return;
            var captured = m_MissionHolding >= 0 ? m_MissionHoldSnapshot : m_MissionSnapshot;
            bool sent = Send(new RaidMissionActionRpc { RaidId = captured.RaidId, OperationId = captured.OperationId,
                Epoch = captured.Epoch, Node = node, RequestId = m_MissionRequest, Intent = intent });
            if (sent && intent == MissionIntent.Begin)
                m_MissionAudio.Track(m_MissionSnapshot.RaidId, m_MissionRequest, MoonkovAudio.Library?.Confirm, MoonkovAudio.Library?.Equipment);
            else if (intent == MissionIntent.Cancel) { m_MissionAudio.Reset(); MoonkovAudio.Play(MoonkovAudio.Library?.Equipment, Vector3.zero, .6f); }
            else if (!sent && intent != MissionIntent.Renew)
            { MoonkovAudio.Error(); m_MissionHolding = -1; MissionNotice("连接不可用，设备操作尚未确认。"); }
        }
        private void OpenMissionOutput(int id)
        {
            CancelMissionHold();
            if (!RequestLoot(id)) return;
            m_HealthSelected = false; m_EquipmentHost.style.display = DisplayStyle.Flex; m_HealthHost.style.display = DisplayStyle.None;
            m_EquipmentTab.EnableInClassList("raid-tab-active", true); m_HealthTab.EnableInClassList("raid-tab-active", false);
            m_DissectingLootId = -1; m_LootOpenPending = true;
            m_InventoryView.Present(null, "正在打开任务输出槽…", operationCompleted: false);
            m_InventoryView.SetReadOnly("等待服务器确认"); SetInventory(true);
            m_InventoryTitle.text = "任务输出槽 / 随身装备";
        }

        private void OpenMissionInspection(RaidMissionAnchor anchor, MissionObjectiveDefinition objective, int node, bool choices)
        {
            SetMissionDetails(true);
            m_MissionInspectAnchor = anchor.Id;
            m_MissionInspected.Add(anchor.Id);
            m_MissionConfiguring = objective?.Kind == MissionObjectiveKind.Configure;
            m_MissionDetails.AddToClassList("mission-inspecting");
            m_Root.Q<Label>("missionDetailTitle").text = anchor.Label;
            m_Root.Q<Label>("missionPlate").text = string.IsNullOrWhiteSpace(anchor.Inscription) ? "设备没有可读铭牌。" : anchor.Inscription;
            m_Root.Q<Label>("missionInspectionReason").text = anchor.Id == "power_a" ? "A 支路烧损。先隔离故障支路，保留备用母线。" :
                anchor.Id == "power_b" ? "备用母线在 18:47 带过电。恢复维修配电后，取样设备才能工作。" :
                anchor.Id == "entry" ? "登记清理队身份，开启本地设施检查。" : "设备记录只说明现场状态，调查结论以原始资料为准。";
            var controls = m_Root.Q("missionControls"); controls.Clear();
            var captured = m_MissionSnapshot;
            if (m_MissionConfiguring && (captured.CompletedMask & (1u << node)) == 0)
                for (int i = 0; i < objective.Choices.Length; i++)
                {
                    int choice = i + 1;
                    var button = new Button(() =>
                    {
                        // Revalidate after reading; no stale inspector may configure a later operation.
                        var current = m_MissionSnapshot;
                        var site = RaidMissionSite.Active; var currentAnchor = site ? site.Anchor(anchor.Id) : null;
                        if (current.Epoch != captured.Epoch || current.RaidId != captured.RaidId ||
                            !current.OperationId.Equals(captured.OperationId) || !m_PlayerQuery.HasSingleton<LocalTransform>() ||
                            currentAnchor == null || Vector3.Distance(m_PlayerQuery.GetSingleton<LocalTransform>().Position, currentAnchor.Position) > 3 ||
                            !RaidInteractionAim.Visible(Camera.main.transform.position, currentAnchor.Position, RaidInteractionAim.MissionSurface(anchor.Id)))
                        { SetMissionDetails(false); MissionNotice("设备状态或站位已变化，重新检查面板。"); return; }
                        m_MissionRequest = NextManualActionRequest();
                        m_MissionRequestedAction = objective.Name;
                        bool sent = Send(new RaidMissionActionRpc { RaidId = captured.RaidId, Epoch = captured.Epoch,
                            OperationId = captured.OperationId, Node = node, RequestId = m_MissionRequest, Intent = MissionIntent.Configure, Choice = choice });
                        if (sent) m_MissionAudio.Track(captured.RaidId, m_MissionRequest, MoonkovAudio.Library?.Confirm);
                        else MoonkovAudio.Error();
                        SetMissionDetails(false);
                    }) { text = objective.Choices[i] };
                    button.AddToClassList("mission-circuit-row"); controls.Add(button);
                }
            m_Root.Q<Button>("missionCircuitTab").SetEnabled(controls.childCount > 0);
            ShowMissionInspectionTab(choices && controls.childCount > 0);
        }

        private void ShowMissionInspectionTab(bool choices)
        {
            m_Root.Q("missionPlateBody").style.display = choices ? DisplayStyle.None : DisplayStyle.Flex;
            m_Root.Q("missionControls").style.display = choices ? DisplayStyle.Flex : DisplayStyle.None;
            m_Root.Q<Button>("missionPlateTab").EnableInClassList("is-selected", !choices);
            m_Root.Q<Button>("missionCircuitTab").EnableInClassList("is-selected", choices);
            if (choices) m_Root.Q("missionControls").Q<Button>()?.Focus();
            else (m_Root.Q<Button>("missionCircuitTab").enabledSelf ? m_Root.Q<Button>("missionCircuitTab") : m_Root.Q<Button>("missionPlateTab")).Focus();
        }

        private void RenderMission(MissionDefinition definition, RaidMissionSnapshotRpc snapshot, bool live)
        {
            m_MissionTitle.text = definition.Name;
            if (m_MissionInspectAnchor == null) m_Root.Q<Label>("missionDetailTitle").text = definition.Name;
            var kind = definition.ArtifactCode == "mooncore_fragment" ? Client.StashArtKind.MooncoreFragment : Client.StashArtKind.MissionArchive;
            if (m_MissionIcon.ArtKind != kind) m_MissionIcon.ArtKind = kind;
            if (m_MissionNodeLabels.Count != definition.Objectives.Length)
            {
                m_MissionNodes.Clear(); m_MissionNodeLabels.Clear();
                foreach (var _ in definition.Objectives)
                { var label = new Label(); label.AddToClassList("mission-node"); m_MissionNodes.Add(label); m_MissionNodeLabels.Add(label); }
                var exit = new Label("撤离条件：真实携带任务产物，成功带回后自动记录。");
                exit.AddToClassList("mission-node"); m_MissionNodes.Add(exit);
            }
            int next = -1;
            for (int i = 0; i < definition.Objectives.Length; i++)
            {
                bool done = (snapshot.CompletedMask & (1u << i)) != 0;
                var node = definition.Objectives[i];
                if (!done && next < 0) next = i;
                bool ready = (snapshot.CompletedMask & node.Prerequisites) == node.Prerequisites;
                bool terminal = snapshot.Phase == MissionPhase.Expired || snapshot.Phase == MissionPhase.Resolved;
                string state = done ? "已完成" : terminal ? "已结束" : !ready && node.RecordBeforePrerequisites ? "可提前读取" :
                    !ready ? "待前置" : node.Kind == MissionObjectiveKind.Process ? "自动运行" : "可操作";
                var label = m_MissionNodeLabels[i]; label.text = $"{i + 1:00}  {state}  {node.Name}";
                label.EnableInClassList("mission-node-complete", done);
            }
            var position = m_PlayerQuery.HasSingleton<LocalTransform>() ? (Vector3)m_PlayerQuery.GetSingleton<LocalTransform>().Position : Vector3.zero;
            var anchor = RaidMissionSite.Active.Anchor(next >= 0 ? definition.Objectives[next].Anchor : definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive");
            int carried = 0;
            if (!m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
                if (inventory.Graph != null && inventory.RaidId == m_Snapshot.RaidId)
                    foreach (var item in inventory.Graph.Items) if (MissionInventory.IsArtifact(item.Code) && item.MissionOperationId == snapshot.OperationId.ToString() && inventory.Graph.Carried(item)) carried++;
            }
            bool ended = snapshot.Phase == MissionPhase.Expired || snapshot.Phase == MissionPhase.Resolved;
            bool destination = MissionHasDestination(snapshot, carried);
            m_MissionHasDestination = destination;
            m_MissionStep.text = !ended && Time.unscaledTime < m_MissionNoticeUntil ? m_MissionNotice : MissionStepCopy(definition, snapshot, carried);
            float progress = next < 0 ? 1 : next < 4 ? snapshot.ProgressA[next] : snapshot.ProgressB[next - 4];
            m_MissionFill.parent.style.display = destination && carried == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_MissionFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
            m_MissionLocation.style.display = destination ? DisplayStyle.Flex : DisplayStyle.None;
            string layer = anchor.Position.y - position.y > 3 ? "楼上" : position.y - anchor.Position.y > 3 ? "楼下" : "当前层附近";
            m_MissionLocation.text = $"{anchor.Region}  {layer}  直线 {Vector3.Distance(position, anchor.Position):0} 米";
            m_MissionTarget = anchor.Position; m_MissionTargetLabel = anchor.Region + "  " + (next >= 0 ? definition.Objectives[next].Name : "任务产物");
            if (!ended && live && carried > 0 && MoonRaidMap.Active)
            { var exit = MoonRaidMap.Active.NearestExtraction(position);
                m_MissionTarget = exit + Vector3.up; m_MissionTargetLabel = "携带产物撤离";
                m_MissionLocation.text = $"返回撤离点  直线 {Vector3.Distance(position, exit):0} 米"; }
            var direction = m_MissionTarget - position;
            int cardinal = (Mathf.RoundToInt(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg / 45) + 8) % 8;
            m_MissionLocation.text += "  " + MissionCardinals[cardinal];
            m_MissionRisk.text = MissionRiskCopy(snapshot, live ? carried : 0);
            m_MissionRisk.EnableInClassList("mission-risk-urgent", !ended && snapshot.Remaining <= 60 && snapshot.Remaining > 0);
            m_MissionHud.EnableInClassList("mission-ended", ended);
            if (m_MissionInspectAnchor == null) m_Root.Q<Label>("missionClue").text = "路线提示：从 MC00 入口沿楼梯前往 L22。楼层与直线方向是定位参考，不是穿墙路线。\n" +
                (definition.ArtifactCode == "mooncore_fragment" ?
                "配电 A/B 顺序不限。启动切割后，设备声可能引来附近敌人。夹具内结晶带有重复编号，可能来自另一批次。" :
                "自检和标签读取可以穿插进行，提前扫描会保留；两处校验齐全后返回终端确认下载。下载声可能引来附近敌人。事故设备曾在烧损前持续上传。");
            m_Root.Q<Label>("missionOutcome").text = (definition.ArtifactCode == "mooncore_fragment" ? "首次有效带回月核 +30 月尘" : "首次有效带回档案 +20 月尘") +
                MoonkovLocalization.Text(definition.ArtifactCode == "mooncore_fragment" ?
                    "; first non-emergency recovery also grants +10. Keep the fragment for Suppliers → Halo research: permanent stabilization research, then repeatable recycler fabrication. Confirmed actions consume separate materials. Selling remains available." :
                    "; first non-emergency recovery also grants +10. Archive drives remain trade goods; no archive research recipe is available.");
        }

        private void MissionNotice(string text)
        { m_MissionNotice = text; m_MissionNoticeUntil = Time.unscaledTime + 4; }

        private static bool MissionHasDestination(RaidMissionSnapshotRpc snapshot, int carried) =>
            snapshot.Phase != MissionPhase.Expired && snapshot.Phase != MissionPhase.Resolved &&
            (carried > 0 || !(snapshot.Phase == MissionPhase.OutputReady && snapshot.OutputRemaining == 0));

        private static string MissionRiskCopy(RaidMissionSnapshotRpc snapshot, int carried)
        {
            if (snapshot.Phase == MissionPhase.Expired) return "回收窗口已关闭  本批未撤出产物失效  普通搜刮与撤离仍可继续";
            if (snapshot.Phase == MissionPhase.Resolved) return "本次设施行动结束  可继续普通搜刮或撤离";
            if (carried > 0) return snapshot.Remaining <= 60 ?
                $"回收窗口将在 {snapshot.Remaining:0} 秒后关闭！未撤出的产物将失效，请立即返回。" :
                $"已携带 {carried} 件  尚未入库  窗口 {snapshot.Remaining:0} 秒";
            if (snapshot.Phase == MissionPhase.OutputReady) return snapshot.OutputRemaining > 0 ?
                "物品尚未入库  可以争抢  死亡会掉落" : "输出槽已空  可继续探索或提前撤离";
            return snapshot.Phase == MissionPhase.Offered ? $"设施信号可用  {snapshot.Remaining:0} 秒" :
                $"回收窗口 {snapshot.Remaining:0} 秒  可随时撤离";
        }

        private static string MissionStepCopy(MissionDefinition definition, RaidMissionSnapshotRpc snapshot, int carried)
        {
            bool Done(string id)
            {
                for (int i = 0; i < definition.Objectives.Length; i++)
                    if (definition.Objectives[i].Id == id) return (snapshot.CompletedMask & (1u << i)) != 0;
                return false;
            }
            string Running(string id, string action)
            {
                foreach (var node in definition.Objectives)
                    if (node.Id == id) return $"{action}  自动运行 {node.Seconds:0.#} 秒，无需持续按住";
                return action + "  设备自动运行，无需持续按住";
            }
            if (snapshot.Phase == MissionPhase.Expired) return "回收窗口已关闭  本批未撤出的产物已失效";
            if (snapshot.Phase == MissionPhase.Resolved) return "本次设施行动结束  个人成果以撤离保存为准";
            if (carried > 0) return $"已携带 {carried} 件任务产物  返回撤离点，尚未入库";
            if (snapshot.Phase == MissionPhase.OutputReady) return snapshot.OutputRemaining > 0 ?
                "设备完成，打开输出槽取走产物后撤离" : "输出槽已空  产物已被取走，可继续探索或撤离";
            if (definition.ArtifactCode == "mooncore_fragment")
            {
                if (Done("start")) return Running("cutting", "正在切割");
                if (Done("power_a") && Done("power_b")) return "前往取样机启动切割。设备声可能引来附近敌人";
                if (Done("power_a")) return "A 路已隔离  继续恢复维修配电 B";
                if (Done("power_b")) return "B 路已恢复  继续隔离烧损的 A 支路";
                return "前往 MC00  L22，恢复两处供电，A/B 顺序不限";
            }
            int scanned = (Done("tag_a") ? 1 : 0) + (Done("tag_b") ? 1 : 0);
            if (Done("download_start")) return Running("download", "正在下载");
            if (!Done("self_check")) return scanned > 0 ? $"校验标签已记录 {scanned}/2  可回档案终端完成自检" :
                "前往 MC00  L22，自检或先读取配电柜上的校验标签";
            return scanned < 2 ? $"自检完成  读取两柜校验标签 {scanned}/2，已扫结果保留" :
                "自检与校验已齐全。返回终端下载，设备声可能引来附近敌人";
        }

        private static string MissionInventoryFailure(InventoryError error)
        {
            switch (error)
            {
                case InventoryError.Full: case InventoryError.Bounds: case InventoryError.Collision:
                    return "空间不足：每件任务产物需要 1×1 空位。先整理口袋、胸章或背包，再从输出槽取走。";
                case InventoryError.Incompatible: return "该装备槽不能放任务产物，请选择口袋、胸章或背包空位。";
                case InventoryError.Overweight: return "携带重量已达上限，先移出部分物品再取任务产物。";
                case InventoryError.Missing: return "这件产物已不在原位置，可能已被取走，请等待库存刷新后确认。";
                case InventoryError.Stale: return "库存或输出槽已有变化，请等待界面刷新后重试。";
                case InventoryError.Inaccessible: return "输出槽暂不可访问，请靠近设备并确认回收窗口仍开放；普通撤离不受影响。";
                default: return "操作尚未确认，请等待库存刷新后重试；未确认的物品不计入回收。";
            }
        }

        private void RenderMissionResult(RaidMissionSnapshotRpc snapshot)
        {
            if (m_ResultStep != 0 || snapshot.RaidId != m_Snapshot.RaidId || m_Snapshot.Phase == RaidPhase.Active)
            { m_MissionResult.style.display = DisplayStyle.None; return; }
            m_MissionResult.style.display = DisplayStyle.Flex;
            if (m_Snapshot.Phase != RaidPhase.Extracted) { m_MissionResult.text = "本次未安全带回任务产物；已有调查发现与首次奖励保留。"; return; }
            if (m_Snapshot.SaveState == RaidSaveState.Saving)
            { m_MissionResult.text = $"已撤离  正在保存 {snapshot.RecoveredArtifacts} 件任务产物，到账结果尚未确认。"; return; }
            if (m_Snapshot.SaveState == RaidSaveState.Retrying)
            { m_MissionResult.text = $"已撤离  {snapshot.RecoveredArtifacts} 件任务产物的保存正在重试。当前尚未确认到账，请等待结果刷新。"; return; }
            if (m_Snapshot.SaveState == RaidSaveState.SessionOnly)
            { m_MissionResult.text = "当前仅连接内存模式，任务成长未写入账号；离线档案或已配置的账号服务可持久保存。"; return; }
            if (snapshot.SaveState != RaidSaveState.Saved)
            { m_MissionResult.text = "已撤离，正在同步任务保存结果…"; return; }
            int added = snapshot.MissionFirstsSaved & ~snapshot.MissionFirstsBefore;
            int reward = ((added & 1) != 0 ? 10 : 0) + ((added & 2) != 0 ? 30 : 0) + ((added & 4) != 0 ? 20 : 0);
            m_MissionResult.text = (snapshot.RecoveredArtifacts > 0 ? $"任务产物已保存 {snapshot.RecoveredArtifacts} 件。" : "本次回收结果已保存。") +
                (reward > 0 ? $"首次成长奖励 +{reward} 月尘，调查记录已保存。" : "首次奖励不重复发放，已有调查记录保留。") +
                (snapshot.RecoveredArtifacts > 0 ? MoonkovLocalization.Text("Recovered materials are retained. Mooncore fragments can fund stabilization research and repeatable recycler fabrication at Suppliers → Halo research; install the crafted module before deploying. Archive drives remain trade goods.") : "");
        }

        private void UpdateMissionMarker(bool visible)
        {
            var camera = Camera.main;
            if (!visible || !camera || Vector3.Distance(camera.transform.position, m_MissionTarget) < 5 ||
                UnityEngine.Physics.Linecast(camera.transform.position, m_MissionTarget, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
            { m_MissionMarker.style.display = DisplayStyle.None; return; }
            var point = camera.WorldToViewportPoint(m_MissionTarget);
            bool behind = point.z <= 0;
            if (behind) { point.x = camera.transform.InverseTransformPoint(m_MissionTarget).x >= 0 ? .9f : .1f; point.y = .35f; }
            float x = Mathf.Clamp(point.x, .09f, .91f), y = Mathf.Clamp(point.y, .15f, .82f);
            m_MissionMarker.text = (behind ? "身后  " : "") + m_MissionTargetLabel;
            m_MissionMarker.style.left = x * m_Root.resolvedStyle.width - 90;
            m_MissionMarker.style.top = (1 - y) * m_Root.resolvedStyle.height;
            m_MissionMarker.style.display = DisplayStyle.Flex;
        }
    }
}
