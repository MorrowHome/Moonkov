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
    public sealed class RaidHUD : MonoBehaviour
    {
        private World m_World;
        private EntityQuery m_StateQuery, m_PlayerQuery, m_ConnectionQuery;
        private VisualElement m_Root, m_Result, m_Status, m_Inventory;
        private Client.ContainerInventoryView m_InventoryView;
        private Client.RaidHealthView m_HealthView;
        private VisualElement m_EquipmentHost, m_HealthHost;
        private Button m_EquipmentTab, m_HealthTab;
        private Label m_OxygenWarning;
        private bool m_HealthSelected;
        private float m_NextOxygenAlert;
        private EntityQuery m_InventoryQuery;
        private uint m_InventorySequence, m_InventoryRequestId;
        private float m_InventoryRequestedAt;
        private bool m_InventoryRequestPending;
        private bool m_LootOpenPending;
        private int m_OpenedLootId=-1;
        private Label m_InventoryTitle;
        private Label m_Bag, m_Timer, m_Clock, m_Prompt, m_Exit, m_ResultText, m_SaveStatus;
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
        public static bool InventoryOpen => s_Active != null && s_Active.m_InventoryVisible;
        public static bool PointerRequested => s_Active != null && (s_Active.m_InventoryVisible || s_Active.m_WasSettled);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPresentation() => s_Active = null;
        public static bool CloseInventory()
        {
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
            m_Status = m_Root.Q("raidStatus"); m_Timer = m_Root.Q<Label>("raidTimer"); m_Bag = m_Root.Q<Label>("raidBag");
            m_Clock = m_Root.Q<Label>("raidClock");
            m_Exit = m_Root.Q<Label>("raidExtraction"); m_Prompt = m_Root.Q<Label>("raidPrompt");
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
            m_InventoryView = new Client.ContainerInventoryView(m_EquipmentHost, false, SendInventoryMove, UseMedical);
            m_HealthView = new Client.RaidHealthView(m_HealthHost, TreatPart);
            m_EquipmentTab = m_Root.Q<Button>("raidEquipmentTab"); m_EquipmentTab.clicked += EquipmentTab;
            m_HealthTab = m_Root.Q<Button>("raidHealthTab"); m_HealthTab.clicked += HealthTab;
            m_HealthSelected = false; m_NextOxygenAlert = 0;
            m_OxygenWarning = m_Root.Q<Label>("raidOxygenWarning");
            m_InventorySequence=0; m_InventoryRequestId=0;
            m_InventoryRequestPending=false;
            m_LootOpenPending=false;m_OpenedLootId=-1;
            m_Root.Q<Button>("raidPackClose").clicked += ClosePack;
            MoonkovLocalization.Bind(m_Root);
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
                m_OpenedLootId >= RaidLootContainers.FirstDeathBagId ? "FALLEN EXPEDITION / CARRIED INVENTORY" :
                m_OpenedLootId >= 0 ? "SUPPLY CACHE / CARRIED INVENTORY" : "CHARACTER / CARRIED INVENTORY");
            if (health) m_InventoryView.Suspend();
            else if (m_InventoryVisible) m_InventoryView.Show();
            RefreshHealth();
        }
        private void RefreshHealth()
        {
            if (m_World == null || !m_World.IsCreated) return;
            var player = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>() : default;
            int medicines = 0;
            if (!m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
                if (inventory.RaidId == m_Snapshot.RaidId && inventory.Graph != null)
                    foreach (var item in inventory.Graph.Items) if (inventory.Graph.MedicalAccessible(item)) medicines += item.Quantity;
            }
            if (m_InventoryVisible && m_HealthSelected) m_HealthView.Present(player, medicines, m_InventoryRequestPending);
            bool warning = player.BodyHealthInitialized && player.CurrentHealth > 0 && player.Oxygen <= 25 && !m_WasSettled;
            m_OxygenWarning.style.display = warning ? DisplayStyle.Flex : DisplayStyle.None;
            if (!warning) { m_NextOxygenAlert = 0; return; }
            m_OxygenWarning.EnableInClassList("oxygen-critical", player.Oxygen <= 10);
            MoonkovLocalization.Set(m_OxygenWarning, player.BreathableAir ? "OXYGEN REFILLING / {0:0}%" :
                player.Oxygen <= 0 ? "HYPOXIA / SEEK PRESSURIZED AIR" : "LOW OXYGEN / {0:0}%", player.Oxygen);
            if (!player.BreathableAir && Time.unscaledTime >= m_NextOxygenAlert)
            {
                MoonkovAudio.Error(); m_NextOxygenAlert = Time.unscaledTime + (player.Oxygen <= 10 ? 10 : 20);
            }
        }

        private void SetInventory(bool visible)
        {
            if(m_InventoryRequestPending && m_Snapshot.Phase==RaidPhase.Active && !visible)return;
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
            MoonkovLocalization.Set(m_ResultText, m_ResultStep == 0 ? MoonkovLocalization.Format($"{title}\n\nEXPEDITION {m_Snapshot.RaidId:000}\n{(m_Snapshot.Phase == RaidPhase.Extracted ? "Cargo recovered" : "Carried cargo lost")} / {count} supplies")
                : m_ResultStep == 1 ? MoonkovLocalization.Format($"{(m_Snapshot.Phase == RaidPhase.Extracted ? "RECOVERED CARGO" : "LOST CARGO")}\n\nMoon dust      {m_Snapshot.Dust}\nAlloy               {m_Snapshot.Alloy}\nEnergy cells   {m_Snapshot.Cells}")
                : MoonkovLocalization.Format($"PERSONAL STORAGE\n\nMoon dust      {m_Snapshot.StashDust}\nAlloy               {m_Snapshot.StashAlloy}\nEnergy cells   {m_Snapshot.StashCells}"));
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
            if (!inGame) return;
            if (m_World == null || !m_World.IsCreated) InitializeWorld();
            if (m_World == null || !m_World.IsCreated || m_StateQuery.IsEmptyIgnoreFilter) return;
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
            bool alive = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>();
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
                bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
                               (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
                if (nearest >= 0 && pressed && !m_InventoryVisible && !m_InventoryRequestPending && UnityEngine.Cursor.lockState == CursorLockMode.Locked && !GameSettings.Instance.IsPauseMenuOpen)
                {
                    if(RequestLoot(nearest))
                    {
                        // Loot always opens the equipment page, even if Tab last
                        // showed health. Selection does not discard an in-flight intent.
                        m_HealthSelected = false;
                        m_EquipmentHost.style.display = DisplayStyle.Flex; m_HealthHost.style.display = DisplayStyle.None;
                        m_EquipmentTab.EnableInClassList("raid-tab-active", true); m_HealthTab.EnableInClassList("raid-tab-active", false);
                        string opening=nearest>=RaidLootContainers.FirstDeathBagId ? "Searching fallen expedition…" : "Opening supply cache…";
                        m_LootOpenPending=true;m_InventoryView.Present(null,opening,operationCompleted:false);
                        m_InventoryView.SetReadOnly(opening);SetInventory(true);
                    }
                }
            }
            // Input is sampled every frame; presentation text refreshes at 10 Hz.
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
                    batteryGraph.CellEnergy(carriedOnly: true), batteryGraph.CellEnergy(accessibleOnly: true), batteryGraph.Count("medkit", true));
            }
            bool deathBag=nearest>=RaidLootContainers.FirstDeathBagId;
            bool empty=deathBag ? deathBags.Bags[nearest].Empty : nearest>=0 && (m_Snapshot.TakenMask & (1u<<nearest))!=0;
            MoonkovLocalization.Set(m_Prompt, Time.unscaledTime<m_LootErrorUntil ? "Cannot reach this container. Move closer with a clear line of sight."
                : nearest < 0 || settled || m_InventoryVisible ? "" :
                    (deathBag ? MoonkovLocalization.Text("[E]  SEARCH FALLEN EXPEDITION") : MoonkovLocalization.Format($"[E]  OPEN SUPPLY CACHE / {nearest+1:00}")) +
                    (empty ? MoonkovLocalization.Text(" / EMPTY") : ""));
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
            if(m_InventoryRequestPending && Time.unscaledTime-m_InventoryRequestedAt>5)
            {
                m_InventoryRequestPending=false;m_LootOpenPending=false;m_InventoryView.SetReadOnly(null);
                m_InventoryView.Present(inventory.Graph,"Waiting for the server. Retry after the connection recovers.",lootId:inventory.LootId);
                m_HealthView.Message("Waiting for the server. Retry after the connection recovers.");
            }
            if (inventory.Graph==null || inventory.RaidId!=m_Snapshot.RaidId) return;
            if (inventory.Sequence!=m_InventorySequence)
            {
                m_InventorySequence=inventory.Sequence;
                bool wasOpen=m_OpenedLootId>=0;
                bool acknowledged = !m_InventoryRequestPending || inventory.RequestId == m_InventoryRequestId || wasOpen && inventory.LootId<0;
                if (acknowledged) m_InventoryRequestPending = false;
                m_OpenedLootId=inventory.LootId;
                if(acknowledged){m_LootOpenPending=false;m_InventoryView.SetReadOnly(null);}
                MoonkovLocalization.Set(m_InventoryTitle, inventory.LootId>=RaidLootContainers.FirstDeathBagId ? "FALLEN EXPEDITION / CARRIED INVENTORY"
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
            if (m_InventoryVisible) Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
            m_InventoryVisible = false;
            m_Root?.Clear();
        }
    }
}
