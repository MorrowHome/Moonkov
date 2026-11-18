// 负责局内大地图的开关、按键屏蔽和当前战局的地图数据。大图和远征小图共用同一份地图资源，退局或切玩法时重置显示。
using Unity.MP_FPS.DollSinger;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private RaidMapView m_RaidMap;
        private bool m_MapOpen, m_MapContextValid, m_MapOpenedInContext;
        private int m_MapInputGuardFrame = -1, m_MapEscapeFrame = -1, m_MapToggleFrame = -1;
        private World m_MapWorld;
        private int m_MapRaid, m_MapEpoch;
        private string m_MapMatch;

        private bool MapPlayable =>
            GameSettings.Instance.GameState == GlobalGameState.InGame && !GameSettings.Instance.IsPauseMenuOpen &&
            m_World != null && m_World.IsCreated && m_Snapshot.RaidId > 0 && m_Snapshot.Phase == RaidPhase.Active &&
            m_ConnectionQuery.HasSingleton<NetworkId>() && m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() &&
            m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().CurrentHealth > 0 && (!m_ClashMode ||
                (m_Clash is { Approved: true, HasSnapshot: true } && m_Clash.Snapshot.Connected &&
                 m_Clash.Snapshot.Phase == ClashPhase.Active && m_Clash.Snapshot.Life == ClashLife.Alive));

        private bool MapKeyPressed =>
            !ChatInputBlocked && Application.isFocused && RaidKeyBindings.Pressed(RaidKey.Map) &&
            !Client.QuickSlotBindings.Typing(
                m_Root?.panel?.focusController.focusedElement as UnityEngine.UIElements.VisualElement);

        // 输入回调在 HUD 的 LateUpdate 之前执行。预先占用按 M 的打开帧，
        // 并保留关闭帧，避免界面切换时滚轮、点击或转身穿过去。
        // 输入回调早于 LateUpdate。打开和关闭地图的那一帧都先挡住转身、开火和滚轮，避免一次按键穿过界面。
        private bool MapBlocksInput =>
            m_MapOpen || Time.frameCount <= m_MapInputGuardFrame || (MapKeyPressed &&
                                                                     GameSettings.Instance.GameState ==
                                                                     GlobalGameState.InGame &&
                                                                     !GameSettings.Instance.IsPauseMenuOpen &&
                                                                     !QuickManualBusy);

        private static bool MapInputBlocked => s_Active != null && s_Active.MapBlocksInput;

        private void InitializeRaidMap() {
            m_MapOpen = m_MapContextValid = m_MapOpenedInContext = false;
            m_MapInputGuardFrame = m_MapEscapeFrame = m_MapToggleFrame = -1;
            m_RaidMap = new RaidMapView(m_Root, m_ClashView.Map, CloseRaidMapFromUI);
        }

        private void CloseRaidMapFromUI() {
            m_MapEscapeFrame = Time.frameCount;
            SetRaidMapOpen(false);
        }

        private void SetRaidMapOpen(bool open) {
            if (open == m_MapOpen) return;
            if (open) {
                // 必须有活着的本地角色以及可用连接才能开大地图。其他手动操作和正在读的日志不能被大地图强行打断。
                if (!MapPlayable || QuickManualBusy || m_FieldRecordOpen) return;
                if (m_InventoryVisible) SetInventory(false);
                // 背包可能因为等待确认拒绝关闭。还开着就不要再叠一层地图，以免焦点同时被两个界面要走。
                if (m_InventoryVisible) return;
                if (m_MissionDetailsVisible) SetMissionDetails(false);
                if (m_CampaignJournalVisible) SetCampaignJournal(false);
                // 每局首次打开回到适合导航的范围，有底图时放大到玩家附近。同一局中再次打开保留之前视图。
                if (!m_MapOpenedInContext) {
                    m_ClashView.Map.FullMap();
                    if (m_ClashView.Map.HasSurface) {
                        m_ClashView.Map.ZoomBy(3);
                        m_ClashView.Map.CenterOnPlayer();
                    }
                    m_MapOpenedInContext = true;
                }
                m_MapOpen = true;
                m_MapInputGuardFrame = Time.frameCount;
                m_Clash?.StopView();
                CancelClashRevive();
                BountyClashDarkSightRenderer.ClearImmediate();
                RaidQuickSlotInput.ReleasePrimary();
                m_RaidMap.Open();
                Utils.SetCursorVisible(true);
            } else {
                m_MapOpen = false;
                m_MapInputGuardFrame = Time.frameCount;
                m_RaidMap?.Hide();
                RaidQuickSlotInput.ReleasePrimary();
                Utils.SetCursorVisible(m_InventoryVisible || m_MissionDetailsVisible || m_WasSettled ||
                                       m_ClashLobbyPointer || GameSettings.Instance.IsPauseMenuOpen);
            }
        }

        private void UpdateMinimap(bool available) {
            if (!available || m_TutorialConfigured || !MapPlayable || !MoonRaidMap.Active || m_MapOpen ||
                m_InventoryVisible || m_MissionDetailsVisible || m_CampaignJournalVisible || m_FieldRecordOpen ||
                ChatInputBlocked) {
                m_Minimap.Hide();
                return;
            }
            var pose = m_PlayerQuery.GetSingleton<LocalTransform>();
            var controller = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().ControllerState;
            // 小地图箭头跟着视角方向。自由转头会加上额外偏移，即使角色身体没转，箭头也能指向看过去的方向。
            var forward = Quaternion.Euler(0, controller.YawDegrees + controller.FreeLookOffset.x, 0) * Vector3.forward;
            m_Minimap.Present(MoonRaidMap.Active, m_ClashView.Map, pose.Position, forward, m_Snapshot.TakenMask);
        }

        private void UpdateRaidMap(bool clash) {
            if (m_RaidMap == null) return;
            if (!MapPlayable || (!clash && !MoonRaidMap.Active)) {
                ResetRaidMap();
                return;
            }
            int epoch = clash ? m_Clash.Snapshot.Epoch : 0;
            string match = clash ? m_Clash.Snapshot.MatchId.ToString() : null;
            // 记录地图属于哪一个 World、战局和赏金阶段。任何一项切换就清掉旧图的缩放和上下文。
            if (!m_MapContextValid || m_MapWorld != m_World || m_MapRaid != m_Snapshot.RaidId || m_MapEpoch != epoch ||
                m_MapMatch != match) {
                SetRaidMapOpen(false);
                m_ClashView.Map.FullMap();
                m_MapOpenedInContext = false;
                m_MapWorld = m_World;
                m_MapRaid = m_Snapshot.RaidId;
                m_MapEpoch = epoch;
                m_MapMatch = match;
                m_MapContextValid = true;
            }
            if (!clash) {
                var pose = m_PlayerQuery.GetSingleton<LocalTransform>();
                m_ClashView.Map.PresentExpedition(MoonRaidMap.Active, pose.Position,
                    math.mul(pose.Rotation, math.forward()), m_Snapshot.TakenMask);
            }
            // 一次按键可能被多个刷新入口看到。记录切换帧，保证一帧里只开或关一次。
            if (MapKeyPressed && m_MapToggleFrame != Time.frameCount) {
                m_MapToggleFrame = Time.frameCount;
                SetRaidMapOpen(!m_MapOpen);
            }
            if (m_MapOpen) Utils.SetCursorVisible(true);
            m_RaidMap.Present((!QuickManualBusy && !m_InventoryVisible && !m_MissionDetailsVisible) || m_MapOpen,
                m_MapOpen, clash, clash ? m_Clash.Snapshot : default);
            if (clash)
                m_ClashView.SetMenuOpen(m_MapOpen || m_InventoryVisible || m_MissionDetailsVisible ||
                                        GameSettings.Instance.IsPauseMenuOpen);
        }

        private void ResetRaidMap() {
            SetRaidMapOpen(false);
            m_RaidMap?.Hide();
            if (m_MapContextValid) m_ClashView?.Map.ResetData();
            m_MapContextValid = m_MapOpenedInContext = false;
            m_MapWorld = null;
            m_MapRaid = m_MapEpoch = 0;
            m_MapMatch = null;
        }

        private void DisposeRaidMap() {
            ResetRaidMap();
            m_RaidMap?.Dispose();
            m_RaidMap = null;
        }
    }
}