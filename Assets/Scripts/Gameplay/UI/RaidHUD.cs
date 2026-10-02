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
        private VisualElement m_Root, m_Result;
        private Label m_Bag, m_Timer, m_Prompt, m_Exit, m_ResultText;
        private Button m_Deploy;
        private bool m_WasSettled;
        private float m_RefreshTimer;
        private RaidSnapshotRpc m_Snapshot;

        private void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_Root.pickingMode = PickingMode.Ignore;
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.position = Position.Absolute;
            card.style.left = 20; card.style.top = 140; card.style.width = 390;
            card.style.paddingLeft = card.style.paddingRight = 16;
            card.style.paddingTop = card.style.paddingBottom = 12;
            card.style.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 0.85f);
            m_Root.Add(card);
            AddLabel(card, "MOON RAID", 24);
            m_Timer = AddLabel(card, "Connecting...", 18);
            m_Bag = AddLabel(card, "", 18);
            m_Exit = AddLabel(card, "", 18);
            m_Prompt = AddLabel(card, "", 18);
            AddLabel(card, "Blue: Dust   Orange: Alloy   Purple: Cell\nE / gamepad X: collect nearby supplies", 14);

            m_Result = new VisualElement();
            m_Result.style.position = Position.Absolute;
            m_Result.style.left = new Length(30, LengthUnit.Percent);
            m_Result.style.top = new Length(28, LengthUnit.Percent);
            m_Result.style.width = new Length(40, LengthUnit.Percent);
            m_Result.style.paddingLeft = m_Result.style.paddingRight = 28;
            m_Result.style.paddingTop = m_Result.style.paddingBottom = 28;
            m_Result.style.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 0.97f);
            m_ResultText = AddLabel(m_Result, "", 22);
            m_Deploy = new Button(Deploy) { text = "DEPLOY AGAIN" };
            m_Deploy.style.height = 48; m_Deploy.style.marginTop = 20; m_Deploy.style.fontSize = 20;
            m_Result.Add(m_Deploy);
            AddLabel(m_Result, "Stash lasts for this connection only.", 14);
            m_Root.Add(m_Result);
            m_Result.style.display = DisplayStyle.None;
        }

        private static Label AddLabel(VisualElement parent, string text, int size)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.color = Color.white; label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 8;
            parent.Add(label);
            return label;
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
            bool ready = m_Snapshot.RaidId > 0;
            bool settled = ready && m_Snapshot.Phase != RaidPhase.Active;
            MoonRaidMap.Active.ShowLoot(m_Snapshot.TakenMask, ready);
            if (settled)
            {
                // LateUpdate runs after character teardown, which can restore the previous cursor state.
                Utils.SetCursorVisible(true);
                if (!m_WasSettled) { m_Deploy.SetEnabled(true); m_Deploy.Focus(); }
            }
            m_WasSettled = settled;
            m_Result.style.display = settled ? DisplayStyle.Flex : DisplayStyle.None;

            int nearest = -1;
            float distance = RaidRules.PickupRange;
            Vector3 position = default;
            bool alive = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>();
            if (alive && ready && !settled)
            {
                position = m_PlayerQuery.GetSingleton<LocalTransform>().Position;
                var map = MoonRaidMap.Active;
                for (int i = 0; i < map.LootPositions.Length; i++)
                {
                    if ((m_Snapshot.TakenMask & (1u << i)) != 0) continue;
                    float candidate = Vector3.Distance(position, map.LootPositions[i]);
                    if (candidate < distance) { nearest = i; distance = candidate; }
                }
                bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
                               (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
                if (nearest >= 0 && pressed && UnityEngine.Cursor.lockState == CursorLockMode.Locked && !GameSettings.Instance.IsPauseMenuOpen)
                    Send(new RaidPickupRpc { RaidId = m_Snapshot.RaidId, LootId = nearest });
            }
            // Input is sampled every frame; presentation text refreshes at 10 Hz.
            m_RefreshTimer -= Time.deltaTime;
            if (m_RefreshTimer > 0) return;
            m_RefreshTimer = 0.1f;
            int seconds = Mathf.CeilToInt(m_Snapshot.TimeLeft);
            m_Timer.text = ready ? $"Raid {m_Snapshot.RaidId}   {seconds / 60:00}:{seconds % 60:00} remaining" : "Connecting...";
            int count = m_Snapshot.Dust + m_Snapshot.Alloy + m_Snapshot.Cells;
            m_Bag.text = $"Bag {count}/{RaidRules.BagCapacity}\nDust {m_Snapshot.Dust}   Alloy {m_Snapshot.Alloy}   Cells {m_Snapshot.Cells}";
            m_Prompt.text = nearest < 0 ? "Find glowing supply caches" : count >= RaidRules.BagCapacity ? "Backpack full" : $"[E / X] Collect {ItemName(nearest)}";
            if (alive && !settled)
            {
                Vector3 delta = MoonRaidMap.Active.ExtractionPosition - position;
                string heading = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? delta.x > 0 ? "E" : "W" : delta.z > 0 ? "N" : "S";
                m_Exit.text = m_Snapshot.ExtractionRemaining >= 0 ? $"EXTRACTING: {m_Snapshot.ExtractionRemaining:F1}s - stay inside" : $"Green beacon: {delta.magnitude:F0}m {heading}\nStay inside for {MoonRaidMap.Active.ExtractionSeconds:F0}s";
            }
            else m_Exit.text = settled ? "Raid ended" : "Deploying...";
            if (settled)
            {
                string title = m_Snapshot.Phase == RaidPhase.Extracted ? "EXTRACTION SUCCESS" : m_Snapshot.Phase == RaidPhase.Dead ? "KILLED IN ACTION" : "RAID TIME EXPIRED";
                string result = m_Snapshot.Phase == RaidPhase.Extracted ? $"Recovered {count} supplies" : $"Lost {count} carried supplies";
                m_ResultText.text = $"{title}\n\n{result}\nDust {m_Snapshot.Dust}   Alloy {m_Snapshot.Alloy}   Cells {m_Snapshot.Cells}\n\nSTASH\nDust {m_Snapshot.StashDust}   Alloy {m_Snapshot.StashAlloy}   Cells {m_Snapshot.StashCells}";
            }
        }

        private static string ItemName(int id) => id % 3 == 0 ? "Moon Dust" : id % 3 == 1 ? "Alloy" : "Energy Cell";

        private void Deploy()
        {
            if (m_Snapshot.RaidId <= 0 || m_Snapshot.Phase == RaidPhase.Active) return;
            if (Send(new RaidDeployRpc { SettledRaidId = m_Snapshot.RaidId }))
            {
                GameSettings.Instance.IsPauseMenuOpen = false;
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
            if (m_World != null && m_World.IsCreated)
            {
                m_StateQuery.Dispose(); m_PlayerQuery.Dispose(); m_ConnectionQuery.Dispose();
            }
            m_World = null;
            m_Root?.Clear();
        }
    }
}
