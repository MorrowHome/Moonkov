using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    public class DamageVisualsController : MonoBehaviour
    {
        [Header("References")] [Tooltip("The Material template used for the damage vignette effect.")] [SerializeField]
        private Material screenDamageMaterial;

        [Header("Intensity Range")] [SerializeField]
        private float minIntensity = 0.4f;

        [SerializeField] private float maxIntensity = 0.8f;

        [Header("Effect Parameters")] [SerializeField]
        private float fadeSpeed = 2f;

        private FullScreenPassWrapper _damagePass;
        private Material _runtimeDamageMaterial;
        private PlayerGhost _playerGhost; // To get a reference to this player's camera

        private float _currentIntensity = 0f;
        private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        private Material m_MucusMaterial;
        private FullScreenPassWrapper m_MucusPass;
        private float m_MucusRemaining, m_MucusElapsed;

        void Awake()
        {
            Debug.Assert(screenDamageMaterial,
                "[DamageVisualsController] Player has no screenDamageMaterial assigned.");

            // Get the PlayerGhost component to identify this player's camera
            _playerGhost = GetComponentInParent<PlayerGhost>();
            Debug.Assert(_playerGhost, "[DamageVisualsController] Could not find PlayerGhost component in parent.");

            _runtimeDamageMaterial = new Material(screenDamageMaterial);

            // Create an instance of our custom render pass.
            _damagePass = new FullScreenPassWrapper("_ScreenDamagePass", _runtimeDamageMaterial, 0, false, false, injectionPoint:(UnityEngine.Rendering.Universal.RenderPassEvent)602);

            // Subscribe our injection method to the render pipeline manager.
            RenderPipelineManager.beginCameraRendering += InjectRenderPass;
        }

        // This is the core of the new solution.
        private void InjectRenderPass(ScriptableRenderContext context, Camera camera)
        {
            // Only proceed if the effect is active.
            if (_currentIntensity <= 0 && m_MucusRemaining <= 0)
            {
                return;
            }

            if (_playerGhost != null && camera == _playerGhost.GetPlayerCamera())
            {
                // Setup and enqueue the pass for this camera's renderer.
                if (_currentIntensity > 0) _damagePass.EnqueuePass(camera);
                if (m_MucusRemaining > 0) m_MucusPass?.EnqueuePass(camera);
            }
        }

        void Update()
        {
            UpdateMucus();
            // The update logic now only needs to manage the intensity value.
            // The toggling of the feature is gone.
            if (_currentIntensity > 0)
            {
                _currentIntensity -= fadeSpeed * Time.deltaTime;
                _currentIntensity = Mathf.Max(0f, _currentIntensity);

                UpdateVignetteMaterial();
            }
        }

        private void UpdateMucus()
        {
            float remaining=0;
            var ghost=_playerGhost ? _playerGhost.GhostGameObject : null;
            if(ghost && ghost.Role==MultiplayerRole.ClientOwned && ghost.GhostEntityExists() && ghost.GhostHasComponent<PredictedPlayerGhost>())
            {
                var state=ghost.ReadGhostComponentData<PredictedPlayerGhost>();
                if(state.CurrentHealth>0)remaining=state.MucusBlindSeconds;
            }
            if(remaining>m_MucusRemaining+.15f)m_MucusElapsed=0;
            m_MucusRemaining=remaining;
            if(remaining<=0)return;
            if(!m_MucusMaterial)
            {
                var shader=Resources.Load<Shader>("Moonkov/AlienMucusScreen");
                if(!shader)return;
                m_MucusMaterial=new Material(shader){name="Local camera / alien mucus",hideFlags=HideFlags.DontSave};
                m_MucusPass=new FullScreenPassWrapper("_AlienMucusPass",m_MucusMaterial,0,false,false,injectionPoint:(UnityEngine.Rendering.Universal.RenderPassEvent)602);
            }
            m_MucusElapsed+=Time.deltaTime;
            m_MucusMaterial.SetFloat("_Remaining",remaining);
            m_MucusMaterial.SetFloat("_Elapsed",m_MucusElapsed);
        }
        private void OnDisable(){m_MucusRemaining=0;}

        /// <summary>
        /// Triggers the damage effect with a default intensity.
        /// </summary>
        public void TriggerDamageEffect()
        {
            var intensity = Mathf.Clamp01(minIntensity);
            float visualIntensity = Mathf.Lerp(minIntensity, maxIntensity, intensity);
            _currentIntensity = Mathf.Max(_currentIntensity, visualIntensity);

            UpdateVignetteMaterial();
        }

        /// <summary>
        /// Triggers the damage effect with a specified intensity (0 to 1).
        /// </summary>
        /// <param name="intensity"></param>
        public void TriggerDamageEffect(float intensity)
        {
            if (screenDamageMaterial == null || intensity <= 0) return;

            intensity = Mathf.Clamp01(intensity);
            float visualIntensity = Mathf.Lerp(minIntensity, maxIntensity, intensity);
            _currentIntensity = Mathf.Max(_currentIntensity, visualIntensity);

            UpdateVignetteMaterial();
        }

        /// <summary>
        /// Triggers the damage effect based on damage amount and max health.
        /// </summary>
        /// <param name="damageAmount"></param>
        /// <param name="maxHealth"></param>
        public void TriggerDamageEffect(float damageAmount, float maxHealth)
        {
            if (_runtimeDamageMaterial == null || maxHealth <= 0 || damageAmount <= 0) return;

            float damagePercent = Mathf.Clamp01(damageAmount / maxHealth);
            float visualIntensity = Mathf.Lerp(minIntensity, maxIntensity, damagePercent);

            _currentIntensity = Mathf.Max(_currentIntensity, visualIntensity);

            UpdateVignetteMaterial();
        }

        private void UpdateVignetteMaterial()
        {
            if (_runtimeDamageMaterial == null) return;
            _runtimeDamageMaterial.SetFloat(IntensityID, _currentIntensity);
        }

        void OnDestroy()
        {
            if(m_MucusMaterial)Destroy(m_MucusMaterial);
            if (_runtimeDamageMaterial != null)
            {
                Destroy(_runtimeDamageMaterial);
                _runtimeDamageMaterial = null;
            }

            // Unsubscribe to prevent memory leaks.
            RenderPipelineManager.beginCameraRendering -= InjectRenderPass;
        }
    }
}
