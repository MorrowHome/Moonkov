using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(150)]
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
        private VisualElement m_Reticle;

        // UI-side timer to ensure shot feedback is visible for a minimum duration.
        private float m_shotFeedbackTimer = 0f;
        private float m_LastHealth = -1f, m_HealthRevealUntil;

        // The reticle will stay white for at least 100ms after a shot.
        private const float k_ShotFeedbackDuration = 0.1f;
        private static readonly Color k_HealthBarColor = new Color(0.29f, 0.83f, 0.43f, 0.75f);
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
            m_Reticle = m_RootElement.Q<VisualElement>("player-reticle");
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
                            GameSettings.Instance.GameState == GlobalGameState.InGame;
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
            if (healthPanel != null) healthPanel.style.display = alwaysShow || statusCheck || Time.unscaledTime < m_HealthRevealUntil || playerData.CurrentHealth < playerData.MaxHealth * .35f ? DisplayStyle.Flex : DisplayStyle.None;

            // Update Health Bar
            if (m_HealthBar != null)
            {
                m_HealthBar.highValue = playerData.MaxHealth > 0 ? playerData.MaxHealth : 100;
                m_HealthBar.value = playerData.CurrentHealth;

                float healthPercent = playerData.CurrentHealth / Mathf.Max(1f, playerData.MaxHealth);
                Color healthColor = Color.Lerp(Color.red, k_HealthBarColor, healthPercent);

                // Apply the calculated color to the fill element's background
                m_PlayerHealthBarFill.style.backgroundColor = healthColor;
            }

            // Update Ammo Label
            if (m_AmmoLabel != null)
            {
                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID);
                int magazineSize = weaponData != null ? weaponData.MagazineSize : 0;

                // Update Ammo Text and Color
                float fraction = magazineSize > 0 ? (float)playerData.CurrentAmmo / magazineSize : 0;
                m_AmmoLabel.text = playerData.EquippedWeaponID == DollSingerWeapons.Revolver
                    ? $"REVOLVER / {playerData.CurrentAmmo} / 6"
                    : playerData.EquippedWeaponID == DollSingerWeapons.Shotgun ? $"SHOTGUN / {playerData.CurrentAmmo} / {magazineSize}"
                    : weaponData == null ? "UNARMED"
                    : fraction > .75f ? "ENERGY / HIGH" : fraction > .4f ? "ENERGY / MEDIUM" : fraction > .15f ? "ENERGY / LOW" : "ENERGY / CRITICAL";
                var ammoPanel = m_RootElement.Q<VisualElement>("weapon-info-container");
                if (ammoPanel != null) ammoPanel.style.display = playerData.EquippedWeaponID == DollSingerWeapons.Revolver || playerData.EquippedWeaponID == DollSingerWeapons.Shotgun || alwaysShow || statusCheck || fraction <= .15f || playerData.ControllerState.IsReloadingState ? DisplayStyle.Flex : DisplayStyle.None;
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

            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID);

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
