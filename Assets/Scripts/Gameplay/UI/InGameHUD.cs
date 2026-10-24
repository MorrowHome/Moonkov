using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(160)]
    public class InGameHUD : MonoBehaviour
    {
        [Header("Reticle Configuration")] [SerializeField]
        private float reticleBaseSize = 80f;

        // UI Element references
        private VisualElement m_RootElement;
        private ProgressBar m_HealthBar;
        private VisualElement m_PlayerHealthBarFill;
        private ProgressBar m_AmmoBar;
        private Label m_AmmoLabel;
        private Label m_ReloadingLabel;
        private Label m_HealthState, m_HealthValue;
        private VisualElement m_Reticle;
        private static InGameHUD s_Active;
        private VisualElement m_RifleHit;
        private float m_RifleHitUntil, m_RifleHitStarted;
        private bool m_RifleKill;
        private HaloWeaponFeedbackProfile m_HitProfile;

        public static void ConfirmRifleHit(bool killed, HaloWeaponFeedbackProfile profile = null)
        {
            if (!s_Active) return;
            s_Active.PresentRifleHit(killed, profile, Time.unscaledTime);
        }

        private void PresentRifleHit(bool killed, HaloWeaponFeedbackProfile profile, float now)
        {
            // Keep the kill readable, but never let subsequent body hits prolong a
            // gold marker indefinitely and imply further kills.
            if (!killed && m_RifleKill && now < m_RifleHitUntil) return;
            m_RifleKill = killed;
            m_HitProfile = profile;
            float duration = killed ? profile?.KillDuration ?? .22f : profile?.HitDuration ?? .12f;
            m_RifleHitStarted = now;
            m_RifleHitUntil = now + duration;
            m_RifleHit?.MarkDirtyRepaint();
        }

        // UI-side timer to ensure shot feedback is visible for a minimum duration.
        private float m_shotFeedbackTimer = 0f;
        private float m_LastHealth = -1f, m_HealthRevealUntil;

        // The reticle will stay white for at least 100ms after a shot.
        private const float k_ShotFeedbackDuration = 0.1f;
        private static readonly Color k_HealthBarColor = new Color(.92f, .93f, .90f);
        private const string k_CrossReticleClass = "kits-reticle-cross";
        private const string k_TCrossReticleClass = "kits-reticle-tcross";
        private const string k_CircularCrossClass = "kits-reticle-circularcross";
        private const string k_OpenCircularReticleClass = "kits-reticle-opencircular";

        // ECS query fields
        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;

        void OnEnable()
        {
            s_Active = this;
            m_RootElement = GetComponent<UIDocument>().rootVisualElement;

            // Find the UI elements by name
            m_HealthBar = m_RootElement.Q<ProgressBar>("player-health-bar");
            if (m_HealthBar != null)
            {
                m_PlayerHealthBarFill = m_HealthBar.Q<VisualElement>(null, "unity-progress-bar__progress");
            }

            m_AmmoLabel = m_RootElement.Q<Label>("ammo-label");
            m_AmmoBar = m_RootElement.Q<ProgressBar>("player-ammo-bar");
            m_ReloadingLabel = m_RootElement.Q<Label>("reloading-label");
            m_HealthState = m_RootElement.Q<Label>("health-state");
            m_HealthValue = m_RootElement.Q<Label>("health-value");
            m_Reticle = m_RootElement.Q<VisualElement>("player-reticle");
            m_RifleHit = m_RootElement.Q<VisualElement>("rifle-hit-confirmation");
            if (m_RifleHit == null)
            {
                m_RifleHit = new VisualElement { name = "rifle-hit-confirmation", pickingMode = PickingMode.Ignore };
                m_RifleHit.style.position = Position.Absolute;
                m_RifleHit.style.left = Length.Percent(50); m_RifleHit.style.top = Length.Percent(50);
                m_RifleHit.style.width = 28; m_RifleHit.style.height = 28;
                m_RifleHit.style.marginLeft = -14; m_RifleHit.style.marginTop = -14;
                m_RootElement.Add(m_RifleHit);
            }
            m_RifleHit.generateVisualContent += DrawRifleHit;
            m_RifleHitUntil = 0;
        }

        private void OnDisable()
        {
            if (s_Active == this) s_Active = null;
            if (m_RifleHit != null) m_RifleHit.generateVisualContent -= DrawRifleHit;
            m_RifleHitUntil = 0;
        }

        private void DrawRifleHit(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = m_RifleKill ? new Color(1f, .8f, .35f) : m_HitProfile?.HitColor ?? new Color(.9f, 1f, 1f);
            float pulse = Mathf.Exp(-Mathf.Max(0, Time.unscaledTime - m_RifleHitStarted) * 35f);
            painter.lineWidth = (m_HitProfile?.HitLineWidth ?? 1.5f) + (m_RifleKill ? .4f : .25f) * pulse;
            float inner = 6f + 1.25f * pulse;
            float outer = Mathf.Clamp((m_HitProfile?.HitOuterRadius ?? 10f) + pulse, 9f, 12f);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -1 : 1, y = i < 2 ? -1 : 1;
                painter.BeginPath();
                painter.MoveTo(new Vector2(14 + x * inner, 14 + y * inner));
                painter.LineTo(new Vector2(14 + x * outer, 14 + y * outer)); painter.Stroke();
                if (m_RifleKill)
                {
                    // Forked ends distinguish a kill by shape as well as color.
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(14 + x * (outer - 2.5f), 14 + y * outer));
                    painter.LineTo(new Vector2(14 + x * outer, 14 + y * outer));
                    painter.LineTo(new Vector2(14 + x * outer, 14 + y * (outer - 2.5f)));
                    painter.Stroke();
                }
            }
        }

        private void InitializeEcs()
        {
            m_ClientWorld = null;
            // Find the active client world
            foreach (var world in World.All)
            {
                if (world.IsCreated && world.IsClient())
                {
                    m_ClientWorld = world;
                    m_EntityManager = world.EntityManager;
                    break;
                }
            }

            if (m_ClientWorld != null)
            {
                // This query finds the single entity that is both a predicted player ghost
                // and is owned by the local client.
                m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>()
                );
            }
        }

        void LateUpdate()
        {
            // Toggle HUD visibility based on the overall game state
            bool isInGame = ClientServerBootstrap.HasClientWorlds &&
                            GameSettings.Instance.GameState == GlobalGameState.InGame && !RaidHUD.CinematicViewActive;
            bool hitVisible = isInGame && Time.unscaledTime < m_RifleHitUntil;
            if (!isInGame) m_RifleHitUntil = 0;
            if (m_RifleHit != null)
            {
                m_RifleHit.style.display = hitVisible ? DisplayStyle.Flex : DisplayStyle.None;
                if (hitVisible)
                {
                    m_RifleHit.MarkDirtyRepaint();
                    m_RifleHit.style.opacity = Mathf.Clamp01((m_RifleHitUntil - Time.unscaledTime) / .06f);
                    m_RifleHit.style.rotate = new Rotate(new Angle(GetOwnedReticleRoll(), AngleUnit.Degree));
                }
            }
            if (m_RootElement.style.display != (isInGame ? DisplayStyle.Flex : DisplayStyle.None))
            {
                m_RootElement.style.display = isInGame ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!isInGame) return;

            // If the world was destroyed (e.g., returned to main menu), try to re-initialize
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
            {
                InitializeEcs();
            }

            // Ensure the ECS systems are ready
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated ||
                !m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>())
            {
                // No local player entity found, hide the HUD. This occurs when the player is dead.
                m_RootElement.style.display = DisplayStyle.None;
                return;
            }

            // Get the player's data directly from the ECS component
            PredictedPlayerGhost playerData = m_LocalPlayerQuery.GetSingleton<PredictedPlayerGhost>();
            bool statusCheck = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.hKey.isPressed;
            bool alwaysShow = PlayerPrefs.GetInt("Moonkov.AlwaysShowHUD", 0) != 0;
            if (m_LastHealth != playerData.CurrentHealth) { m_LastHealth = playerData.CurrentHealth; m_HealthRevealUntil = Time.unscaledTime + 4f; }
            var healthPanel = m_RootElement.Q<VisualElement>("health-info-container");
            if (healthPanel != null) healthPanel.style.display = alwaysShow || statusCheck || Time.unscaledTime < m_HealthRevealUntil || RaidHealth.DangerFraction(playerData) < .35f ? DisplayStyle.Flex : DisplayStyle.None;

            // Update Health Bar
            if (m_HealthBar != null)
            {
                m_HealthBar.highValue = playerData.MaxHealth > 0 ? playerData.MaxHealth : 100;
                m_HealthBar.value = playerData.CurrentHealth;

                float healthPercent = RaidHealth.DangerFraction(playerData);
                Color healthColor = healthPercent < .35f ? new Color(.95f, .47f, .38f) : k_HealthBarColor;
                healthPanel?.EnableInClassList("health-critical", healthPercent < .35f);
                MoonkovLocalization.Set(m_HealthState, healthPercent < .35f ? "HEALTH / CRITICAL" : "HEALTH");
                MoonkovLocalization.Set(m_HealthValue, "{0:0}/{1:0}", playerData.CurrentHealth, m_HealthBar.highValue);

                // Apply the calculated color to the fill element's background
                m_PlayerHealthBarFill.style.backgroundColor = healthColor;
            }

            // Update Ammo Label
            if (m_AmmoLabel != null)
            {
                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID, playerData.EquippedModules);
                int magazineSize = weaponData != null ? weaponData.MagazineSize : 0;

                // Update Ammo Text and Color
                float fraction = magazineSize > 0 ? (float)playerData.CurrentAmmo / magazineSize : 0;
                m_AmmoLabel.text = playerData.EquippedWeaponID == DollSingerWeapons.Revolver
                    ? $"REVOLVER / {playerData.CurrentAmmo} / 6"
                    : playerData.EquippedWeaponID == DollSingerWeapons.Shotgun ? $"SHOTGUN / {playerData.CurrentAmmo} / {magazineSize}"
                    : playerData.EquippedWeaponID == DollSingerWeapons.Sniper ? $"SNIPER / {playerData.CurrentAmmo} / {magazineSize}"
                    : weaponData == null ? "UNARMED"
                    : fraction > .75f ? "ENERGY / HIGH" : fraction > .4f ? "ENERGY / MEDIUM" : fraction > .15f ? "ENERGY / LOW" : "ENERGY / CRITICAL";
                var ammoPanel = m_RootElement.Q<VisualElement>("weapon-info-container");
                if (ammoPanel != null) ammoPanel.style.display = playerData.EquippedWeaponID == DollSingerWeapons.Revolver || playerData.EquippedWeaponID == DollSingerWeapons.Shotgun || playerData.EquippedWeaponID == DollSingerWeapons.Sniper || alwaysShow || statusCheck || fraction <= .15f || playerData.ControllerState.IsReloadingState ? DisplayStyle.Flex : DisplayStyle.None;
                if (playerData.CurrentAmmo == 0) m_AmmoLabel.style.color = Color.red;
                else if (playerData.CurrentAmmo <= magazineSize * 0.3f) m_AmmoLabel.style.color = Color.yellow;
                else m_AmmoLabel.style.color = Color.white;

                m_AmmoBar.highValue = magazineSize;
                m_AmmoBar.value = playerData.CurrentAmmo;
                m_AmmoBar.style.display = DisplayStyle.None;
            }

            // Update Reloading Indicator
            if (m_ReloadingLabel != null)
            {
                m_ReloadingLabel.style.display =
                    playerData.ControllerState.IsReloadingState ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Update Reticle
            UpdateReticleVisual(playerData);
        }

        private void UpdateReticleVisual(PredictedPlayerGhost playerData)
        {
            if (m_Reticle == null)
                return;

            m_Reticle.style.rotate = new Rotate(new Angle(GetOwnedReticleRoll(), AngleUnit.Degree));

            if (m_shotFeedbackTimer > 0)
            {
                m_shotFeedbackTimer -= Time.deltaTime;
            }

            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID, playerData.EquippedModules);

            if (weaponData == null)
            {
                m_Reticle.style.display = DisplayStyle.None;
                return;
            }

            if (DollSingerWeapons.IsHalo(playerData.EquippedWeaponID) && playerData.ControllerState.Aiming)
            {
                // The halo and its small local dot own the aiming sight.
                m_Reticle.style.display = DisplayStyle.None;
                return;
            }

            // Update Reticle Type
            string desiredClass = GetReticleClassName(weaponData.ReticleType);

            // Remove all reticle classes that are NOT the desired one.
            if (desiredClass != k_CrossReticleClass) m_Reticle.RemoveFromClassList(k_CrossReticleClass);
            if (desiredClass != k_TCrossReticleClass) m_Reticle.RemoveFromClassList(k_TCrossReticleClass);
            if (desiredClass != k_OpenCircularReticleClass) m_Reticle.RemoveFromClassList(k_OpenCircularReticleClass);
            if (desiredClass != k_CircularCrossClass) m_Reticle.RemoveFromClassList(k_CircularCrossClass);

            // Now, add the correct class if it's not already present.
            if (!m_Reticle.ClassListContains(desiredClass))
            {
                m_Reticle.AddToClassList(desiredClass);
            }

            // Update Reticle Visibility
            if (m_Reticle.style.display == DisplayStyle.None)
            {
                if (m_Reticle.style.position != StyleKeyword.Initial)
                {
                    m_Reticle.style.position = StyleKeyword.Initial;
                    m_Reticle.style.left = StyleKeyword.Initial;
                    m_Reticle.style.top = StyleKeyword.Initial;
                }

                m_Reticle.style.display = DisplayStyle.Flex;
            }

            m_Reticle.style.width = reticleBaseSize;
            m_Reticle.style.height = reticleBaseSize;

            // Determine if the player can shoot
            bool isReloading = playerData.ControllerState.IsReloadingState;
            bool justFired = playerData.ControllerState.Shoot;
            bool isWeaponOnCooldown = playerData.WeaponCooldown < weaponData.CooldownInMs;

            if (justFired)
            {
                m_shotFeedbackTimer = k_ShotFeedbackDuration;
            }

            bool greyReticleVisual = false;

            // Weapon reloading
            if (isReloading)
            {
                greyReticleVisual = true;
                m_shotFeedbackTimer = 0f;
            }
            // Weapon is on cooldown AND we are not in the shot feedback window.
            else if (isWeaponOnCooldown && m_shotFeedbackTimer <= 0)
            {
                greyReticleVisual = true;
            }

            // Update Reticle Color
            m_Reticle.style.unityBackgroundImageTintColor = greyReticleVisual
                ? new StyleColor(Color.grey)
                : new StyleColor(Color.white);
        }

        private static float GetOwnedReticleRoll()
        {
            if (!PlayerGhostManager.TryGetClientInstance(out var manager) || !manager ||
                !manager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players)) return 0f;
            foreach (var player in players)
                if (player && player.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                    return presentation.OwnedReticleScreenRoll;
            return 0f;
        }

        private string GetReticleClassName(ReticleType reticleType)
        {
            switch (reticleType)
            {
                case ReticleType.Cross: return k_CrossReticleClass;
                case ReticleType.TCross: return k_TCrossReticleClass;
                case ReticleType.OpenCircular: return k_OpenCircularReticleClass;
                case ReticleType.CircularCross: return k_CircularCrossClass;
                default: return "";
            }
        }
    }
}
