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
            m_Root.Q<Button>("missionClose").clicked += () => SetMissionDetails(false);
            m_Root.Q<Button>("missionPin").clicked += () =>
            {
                m_MissionPinned = !m_MissionPinned;
                m_Root.Q<Button>("missionPin").text = m_MissionPinned ? "取消追踪" : "固定追踪";
                m_MissionHud.style.display = m_MissionPinned ? DisplayStyle.Flex : DisplayStyle.None;
            };
            m_MissionAudio.Reset(); m_MissionSequence = m_MissionRequest = 0; m_MissionOperation = null; m_MissionHolding = -1;
            m_MissionDetailsVisible = false; m_MissionConfiguring = false; m_MissionPinned = true;
            m_MissionNotice = null; m_MissionNoticeUntil = m_MissionNextPresentation = 0;
            m_MissionNodeLabels.Clear();
            m_MissionHud.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            m_MissionInteraction.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            MoonkovLocalization.Bind(m_MissionDetails); MoonkovLocalization.Bind(m_MissionHud);
            MoonkovAudio.BindUI(m_MissionDetails);
        }

        private void SetMissionDetails(bool visible)
        {
            if (m_MissionDetails == null) return;
            if (visible) SetRaidMapOpen(false);
            CancelMissionHold();
            m_MissionDetailsVisible = visible;
            m_Root.Q<Button>("missionPin")?.EnableInClassList("campaign-hidden", CampaignJournalAvailable);
            if (!visible) { m_Root.Q("missionControls")?.Clear(); m_MissionConfiguring = false; }
            m_MissionDetails.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            Utils.SetCursorVisible(visible || m_MapOpen || m_InventoryVisible || m_WasSettled || GameSettings.Instance.IsPauseMenuOpen);
            if (visible) m_Root.Q<Button>("missionClose").Focus();
        }

        // Returns ownership of the shared F/gamepad interaction for this frame.
        private bool UpdateMissions(bool live)
        {
            if (m_MissionHud == null) return false;
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
                if (live && !CampaignJournalAvailable && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
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
                    MissionNotice("发现 MC00 设施信号 · 无需接取，直接操作有效。去向与流程可按 J 查看。");
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
            m_MissionSnapshot = snapshot;
            m_MissionRequest = System.Math.Max(m_MissionRequest, snapshot.RequestId);
            if (snapshot.Sequence != m_MissionSequence || Time.unscaledTime >= m_MissionNextPresentation)
            {
                m_MissionSequence = snapshot.Sequence;
                m_MissionNextPresentation = Time.unscaledTime + .25f;
                RenderMission(definition, snapshot, live);
            }
            m_MissionHud.style.display = live && m_MissionPinned && !m_InventoryVisible ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateMissionMarker(m_MissionHasDestination && live && m_MissionPinned && !m_InventoryVisible && !m_MissionDetailsVisible && !GameSettings.Instance.IsPauseMenuOpen);
            if (!CampaignJournalAvailable && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && live && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
                SetMissionDetails(!m_MissionDetailsVisible);
            if (!live || MapInputBlocked || m_InventoryVisible || m_MissionDetailsVisible || m_CampaignJournalVisible || GameSettings.Instance.IsPauseMenuOpen || UnityEngine.Cursor.lockState != CursorLockMode.Locked)
            {
                CancelMissionHold(); m_MissionInteraction.style.display = DisplayStyle.None;
                if (!live && m_MissionDetailsVisible) SetMissionDetails(false);
                return false;
            }
            var local = m_PlayerQuery.GetSingleton<LocalTransform>();
            var view = Camera.main;
            Vector3 eye = view ? view.transform.position : (Vector3)local.Position + Vector3.up * 1.4f;
            Vector3 forward = view ? view.transform.forward : Vector3.forward;
            int target = -1, targetPriority = -1; bool output = false; float best = 3;
            for (int i = 0; i < definition.Objectives.Length; i++)
            {
                var node = definition.Objectives[i]; var anchor = site.Anchor(node.Anchor);
                if (anchor == null) continue;
                float distance = Vector3.Distance(local.Position, anchor.Position);
                if (distance > 3 || Vector3.Dot(forward, (anchor.Position - eye).normalized) < .92f ||
                    UnityEngine.Physics.Linecast(eye, anchor.Position, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                bool completed = (snapshot.CompletedMask & (1u << i)) != 0;
                bool ready = node.RecordBeforePrerequisites || (snapshot.CompletedMask & node.Prerequisites) == node.Prerequisites;
                // Several steps share a machine. A locked download/process must never hide its actionable start/self-check.
                int priority = completed ? 0 : !ready ? 1 : node.Kind == MissionObjectiveKind.Process ? 2 : 3;
                if (target >= 0 && (priority < targetPriority || priority == targetPriority && distance >= best)) continue;
                best = distance; target = i; targetPriority = priority;
            }
            if (snapshot.Phase == MissionPhase.OutputReady && snapshot.OutputLootId >= 0)
            {
                var anchor = site.Anchor(definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive");
                float distance = Vector3.Distance(local.Position, anchor.Position);
                if (distance <= 3 && Vector3.Dot(forward, (anchor.Position - eye).normalized) >= .92f &&
                    !UnityEngine.Physics.Linecast(eye, anchor.Position, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                { target = -1; output = true; best = distance; }
            }
            bool pressed = (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
            bool held = (Keyboard.current != null && Keyboard.current.fKey.isPressed) || (Gamepad.current != null && Gamepad.current.buttonWest.isPressed);
            bool terminal = snapshot.Phase == MissionPhase.Expired || snapshot.Phase == MissionPhase.Resolved;
            bool selected = (target >= 0 || output) && !terminal;
            m_MissionInteraction.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_MissionHolding >= 0 && (!held || target != m_MissionHolding || !selected))
            {
                if (!terminal && (snapshot.CompletedMask & (1u << m_MissionHolding)) == 0)
                    MissionNotice("当前操作已中断 · 已完成的其他步骤保留，重新靠近设备后按住 F 继续。");
                CancelMissionHold();
            }
            if (output)
            {
                m_MissionAction.text = "F  打开任务输出槽"; m_MissionActionNote.text = $"{best:0.0} 米 · 物品尚未入库，死亡会丢失";
                if (pressed) OpenMissionOutput(snapshot.OutputLootId);
                return true;
            }
            if (!selected) return false;
            if (snapshot.RequestId == m_MissionRequest && snapshot.Result != MissionActionResult.Started && snapshot.Result != MissionActionResult.Running)
            {
                if (m_MissionHolding >= 0 && snapshot.Result == MissionActionResult.Invalid)
                    MissionNotice("操作未完成 · 请检查站位与视线；受伤会中断长按，已完成步骤不丢失。");
                else if (m_MissionHolding >= 0 && snapshot.Result == MissionActionResult.Busy)
                    MissionNotice("其他人正在操作这台设备 · 可以稍后重试，或先完成其他步骤。");
                m_MissionHolding = -1;
            }
            var objective = definition.Objectives[target];
            bool done = (snapshot.CompletedMask & (1u << target)) != 0;
            bool prerequisite = objective.RecordBeforePrerequisites || (snapshot.CompletedMask & objective.Prerequisites) == objective.Prerequisites;
            bool process = objective.Kind == MissionObjectiveKind.Process;
            bool configuration = objective.Kind == MissionObjectiveKind.Configure;
            float progress = target < 4 ? snapshot.ProgressA[target] : snapshot.ProgressB[target - 4];
            m_MissionAction.text = done ? "已完成 · " + objective.Name : process ? objective.Name : configuration ? "F  检查配电线路" : "按住 F  " + objective.Name;
            m_MissionActionNote.text = !prerequisite ? "前置步骤尚未完成 · 按 J 查看设施流程" : done ? "设施状态已同步" :
                process ? $"设备自动运行 · {progress * 100:0}%" : configuration ? "故障牌：烧损支路需隔离，备用母线仍有电" : $"{best:0.0} 米 · {objective.Seconds:0.#} 秒 · 移动、转开视线或受伤会中断";
            if (snapshot.RequestId == m_MissionRequest && snapshot.Result == MissionActionResult.Busy) m_MissionActionNote.text = "设备正由其他人操作，稍后重试";
            if (configuration && !done && snapshot.RequestId == m_MissionRequest && snapshot.Result == MissionActionResult.Invalid)
                m_MissionActionNote.text = "线路未接通 · 按 F 根据故障牌重新配置";
            var inscription = site.Anchor(objective.Anchor)?.Inscription;
            if (!string.IsNullOrWhiteSpace(inscription)) m_MissionActionNote.text += "\n" + inscription;
            if (!done && !process && prerequisite && pressed)
            {
                m_MissionNoticeUntil = 0;
                if (configuration)
                {
                    OpenMissionConfiguration(objective, target); return true;
                }
                CancelMissionHold(); m_MissionRequest=NextManualActionRequest(); m_MissionHolding = target;
                SendMission(MissionIntent.Begin, target); m_MissionNextRenew = Time.unscaledTime + .25f;
            }
            if (m_MissionHolding == target && held && Time.unscaledTime >= m_MissionNextRenew)
            { SendMission(MissionIntent.Renew, target); m_MissionNextRenew = Time.unscaledTime + .25f; }
            return true;
        }

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
            bool sent = Send(new RaidMissionActionRpc { RaidId = m_MissionSnapshot.RaidId, OperationId = m_MissionSnapshot.OperationId,
                Epoch = m_MissionSnapshot.Epoch, Node = node, RequestId = m_MissionRequest, Intent = intent });
            if (sent && intent == MissionIntent.Begin)
                m_MissionAudio.Track(m_MissionSnapshot.RaidId, m_MissionRequest, MoonkovAudio.Library?.Confirm, MoonkovAudio.Library?.Equipment);
            else if (intent == MissionIntent.Cancel) { m_MissionAudio.Reset(); MoonkovAudio.Play(MoonkovAudio.Library?.Equipment, Vector3.zero, .6f); }
            else if (!sent && intent != MissionIntent.Renew) MoonkovAudio.Error();
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

        private void OpenMissionConfiguration(MissionObjectiveDefinition objective, int node)
        {
            SetMissionDetails(true);
            m_MissionConfiguring = true;
            var controls = m_Root.Q("missionControls"); controls.Clear();
            for (int i = 0; i < objective.Choices.Length; i++)
            {
                int choice = i + 1;
                var button = new Button(() =>
                {
                    m_MissionRequest=NextManualActionRequest();
                    bool sent = Send(new RaidMissionActionRpc { RaidId = m_MissionSnapshot.RaidId, Epoch = m_MissionSnapshot.Epoch,
                        OperationId = m_MissionSnapshot.OperationId, Node = node, RequestId = m_MissionRequest, Intent = MissionIntent.Configure, Choice = choice });
                    if (sent) m_MissionAudio.Track(m_MissionSnapshot.RaidId, m_MissionRequest, MoonkovAudio.Library?.Confirm);
                    else MoonkovAudio.Error();
                    SetMissionDetails(false);
                }) { text = objective.Choices[i] };
                controls.Add(button);
            }
            m_Root.Q<Label>("missionClue").text = (RaidMissionSite.Active.Anchor(objective.Anchor)?.Inscription ?? "") +
                "\n故障牌：A 支路烧损。隔离故障支路后，备用母线才能接通；切断母线会让封存设施失电。";
            controls.Q<Button>()?.Focus();
        }

        private void RenderMission(MissionDefinition definition, RaidMissionSnapshotRpc snapshot, bool live)
        {
            m_MissionTitle.text = definition.Name; m_Root.Q<Label>("missionDetailTitle").text = definition.Name;
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
            m_MissionLocation.text = $"{anchor.Region} · {layer} · 直线 {Vector3.Distance(position, anchor.Position):0} 米";
            m_MissionTarget = anchor.Position; m_MissionTargetLabel = anchor.Region + " · " + (next >= 0 ? definition.Objectives[next].Name : "任务产物");
            if (!ended && live && carried > 0 && MoonRaidMap.Active)
            { m_MissionTarget = MoonRaidMap.Active.ExtractionPosition + Vector3.up; m_MissionTargetLabel = "携带产物撤离";
                m_MissionLocation.text = $"返回撤离点 · 直线 {Vector3.Distance(position, MoonRaidMap.Active.ExtractionPosition):0} 米"; }
            var direction = m_MissionTarget - position;
            int cardinal = (Mathf.RoundToInt(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg / 45) + 8) % 8;
            m_MissionLocation.text += " · " + MissionCardinals[cardinal];
            m_MissionRisk.text = MissionRiskCopy(snapshot, live ? carried : 0);
            m_MissionRisk.EnableInClassList("mission-risk-urgent", !ended && snapshot.Remaining <= 60 && snapshot.Remaining > 0);
            m_MissionHud.EnableInClassList("mission-ended", ended);
            if (!m_MissionConfiguring) m_Root.Q<Label>("missionClue").text = "路线提示：从 MC00 入口沿楼梯前往 L22。楼层与直线方向是定位参考，不是穿墙路线。\n" +
                (definition.ArtifactCode == "mooncore_fragment" ?
                "配电 A/B 顺序不限，两路完成后在取样机启动切割。夹具内结晶带有重复编号，可能来自另一批次。" :
                "自检和标签读取可以穿插进行，提前扫描会保留；两处校验齐全后返回终端确认下载。事故设备曾在烧损前持续上传。");
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
            if (snapshot.Phase == MissionPhase.Expired) return "回收窗口已关闭 · 本批未撤出产物失效 · 普通搜刮与撤离仍可继续";
            if (snapshot.Phase == MissionPhase.Resolved) return "本次设施行动结束 · 可继续普通搜刮或撤离";
            if (carried > 0) return snapshot.Remaining <= 60 ?
                $"回收窗口将在 {snapshot.Remaining:0} 秒后关闭！未撤出的产物将失效，请立即返回。" :
                $"已携带 {carried} 件 · 尚未入库 · 窗口 {snapshot.Remaining:0} 秒";
            if (snapshot.Phase == MissionPhase.OutputReady) return snapshot.OutputRemaining > 0 ?
                "物品尚未入库 · 可以争抢 · 死亡会掉落" : "输出槽已空 · 可继续探索或提前撤离";
            return snapshot.Phase == MissionPhase.Offered ? $"设施信号可用 · {snapshot.Remaining:0} 秒" :
                $"回收窗口 {snapshot.Remaining:0} 秒 · 可随时撤离";
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
                    if (node.Id == id) return $"{action} · 自动运行 {node.Seconds:0.#} 秒，无需持续按住";
                return action + " · 设备自动运行，无需持续按住";
            }
            if (snapshot.Phase == MissionPhase.Expired) return "回收窗口已关闭 · 本批未撤出的产物已失效";
            if (snapshot.Phase == MissionPhase.Resolved) return "本次设施行动结束 · 个人成果以撤离保存为准";
            if (carried > 0) return $"已携带 {carried} 件任务产物 · 返回撤离点，尚未入库";
            if (snapshot.Phase == MissionPhase.OutputReady) return snapshot.OutputRemaining > 0 ?
                "设备完成 · 按 F 打开输出槽，取走产物后撤离" : "输出槽已空 · 产物已被取走，可继续探索或撤离";
            if (definition.ArtifactCode == "mooncore_fragment")
            {
                if (Done("start")) return Running("cutting", "正在切割");
                if (Done("power_a") && Done("power_b")) return "两路供电已恢复 · 前往取样机启动切割";
                if (Done("power_a")) return "A 路已隔离 · 继续恢复维修配电 B";
                if (Done("power_b")) return "B 路已恢复 · 继续隔离烧损的 A 支路";
                return "前往 MC00 · L22，恢复两处供电，A/B 顺序不限";
            }
            int scanned = (Done("tag_a") ? 1 : 0) + (Done("tag_b") ? 1 : 0);
            if (Done("download_start")) return Running("download", "正在下载");
            if (!Done("self_check")) return scanned > 0 ? $"校验标签已记录 {scanned}/2 · 可回档案终端完成自检" :
                "前往 MC00 · L22，自检或先读取配电柜上的校验标签";
            return scanned < 2 ? $"自检完成 · 读取两柜校验标签 {scanned}/2，已扫结果保留" :
                "自检与校验已齐全 · 返回档案终端确认下载";
        }

        private static string MissionInventoryFailure(InventoryError error)
        {
            switch (error)
            {
                case InventoryError.Full: case InventoryError.Bounds: case InventoryError.Collision:
                    return "空间不足：每件任务产物需要 1×1 空位。先整理口袋、胸挂或背包，再从输出槽取走。";
                case InventoryError.Incompatible: return "该装备槽不能放任务产物，请选择口袋、胸挂或背包空位。";
                case InventoryError.Overweight: return "携带重量已达上限，先移出部分物品再取任务产物。";
                case InventoryError.Missing: return "这件产物已不在原位置，可能已被取走，请等待库存刷新后确认。";
                case InventoryError.Stale: return "库存或输出槽已有变化，请等待界面刷新后重试。";
                case InventoryError.Inaccessible: return "输出槽暂不可访问，请靠近设备并确认回收窗口仍开放；普通撤离不受影响。";
                default: return "操作尚未确认，请等待库存刷新后重试；未确认的物品不计入回收。";
            }
        }

        private void RenderMissionResult(RaidMissionSnapshotRpc snapshot)
        {
            if (snapshot.RaidId != m_Snapshot.RaidId || m_Snapshot.Phase == RaidPhase.Active)
            { m_MissionResult.style.display = DisplayStyle.None; return; }
            m_MissionResult.style.display = DisplayStyle.Flex;
            if (m_Snapshot.Phase != RaidPhase.Extracted) { m_MissionResult.text = "本次未安全带回任务产物；已有调查发现与首次奖励保留。"; return; }
            if (m_Snapshot.SaveState == RaidSaveState.Saving)
            { m_MissionResult.text = $"已撤离 · 正在保存 {snapshot.RecoveredArtifacts} 件任务产物，到账结果尚未确认。"; return; }
            if (m_Snapshot.SaveState == RaidSaveState.Retrying)
            { m_MissionResult.text = $"已撤离 · {snapshot.RecoveredArtifacts} 件任务产物的保存正在重试。当前尚未确认到账，请等待结果刷新。"; return; }
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
            m_MissionMarker.text = (behind ? "身后 · " : "") + m_MissionTargetLabel;
            m_MissionMarker.style.left = x * m_Root.resolvedStyle.width - 90;
            m_MissionMarker.style.top = (1 - y) * m_Root.resolvedStyle.height;
            m_MissionMarker.style.display = DisplayStyle.Flex;
        }
    }
}
