// 战斗时的准星、命中反馈、弹药和生命状态。它从客户端 World 找代表本地角色的实体，RaidHUD 则负责背包、任务和结算。
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.NetCode;
using Unity.MP_FPS.Client;

namespace Unity.MP_FPS {
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(160)]
    public partial class InGameHUD : MonoBehaviour {
        [Header("Reticle Configuration")] [SerializeField]
        private float reticleBaseSize = 80f;

        // 界面控件引用
        private VisualElement m_RootElement;
        private RaidVitalsMonitor m_Vitals;
        private VisualElement m_VitalsPanel;
        private ProgressBar m_AmmoBar;
        private Label m_AmmoLabel;
        private Label m_ReloadingLabel;
        private VisualElement m_Reticle;
        private static InGameHUD s_Active;
        private VisualElement m_RifleHit;
        private float m_RifleHitUntil, m_RifleHitStarted;
        private bool m_RifleKill;
        private HaloWeaponFeedbackProfile m_HitProfile;

        public static void ConfirmRifleHit(bool killed, HaloWeaponFeedbackProfile profile = null) {
            if (!s_Active) return;
            s_Active.PresentRifleHit(killed, profile, Time.unscaledTime);
        }

        private void PresentRifleHit(bool killed, HaloWeaponFeedbackProfile profile, float now) {
            // 击杀反馈要留足时间让玩家看清，
            // 接下来的普通命中不能一直延长金色标记，以免看成连续击杀。
            if (!killed && m_RifleKill && now < m_RifleHitUntil) return;
            m_RifleKill = killed;
            m_HitProfile = profile;
            float duration = killed ? profile?.KillDuration ?? .22f : profile?.HitDuration ?? .12f;
            m_RifleHitStarted = now;
            m_RifleHitUntil = now + duration;
            m_RifleHit?.MarkDirtyRepaint();
        }

        // 界面计时器保证开火反馈有最短可见时间。
        private float m_shotFeedbackTimer = 0f;

        // 开火后准星至少保持白色 100 毫秒。
        private const float k_ShotFeedbackDuration = 0.1f;
        private const string k_CrossReticleClass = "kits-reticle-cross";
        private const string k_TCrossReticleClass = "kits-reticle-tcross";
        private const string k_CircularCrossClass = "kits-reticle-circularcross";
        private const string k_OpenCircularReticleClass = "kits-reticle-opencircular";

        // ECS 查询字段
        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;

        private void OnEnable() {
            ResetChargeFeedback();
            s_Active = this;
            m_RootElement = GetComponent<UIDocument>().rootVisualElement;
            m_ChargeWeaponPanel = m_RootElement.Q("weapon-info-container");

            // 根据控件名称找到对应的界面元素。
            var healthHost = m_RootElement.Q("health-body-host");
            healthHost.Clear();
            m_Vitals = new RaidVitalsMonitor();
            healthHost.Add(m_Vitals);
            m_VitalsPanel = m_RootElement.Q("health-info-container");
            m_RootElement.RegisterCallback<GeometryChangedEvent>(FitVitals);
            FitVitals(null);
            m_AmmoLabel = m_RootElement.Q<Label>("ammo-label");
            m_AmmoBar = m_RootElement.Q<ProgressBar>("player-ammo-bar");
            m_ReloadingLabel = m_RootElement.Q<Label>("reloading-label");
            m_Reticle = m_RootElement.Q<VisualElement>("player-reticle");
            m_RifleHit = m_RootElement.Q<VisualElement>("rifle-hit-confirmation");
            if (m_RifleHit == null) {
                m_RifleHit = new VisualElement { name = "rifle-hit-confirmation", pickingMode = PickingMode.Ignore };
                m_RifleHit.style.position = Position.Absolute;
                m_RifleHit.style.left = Length.Percent(50);
                m_RifleHit.style.top = Length.Percent(50);
                m_RifleHit.style.width = 28;
                m_RifleHit.style.height = 28;
                m_RifleHit.style.marginLeft = -14;
                m_RifleHit.style.marginTop = -14;
                m_RootElement.Add(m_RifleHit);
            }
            m_RifleHit.generateVisualContent += DrawRifleHit;
            m_RifleHitUntil = 0;
        }

        private void OnDisable() {
            ResetChargeFeedback();
            m_RootElement?.UnregisterCallback<GeometryChangedEvent>(FitVitals);
            if (s_Active == this) s_Active = null;
            if (m_RifleHit != null) m_RifleHit.generateVisualContent -= DrawRifleHit;
            m_RifleHitUntil = 0;
        }

        private void FitVitals(GeometryChangedEvent evt) {
            if (m_VitalsPanel == null) return;
            float panelWidth = m_RootElement.contentRect.width;
            float viewportWidth = Mathf.Min(Screen.width, panelWidth);
            bool compact = viewportWidth > 0 && viewportWidth < 1100;
            float scale = viewportWidth > 0 ? Mathf.Max(1, panelWidth / viewportWidth) : 1;
            m_VitalsPanel.style.width = compact ? 280 : 300;
            // HUD 使用绝对定位，独立计算占用范围。图标和文字要保持在屏幕上可读的大小。
            m_VitalsPanel.style.transformOrigin = new TransformOrigin(0, Length.Percent(100));
            m_VitalsPanel.style.scale = new Scale(new Vector3(scale, scale, 1));
            m_VitalsPanel.style.left = 24 * scale;
            m_VitalsPanel.style.bottom = (compact ? 112 : 28) * scale;
        }

        private void DrawRifleHit(MeshGenerationContext context) {
            var painter = context.painter2D;
            painter.strokeColor =
                m_RifleKill ? new Color(1f, .8f, .35f) : m_HitProfile?.HitColor ?? new Color(.9f, 1f, 1f);
            float pulse = Mathf.Exp(-Mathf.Max(0, Time.unscaledTime - m_RifleHitStarted) * 35f);
            painter.lineWidth = (m_HitProfile?.HitLineWidth ?? 1.5f) + (m_RifleKill ? .4f : .25f) * pulse;
            float inner = 6f + 1.25f * pulse;
            float outer = Mathf.Clamp((m_HitProfile?.HitOuterRadius ?? 10f) + pulse, 9f, 12f);
            for (int i = 0; i < 4; i++) {
                float x = i % 2 == 0 ? -1 : 1, y = i < 2 ? -1 : 1;
                painter.BeginPath();
                painter.MoveTo(new Vector2(14 + x * inner, 14 + y * inner));
                painter.LineTo(new Vector2(14 + x * outer, 14 + y * outer));
                painter.Stroke();
                if (m_RifleKill) {
                    // 用分叉形状区分击杀，除了颜色还能靠外形认出来。
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(14 + x * (outer - 2.5f), 14 + y * outer));
                    painter.LineTo(new Vector2(14 + x * outer, 14 + y * outer));
                    painter.LineTo(new Vector2(14 + x * outer, 14 + y * (outer - 2.5f)));
                    painter.Stroke();
                }
            }
        }

        // 这里连到客户端 World 和本地玩家查询。换局后 World 会换，读取前要重新找到正确的实体。
        private void InitializeEcs() {
            ResetChargeFeedback();
            m_ClientWorld = null;
            // 查找正在使用的客户端 World。
            foreach (var world in World.All)
                if (world.IsCreated && world.IsClient()) {
                    m_ClientWorld = world;
                    m_EntityManager = world.EntityManager;
                    break;
                }
            if (m_ClientWorld != null)
                // 查询预测的玩家 Ghost，
                // 且必须由本地客户端持有。
                m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        private void LateUpdate() {
            // 根据游戏全局状态切换 HUD 显隐。
            bool isInGame = ClientServerBootstrap.HasClientWorlds &&
                            GameSettings.Instance.GameState == GlobalGameState.InGame && !RaidHUD.CinematicViewActive;
            bool hitVisible = isInGame && Time.unscaledTime < m_RifleHitUntil;
            if (!isInGame) m_RifleHitUntil = 0;
            if (m_RifleHit != null) {
                m_RifleHit.style.display = hitVisible ? DisplayStyle.Flex : DisplayStyle.None;
                if (hitVisible) {
                    m_RifleHit.MarkDirtyRepaint();
                    m_RifleHit.style.opacity = Mathf.Clamp01((m_RifleHitUntil - Time.unscaledTime) / .06f);
                    m_RifleHit.style.rotate = new Rotate(new Angle(GetOwnedReticleRoll(), AngleUnit.Degree));
                }
            }
            if (m_RootElement.style.display != (isInGame ? DisplayStyle.Flex : DisplayStyle.None))
                m_RootElement.style.display = isInGame ? DisplayStyle.Flex : DisplayStyle.None;
            if (!isInGame) {
                m_ChargeFeedback.Reset();
                return;
            }

            // World 已销毁时重新初始化，例如刚从主菜单再进局。
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated) InitializeEcs();

            // 先确认 ECS 相关系统已经就绪。
            // 客户端 World 可能已销毁或本地角色还在出生。找不到唯一拥有的角色时隐藏 HUD 并清理武器反馈。
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated ||
                !m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>()) {
                // 找不到本地玩家就隐藏 HUD。角色死亡时也会走这里。
                m_ChargeFeedback.Reset();
                m_RootElement.style.display = DisplayStyle.None;
                return;
            }

            // 直接从 ECS 组件读取玩家状态。
            var playerData = m_LocalPlayerQuery.GetSingleton<PredictedPlayerGhost>();
            FitVitals(null);
            bool statusCheck = RaidKeyBindings.Held(RaidKey.Status);
            bool alwaysShow = PlayerPrefs.GetInt("Moonkov.AlwaysShowHUD", 0) != 0;
            // 打开背包或暂停时让生命体征 HUD 给页面让位。共享能量有自己的读数，因此分给它的高度会变化。
            m_VitalsPanel.style.display = RaidHUD.InventoryOpen || GameSettings.Instance.IsPauseMenuOpen
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            m_VitalsPanel.style.height = playerData.HaloEnergyInitialized && playerData.HaloEquipped ? 320 : 302;

            // 更新弹药标签。
            if (m_AmmoLabel != null) {
                var weaponData =
                    WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID,
                        playerData.EquippedModules);
                int magazineSize = weaponData != null ? weaponData.MagazineSize : 0;
                // 光环的共享能量显示由生命监视区域承担。这里隐藏旧弹药面板，免得玩家看到两个不同口径的数字。
                bool sharedHalo = playerData.HaloEquipped || DollSingerWeapons.IsHalo(playerData.EquippedWeaponID);
                m_AmmoLabel.style.display = sharedHalo ? DisplayStyle.None : DisplayStyle.Flex;

                // 计算弹药文字与颜色。
                float fraction = magazineSize > 0 ? (float)playerData.CurrentAmmo / magazineSize : 0;
                m_AmmoLabel.text = playerData.EquippedWeaponID == DollSingerWeapons.Revolver
                    ?
                    $"REVOLVER / {playerData.CurrentAmmo} / 6"
                    : playerData.EquippedWeaponID == DollSingerWeapons.Shotgun
                        ? $"SHOTGUN / {playerData.CurrentAmmo} / {magazineSize}"
                        : playerData.EquippedWeaponID == DollSingerWeapons.Sniper
                            ? $"SNIPER / {playerData.CurrentAmmo} / {magazineSize}"
                            : weaponData == null
                                ? "UNARMED"
                                : fraction > .75f
                                    ? "ENERGY / HIGH"
                                    : fraction > .4f
                                        ? "ENERGY / MEDIUM"
                                        : fraction > .15f
                                            ? "ENERGY / LOW"
                                            : "ENERGY / CRITICAL";
                var ammoPanel = m_RootElement.Q<VisualElement>("weapon-info-container");
                if (ammoPanel != null)
                    ammoPanel.style.display = playerData.EquippedWeaponID == DollSingerWeapons.Revolver ||
                                              playerData.EquippedWeaponID == DollSingerWeapons.Shotgun ||
                                              playerData.EquippedWeaponID == DollSingerWeapons.Sniper || alwaysShow ||
                                              statusCheck || fraction <= .15f ||
                                              playerData.ControllerState.IsReloadingState
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                if (playerData.CurrentAmmo == 0)
                    m_AmmoLabel.style.color = Color.red;
                else if (playerData.CurrentAmmo <= magazineSize * 0.3f)
                    m_AmmoLabel.style.color = Color.yellow;
                else
                    m_AmmoLabel.style.color = Color.white;
                if (sharedHalo && ammoPanel != null) ammoPanel.style.display = DisplayStyle.None;
                if (m_AmmoBar != null && (sharedHalo || !playerData.InventoryWeapons ||
                                          !DollSingerWeapons.IsHalo(playerData.EquippedWeaponID)))
                    m_AmmoBar.style.display = DisplayStyle.None;
            }

            // 刷新装填提示。
            if (m_ReloadingLabel != null &&
                (!playerData.InventoryWeapons || !DollSingerWeapons.IsHalo(playerData.EquippedWeaponID))) {
                m_ReloadingLabel.RemoveFromClassList("halo-charge-warning");
                MoonkovLocalization.Set(m_ReloadingLabel, "RELOADING");
                m_ReloadingLabel.style.display =
                    playerData.ControllerState.IsReloadingState ? DisplayStyle.Flex : DisplayStyle.None;
            }
            PresentChargeFeedback(playerData);
            m_ChargeWeaponPanel.style.display = DisplayStyle.None;
            m_Vitals.Present(playerData, haloNotice: m_LastChargeNotice);

            // 更新准星画面。
            UpdateReticleVisual(playerData);
        }

        // 用角色武器、准星散布和瞄准状态更新画面。准星偏转跟着自己角色的镜头侧倾，让画面和武器反馈对得上。
        private void UpdateReticleVisual(PredictedPlayerGhost playerData) {
            if (m_Reticle == null) return;
            m_Reticle.style.rotate = new Rotate(new Angle(GetOwnedReticleRoll(), AngleUnit.Degree));
            if (m_shotFeedbackTimer > 0) m_shotFeedbackTimer -= Time.deltaTime;
            var weaponData =
                WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID,
                    playerData.EquippedModules);
            if (weaponData == null) {
                m_Reticle.style.display = DisplayStyle.None;
                return;
            }
            // 瞄准时切成光环自己的准星。只在腰射时让通用准星接管，否则两套记号会重叠。
            if (DollSingerWeapons.IsHalo(playerData.EquippedWeaponID) && playerData.ControllerState.Aiming) {
                // 瞄准视图由光环和本地小圆点显示。
                m_Reticle.style.display = DisplayStyle.None;
                return;
            }

            // 选出新武器的准星类型。
            // 武器注册表决定用十字、圆环等哪种准星。应用一种样式前移除旧类型，切武器时不会叠出两个形状。
            string desiredClass = GetReticleClassName(weaponData.ReticleType);

            // 去掉和当前武器不匹配的准星样式。
            if (desiredClass != k_CrossReticleClass) m_Reticle.RemoveFromClassList(k_CrossReticleClass);
            if (desiredClass != k_TCrossReticleClass) m_Reticle.RemoveFromClassList(k_TCrossReticleClass);
            if (desiredClass != k_OpenCircularReticleClass) m_Reticle.RemoveFromClassList(k_OpenCircularReticleClass);
            if (desiredClass != k_CircularCrossClass) m_Reticle.RemoveFromClassList(k_CircularCrossClass);

            // 当前样式还没有时再加进去。
            if (!m_Reticle.ClassListContains(desiredClass)) m_Reticle.AddToClassList(desiredClass);

            // 更新准星是否可见。
            if (m_Reticle.style.display == DisplayStyle.None) {
                if (m_Reticle.style.position != StyleKeyword.Initial) {
                    m_Reticle.style.position = StyleKeyword.Initial;
                    m_Reticle.style.left = StyleKeyword.Initial;
                    m_Reticle.style.top = StyleKeyword.Initial;
                }
                m_Reticle.style.display = DisplayStyle.Flex;
            }
            m_Reticle.style.width = reticleBaseSize;
            m_Reticle.style.height = reticleBaseSize;

            // 根据装填和冷却判断玩家是否能继续开火。
            bool isReloading = playerData.ControllerState.IsReloadingState;
            bool justFired = playerData.ControllerState.Shoot;
            bool isWeaponOnCooldown = playerData.WeaponCooldown < weaponData.CooldownInMs;
            // 刚开火给准星保留极短的可见反馈。超过这个窗口还在冷却时才变灰。
            if (justFired) m_shotFeedbackTimer = k_ShotFeedbackDuration;
            bool greyReticleVisual = false;

            // 武器正在装填。
            // 装填时不能继续射击，立即清掉之前的开火反馈并变灰。
            if (isReloading) {
                greyReticleVisual = true;
                m_shotFeedbackTimer = 0f;
            }
            // 冷却中且开火反馈已结束，才显示冷却外观。
            else if (isWeaponOnCooldown && m_shotFeedbackTimer <= 0) { greyReticleVisual = true; }

            // 更新准星颜色。
            m_Reticle.style.unityBackgroundImageTintColor = greyReticleVisual
                ? new StyleColor(Color.grey)
                : new StyleColor(Color.white);
        }

        private static float GetOwnedReticleRoll() {
            if (!PlayerGhostManager.TryGetClientInstance(out var manager) || !manager ||
                !manager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players))
                return 0f;
            foreach (var player in players)
                if (player && player.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                    return presentation.OwnedReticleScreenRoll;
            return 0f;
        }

        private string GetReticleClassName(ReticleType reticleType) {
            switch (reticleType) {
                case ReticleType.Cross: return k_CrossReticleClass;
                case ReticleType.TCross: return k_TCrossReticleClass;
                case ReticleType.OpenCircular: return k_OpenCircularReticleClass;
                case ReticleType.CircularCross: return k_CircularCrossClass;
                default: return "";
            }
        }
    }
}