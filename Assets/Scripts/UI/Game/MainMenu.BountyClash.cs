// 把模式选择器接到仓库和高级连接页。选择结果写进 GameSettings.RequestedMode，出发流程随后读取它。好友房内用房间自己的选模式入口。
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public partial class MainMenu {
        private BountyModeView m_ModePicker;
        private Label m_ModeStatus;
        private Label m_ModeHint;
        private IVisualElementScheduledItem m_ModeRefresh;
        private Button m_StashModeEntry;

        private void InitializeBountyModeUI() {
            m_ModePicker = new BountyModeView(m_MainMenu.Q("Canvas"), mode => {
                // 好友房的模式由房主页面设置。这里的按钮只为普通单人和非好友房连接修改默认玩法。
                if (PlayerProfileClient.IsLan) return;
                GameSettings.Instance.RequestedMode = mode;
                MoonkovTerminal.PresentCompanionPicker(m_MainMenu);
                ConnectionSettings.Instance.ConnectionError = null;
                RefreshModePicker();
            });
            m_StashModeEntry = m_ModePicker.CreateEntry(m_MainMenu.Q(className: "stash-footer"));
            m_ModeHint = m_MainMenu.Q<Label>(className: "stash-footer-hint");
            m_ModePicker.CreateEntry(m_MainMenu.Q("AdvancedConnection"));
            m_ModeStatus = new Label { pickingMode = PickingMode.Ignore };
            m_ModeStatus.AddToClassList("bounty-mode-message");
            m_MainMenu.Q("AdvancedConnection").Add(m_ModeStatus);
            m_ModeRefresh = m_MainMenu.schedule.Execute(RefreshModePicker).Every(250);
            RefreshModePicker();
        }

        private void RefreshModePicker() {
            var mode = GameSettings.Instance.RequestedMode;
            bool clash = mode == RaidGameMode.BountyClash;
            bool offline = PlayerProfileClient.IsOffline;
            // 普通线上小队从自己的房间页面选模式。仓库入口只在对应档案类型下可见。
            bool online = !offline && !PlayerProfileClient.IsLan;
            m_StashModeEntry.style.display = online ? DisplayStyle.None : DisplayStyle.Flex;
            m_ModePicker.Present(mode, offline);
            if (m_ModeHint != null)
                MoonkovLocalization.Set(m_ModeHint,
                    online
                        ? "在线小队：1–4 人合作直接进图，或选择好友对抗；无需凑满人数。"
                        :
                        mode == RaidGameMode.Classic
                            ? "沙2 5v5，空位由人机补齐"
                            :
                            clash && offline
                                ?
                                "SOLO BOUNTY CLASH / Offline stash. Death loses equipment; extraction keeps loot."
                                : "Choose the game mode, then choose how to connect.");
            // 有实际连接错误就优先显示错误。其余时候根据选择的模式解释准备阶段。
            MoonkovLocalization.Set(m_ModeStatus,
                ConnectionSettings.Instance.ConnectionError ?? (clash
                    ?
                    offline
                        ? "SOLO BOUNTY CLASH / You against three AI duos. Prepare your offline loadout, then deploy."
                        : "Online Bounty Clash / waiting for server confirmation. At least two teams must be ready."
                    : mode == RaidGameMode.Classic
                        ? ""
                        : "Original expedition rules. Connection method does not change the game mode."));
        }

        private void DisposeBountyModeUI() {
            m_ModeRefresh?.Pause();
            m_ModePicker?.Dispose();
            m_ModePicker = null;
            m_ModeStatus?.RemoveFromHierarchy();
            m_ModeStatus = null;
        }
    }
}