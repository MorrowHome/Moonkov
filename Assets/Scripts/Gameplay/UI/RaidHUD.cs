using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class RaidHUD : MonoBehaviour
    {
        private World m_World;
        private EntityQuery m_StateQuery, m_PlayerQuery, m_ConnectionQuery;
        private VisualElement m_Root, m_Result, m_Status, m_Inventory;
        private Client.ContainerInventoryView m_InventoryView;
        private Client.RaidHealthView m_HealthView;
        private VisualElement m_EquipmentHost, m_HealthHost;
        private Button m_EquipmentTab, m_HealthTab;
        private Label m_OxygenWarning, m_OxygenReserve, m_OxygenTime, m_OxygenAir;
        private VisualElement m_OxygenPanel, m_OxygenFill;
        private bool m_HealthSelected;
        private float m_NextOxygenAlert;
        private EntityQuery m_InventoryQuery;
        private uint m_InventorySequence, m_InventoryRequestId;
        private float m_InventoryRequestedAt;
        private bool m_InventoryRequestPending;
        private bool m_LootOpenPending;
        private int m_DissectingLootId=-1;
        private float m_DissectionEndsAt;
        private bool m_DissectionInterrupted;
        private int m_OpenedLootId=-1;
        private Label m_InventoryTitle;
        private Label m_Bag, m_Timer, m_Clock, m_Prompt, m_Exit, m_ResultText, m_SaveStatus;
        private VisualElement m_Interaction, m_InteractionDetails, m_ExtractionPanel, m_ExtractionTrack, m_ExtractionFill, m_Receipt;
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
        private RaidSnapshotRpc m_Snapshot;
        private int m_DeployRequestedRaid;
        private uint m_DeployRequestedSequence;
        private uint m_DeployRequestId;
        private IntegerField m_CarryCells;
        private VisualElement m_LoadoutPanel;
        private Label m_LoadoutNote;
        private int m_ResultStep;
        private bool m_InventoryVisible;
        private Button m_ResultBack, m_ResultNext, m_Return;
        private float m_RevealUntil;
        private float m_LootErrorUntil;
        private int m_PreviousCount = -1;
        private static RaidHUD s_Active;
        public static bool InventoryOpen => s_Active != null && (s_Active.m_InventoryVisible || s_Active.m_MissionDetailsVisible);
        public static bool PointerRequested => InventoryOpen || (s_Active != null && s_Active.m_WasSettled);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPresentation() => s_Active = null;
        public static bool CloseInventory()
        {
            if (s_Active != null && s_Active.m_MissionDetailsVisible) { s_Active.SetMissionDetails(false); return true; }
            if (!InventoryOpen) return false;
            if (!s_Active.m_HealthSelected && s_Active.m_InventoryView.Escape()) return true;
            s_Active.SetInventory(false); return true;
        }

        private void OnEnable()
        {
            m_DeployRequestedRaid = 0;
            m_DeployRequestedSequence = 0;
            m_DeployRequestId = 0;
            m_WasSettled = false;
            s_Active = this; m_ResultStep = 0; m_InventoryVisible = false; m_PreviousCount = -1;
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            MoonkovAudio.BindUI(m_Root);
            m_Root.pickingMode = PickingMode.Ignore;
            Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(m_Root);
            m_Minimap = new RaidMinimapView(m_Root);
            m_AirlockPrompt = GetComponent<AirlockInteractionPrompt>();
            if (!m_AirlockPrompt) m_AirlockPrompt = gameObject.AddComponent<AirlockInteractionPrompt>();
            m_AirlockPrompt.Initialize(GetComponent<UIDocument>());
            m_Status = m_Root.Q("raidStatus"); m_Timer = m_Root.Q<Label>("raidTimer"); m_Bag = m_Root.Q<Label>("raidBag");
            m_Clock = m_Root.Q<Label>("raidClock");
            m_Exit = m_Root.Q<Label>("raidExtraction"); m_Prompt = m_Root.Q<Label>("raidPrompt");
            m_Interaction = m_Root.Q("raidInteraction"); m_InteractionDetails = m_Root.Q("raidInteractionDetails");
            m_InteractionKey = m_Root.Q<Label>("raidInteractionKey");
            m_InteractionState = m_Root.Q<Label>("raidInteractionState"); m_InteractionRange = m_Root.Q<Label>("raidInteractionRange");
            m_ExtractionPanel = m_Root.Q("raidExtractionPanel"); m_ExtractionTrack = m_Root.Q("raidExtractionTrack");
            m_ExtractionFill = m_Root.Q("raidExtractionFill");
            m_Receipt = m_Root.Q("raidReceipt"); m_ReceiptText = m_Root.Q<Label>("raidReceiptText");
            m_FeedbackRaid = 0; m_ReceiptUntil = 0; m_ReceiptQueued = false; m_ReceiptSupplies = default;
            m_Result = m_Root.Q("raidResult"); m_ResultText = m_Root.Q<Label>("raidResultText"); m_SaveStatus = m_Root.Q<Label>("raidSaveStatus");
            m_Deploy = m_Root.Q<Button>("raidDeploy"); m_Deploy.clicked += Deploy;
            m_LoadoutPanel = m_Root.Q("raidLoadout");
            m_CarryCells = m_Root.Q<IntegerField>("raidCarryCells");
            m_CarryCells.RegisterValueChangedCallback(CarryCellsChanged);
            m_LoadoutNote = m_Root.Q<Label>("raidLoadoutNote");
            m_ResultBack = m_Root.Q<Button>("raidResultBack"); m_ResultBack.clicked += ResultBack;
            m_ResultNext = m_Root.Q<Button>("raidResultNext"); m_ResultNext.clicked += ResultNext;
            m_Return = m_Root.Q<Button>("raidReturn"); m_Return.clicked += ReturnToShip;
            m_Return.SetEnabled(GameManager.CanUseMainMenu);
            m_Inventory = m_Root.Q("raidInventory");
            m_InventoryTitle=m_Root.Q<Label>(className:"raid-inventory-title");
            m_EquipmentHost = m_Root.Q("raidInventoryHost"); m_HealthHost = m_Root.Q("raidHealthHost");
            m_InventoryView = new Client.ContainerInventoryView(m_EquipmentHost, false, SendInventoryMove, UseMedical, UseConsumable);
            m_HealthView = new Client.RaidHealthView(m_HealthHost, TreatPart, ConsumeSupply);
            m_EquipmentTab = m_Root.Q<Button>("raidEquipmentTab"); m_EquipmentTab.clicked += EquipmentTab;
            m_HealthTab = m_Root.Q<Button>("raidHealthTab"); m_HealthTab.clicked += HealthTab;
            m_HealthSelected = false; m_NextOxygenAlert = 0;
            m_OxygenWarning = m_Root.Q<Label>("raidOxygenWarning");
            m_OxygenPanel = m_Root.Q("raidOxygenPanel"); m_OxygenFill = m_Root.Q("raidOxygenFill");
            m_OxygenReserve = m_Root.Q<Label>("raidOxygenReserve"); m_OxygenTime = m_Root.Q<Label>("raidOxygenTime");
            m_OxygenAir = m_Root.Q<Label>("raidOxygenAir");
            m_InventorySequence=0; m_InventoryRequestId=0;
            m_InventoryRequestPending=false;
            m_LootOpenPending=false;m_OpenedLootId=-1;m_DissectingLootId=-1;
            m_Root.Q<Button>("raidPackClose").clicked += ClosePack;
            MoonkovLocalization.Bind(m_Root);
            InitializeMissions();
        }

        private void EquipmentTab() => SelectHealth(false);
        private void HealthTab() => SelectHealth(true);
        private void SelectHealth(bool health)
        {
            if (m_InventoryRequestPending || m_HealthSelected == health) return;
            m_HealthSelected = health;
            m_EquipmentHost.style.display = health ? DisplayStyle.None : DisplayStyle.Flex;
            m_HealthHost.style.display = health ? DisplayStyle.Flex : DisplayStyle.None;
            m_EquipmentTab.EnableInClassList("raid-tab-active", !health);
            m_HealthTab.EnableInClassList("raid-tab-active", health);
            MoonkovLocalization.Set(m_InventoryTitle, health ? "HEALTH / SUIT STATUS" :
                m_OpenedLootId >= RaidLootContainers.FirstDeathBagId ? "REMAINS / CARRIED INVENTORY" :
                m_OpenedLootId >= 0 ? "SUPPLY CACHE / CARRIED INVENTORY" : "CHARACTER / CARRIED INVENTORY");
            if (health) m_InventoryView.Suspend();
            else if (m_InventoryVisible) m_InventoryView.Show();
            RefreshHealth();
        }
        private void RefreshHealth()
        {
            if (m_World == null || !m_World.IsCreated) return;
            var player = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>() : default;
            int medicines = 0, food = 0, water = 0, oxygenCanisters = 0;
            string foodCode = null, waterCode = null, oxygenCode = null;
            float medicalRestore = 0;
            if (!m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
                if (inventory.RaidId == m_Snapshot.RaidId && inventory.Graph != null)
                    foreach (var item in inventory.Graph.Items)
                    {
                        if (inventory.Graph.MedicalAccessible(item))
                        {
                            medicines += item.Quantity;
                            if (medicalRestore == 0) medicalRestore = Inventory.ConsumableCatalog.MedicalRestore(item.Code);
                        }
                        if (inventory.Graph.ConsumableAccessible(item))
                        {
                            var supply = Inventory.ConsumableCatalog.Get(item.Code);
                            if (supply.Oxygen > 0) { oxygenCanisters += item.Quantity; if (oxygenCode == null) oxygenCode = item.Code; }
                            else if (supply.Food)
                            {
                                food += item.Quantity;
                                if (foodCode == null && RaidNutrition.CanConsume(player, supply)) foodCode = item.Code;
                            }
                            else
                            {
                                water += item.Quantity;
                                if (waterCode == null && RaidNutrition.CanConsume(player, supply)) waterCode = item.Code;
                            }
                        }
                    }
            }
            if (m_InventoryVisible && m_HealthSelected) m_HealthView.Present(player, medicines, m_InventoryRequestPending,
                food, water, oxygenCanisters, foodCode, waterCode, oxygenCode, medicalRestore);
            bool oxygenReady = player.BodyHealthInitialized && player.CurrentHealth > 0 && !m_WasSettled;
            bool needsSuitAir = RaidOxygen.NeedsSuitAir(player);
            m_OxygenPanel.style.display = oxygenReady ? DisplayStyle.Flex : DisplayStyle.None;
            if (oxygenReady)
            {
                MoonkovLocalization.Set(m_OxygenReserve, "O₂ / {0:0}%", player.Oxygen);
                MoonkovLocalization.Set(m_OxygenTime, "EST. {0}", Client.RaidHealthView.OxygenTimeText(player));
                MoonkovLocalization.Set(m_OxygenAir, Client.RaidHealthView.OxygenAirText(player));
                m_OxygenFill.style.width = Length.Percent(Mathf.Clamp(player.Oxygen, 0, 100));
                m_OxygenPanel.EnableInClassList("oxygen-low", player.Oxygen <= 25);
                m_OxygenPanel.EnableInClassList("oxygen-critical", needsSuitAir && player.Oxygen <= 10);
            }
            var effects = RaidNutrition.Effects(player);
            bool oxygenWarning = oxygenReady && needsSuitAir && player.Oxygen <= 25;
            var otherEffects = effects & ~(SurvivalEffects.LowOxygen | SurvivalEffects.Hypoxia);
            bool warning = (oxygenWarning || otherEffects != SurvivalEffects.None) && !m_WasSettled;
            m_OxygenWarning.style.display = warning ? DisplayStyle.Flex : DisplayStyle.None;
            if (!warning) { m_NextOxygenAlert = 0; return; }
            bool critical = (oxygenWarning && player.Oxygen <= 10) || (effects & (SurvivalEffects.Starving | SurvivalEffects.Dehydrated)) != 0;
            m_OxygenWarning.EnableInClassList("oxygen-critical", critical);
            string text = oxygenWarning ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                MoonkovLocalization.Text(player.Oxygen <= 0 ? "HYPOXIA / SEEK PRESSURIZED AIR" : "LOW OXYGEN / {0:0}%"), player.Oxygen) : "";
            string nutritionText = Client.RaidHealthView.EffectText(otherEffects);
            if (!string.IsNullOrEmpty(nutritionText)) text += (text.Length > 0 ? "\n" : "") + nutritionText;
            MoonkovLocalization.Set(m_OxygenWarning, text);
            bool alert = oxygenWarning ||
                (effects & (SurvivalEffects.Hungry | SurvivalEffects.Starving | SurvivalEffects.Thirsty | SurvivalEffects.Dehydrated)) != 0;
            if (alert && Time.unscaledTime >= m_NextOxygenAlert)
            {
                MoonkovAudio.Play(oxygenWarning ? MoonkovAudio.Library?.SuitWarning : MoonkovAudio.Library?.Notification, Vector3.zero); m_NextOxygenAlert = Time.unscaledTime + (critical ? 10 : 20);
            }
        }

        private bool m_AudioExtracting;
        private float m_ExtractionSoundAt;
        private int m_ExtractionSoundRaid;

        private void SetInventory(bool visible)
        {
            if (visible && m_MissionDetailsVisible) SetMissionDetails(false);
            if(m_InventoryRequestPending && m_Snapshot.Phase==RaidPhase.Active && !visible)
            {
                if(m_DissectingLootId<0)return;
                RequestLoot(-1);m_DissectingLootId=-1;
            }
            if (visible != m_InventoryVisible) MoonkovAudio.Play(MoonkovAudio.Library?.Container, Vector3.zero);
            if(!visible && m_OpenedLootId>=0 && m_Snapshot.Phase==RaidPhase.Active)RequestLoot(-1);
            m_InventoryVisible = visible; m_Inventory.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) { UpdateInventoryView(); if(!m_LootOpenPending && !m_HealthSelected)m_InventoryView.Show(); RefreshHealth(); }
            else m_InventoryView.Suspend();
            Utils.SetCursorVisible(visible || GameSettings.Instance.IsPauseMenuOpen || m_WasSettled);
        }
        private void ClosePack() => SetInventory(false);
        private void ResultBack() { m_ResultStep = Mathf.Max(0, m_ResultStep - 1); RenderResult(); }
        private void ResultNext() { m_ResultStep = Mathf.Min(2, m_ResultStep + 1); RenderResult(); }
        private void ReturnToShip() { if (GameManager.CanUseMainMenu) GameManager.Instance.ReturnToMainMenuAsync(); }
        private void RenderResult()
        {
            int count = m_Snapshot.Dust + m_Snapshot.Alloy + m_Snapshot.Cells;
            string title = m_Snapshot.Phase == RaidPhase.Extracted ? "SURVIVED" : m_Snapshot.Phase == RaidPhase.Dead ? "KILLED IN ACTION" : "TIME EXPIRED";
            MoonkovLocalization.Set(m_Root.Q<Label>("raidResultHeading"), m_ResultStep == 0 ? title : m_ResultStep == 1 ?
                m_Snapshot.Phase == RaidPhase.Extracted ? "RECOVERED CARGO" : "LOST CARGO" : "PERSONAL STORAGE");
            MoonkovLocalization.Set(m_ResultText, MoonkovLocalization.Format($"EXPEDITION {m_Snapshot.RaidId:000}\n{(m_Snapshot.Phase == RaidPhase.Extracted ? "Cargo recovered" : "Carried cargo lost")} / {count} supplies"));
            m_Result.EnableInClassList("raid-result-failed", m_Snapshot.Phase != RaidPhase.Extracted);
            m_Result.EnableInClassList("raid-result-cargo", m_ResultStep == 1);
            m_Result.EnableInClassList("raid-result-storage", m_ResultStep == 2);
            m_Root.Q("raidManifest").style.display = m_ResultStep == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            MoonkovLocalization.Set(m_Root.Q<Label>("raidManifestDust"), "{0:N0}", m_ResultStep == 2 ? m_Snapshot.StashDust : m_Snapshot.Dust);
            MoonkovLocalization.Set(m_Root.Q<Label>("raidManifestAlloy"), "{0:N0}", m_ResultStep == 2 ? m_Snapshot.StashAlloy : m_Snapshot.Alloy);
            MoonkovLocalization.Set(m_Root.Q<Label>("raidManifestCells"), "{0:N0}", m_ResultStep == 2 ? m_Snapshot.StashCells : m_Snapshot.Cells);
            for (int i = 0; i < 3; i++) m_Root.Q<Label>("raidStep" + i).EnableInClassList("raid-step-active", i == m_ResultStep);
            m_ResultBack.SetEnabled(m_ResultStep > 0); m_ResultNext.style.display = m_ResultStep < 2 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Deploy.style.display = m_Return.style.display = m_ResultStep == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            m_LoadoutPanel.style.display = m_ResultStep == 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void InitializeWorld()
        {
            foreach (var world in World.All)
            {
                if (!world.IsCreated || !world.IsClient() || world.IsThinClient()) continue;
                m_World = world;
                m_StateQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RaidClientState>());
                m_PlayerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>(), ComponentType.ReadOnly<LocalTransform>());
                m_ConnectionQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                m_InventoryQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RaidInventoryClientState>());
                break;
            }
        }

        private void LateUpdate()
        {
            bool inGame = GameSettings.Instance.GameState == GlobalGameState.InGame;
            m_Root.style.display = inGame ? DisplayStyle.Flex : DisplayStyle.None;
            if (!inGame) { m_Minimap.Hide(); return; }
            if (m_World == null || !m_World.IsCreated) InitializeWorld();
            if (m_World == null || !m_World.IsCreated || m_StateQuery.IsEmptyIgnoreFilter) { m_Minimap.Hide(); return; }
            m_Snapshot = m_StateQuery.GetSingleton<RaidClientState>().Snapshot;
            UpdateInventoryView();
            bool ready = m_Snapshot.RaidId > 0;
            bool settled = ready && m_Snapshot.Phase != RaidPhase.Active;
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame && ready && !settled && !GameSettings.Instance.IsPauseMenuOpen)
                {if(!m_InventoryRequestPending)SetInventory(!m_InventoryVisible);}
            if (settled && m_InventoryVisible) SetInventory(false);
            if (!settled && m_WasSettled) { m_ResultStep = 0; Utils.SetCursorVisible(false); }
            if (settled && !m_WasSettled)
            {
                m_ResultStep = 0;
                if (m_Snapshot.Phase == RaidPhase.Extracted) MoonkovAudio.Confirm();
                else MoonkovAudio.Error();
            }
            if (m_InventoryVisible) Utils.SetCursorVisible(true);
            MoonRaidMap.Active.ShowLoot(m_Snapshot.TakenMask, ready);
            var deathBags=m_World.EntityManager.GetComponentObject<RaidDeathBagClientState>(m_StateQuery.GetSingletonEntity());
            MoonRaidMap.Active.ShowDeathBags(deathBags);
            if (settled)
            {
                // LateUpdate runs after character teardown, which can restore the previous cursor state.
                Utils.SetCursorVisible(true);
                bool saving = m_Snapshot.SaveState == RaidSaveState.Saving || m_Snapshot.SaveState == RaidSaveState.Retrying;
                if (m_DeployRequestedRaid == m_Snapshot.RaidId && m_Snapshot.Sequence > m_DeployRequestedSequence &&
                    m_Snapshot.LoadoutRequestId == m_DeployRequestId &&
                    !m_Snapshot.DeployPending && m_Snapshot.LoadoutError != RaidLoadoutError.None)
                    m_DeployRequestedRaid = 0;
                bool deploying = m_Snapshot.DeployPending || m_DeployRequestedRaid == m_Snapshot.RaidId;
                m_Deploy.SetEnabled(!saving && !deploying);
                m_Return.SetEnabled(!saving && !deploying && GameManager.CanUseMainMenu);
                m_CarryCells.SetEnabled(!saving && !deploying);
                // Do not replace text while the player is editing it.
                if (m_CarryCells.panel?.focusController.focusedElement is not VisualElement focused || !m_CarryCells.Contains(focused))
                    m_CarryCells.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
                MoonkovLocalization.Set(m_LoadoutNote, deploying ? "Preparing loadout. Waiting for server confirmation..."
                    : m_Snapshot.LoadoutError == RaidLoadoutError.InsufficientCells ? "Not enough cells in storage. Reduce the quantity or refresh storage on the ship."
                    : m_Snapshot.LoadoutError == RaidLoadoutError.InvalidCount ? "Choose 0-12 energy cells."
                    : m_Snapshot.LoadoutError == RaidLoadoutError.Rejected ? "Loadout rejected. You can retry or return to the ship."
                    : MoonkovLocalization.Format($"Storage: {m_Snapshot.StashCells} cells / Carry limit: {RaidRules.BagCapacity}. [R] spends energy per round. Unspent energy returns on extraction."));
                if (!m_WasSettled && !saving) m_Deploy.Focus();
            }
            m_WasSettled = settled;
            m_Result.style.display = settled ? DisplayStyle.Flex : DisplayStyle.None;

            int nearest = -1;
            float distance = RaidRules.PickupRange;
            Vector3 position = default;
            bool alive = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() && m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().CurrentHealth > 0;
            if (alive && ready && !settled && !m_InventoryVisible && !GameSettings.Instance.IsPauseMenuOpen)
            {
                var local = m_PlayerQuery.GetSingleton<LocalTransform>();
                var view = Camera.main;
                Vector3 forward = view ? view.transform.forward : (Vector3)math.mul(local.Rotation, math.forward());
                m_Minimap.Present(MoonRaidMap.Active, local.Position, forward, m_Snapshot.TakenMask);
            }
            else m_Minimap.Hide();
            UpdateGrenades(alive && ready && !settled);
            bool missionControl = UpdateMissions(alive && ready && !settled);
            if (alive && ready && !settled && !m_InventoryVisible && !m_InventoryRequestPending &&
                !GameSettings.Instance.IsPauseMenuOpen && UnityEngine.Cursor.lockState == CursorLockMode.Locked &&
                Keyboard.current != null && Keyboard.current.digit4Key.wasPressedThisFrame && !m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
                var player = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
                var medicine = inventory.Graph?.Items.Find(inventory.Graph.MedicalAccessible);
                if (medicine != null && player.CurrentHealth > 0f && player.CurrentHealth < player.MaxHealth) UseMedical(medicine);
            }
            if (alive && ready && !settled)
            {
                position = m_PlayerQuery.GetSingleton<LocalTransform>().Position;
                var map = MoonRaidMap.Active;
                for (int i = 0; i < map.LootPositions.Length; i++)
                {
                    float candidate = Vector3.Distance(position, map.LootPositions[i]);
                    if (candidate < distance) { nearest = i; distance = candidate; }
                }
                foreach(var bag in deathBags.Bags.Values)
                {
                    float candidate=Vector3.Distance(position,(Vector3)bag.Position);
                    if(candidate<distance){nearest=bag.LootId;distance=candidate;}
                }
                m_AirlockPrompt.RefreshSelection(Camera.main, position, !m_InventoryVisible && !m_InventoryRequestPending &&
                    UnityEngine.Cursor.lockState == CursorLockMode.Locked && !GameSettings.Instance.IsPauseMenuOpen, m_World);
                bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
                               (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
                bool controlInput = (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) ||
                    (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
                bool controlPressed = !missionControl && controlInput && !m_InventoryVisible && !m_InventoryRequestPending &&
                    UnityEngine.Cursor.lockState == CursorLockMode.Locked && !GameSettings.Instance.IsPauseMenuOpen &&
                    m_AirlockPrompt.TryInteract(Camera.main, position, m_World);
                if (nearest >= 0 && pressed && !controlPressed && !missionControl && !m_InventoryVisible && !m_InventoryRequestPending && UnityEngine.Cursor.lockState == CursorLockMode.Locked && !GameSettings.Instance.IsPauseMenuOpen)
                {
                    if(RequestLoot(nearest))
                    {
                        // Loot always opens the equipment page, even if Tab last
                        // showed health. Selection does not discard an in-flight intent.
                        m_HealthSelected = false;
                        m_EquipmentHost.style.display = DisplayStyle.Flex; m_HealthHost.style.display = DisplayStyle.None;
                        m_EquipmentTab.EnableInClassList("raid-tab-active", true); m_HealthTab.EnableInClassList("raid-tab-active", false);
                        bool dissect=deathBags.Bags.TryGetValue(nearest,out var remains) && remains.RequiresDissection;
                        m_DissectingLootId=dissect ? nearest : -1;
                        m_DissectionEndsAt=Time.unscaledTime+Inventory.AlienHarvest.DissectionSeconds;
                        string opening=dissect ? "Dissecting alien… Stay still. Close to cancel; moving or damage interrupts."
                            : nearest>=RaidLootContainers.FirstDeathBagId ? "Searching fallen expedition…" : "Opening supply cache…";
                        m_LootOpenPending=true;m_InventoryView.Present(null,opening,operationCompleted:false);
                        m_InventoryView.SetReadOnly(opening);SetInventory(true);
                    }
                }
            }
            // Input is sampled every frame; presentation text refreshes at 10 Hz.
            bool interactionVisible = alive && ready && !settled && !m_InventoryVisible &&
                !m_InventoryRequestPending && !GameSettings.Instance.IsPauseMenuOpen &&
                UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            if (!interactionVisible)
            {
                m_Interaction.EnableInClassList("is-visible", false);
                m_AirlockPrompt.RefreshSelection(Camera.main, position, false);
            }
            m_RefreshTimer -= Time.deltaTime;
            if (m_RefreshTimer > 0) return;
            m_RefreshTimer = 0.1f;
            RefreshHealth();
            int seconds = Mathf.CeilToInt(m_Snapshot.TimeLeft);
            MoonkovLocalization.Set(m_Timer, ready ? MoonkovLocalization.Format($"Raid {m_Snapshot.RaidId}   {seconds / 60:00}:{seconds % 60:00} remaining") : "Connecting...");
            var clock = ExpeditionClockPresentation.Active;
            MoonkovLocalization.Set(m_Clock, clock != null && clock.IsSynchronized ? ExpeditionClock.Display(clock.TotalHours) : "");
            int count = m_Snapshot.Dust + m_Snapshot.Alloy + m_Snapshot.Cells;
            if (count != m_PreviousCount) { m_PreviousCount = count; m_RevealUntil = Time.unscaledTime + 4; }
            RefreshSupplyReceipt(ready && !settled);
            bool check = Keyboard.current != null && Keyboard.current.hKey.isPressed;
            bool status = PlayerPrefs.GetInt("Moonkov.AlwaysShowHUD", 0) != 0 || check || Time.unscaledTime < m_RevealUntil || m_Snapshot.TimeLeft < 60;
            m_Status.style.display = !settled && status ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_Bag, "CARRIED / {0} supplies\nDust {1}   Alloy {2}   Cells {3}" , count, m_Snapshot.Dust, m_Snapshot.Alloy, m_Snapshot.Cells);
            if (!m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var batteryGraph = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity()).Graph;
                if (batteryGraph != null) MoonkovLocalization.Set(m_Bag,
                    "CARRIED / {0} supplies\nDust {1}   Alloy {2}   Cells {3}\nENERGY {4} / READY {5}\n[4] MEDICAL / {6}",
                    count, m_Snapshot.Dust, m_Snapshot.Alloy, m_Snapshot.Cells,
                    batteryGraph.CellEnergy(carriedOnly: true), batteryGraph.CellEnergy(accessibleOnly: true),
                    batteryGraph.Count("medkit", true) + batteryGraph.Count("med_gel", true) + batteryGraph.Count("trauma_kit", true));
            }
            bool deathBag=nearest>=RaidLootContainers.FirstDeathBagId;
            bool empty=deathBag ? deathBags.Bags[nearest].Empty : nearest>=0 && (m_Snapshot.TakenMask & (1u<<nearest))!=0;
            bool alien=deathBag && deathBags.Bags[nearest].CharacterIndex==-1;
            RefreshInteraction(nearest, distance, deathBag, empty, interactionVisible && !m_AirlockPrompt.HasSelection,alien);
            bool extracting = alive && !settled && m_Snapshot.ExtractionRemaining >= 0;
            if (m_ExtractionSoundRaid != m_Snapshot.RaidId)
            { m_ExtractionSoundRaid = m_Snapshot.RaidId; m_AudioExtracting = false; m_ExtractionSoundAt = 0; }
            if (extracting != m_AudioExtracting)
            {
                if (extracting || alive && !settled)
                    MoonkovAudio.Play(extracting ? MoonkovAudio.Library?.ExtractStart : MoonkovAudio.Library?.ExtractCancel, Vector3.zero);
                m_AudioExtracting = extracting; m_ExtractionSoundAt = Time.unscaledTime + 1;
            }
            if (extracting && m_Snapshot.ExtractionRemaining <= 3 && Time.unscaledTime >= m_ExtractionSoundAt)
            {
                MoonkovAudio.Play(MoonkovAudio.Library?.ExtractTick, Vector3.zero, .65f);
                m_ExtractionSoundAt = Time.unscaledTime + 1;
            }
            m_ExtractionPanel.EnableInClassList("is-visible", alive && !settled && (extracting || check));
            m_ExtractionTrack.style.display = extracting ? DisplayStyle.Flex : DisplayStyle.None;
            if (extracting)
                m_ExtractionFill.style.width = Length.Percent(100 * Mathf.Clamp01(1 - m_Snapshot.ExtractionRemaining / MoonRaidMap.Active.ExtractionSeconds));
            else m_ExtractionFill.style.width = Length.Percent(0);
            if (alive && !settled)
            {
                Vector3 delta = MoonRaidMap.Active.ExtractionPosition - position;
                string heading = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? delta.x > 0 ? "E" : "W" : delta.z > 0 ? "N" : "S";
                MoonkovLocalization.Set(m_Exit, m_Snapshot.ExtractionRemaining >= 0 ? MoonkovLocalization.Format($"EXTRACTION\nSHUTTLE BEACON / {m_Snapshot.ExtractionRemaining:00.0}s") : check ? MoonkovLocalization.Format($"GREEN BEACON / {delta.magnitude:F0}m {heading}") : "");
            }
            else MoonkovLocalization.Set(m_Exit, "");
            if (settled)
            {
                MoonkovLocalization.Set(m_SaveStatus, m_Snapshot.SaveState == RaidSaveState.SessionOnly ? "Stash lasts for this connection only."
                    : m_Snapshot.SaveState == RaidSaveState.Saved ? "Stash saved. You can reconnect later."
                    : m_Snapshot.SaveState == RaidSaveState.Retrying ? "Waiting to save. Retrying..." : "Saving raid results...");
                RenderResult();
            }
        }

        private void RefreshInteraction(int nearest, float distance, bool deathBag, bool empty, bool allowed,bool alien=false)
        {
            bool error = Time.unscaledTime < m_LootErrorUntil;
            bool visible = allowed && (nearest >= 0 || error);
            m_Interaction.EnableInClassList("is-visible", visible);
            if (!visible) return;
            m_Interaction.EnableInClassList("is-error", error);
            m_Interaction.EnableInClassList("is-empty", !error && empty);
            MoonkovLocalization.Set(m_InteractionKey, error ? "!" : "E");
            m_InteractionDetails.style.display = error ? DisplayStyle.None : DisplayStyle.Flex;
            if (error)
            {
                MoonkovLocalization.Set(m_Prompt, m_DissectionInterrupted ? "Dissection interrupted. Stay beside the remains and avoid damage." :
                    "Cannot reach this container. Move closer with a clear line of sight.");
                return;
            }
            // Reuse the localized action wording; the physical key now has its own visual.
            string prompt = alien ? MoonkovLocalization.Text("[E]  DISSECT / SEARCH ALIEN REMAINS") : deathBag ? MoonkovLocalization.Text("[E]  SEARCH FALLEN EXPEDITION")
                : MoonkovLocalization.Format($"[E]  OPEN SUPPLY CACHE / {nearest + 1:00}");
            if (prompt.StartsWith("[E]", System.StringComparison.Ordinal)) prompt = prompt.Substring(3).TrimStart();
            MoonkovLocalization.Set(m_Prompt, prompt);
            MoonkovLocalization.Set(m_InteractionState, empty ? "EMPTY" : "CONTENTS");
            MoonkovLocalization.Set(m_InteractionRange, $"{distance:0.0} m");
        }

        private void RefreshSupplyReceipt(bool active)
        {
            int3 supplies = new int3(m_Snapshot.Dust, m_Snapshot.Alloy, m_Snapshot.Cells);
            if (m_FeedbackRaid != m_Snapshot.RaidId || !active)
            {
                m_FeedbackRaid = m_Snapshot.RaidId;
                m_PreviousSupplies = supplies;
                m_ReceiptSupplies = default; m_ReceiptQueued = false; m_ReceiptUntil = 0;
            }
            else
            {
                // Only server snapshot increases earn a receipt. Opening or clicking a cache is not a pickup.
                int3 gained = math.max(supplies - m_PreviousSupplies, 0);
                m_PreviousSupplies = supplies;
                if (math.any(gained > 0))
                {
                    MoonkovAudio.Play(MoonkovAudio.Library?.Pickup, Vector3.zero, .8f);
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
            if (m_ReceiptQueued && !m_InventoryVisible)
            {
                m_ReceiptQueued = false;
                m_ReceiptUntil = Time.unscaledTime + 3.2f;
            }
            m_Receipt.EnableInClassList("is-visible", active && !m_InventoryVisible &&
                !GameSettings.Instance.IsPauseMenuOpen && Time.unscaledTime < m_ReceiptUntil);
        }

        private static void AppendReceipt(ref string text, int amount, string item)
        {
            if (amount <= 0) return;
            if (text.Length > 0) text += "\n";
            text += $"+{amount}  {MoonkovLocalization.Text(item)}";
        }

        private bool RequestLoot(int lootId)
        {
            if(m_World==null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter)return false;
            var inventory=m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            m_InventoryRequestId=System.Math.Max(m_InventoryRequestId,inventory.RequestId)+1;
            if(!Send(new RaidLootOpenRpc {RaidId=m_Snapshot.RaidId,LootId=lootId,RequestId=m_InventoryRequestId}))return false;
            m_InventoryRequestedAt=Time.unscaledTime;m_InventoryRequestPending=true;return true;
        }
        private void UpdateInventoryView()
        {
            if (m_World==null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory=m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            if(m_InventoryRequestPending && Time.unscaledTime-m_InventoryRequestedAt>(m_DissectingLootId>=0 ? Inventory.AlienHarvest.DissectionSeconds+5 : 5))
            {
                m_DissectingLootId=-1;
                m_InventoryRequestPending=false;m_LootOpenPending=false;m_InventoryView.SetReadOnly(null);
                m_InventoryView.Present(inventory.Graph,"Waiting for the server. Retry after the connection recovers.",lootId:inventory.LootId);
                m_HealthView.Message("Waiting for the server. Retry after the connection recovers.");
            }
            if (inventory.Graph==null || inventory.RaidId!=m_Snapshot.RaidId) return;
            if(m_DissectingLootId>=0)
                MoonkovLocalization.Set(m_InventoryTitle,"DISSECTING ALIEN / {0:0.0}s",Mathf.Max(0,m_DissectionEndsAt-Time.unscaledTime));
            if (inventory.Sequence!=m_InventorySequence)
            {
                m_InventorySequence=inventory.Sequence;
                if(inventory.DissectingLootId>=0 && inventory.RequestId==m_InventoryRequestId)
                {
                    m_DissectingLootId=inventory.DissectingLootId;
                    m_DissectionEndsAt=Time.unscaledTime+inventory.DissectionRemaining;
                    m_InventoryView.Present(null,"Dissecting alien… Stay still. Close to cancel; moving or damage interrupts.",operationCompleted:false);
                    m_InventoryView.SetReadOnly("Dissecting alien… Stay still. Close to cancel; moving or damage interrupts.");
                    return;
                }
                bool wasOpen=m_OpenedLootId>=0;
                bool acknowledged = !m_InventoryRequestPending || inventory.RequestId == m_InventoryRequestId || wasOpen && inventory.LootId<0;
                if (acknowledged) m_InventoryRequestPending = false;
                m_OpenedLootId=inventory.LootId;
                if(acknowledged)
                {
                    m_DissectionInterrupted=m_DissectingLootId>=0 && inventory.Error==Inventory.InventoryError.Inaccessible;
                    m_DissectingLootId=-1;m_LootOpenPending=false;m_InventoryView.SetReadOnly(null);
                }
                MoonkovLocalization.Set(m_InventoryTitle, inventory.LootId>=RaidLootContainers.FirstDeathBagId ? "REMAINS / CARRIED INVENTORY"
                    : inventory.LootId>=0 ? "SUPPLY CACHE / CARRIED INVENTORY" : "CHARACTER / CARRIED INVENTORY");
                m_InventoryView.Present(inventory.Graph,acknowledged && inventory.Error!=Inventory.InventoryError.None ? MoonkovLocalization.Format($"Inventory: {inventory.Error.ToString()}") : null,
                    operationCompleted: acknowledged,lootId:inventory.LootId);
                if (acknowledged) m_HealthView.Message(inventory.Error == Inventory.InventoryError.None ? "" : MoonkovLocalization.Format($"Inventory: {inventory.Error.ToString()}"));
                if (m_HealthSelected) MoonkovLocalization.Set(m_InventoryTitle, "HEALTH / SUIT STATUS");
                if(inventory.LootId<0 && (wasOpen || acknowledged && inventory.Error==Inventory.InventoryError.Inaccessible))
                {if(inventory.Error==Inventory.InventoryError.Inaccessible)m_LootErrorUntil=Time.unscaledTime+3;SetInventory(false);}
                else if(m_InventoryVisible && !m_HealthSelected && inventory.LootId<0 && !m_LootOpenPending)m_InventoryView.Show();
            }
        }
        private void SendInventoryMove(Inventory.InventoryCommand command)
        {
            var inventory=m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            m_InventoryRequestId=System.Math.Max(m_InventoryRequestId,inventory.RequestId)+1;
            bool sent=inventory.LootId>=0
                ? Send(new RaidLootMoveRpc {RaidId=m_Snapshot.RaidId,LootId=inventory.LootId,ExpectedLootVersion=inventory.LootVersion,
                    RequestId=m_InventoryRequestId,ExpectedVersion=command.ExpectedVersion,Operation=command.Operation,
                    ItemId=command.ItemId??"",Parent=command.Parent??"",Region=command.Region??"",TargetId=command.TargetId??"",
                    X=command.X,Y=command.Y,Quantity=command.Quantity,Rotated=command.Rotated})
                : Send(new RaidInventoryMoveRpc { RaidId=m_Snapshot.RaidId, RequestId=m_InventoryRequestId, ExpectedVersion=command.ExpectedVersion,
                Operation=command.Operation, ItemId=command.ItemId??"", Parent=command.Parent??"", Region=command.Region??"", TargetId=command.TargetId??"",
                X=command.X,Y=command.Y,Quantity=command.Quantity,Rotated=command.Rotated });
            if(sent)
            { m_InventoryRequestedAt=Time.unscaledTime; m_InventoryRequestPending=true; }
            else m_InventoryView.Present(inventory.Graph,"Not connected. Item remains in its container.");
        }

        private void UseMedical(Inventory.InventoryItem item)
            => UseMedical(item, BodyPart.Auto);
        private void ConsumeSupply(string code)
        {
            if (m_World == null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            var item = inventory.Graph?.Items.Find(i => i.Code == code && inventory.Graph.ConsumableAccessible(i));
            if (item != null) UseConsumable(item);
        }
        private void UseConsumable(Inventory.InventoryItem item)
        {
            if (m_InventoryRequestPending || m_Snapshot.Phase != RaidPhase.Active || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            var player = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>() : default;
            var definition = Inventory.ConsumableCatalog.Get(item.Code);
            bool oxygenSupply = definition != null && definition.Oxygen > 0;
            if (!(oxygenSupply ? RaidOxygen.CanRefill(player) : RaidNutrition.CanConsume(player, definition)))
            {
                string message = oxygenSupply ? "Cannot refill oxygen: reserve is full or the player is inactive." :
                    "Cannot consume: resource is full, stomach capacity is insufficient, or the player is inactive.";
                m_InventoryView.Present(inventory.Graph, message); m_HealthView.Message(message); return;
            }
            m_InventoryRequestId = System.Math.Max(m_InventoryRequestId, inventory.RequestId) + 1;
            if (Send(new RaidConsumableUseRpc { RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId,
                ExpectedVersion = inventory.Graph.Version, ItemId = item.Id }))
            { m_InventoryRequestedAt = Time.unscaledTime; m_InventoryRequestPending = true; }
            else
            {
                const string message = "Not connected. Consumable remains in its container.";
                m_InventoryView.Present(inventory.Graph, message); m_HealthView.Message(message);
            }
            RefreshHealth();
        }
        private void TreatPart(BodyPart part)
        {
            if (m_World == null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            var item = inventory.Graph?.Items.Find(inventory.Graph.MedicalAccessible);
            if (item != null) UseMedical(item, part);
        }
        private void UseMedical(Inventory.InventoryItem item, BodyPart part)
        {
            if (m_InventoryRequestPending || m_Snapshot.Phase != RaidPhase.Active) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            m_InventoryRequestId = System.Math.Max(m_InventoryRequestId, inventory.RequestId) + 1;
            if (Send(new RaidMedicalUseRpc { RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId,
                ExpectedVersion = inventory.Graph.Version, ItemId = item.Id, Part = part }))
            { m_InventoryRequestedAt = Time.unscaledTime; m_InventoryRequestPending = true; }
            else m_InventoryView.Present(inventory.Graph, "Not connected. Medical item remains in its container.");
            RefreshHealth();
        }

        private void CarryCellsChanged(ChangeEvent<int> evt)
        {
            PlayerProfileClient.SelectCarryCells(evt.newValue);
            m_CarryCells.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
        }

        private void Deploy()
        {
            if (m_Snapshot.RaidId <= 0 || m_Snapshot.Phase == RaidPhase.Active ||
                m_Snapshot.DeployPending || m_DeployRequestedRaid == m_Snapshot.RaidId ||
                m_Snapshot.SaveState == RaidSaveState.Saving || m_Snapshot.SaveState == RaidSaveState.Retrying) return;
            uint requestId = m_Snapshot.LoadoutRequestId + 1;
            if (Send(new RaidDeployRpc { SettledRaidId = m_Snapshot.RaidId, CarryCells = PlayerProfileClient.CarryCells, RequestId = requestId }))
            {
                GameSettings.Instance.IsPauseMenuOpen = false;
                m_DeployRequestedRaid = m_Snapshot.RaidId;
                m_DeployRequestedSequence = m_Snapshot.Sequence;
                m_DeployRequestId = requestId;
                m_Deploy.SetEnabled(false);
            }
        }

        private bool Send<T>(T rpc) where T : unmanaged, IRpcCommand
        {
            if (m_World == null || !m_World.IsCreated || !m_ConnectionQuery.HasSingleton<NetworkId>()) return false;
            var em = m_World.EntityManager;
            var entity = em.CreateEntity();
            em.AddComponentData(entity, rpc);
            em.AddComponentData(entity, new SendRpcCommandRequest { TargetConnection = m_ConnectionQuery.GetSingletonEntity() });
            return true;
        }

        private void OnDisable()
        {
            CancelMissionHold();
            RaidMissionSite.Active?.StopPresentation();
            m_Minimap?.Dispose(); m_Minimap = null;
            m_AirlockPrompt?.RefreshSelection(null, default, false);
            if (m_Root != null) MoonkovAudio.UnbindUI(m_Root);
            m_InventoryView?.Dispose(); m_InventoryView=null;
            m_HealthView?.Dispose(); m_HealthView = null;
            if (m_EquipmentTab != null) m_EquipmentTab.clicked -= EquipmentTab;
            if (m_HealthTab != null) m_HealthTab.clicked -= HealthTab;
            m_CarryCells?.UnregisterValueChangedCallback(CarryCellsChanged);
            if (m_World != null && m_World.IsCreated)
            {
                m_StateQuery.Dispose(); m_PlayerQuery.Dispose(); m_ConnectionQuery.Dispose(); m_InventoryQuery.Dispose();
            }
            m_World = null;
            if (s_Active == this) s_Active = null;
            if (m_InventoryVisible || m_MissionDetailsVisible) Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
            m_MissionDetailsVisible = false;
            m_InventoryVisible = false;
            m_Root?.Clear();
        }
    }
}
