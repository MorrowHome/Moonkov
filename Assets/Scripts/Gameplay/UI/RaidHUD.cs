// 远征局内界面的总入口。每帧从客户端 World 取战局和角色状态，然后更新背包、交互、任务、撤离提示和结算。
// 想跟一遍按 F 搜刮的过程，从 LateUpdate 到 OpenLootContainer，再看 UpdateInventoryView 怎样处理服务器回执。
using Unity.MP_FPS.DollSinger;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(150)]
    public sealed partial class RaidHUD : MonoBehaviour {
        private World m_World;
        private EntityQuery m_StateQuery, m_PlayerQuery, m_ConnectionQuery;
        private VisualElement m_Root, m_Result, m_Status, m_Inventory;
        private VisualElement m_TaskPanel;
        private Button m_TaskToggle, m_ClashTaskToggle;
        private bool m_TasksCollapsed;
        private Label m_Bag, m_Timer, m_Clock, m_Prompt, m_Exit, m_ResultText, m_SaveStatus;

        private VisualElement m_Interaction,
            m_InteractionDetails,
            m_ExtractionPanel,
            m_ExtractionTrack,
            m_ExtractionFill,
            m_Receipt;

        private AirlockInteractionPrompt m_AirlockPrompt;
        private RaidMinimapView m_Minimap;
        private Label m_InteractionKey, m_InteractionState, m_InteractionRange, m_ReceiptText;
        private int m_FeedbackRaid;
        private int3 m_PreviousSupplies, m_ReceiptSupplies;
        private float m_ReceiptUntil;
        private bool m_ReceiptQueued;
        private Button m_Deploy;
        private bool m_WasSettled;
        private float m_RefreshTimer;
        private RaidSnapshotV2Rpc m_Snapshot;
        private int m_DeployRequestedRaid;
        private uint m_DeployRequestedSequence;
        private uint m_DeployRequestId;
        private IntegerField m_CarryCells;
        private VisualElement m_LoadoutPanel;
        private Label m_LoadoutNote;
        private int m_ResultStep;
        private Client.RaidResultCargoView m_ResultCargo;
        private bool m_InventoryVisible;
        private Button m_ResultBack, m_ResultNext, m_Return;
        private float m_RevealUntil;
        private float m_LootErrorUntil;
        private int m_PreviousCount = -1;
        private static RaidHUD s_Active;
        private int m_MenuEscapeFrame = -1;

        public static bool InventoryOpen =>
            s_Active != null && (s_Active.m_InventoryVisible || s_Active.m_MissionDetailsVisible ||
                                 s_Active.m_CampaignJournalVisible || s_Active.m_FieldRecordOpen ||
                                 s_Active.m_ClassicBuyOpen);

        public static bool InventoryDefenseAllowed =>
            s_Active != null && s_Active.m_InventoryVisible && s_Active.QuickAlive &&
            !s_Active.m_MissionDetailsVisible && !s_Active.m_CampaignJournalVisible && !s_Active.m_ClashLobbyPointer &&
            !ChatInputBlocked && !s_Active.CinematicInputBlocked && !MapInputBlocked &&
            !GameSettings.Instance.IsPauseMenuOpen && Application.isFocused &&
            !QuickTyping(s_Active.m_Root.panel?.focusController.focusedElement as VisualElement);

        public static bool PointerRequested =>
            ChatInputBlocked || (s_Active != null && s_Active.CinematicInputBlocked) || InventoryOpen ||
            MapInputBlocked || (s_Active != null &&
                                (s_Active.m_WasSettled || s_Active.m_ArenaFinished || s_Active.m_ClashLobbyPointer));

        public static bool TimedActionInputActive =>
            s_Active != null && (s_Active.GrenadeBusy || s_Active.TimedActionActive ||
                                 s_Active.QuickTacticalActionPending || s_Active.MeleeTimedActionActive);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPresentation() { s_Active = null; }

        public static bool CloseInventory() {
            if (s_Active == null) return false;
            // Input System 和 UI Toolkit 可能在同一帧各收到 Esc。记下已处理的帧，第二次就不会继续关下一层或打开暂停菜单。
            if (s_Active.m_MenuEscapeFrame == Time.frameCount) return true;
            if (!CloseActiveMenu()) return false;
            s_Active.m_MenuEscapeFrame = Time.frameCount;
            RaidQuickSlotInput.ReleasePrimary();
            return true;
        }

        private static bool CloseActiveMenu() {
            // 按面板优先级逐层检查。聊天先处理输入法，然后才是日志、地图和任务窗。背包内的浮窗由背包自己关。
            if (s_Active != null && s_Active.CloseChatForEscape()) return true;
            if (s_Active != null && s_Active.m_FieldRecordOpen) {
                s_Active.SetFieldRecordOpen(false);
                return true;
            }
            if (s_Active != null && (s_Active.m_MapOpen || s_Active.m_MapEscapeFrame == Time.frameCount)) {
                s_Active.CloseRaidMapFromUI();
                return true;
            }
            if (s_Active != null && s_Active.m_CampaignJournalVisible) {
                s_Active.SetCampaignJournal(false);
                return true;
            }
            if (s_Active != null && s_Active.m_MissionDetailsVisible) {
                s_Active.SetMissionDetails(false);
                return true;
            }
            if (!InventoryOpen) return RaidQuickSlotInput.CloseAdjustmentMenu();
            if (!s_Active.m_HealthSelected && s_Active.m_InventoryView.Escape()) return true;
            s_Active.SetInventory(false);
            return true;
        }

        private void OnMenuKeyDown(KeyDownEvent evt) {
            if (evt.keyCode != KeyCode.Escape || GameSettings.Instance.GameState != GlobalGameState.InGame ||
                m_ChatView?.Composing == true)
                return;
            if (CloseInventory()) evt.StopImmediatePropagation();
        }

        private void OnEnable() {
            m_MenuEscapeFrame = -1;
            m_ReturnGuidance.Reset();
            m_DeployRequestedRaid = 0;
            m_DeployRequestedSequence = 0;
            m_DeployRequestId = 0;
            m_WasSettled = false;
            s_Active = this;
            m_ResultStep = 0;
            m_InventoryVisible = false;
            m_PreviousCount = -1;
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            MoonkovAudio.BindUI(m_Root);
            m_Root.RegisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
            m_Root.pickingMode = PickingMode.Ignore;
            Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(m_Root);
            var rightRail = m_Root.Q<ScrollView>("raidRightRail");
            rightRail.Add(m_Root.Q("raidExtractionPanel"));
            m_TaskPanel = m_Root.Q("raidTaskPanel");
            m_TaskPanel.Insert(0, m_Root.Q("raidMinimap"));
            var supplementary = m_Root.Q("raidSupplementaryHud");
            supplementary.Add(m_Root.Q("raidStatus"));
            supplementary.Add(m_Root.Q(className: "raid-cargo-instrument"));
            supplementary.Add(m_Root.Q("raidSurveyReadout"));
            supplementary.Add(m_Root.Q("raidReceipt"));
            m_TaskToggle = m_Root.Q<Button>("raidTaskToggle");
            m_TaskToggle.clicked += ToggleTasks;
            SetTasksCollapsed(false);
            m_TaskPanel.style.display = DisplayStyle.None;
            InitializeTelemetry();
            m_Minimap = new RaidMinimapView(m_Root);
            m_AirlockPrompt = GetComponent<AirlockInteractionPrompt>();
            if (!m_AirlockPrompt) m_AirlockPrompt = gameObject.AddComponent<AirlockInteractionPrompt>();
            m_AirlockPrompt.Initialize(GetComponent<UIDocument>());
            m_Status = m_Root.Q("raidStatus");
            m_Timer = m_Root.Q<Label>("raidTimer");
            m_Bag = m_Root.Q<Label>("raidBag");
            m_Clock = m_Root.Q<Label>("raidClock");
            m_Exit = m_Root.Q<Label>("raidExtraction");
            m_Prompt = m_Root.Q<Label>("raidPrompt");
            m_Interaction = m_Root.Q("raidInteraction");
            m_InteractionDetails = m_Root.Q("raidInteractionDetails");
            m_InteractionKey = m_Root.Q<Label>("raidInteractionKey");
            m_InteractionState = m_Root.Q<Label>("raidInteractionState");
            m_InteractionRange = m_Root.Q<Label>("raidInteractionRange");
            m_ExtractionPanel = m_Root.Q("raidExtractionPanel");
            m_ExtractionTrack = m_Root.Q("raidExtractionTrack");
            m_ExtractionFill = m_Root.Q("raidExtractionFill");
            m_Receipt = m_Root.Q("raidReceipt");
            m_ReceiptText = m_Root.Q<Label>("raidReceiptText");
            m_FeedbackRaid = 0;
            m_ReceiptUntil = 0;
            m_ReceiptQueued = false;
            m_ReceiptSupplies = default;
            m_Result = m_Root.Q("raidResult");
            m_ResultText = m_Root.Q<Label>("raidResultText");
            m_SaveStatus = m_Root.Q<Label>("raidSaveStatus");
            m_Deploy = m_Root.Q<Button>("raidDeploy");
            m_Deploy.clicked += Deploy;
            m_LoadoutPanel = m_Root.Q("raidLoadout");
            m_CarryCells = m_Root.Q<IntegerField>("raidCarryCells");
            m_CarryCells.RegisterValueChangedCallback(CarryCellsChanged);
            m_LoadoutNote = m_Root.Q<Label>("raidLoadoutNote");
            m_ResultBack = m_Root.Q<Button>("raidResultBack");
            m_ResultBack.clicked += ResultBack;
            m_ResultNext = m_Root.Q<Button>("raidResultNext");
            m_ResultNext.clicked += ResultNext;
            m_Return = m_Root.Q<Button>("raidReturn");
            m_Return.clicked += ReturnToShip;
            m_Return.SetEnabled(GameManager.CanUseMainMenu);
            m_ResultCargo = new Client.RaidResultCargoView(m_Root.Q<ScrollView>("raidCargoManifest"));
            m_Inventory = m_Root.Q("raidInventory");
            m_InventoryTitle = m_Root.Q<Label>(className: "raid-inventory-title");
            m_EquipmentHost = m_Root.Q("raidInventoryHost");
            m_HealthHost = m_Root.Q("raidHealthHost");
            // 格子控件只负责收集拖放等操作。这里把局内搬运、用药、使用补给和丢弃接到 RaidHUD 的发送函数。
            m_InventoryView = new Client.ContainerInventoryView(m_EquipmentHost, false, SendInventoryMove, UseMedical,
                UseConsumable, DropItem);
            m_InventoryView.OperationAudioEnabled = false;
            m_InventoryView.ShieldRechargeAvailable = CanRechargeShieldCell;
            m_HealthView = new Client.RaidHealthView(m_HealthHost, TreatPart, ConsumeSupply);
            m_EquipmentTab = m_Root.Q<Button>("raidEquipmentTab");
            m_EquipmentTab.clicked += EquipmentTab;
            m_HealthTab = m_Root.Q<Button>("raidHealthTab");
            m_HealthTab.clicked += HealthTab;
            m_HealthSelected = false;
            m_NextOxygenAlert = 0;
            m_OxygenFeedback.Reset();
            m_OxygenWarning = m_Root.Q<Label>("raidOxygenWarning");
            m_OxygenPanel = m_Root.Q("raidOxygenPanel");
            m_OxygenFill = m_Root.Q("raidOxygenFill");
            m_OxygenReserve = m_Root.Q<Label>("raidOxygenReserve");
            m_OxygenTime = m_Root.Q<Label>("raidOxygenTime");
            m_OxygenAir = m_Root.Q<Label>("raidOxygenAir");
            m_ActionView = new Client.RaidTimedActionView(CancelProgressAction);
            m_Root.Add(m_ActionView);
            ResetTimedAction();
            m_InventorySequence = 0;
            m_InventoryRequestId = 0;
            m_InventoryAudio.Reset();
            m_LootRevealAudio.Reset();
            m_ClassicPurchaseSequence = 0;
            m_ClassicPurchaseMessage = null;
            m_ClassicPurchaseMessageUntil = 0;
            m_InventoryRequestPending = false;
            m_LootOpenPending = false;
            m_OpenedLootId = -1;
            m_DissectingLootId = -1;
            var closePack = m_Root.Q<Button>("raidPackClose");
            closePack.clicked += ClosePack;
            m_SwitchDrop = new Button(SwitchNearbyDrop) { name = "raidSwitchDrop", text = "SWITCH NEARBY BAG" };
            m_SwitchDrop.style.display = DisplayStyle.None;
            closePack.parent.Insert(closePack.parent.IndexOf(closePack), m_SwitchDrop);
            MoonkovLocalization.Bind(m_Root);
            InitializeMissions();
            InitializeCampaignHUD();
            InitializeResearchQuests();
            InitializeFieldRecords();
            InitializeQuickSlots();
            InitializeMelee();
            InitializeBountyClash();
            m_ClashTaskToggle = m_ClashView.Tasks.Q<Button>("raidTaskToggle");
            m_ClashTaskToggle.clicked += ToggleTasks;
            SetTasksCollapsed(m_TasksCollapsed);
            InitializeRaidMap();
            if (ArenaMatch.UsesTemporaryKit(GameSettings.Instance.RequestedMode)) InitializeArenaHud();
            InitializeSpecialEquipment();
            InitializeChat();
        }

        private void ToggleTasks() { SetTasksCollapsed(!m_TasksCollapsed); }

        private void SetTasksCollapsed(bool collapsed) {
            m_TasksCollapsed = collapsed;
            m_TaskPanel.EnableInClassList("is-collapsed", collapsed);
            MoonkovLocalization.Set(m_TaskToggle.Q<Label>("raidTaskChevron"), collapsed ? "+" : "−");
            if (m_ClashTaskToggle != null) {
                m_ClashView.Tasks.EnableInClassList("is-collapsed", collapsed);
                MoonkovLocalization.Set(m_ClashTaskToggle.Q<Label>("raidTaskChevron"), collapsed ? "+" : "−");
            }
        }

        private void UpdateTaskPanel(bool live) {
            bool visible = live && !m_InventoryVisible && !m_MapOpen && !m_MissionDetailsVisible &&
                           !m_CampaignJournalVisible && !ChatInputBlocked && !CinematicInputBlocked &&
                           !GameSettings.Instance.IsPauseMenuOpen && (m_ClashMode ||
                                                                      m_MissionHud.style.display == DisplayStyle.Flex ||
                                                                      (m_CampaignHud != null &&
                                                                       !m_CampaignHud.ClassListContains(
                                                                           "campaign-hidden")) ||
                                                                      (m_ResearchList != null &&
                                                                       m_ResearchList.style.display ==
                                                                       DisplayStyle.Flex) ||
                                                                      m_ExtractionPanel
                                                                          .ClassListContains("is-visible"));
            bool showMinimap = m_Minimap.Visible;
            m_TaskPanel.EnableInClassList("has-minimap", showMinimap);
            m_TaskPanel.style.display =
                !m_ClashMode && (visible || showMinimap) ? DisplayStyle.Flex : DisplayStyle.None;
            m_TaskToggle.style.display = visible && !m_ClashMode ? DisplayStyle.Flex : DisplayStyle.None;
            m_ClashView.Tasks.style.display = visible && m_ClashMode ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible && Application.isFocused && RaidKeyBindings.Pressed(RaidKey.Status) &&
                !Client.QuickSlotBindings.Typing(m_Root.panel?.focusController.focusedElement as VisualElement))
                ToggleTasks();
        }

        private readonly RaidReturnGuidance m_ReturnGuidance = new();

        private void InitializeWorld() {
            m_ReturnGuidance.Reset();
            m_InventoryAudio.Reset();
            m_LootRevealAudio.Reset();
            m_InventorySequence = 0;
            ResetTimedAction();
            m_ActionRaidId = 0;
            ResetBountyConnection();
            foreach (var world in World.All) {
                if (!world.IsCreated || !world.IsClient() || world.IsThinClient()) continue;
                m_World = world;
                m_StateQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RaidClientState>());
                m_FieldRecordClock =
                    world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ExpeditionClockState>());
                m_PlayerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>(), ComponentType.ReadOnly<LocalTransform>());
                m_ConnectionQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(),
                    ComponentType.ReadOnly<NetworkStreamInGame>());
                m_InventoryQuery =
                    world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RaidInventoryClientState>());
                break;
            }
        }

        private void HideRaidPresentation(bool inGame) {
            ClearFieldRecords();
            m_InteractionOutline.Hide();
            UpdateTelemetry(false);
            ClearInventoryBackdrop();
            if (inGame) {
                m_TaskPanel.style.display = DisplayStyle.None;
                UpdateChat();
            } else {
                ResetChat();
            }
            ResetRaidCinematics();
            m_Minimap.Hide();
            ResetRaidMap();
            PresentMelee(false);
            PresentQuickSlots(false);
            HideBountyClash();
        }

        private void LateUpdate() {
            bool inGame = GameSettings.Instance.GameState == GlobalGameState.InGame;
            m_Root.style.display = inGame ? DisplayStyle.Flex : DisplayStyle.None;
            if (inGame && (m_World == null || !m_World.IsCreated)) InitializeWorld();
            if (!inGame || m_World == null || !m_World.IsCreated || m_StateQuery.IsEmptyIgnoreFilter) {
                HideRaidPresentation(inGame);
                return;
            }
            // 先更新不同玩法都用到的库存和会话信息。竞技场或经典爆破接管后直接走自己的 HUD，后面是远征和赏金冲突的流程。
            m_Snapshot = m_StateQuery.GetSingleton<RaidClientState>().Snapshot;
            UpdateTelemetry(m_Snapshot.RaidId > 0);
            if (m_ActionRaidId != m_Snapshot.RaidId) {
                ResetTimedAction();
                m_ActionRaidId = m_Snapshot.RaidId;
            }
            UpdateInventoryView();
            if (m_ArenaHud != null) {
                PresentArenaHud();
                return;
            }
            bool clash = PresentBountyClash();
            UpdateRaidCinematics(clash);
            UpdateChat();
            if (CinematicInputBlocked)
                ResetRaidMap();
            else
                UpdateRaidMap(clash);
            bool ready = m_Snapshot.RaidId > 0;
            bool settled = ready && m_Snapshot.Phase != RaidPhase.Active;
            if ((RaidKeyBindings.Pressed(RaidKey.Inventory) ||
                 (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)) && !ChatInputBlocked &&
                ready && !settled && !m_ClashActionsBlocked && !CinematicInputBlocked &&
                !GameSettings.Instance.IsPauseMenuOpen)
                if (!m_InventoryRequestPending || TimedActionActive || QuickTacticalActionPending)
                    SetInventory(!m_InventoryVisible);
            if (settled && m_InventoryVisible) SetInventory(false);
            UpdateInventoryBackdrop();
            if (!settled && m_WasSettled) {
                m_ResultStep = 0;
                Utils.SetCursorVisible(false);
            }
            // 这是首次进入结算的那一帧。将分页和焦点切到结束状态，之后只更新快照里的保存进度。
            if (settled && !m_WasSettled) {
                m_ResultStep = 0;
                if (m_Snapshot.Phase == RaidPhase.Extracted)
                    MoonkovAudio.Confirm();
                else
                    MoonkovAudio.Error();
            }
            if (m_InventoryVisible) Utils.SetCursorVisible(true);
            MoonRaidMap.Active.ShowLoot(m_Snapshot.TakenMask, ready);
            var deathBags =
                m_World.EntityManager.GetComponentObject<RaidDeathBagClientState>(m_StateQuery.GetSingletonEntity());
            MoonRaidMap.Active.ShowDeathBags(deathBags);
            int viewing = m_DissectingLootId >= 0 ? m_DissectingLootId : m_OpenedLootId;
            bool switchable = m_InventoryVisible && IsDroppedLoot(viewing) &&
                              m_PlayerQuery.HasSingleton<LocalTransform>() && SelectNearbyDrop(deathBags,
                                  m_PlayerQuery.GetSingleton<LocalTransform>().Position, viewing) >= 0;
            m_SwitchDrop.style.display = switchable ? DisplayStyle.Flex : DisplayStyle.None;
            m_SwitchDrop.SetEnabled(!m_InventoryRequestPending || TimedActionActive);
            if (settled) RefreshSettlementControls();
            m_WasSettled = settled;
            m_Result.style.display = settled && !clash && !(m_Cinematic?.HidesHUD ?? false)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            int nearest = -1;
            float distance = RaidRules.PickupRange;
            Vector3 position = default;
            bool alive = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() &&
                         m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().CurrentHealth > 0 &&
                         !m_ClashActionsBlocked && !CinematicInputBlocked;
            PresentFieldRecords(ready && !settled && !clash && !m_TutorialConfigured);
            if (!alive) SetFieldRecordOpen(false);
            bool haloBusy = alive && HaloTransferRules.Occupied(m_PlayerQuery.GetSingleton<PredictedPlayerGhost>());
            bool haloBlocksLoot =
                haloBusy && !HaloSupportRules.Anchored(m_PlayerQuery.GetSingleton<PredictedPlayerGhost>());
            PresentMelee(alive && ready && !settled);
            PresentQuickSlots(alive && ready && !settled);
            CloseLootWhenIdle();
            UpdateMinimap(alive && ready && !settled && !clash);
            UpdateGrenades(alive && ready && !settled && !DarkSightRequested && !MapInputBlocked && !haloBusy);
            // 只有角色还能行动、没有菜单接管、库存没有在等请求时，才允许按键操作世界里的目标。
            bool liveInteraction = alive && ready && !settled && !MapInputBlocked && !DarkSightRequested &&
                                   !m_ClashInteractionFocused && !ChatInputBlocked && !m_InventoryVisible &&
                                   !m_MissionDetailsVisible && !m_CampaignJournalVisible && !m_FieldRecordOpen &&
                                   !m_InventoryRequestPending && !GameSettings.Instance.IsPauseMenuOpen &&
                                   Application.isFocused && UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            // 下面几种操作共享一枚按键。研究和任务设备先判断是否接管，再检查日志、战术道具、气闸和搜刮。
            if (alive) position = m_PlayerQuery.GetSingleton<LocalTransform>().Position;
            SelectWorldInteraction(liveInteraction && !haloBlocksLoot, !clash, position, deathBags, out nearest,
                out distance, !haloBusy);
            bool researchInteraction = ResearchInteractionAllowed(liveInteraction, clash);
            bool researchControl =
                UpdateResearchQuests(alive && ready && !settled && !clash, researchInteraction, position);
            bool missionControl = UpdateMissions(alive && ready && !settled && !clash && !haloBusy);
            missionControl |= researchControl;
            m_AirlockPrompt.BindingLabel = m_InteractionBindings.Label(m_InteractionBindings.Operate);
            m_AirlockPrompt.SetPresentation(liveInteraction && !missionControl && m_WorldControlFocused);
            PresentTimedAction();
            UpdateCampaignHUD(alive && ready && !settled && !clash);
            UpdateTaskPanel(alive && ready && !settled);
            UpdateTutorialHUD(alive && ready && !settled && !clash);
            bool readingTarget = ReadFieldRecord(liveInteraction && !missionControl);
            bool tacticalInteraction = TryQuickTacticalInteraction(liveInteraction && !haloBusy,
                missionControl || readingTarget, m_WorldControlFocused);
            if (liveInteraction && !missionControl && !readingTarget && !haloBlocksLoot) {
                bool controlPressed = !haloBusy && !tacticalInteraction && !clash && m_WorldControlFocused &&
                                      m_InteractionBindings.Operate.WasPressedThisFrame() &&
                                      m_AirlockPrompt.TryInteract(Camera.main, position, m_World);
                if (nearest >= 0 && m_InteractionBindings.Loot.WasPressedThisFrame() && !tacticalInteraction &&
                    !controlPressed && !m_WorldControlFocused)
                    OpenLootContainer(nearest);
            }
            // 每个渲染帧采样输入，显示文字限在 10 Hz 更新。
            // 按键和瞄准可以逐帧响应，较重的界面文字按间隔更新。这里先处理选择与操作，再到后面刷文字。
            bool interactionVisible = liveInteraction && !m_FieldRecordOpen;
            UpdateInteractionOutlines(interactionVisible, !clash, !haloBlocksLoot, !haloBusy, researchInteraction,
                position, deathBags);
            if (!interactionVisible) {
                m_Interaction.EnableInClassList("is-visible", false);
                m_AirlockPrompt.RefreshSelection(Camera.main, position, false);
            }
            m_RefreshTimer -= Time.deltaTime;
            if (m_RefreshTimer > 0) return;
            m_RefreshTimer = 0.1f;
            RefreshHealth();
            bool deathBag = nearest >= RaidLootContainers.FirstDeathBagId &&
                            nearest < RaidLootContainers.FirstMissionCacheId;
            bool empty = deathBag
                ? deathBags.Bags[nearest].Empty
                : nearest >= 0 && nearest < RaidLootContainers.FirstDeathBagId &&
                  (m_Snapshot.TakenMask & (1UL << nearest)) != 0;
            bool alien = deathBag && deathBags.Bags[nearest].CharacterIndex == -1;
            RefreshInteraction(nearest, distance, deathBag, empty,
                interactionVisible && !missionControl && !m_WorldControlFocused, alien, IsDroppedLoot(nearest));
            RefreshHaloTransferInteraction(interactionVisible && !missionControl && !readingTarget &&
                                           !m_WorldControlFocused && nearest < 0 &&
                                           Time.unscaledTime >= m_LootErrorUntil);
            bool extracting = alive && !settled && m_Snapshot.ExtractionRemaining >= 0;
            bool showReturn = !m_InventoryVisible && !MapInputBlocked && !m_MissionDetailsVisible &&
                              !m_CampaignJournalVisible && !GameSettings.Instance.IsPauseMenuOpen &&
                              !CinematicInputBlocked;
            if (m_ReturnGuidance.Update(m_Snapshot.RaidId, m_Snapshot.TimeLeft, ready && alive && !settled && !clash,
                    extracting, showReturn, Time.unscaledTime)) {
                m_RevealUntil = Time.unscaledTime + 8;
                MoonkovAudio.Play(MoonkovAudio.Library?.Notification, Vector3.zero);
            }
            if (clash) {
                m_Status.style.display = DisplayStyle.None;
                m_ExtractionPanel.RemoveFromClassList("is-visible");
                m_OxygenPanel.style.display = DisplayStyle.None;
                m_OxygenWarning.style.display = DisplayStyle.None;
                m_Clock.text = "";
                m_Exit.text = "";
                return;
            }
            RefreshExpeditionStatus(ready, alive, settled, extracting, showReturn, position);
        }

        private void RefreshInteraction(
            int nearest,
            float distance,
            bool deathBag,
            bool empty,
            bool allowed,
            bool alien = false,
            bool dropped = false) {
            m_Interaction.EnableInClassList("is-halo-transfer", false);
            bool record = m_FieldRecordFocus.HasValue;
            bool error = !record && Time.unscaledTime < m_LootErrorUntil;
            bool visible = allowed && (nearest >= 0 || record || error);
            m_Interaction.EnableInClassList("is-visible", visible);
            if (!visible) return;
            m_Interaction.EnableInClassList("is-error", error);
            m_Interaction.EnableInClassList("is-empty", !error && empty);
            MoonkovLocalization.Set(m_InteractionKey,
                error ? "!" : m_InteractionBindings.Label(m_InteractionBindings.Loot));
            m_InteractionDetails.style.display = error ? DisplayStyle.None : DisplayStyle.Flex;
            if (record) {
                MoonkovLocalization.Set(m_Prompt, Client.ResearchQuestCopy.Text("读取日志", "Read field log"));
                MoonkovLocalization.Set(m_InteractionState, m_FieldRecordFocus.Value.Title);
                MoonkovLocalization.Set(m_InteractionRange, $"{distance:0.0} m");
                return;
            }
            if (error) {
                MoonkovLocalization.Set(m_Prompt,
                    m_DissectionInterrupted
                        ? "搜索中断。请留在容器旁，保持静止并避免受伤。"
                        : "Cannot reach this container. Move closer with a clear line of sight.");
                return;
            }
            // 沿用已经翻译的动作文字。实体按键现有自己的视觉标记。
            string prompt = RaidFurnitureLoot.IsFurniture(nearest) ? "搜索 " + RaidFurnitureLoot.NameFor(nearest) :
                dropped ? "搜索地面丢弃物" :
                alien ? MoonkovLocalization.Text("[F]  DISSECT / SEARCH ALIEN REMAINS") :
                deathBag ? MoonkovLocalization.Text(m_ClassicHud ? "SEARCH" : "[F]  SEARCH FALLEN EXPEDITION") :
                MoonkovLocalization.Format($"[F]  OPEN SUPPLY CACHE / {nearest + 1:00}");
            if (prompt.StartsWith("[F]", System.StringComparison.Ordinal)) prompt = prompt.Substring(3).TrimStart();
            MoonkovLocalization.Set(m_Prompt, prompt);
            MoonkovLocalization.Set(m_InteractionState,
                RaidFurnitureLoot.IsFurniture(nearest) ? "小型容器" : empty ? "EMPTY" : "CONTENTS");
            MoonkovLocalization.Set(m_InteractionRange, $"{distance:0.0} m");
        }

        private void RefreshSupplyReceipt(bool active) {
            var supplies = new int3(m_Snapshot.Dust, m_Snapshot.Alloy, m_Snapshot.Cells);
            if (m_FeedbackRaid != m_Snapshot.RaidId || !active) {
                m_FeedbackRaid = m_Snapshot.RaidId;
                m_PreviousSupplies = supplies;
                m_ReceiptSupplies = default;
                m_ReceiptQueued = false;
                m_ReceiptUntil = 0;
            } else {
                // 服务器快照确认随身资源增加时才弹拾取结果。点箱子只是尝试开始搜索。
                // 打开箱子只是开始搜索。等服务器快照里随身资源确实增加，才做拾取提示。
                var gained = math.max(supplies - m_PreviousSupplies, 0);
                m_PreviousSupplies = supplies;
                if (math.any(gained > 0)) {
                    // 拿到转移确认后播放拾取声，各种物品都用这一流程。
                    if (Time.unscaledTime >= m_ReceiptUntil && !m_ReceiptQueued) m_ReceiptSupplies = default;
                    m_ReceiptSupplies += gained;
                    m_ReceiptUntil = Time.unscaledTime + 3.2f;
                    m_ReceiptQueued = m_InventoryVisible;
                    string text = "";
                    AppendReceipt(ref text, m_ReceiptSupplies.x, "Moon dust");
                    AppendReceipt(ref text, m_ReceiptSupplies.y, "Lunar alloy");
                    AppendReceipt(ref text, m_ReceiptSupplies.z, "Energy cell");
                    MoonkovLocalization.Set(m_ReceiptText, text);
                }
            }
            if (m_ReceiptQueued && !m_InventoryVisible) {
                m_ReceiptQueued = false;
                m_ReceiptUntil = Time.unscaledTime + 3.2f;
            }
            m_Receipt.EnableInClassList("is-visible",
                active && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen &&
                Time.unscaledTime < m_ReceiptUntil);
        }

        private static void AppendReceipt(ref string text, int amount, string item) {
            if (amount <= 0) return;
            if (text.Length > 0) text += "\n";
            text += $"+{amount}  {MoonkovLocalization.Text(item)}";
        }

        private void CarryCellsChanged(ChangeEvent<int> evt) {
            PlayerProfileClient.SelectCarryCells(evt.newValue);
            m_CarryCells.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
        }

        // 结算保存结束后才可以再次出发。受大厅管理的对局要退回小队，下一局由大厅另起一张地图。
        private void Deploy() {
            // 大厅管理的战局结束后回原小队。下次出发要创建新的地图会话。
            if (OnlineLobbyClient.InInstance) {
                ReturnToShip();
                return;
            }
            if (m_ClashMode) return;
            if (m_Snapshot.RaidId <= 0 || m_Snapshot.Phase == RaidPhase.Active || m_Snapshot.DeployPending ||
                m_DeployRequestedRaid == m_Snapshot.RaidId || m_Snapshot.SaveState == RaidSaveState.Saving ||
                m_Snapshot.SaveState == RaidSaveState.Retrying)
                return;
            uint requestId = m_Snapshot.LoadoutRequestId + 1;
            // 请求带上已经结算的局号和想携带的电池数。成功发送后立即锁住出发按钮，等待房主确认配装。
            if (Send(new RaidDeployRpc {
                    SettledRaidId = m_Snapshot.RaidId, CarryCells = PlayerProfileClient.CarryCells,
                    RequestId = requestId
                })) {
                GameSettings.Instance.IsPauseMenuOpen = false;
                m_DeployRequestedRaid = m_Snapshot.RaidId;
                m_DeployRequestedSequence = m_Snapshot.Sequence;
                m_DeployRequestId = requestId;
                m_Deploy.SetEnabled(false);
            }
        }

        // Netcode 通过一只临时 ECS 实体发 RPC。这里放进消息组件和目标连接，实际发送由网络系统完成。
        private bool Send<T>(T rpc) where T : unmanaged, IRpcCommand {
            if (m_World == null || !m_World.IsCreated || !m_ConnectionQuery.HasSingleton<NetworkId>()) return false;
            var em = m_World.EntityManager;
            var entity = em.CreateEntity();
            em.AddComponentData(entity, rpc);
            em.AddComponentData(entity,
                new SendRpcCommandRequest { TargetConnection = m_ConnectionQuery.GetSingletonEntity() });
            return true;
        }

        private void OnDisable() {
            ReleaseResearchHold();
            m_ResearchField.Clear();
            m_ResearchRadio.Dispose();
            ClearFieldRecords();
            m_InteractionOutline.Dispose();
            ClearInventoryBackdrop();
            m_Root?.UnregisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
            if (m_TaskToggle != null) m_TaskToggle.clicked -= ToggleTasks;
            if (m_ClashTaskToggle != null) m_ClashTaskToggle.clicked -= ToggleTasks;
            m_ClashTaskToggle = null;
            m_InteractionBindings?.Dispose();
            m_InteractionBindings = null;
            DisposeTelemetry();
            DisposeChat();
            DisposeCampaignHUD();
            ResetRaidCinematics();
            DisposeRaidMap();
            if (m_ClassicBomb) Destroy(m_ClassicBomb);
            m_ClassicBomb = null;
            m_ClassicHeld = false;
            m_ArenaHud = null;
            m_ArenaFinished = false;
            DisposeBountyClash();
            DisposeMelee();
            DisposeQuickSlots();
            CancelMissionHold();
            RaidMissionSite.Active?.StopPresentation();
            m_Minimap?.Dispose();
            m_Minimap = null;
            m_ClassicMinimap?.Dispose();
            m_ClassicMinimap = null;
            m_AirlockPrompt?.RefreshSelection(null, default, false);
            if (m_Root != null) MoonkovAudio.UnbindUI(m_Root);
            m_InventoryView?.Dispose();
            m_InventoryView = null;
            m_ClassicShop?.Dispose();
            m_ClassicShop = null;
            m_HealthView?.Dispose();
            m_HealthView = null;
            if (m_EquipmentTab != null) m_EquipmentTab.clicked -= EquipmentTab;
            if (m_HealthTab != null) m_HealthTab.clicked -= HealthTab;
            m_CarryCells?.UnregisterValueChangedCallback(CarryCellsChanged);
            if (m_World != null && m_World.IsCreated) {
                m_StateQuery.Dispose();
                m_PlayerQuery.Dispose();
                m_ConnectionQuery.Dispose();
                m_InventoryQuery.Dispose();
                m_FieldRecordClock.Dispose();
            }
            m_World = null;
            if (s_Active == this) s_Active = null;
            if (m_InventoryVisible || m_MissionDetailsVisible)
                Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
            m_MissionDetailsVisible = false;
            m_InventoryVisible = false;
            m_Root?.Clear();
        }
    }
}