// 本地角色消失时管理观战镜头，也显示支持自动复活的玩法的倒计时。远征交给结算页，经典爆破等待下回合。
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI {
    [RequireComponent(typeof(UIDocument))]
    public class RespawnScreen : MonoBehaviour {
        public Camera RespawnCamera;
        private VisualElement m_RespawnScreen;
        private Label m_RespawnTimerLabel;
        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;
        private EntityQuery m_ClashQuery;
        private MoonRaidMap m_LastRaidMap;
        private Vector3 m_LastRaidPosition;
        private bool m_HasLastRaidPosition;
        private float m_RespawnCountdown;
        private const float RESPAWN_DURATION = 5.0f;
        private void Awake() { RespawnCamera.gameObject.SetActive(false); }

        private void OnEnable() {
            m_RespawnScreen = GetComponent<UIDocument>().rootVisualElement;
            m_RespawnTimerLabel = m_RespawnScreen.Q<Label>("RespawnMessage");
        }

        private void InitializeEcs() {
            m_ClientWorld = null;
            foreach (var world in World.All)
                if (world.IsCreated && world.IsClient()) {
                    m_ClientWorld = world;
                    m_EntityManager = world.EntityManager;
                    break;
                }
            if (m_ClientWorld != null) {
                m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<GhostOwnerIsLocal>());
                m_ClashQuery = m_EntityManager.CreateEntityQuery(typeof(BountyClashClientState));
            }
        }

        private void LateUpdate() {
            if (!ClientServerBootstrap.HasClientWorlds || GameSettings.Instance.GameState != GlobalGameState.InGame) {
                m_RespawnScreen.style.display = DisplayStyle.None;
                RespawnCamera.gameObject.SetActive(false);
                m_HasLastRaidPosition = false;
                m_LastRaidMap = null;
                return;
            }
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated) {
                InitializeEcs();
                if (m_ClientWorld == null) return;
            }
            // 赏金模式玩家失去角色时先满足观战条件。它自己的快照区分倒地、死亡和离场，通用倒计时先不接管。
            if (BountyClashRuntime.Enabled(m_EntityManager)) {
                m_RespawnScreen.style.display = DisplayStyle.None;
                // Downed players retain their owned character and camera. Only a missing
                // character uses this neutral spectator; never an individual respawn timer.
                bool hasCharacter = m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>();
                RespawnCamera.gameObject.SetActive(!hasCharacter);
                if (!hasCharacter && !m_ClashQuery.IsEmptyIgnoreFilter) {
                    var data = m_EntityManager.GetComponentObject<BountyClashClientState>(
                        m_ClashQuery.GetSingletonEntity());
                    if (data.Approved && data.HasSnapshot) {
                        Vector3 target = data.Snapshot.TargetPosition;
                        RespawnCamera.transform.position = target + new Vector3(0, 18, -22);
                        RespawnCamera.transform.LookAt(target);
                    }
                }
                return;
            }
            // 远征场景由战后结算负责后续。还活着时持续保存角色位置，失去角色时可供观战镜头使用。
            if (MoonRaidMap.Active != null) {
                // Raid settlement replaces arena auto-respawn, while retaining its spectator camera.
                // 地图换了，之前的身体位置对新场景没意义。等新角色出现后重新记住有效位置。
                if (m_LastRaidMap != MoonRaidMap.Active) {
                    m_LastRaidMap = MoonRaidMap.Active;
                    m_HasLastRaidPosition = false;
                }
                m_RespawnScreen.style.display = DisplayStyle.None;
                bool alive = m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>();
                RespawnCamera.gameObject.SetActive(!alive);
                if (alive) {
                    // Typed singleton reads respect the enabled local-owner filter.
                    m_LastRaidPosition = m_LocalPlayerQuery.GetSingleton<LocalTransform>().Position;
                    m_HasLastRaidPosition = true;
                }
                if (!alive) {
                    var target = ClassicDust2Map.Active
                        ?
                        m_HasLastRaidPosition ? m_LastRaidPosition : ClassicDust2Map.Active.Site(0)
                        : m_HasLastRaidPosition
                            ? MoonRaidMap.Active.NearestExtraction(m_LastRaidPosition)
                            : MoonRaidMap.Active.ExtractionPosition;
                    RespawnCamera.transform.position = target + new Vector3(0, 25, -25);
                    RespawnCamera.transform.LookAt(target);
                }
                return;
            }
            // 通用玩法以本地拥有的角色是否存在决定复活 HUD。服务器重新生成该角色后立即收起倒计时。
            bool isPlayerAlive = m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>();
            if (isPlayerAlive) {
                RespawnCamera.gameObject.SetActive(false);
                // Player is alive, hide the respawn screen
                if (m_RespawnScreen.style.display == DisplayStyle.Flex)
                    m_RespawnScreen.style.display = DisplayStyle.None;
            } else {
                RespawnCamera.gameObject.SetActive(true);
                // Player is dead, show the respawn screen and update the timer
                if (m_RespawnScreen.style.display == DisplayStyle.None) {
                    // This is the first frame death is detected, start the countdown
                    m_RespawnCountdown = RESPAWN_DURATION;
                    m_RespawnScreen.style.display = DisplayStyle.Flex;
                }
                m_RespawnCountdown -= Time.deltaTime;
                if (m_RespawnCountdown < 0) m_RespawnCountdown = 0;
                m_RespawnTimerLabel.text = $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown).ToString()}";
            }
        }

        private void OnDisable() {
            if (m_ClientWorld != null && m_ClientWorld.IsCreated) {
                m_LocalPlayerQuery.Dispose();
                m_ClashQuery.Dispose();
            }
            m_ClientWorld = null;
            if (RespawnCamera) RespawnCamera.gameObject.SetActive(false);
        }
    }
}