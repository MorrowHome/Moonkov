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
        private int m_MissionHolding = -1;
        private float m_MissionNextRenew;
        private string m_MissionOperation;
        private bool m_MissionPinned = true, m_MissionDetailsVisible;
        private readonly System.Collections.Generic.List<Label> m_MissionNodeLabels = new System.Collections.Generic.List<Label>();

        private void InitializeMissions()
        {
            var layout = Resources.Load<VisualTreeAsset>("Moonkov/UI/MissionHUD");
            if (!layout) return;
            layout.CloneTree(m_Root);
            m_MissionHud = m_Root.Q("missionHud"); m_MissionDetails = m_Root.Q("missionDetails");
            m_MissionInteraction = m_Root.Q("missionInteraction"); m_MissionNodes = m_Root.Q("missionNodes");
            m_MissionFill = m_Root.Q("missionFill"); m_MissionTitle = m_Root.Q<Label>("missionTitle");
            m_MissionStep = m_Root.Q<Label>("missionStep"); m_MissionLocation = m_Root.Q<Label>("missionLocation");
            m_MissionRisk = m_Root.Q<Label>("missionRisk"); m_MissionAction = m_Root.Q<Label>("missionAction");
            m_MissionActionNote = m_Root.Q<Label>("missionActionNote");
            m_Root.Q<Button>("missionClose").clicked += () => SetMissionDetails(false);
            m_Root.Q<Button>("missionPin").clicked += () =>
            {
                m_MissionPinned = !m_MissionPinned;
                m_Root.Q<Button>("missionPin").text = m_MissionPinned ? "取消追踪" : "固定追踪";
                m_MissionHud.style.display = m_MissionPinned ? DisplayStyle.Flex : DisplayStyle.None;
            };
            m_MissionSequence = m_MissionRequest = 0; m_MissionOperation = null; m_MissionHolding = -1;
            m_MissionDetailsVisible = false; m_MissionPinned = true;
            m_MissionNodeLabels.Clear();
            MoonkovLocalization.Bind(m_MissionDetails); MoonkovLocalization.Bind(m_MissionHud);
            MoonkovAudio.BindUI(m_MissionDetails);
        }

        private void SetMissionDetails(bool visible)
        {
            if (m_MissionDetails == null) return;
            CancelMissionHold();
            m_MissionDetailsVisible = visible;
            if (!visible) m_Root.Q("missionControls")?.Clear();
            m_MissionDetails.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            Utils.SetCursorVisible(visible || m_InventoryVisible || m_WasSettled || GameSettings.Instance.IsPauseMenuOpen);
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
            bool known = snapshot.RaidId == m_Snapshot.RaidId && snapshot.DefinitionIndex >= 0 && snapshot.DefinitionIndex < site.Definitions.Length && snapshot.OperationId.Length > 0;
            if (!known)
            {
                CancelMissionHold(); m_MissionHud.style.display = m_MissionInteraction.style.display = DisplayStyle.None;
                if (live && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
                {
                    m_Root.Q<Label>("missionDetailTitle").text = "设施记录";
                    m_MissionNodes.Clear(); m_MissionNodeLabels.Clear();
                    m_Root.Q<Label>("missionClue").text = "尚未发现附近事件。留意设备信号、故障灯和残留记录；直接操作有效设备也会记录成果。";
                    m_Root.Q<Label>("missionOutcome").text = "任务在局内随机出现，无需事先接取。";
                    SetMissionDetails(!m_MissionDetailsVisible);
                }
                if (!live && m_MissionDetailsVisible) SetMissionDetails(false);
                return false;
            }
            var definition = site.Definitions[snapshot.DefinitionIndex];
            if (m_MissionOperation != snapshot.OperationId.ToString())
            {
                CancelMissionHold(); m_MissionOperation = snapshot.OperationId.ToString(); m_MissionPinned = true;
                m_Root.Q<Button>("missionPin").text = "取消追踪";
                if (live) MoonkovAudio.Play(MoonkovAudio.Library?.Notification, Vector3.zero, .65f);
            }
            m_MissionSnapshot = snapshot;
            m_MissionRequest = System.Math.Max(m_MissionRequest, snapshot.RequestId);
            if (snapshot.Sequence != m_MissionSequence)
            {
                m_MissionSequence = snapshot.Sequence;
                RenderMission(definition, snapshot, live);
            }
            m_MissionHud.style.display = live && m_MissionPinned && !m_InventoryVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && live && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
                SetMissionDetails(!m_MissionDetailsVisible);
            if (!live || m_InventoryVisible || m_MissionDetailsVisible || GameSettings.Instance.IsPauseMenuOpen || UnityEngine.Cursor.lockState != CursorLockMode.Locked)
            {
                CancelMissionHold(); m_MissionInteraction.style.display = DisplayStyle.None;
                if (!live && m_MissionDetailsVisible) SetMissionDetails(false);
                return false;
            }
            var local = m_PlayerQuery.GetSingleton<LocalTransform>();
            var view = Camera.main;
            Vector3 eye = view ? view.transform.position : (Vector3)local.Position + Vector3.up * 1.4f;
            Vector3 forward = view ? view.transform.forward : Vector3.forward;
            int target = -1; bool output = false; float best = 3;
            for (int i = 0; i < definition.Objectives.Length; i++)
            {
                var node = definition.Objectives[i]; var anchor = site.Anchor(node.Anchor);
                if (anchor == null) continue;
                float distance = Vector3.Distance(local.Position, anchor.Position);
                if (distance > best || Vector3.Dot(forward, (anchor.Position - eye).normalized) < .92f ||
                    UnityEngine.Physics.Linecast(eye, anchor.Position, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                bool completed = (snapshot.CompletedMask & (1u << i)) != 0;
                if (completed && target >= 0) continue;
                best = distance; target = i;
            }
            if (snapshot.Phase == MissionPhase.OutputReady && snapshot.OutputLootId >= 0)
            {
                var anchor = site.Anchor(definition.Id == "mc00_mooncore" ? "sampler" : "archive");
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
            if (m_MissionHolding >= 0 && (!held || target != m_MissionHolding || !selected)) CancelMissionHold();
            if (output)
            {
                m_MissionAction.text = "F  打开任务输出槽"; m_MissionActionNote.text = $"{best:0.0} 米 · 物品尚未入库，死亡会丢失";
                if (pressed) OpenMissionOutput(snapshot.OutputLootId);
                return true;
            }
            if (!selected) return false;
            if (snapshot.RequestId == m_MissionRequest && snapshot.Result != MissionActionResult.Started && snapshot.Result != MissionActionResult.Running)
                m_MissionHolding = -1;
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
            if (!done && !process && prerequisite && pressed)
            {
                if (configuration)
                {
                    OpenMissionConfiguration(objective, target); return true;
                }
                CancelMissionHold(); m_MissionRequest++; m_MissionHolding = target;
                SendMission(MissionIntent.Begin, target); m_MissionNextRenew = Time.unscaledTime + .25f;
            }
            if (m_MissionHolding == target && held && Time.unscaledTime >= m_MissionNextRenew)
            { SendMission(MissionIntent.Renew, target); m_MissionNextRenew = Time.unscaledTime + .25f; }
            return true;
        }

        private bool HideMissions()
        {
            CancelMissionHold(); m_MissionHud.style.display = m_MissionInteraction.style.display = DisplayStyle.None;
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
            Send(new RaidMissionActionRpc { RaidId = m_MissionSnapshot.RaidId, OperationId = m_MissionSnapshot.OperationId,
                Epoch = m_MissionSnapshot.Epoch, Node = node, RequestId = m_MissionRequest, Intent = intent });
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
            var controls = m_Root.Q("missionControls"); controls.Clear();
            for (int i = 0; i < objective.Choices.Length; i++)
            {
                int choice = i + 1;
                var button = new Button(() =>
                {
                    m_MissionRequest++;
                    Send(new RaidMissionActionRpc { RaidId = m_MissionSnapshot.RaidId, Epoch = m_MissionSnapshot.Epoch,
                        OperationId = m_MissionSnapshot.OperationId, Node = node, RequestId = m_MissionRequest, Intent = MissionIntent.Configure, Choice = choice });
                    SetMissionDetails(false);
                }) { text = objective.Choices[i] };
                controls.Add(button);
            }
            m_Root.Q<Label>("missionClue").text = "故障牌：A 支路烧损。隔离故障支路后，备用母线才能接通；切断母线会让封存设施失电。";
            controls.Q<Button>()?.Focus();
        }

        private void RenderMission(MissionDefinition definition, RaidMissionSnapshotRpc snapshot, bool live)
        {
            m_MissionTitle.text = definition.Name; m_Root.Q<Label>("missionDetailTitle").text = definition.Name;
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
                var label = m_MissionNodeLabels[i]; label.text = (done ? "已完成  " : $"{i + 1:00}  ") + node.Name;
                label.EnableInClassList("mission-node-complete", done);
            }
            var position = m_PlayerQuery.HasSingleton<LocalTransform>() ? (Vector3)m_PlayerQuery.GetSingleton<LocalTransform>().Position : Vector3.zero;
            var anchor = RaidMissionSite.Active.Anchor(next >= 0 ? definition.Objectives[next].Anchor : definition.Id == "mc00_mooncore" ? "sampler" : "archive");
            m_MissionStep.text = snapshot.Phase == MissionPhase.Expired ? "回收窗口已关闭" : snapshot.Phase == MissionPhase.Resolved ? "本次产物已回收或遗失" :
                snapshot.Phase == MissionPhase.OutputReady ? "取得任务产物并撤离" : next >= 0 ? definition.Objectives[next].Name : "设施操作完成";
            float progress = next < 0 ? 1 : next < 4 ? snapshot.ProgressA[next] : snapshot.ProgressB[next - 4];
            m_MissionFill.style.width = Length.Percent(progress * 100);
            string layer = anchor.Position.y - position.y > 3 ? "楼上" : position.y - anchor.Position.y > 3 ? "楼下" : "当前层附近";
            m_MissionLocation.text = $"{anchor.Region} · {layer} · 直线 {Vector3.Distance(position, anchor.Position):0} 米";
            m_MissionRisk.text = snapshot.Phase == MissionPhase.Offered ? $"设施信号可用 · {snapshot.Remaining:0} 秒" :
                snapshot.Phase == MissionPhase.OutputReady ? "物品尚未入库 · 可以争抢 · 死亡会掉落" : $"回收窗口 {snapshot.Remaining:0} 秒 · 可随时撤离";
            m_Root.Q<Label>("missionClue").text = definition.Id == "mc00_mooncore" ?
                "残留记录：备用线路曾被人为隔离。夹具内的结晶带有重复编号，可能来自另一批次。" :
                "事故痕迹：设备烧损前仍在持续上传。两个校验标签保留了不同时间戳；恢复档案可查明最后一次操作。";
            m_Root.Q<Label>("missionOutcome").text = "不接取、不追踪也有效。设施完成与本人安全带回分别记录；首次收益仅发放一次。";
        }
    }
}
