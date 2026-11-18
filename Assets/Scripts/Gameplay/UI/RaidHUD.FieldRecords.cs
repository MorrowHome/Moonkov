// 场内可阅读的日志平板。选中目标后把来源、标题和正文打开到 HUD 窗口，读完可关掉并回到正常输入。
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private readonly RaidFieldRecords m_FieldRecords = new();
        private RaidFieldRecords.Tablet? m_FieldRecordFocus;
        private EntityQuery m_FieldRecordClock;
        private VisualElement m_FieldRecordModal;
        private ScrollView m_FieldRecordScroll;
        private Label m_FieldRecordTitle, m_FieldRecordSource, m_FieldRecordBody;
        private Button m_FieldRecordClose;
        private bool m_FieldRecordOpen;

        private void InitializeFieldRecords() {
            m_FieldRecordOpen = false;
            m_FieldRecordModal = new VisualElement { name = "raidFieldRecord" };
            m_FieldRecordModal.AddToClassList("mission-modal");
            m_FieldRecordModal.AddToClassList("field-record");
            m_FieldRecordModal.AddToClassList("moonkov-style-font");
            var sheet = new VisualElement();
            sheet.AddToClassList("mission-sheet");
            m_FieldRecordModal.Add(sheet);
            m_FieldRecordSource = new Label();
            m_FieldRecordSource.AddToClassList("mission-eyebrow");
            sheet.Add(m_FieldRecordSource);
            m_FieldRecordTitle = new Label();
            m_FieldRecordTitle.AddToClassList("mission-title");
            sheet.Add(m_FieldRecordTitle);
            m_FieldRecordScroll = new ScrollView(ScrollViewMode.Vertical)
                { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            m_FieldRecordScroll.AddToClassList("mission-scroll");
            sheet.Add(m_FieldRecordScroll);
            m_FieldRecordBody = new Label();
            m_FieldRecordBody.AddToClassList("field-record-body");
            m_FieldRecordScroll.Add(m_FieldRecordBody);
            var actions = new VisualElement();
            actions.AddToClassList("mission-actions");
            sheet.Add(actions);
            m_FieldRecordClose = new Button(() => SetFieldRecordOpen(false));
            actions.Add(m_FieldRecordClose);
            m_FieldRecordModal.RegisterCallback<NavigationCancelEvent>(evt => {
                if (CloseInventory()) evt.StopImmediatePropagation();
            });
            m_Root.Add(m_FieldRecordModal);
        }

        private void PresentFieldRecords(bool active) {
            var map = MoonRaidMap.Active;
            // 只有本局地图与远征时钟都已同步时才生成日志可见物。有一项丢失就清掉旧局生成的内容。
            if (!active || !map || !m_FieldRecordClock.TryGetSingleton<ExpeditionClockState>(out var clock) ||
                !clock.Ready) {
                ClearFieldRecords();
                return;
            }
            // 时钟标识共享的服务端地图。单人再次出发还会按局号变更日志摆放。
            // 日志物体按会话识别。单人再加局号，让重开一局的物体从当前进度重新布置。
            if (m_FieldRecords.Present(map, clock.SessionId, PlayerProfileClient.IsOffline ? m_Snapshot.RaidId : 0))
                SetFieldRecordOpen(false);
        }

        private bool ReadFieldRecord(bool allowed) {
            if (!allowed || !m_FieldRecordFocus.HasValue) return false;
            // 存在瞄准目标就消耗这次交互的优先权，但真正打开只认刚按下的一帧。
            if (m_InteractionBindings.Operate.WasPressedThisFrame()) OpenFieldRecord(m_FieldRecordFocus.Value);
            return true;
        }

        private void OpenFieldRecord(RaidFieldRecords.Tablet record) {
            MoonkovLocalization.Set(m_FieldRecordSource, record.Source);
            MoonkovLocalization.Set(m_FieldRecordTitle, record.Title);
            MoonkovLocalization.Set(m_FieldRecordBody, record.Body);
            MoonkovLocalization.Set(m_FieldRecordClose, Text("返回战局", "Back to raid"));
            m_FieldRecordScroll.scrollOffset = Vector2.zero;
            SetFieldRecordOpen(true);
        }

        private void SetFieldRecordOpen(bool open) {
            if (m_FieldRecordOpen == open) return;
            m_FieldRecordOpen = open;
            m_FieldRecordModal.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            RaidQuickSlotInput.ReleasePrimary();
            if (GameSettings.Instance != null && GameSettings.Instance.GameState == GlobalGameState.InGame)
                Utils.SetCursorVisible(open || m_WasSettled || GameSettings.Instance.IsPauseMenuOpen);
            // 打开后焦点落在退出按钮，键盘可以马上关窗。鼠标是否恢复锁定已经根据其他菜单一起更新。
            if (open) m_FieldRecordClose.Focus();
        }

        private void ClearFieldRecords() {
            SetFieldRecordOpen(false);
            m_FieldRecordFocus = null;
            m_FieldRecords.Dispose();
        }
    }
}