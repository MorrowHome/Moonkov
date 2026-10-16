using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS
{
    // Only wired in the independent placeholder scene, not registered on the formal map.
    public sealed class AirlockPrototypeDemo : MonoBehaviour
    {
        [SerializeField] private GameObject m_CharacterPrefab;
        [SerializeField] private Transform m_Spawn;
        [SerializeField] private TextMesh m_SuitReadout;
        [SerializeField] private AirlockController m_Airlock;
        [SerializeField] private UnityEngine.Audio.AudioMixer m_ValidationMixer;
        [SerializeField] private UnityEngine.UIElements.PanelSettings m_InteractionPanelSettings;
        private SoundSystem m_ValidationSound;
        private SoundGameObjectPool m_ValidationPool;
        private PredictedPlayerGhost m_Health;
        private float m_NextReadout;
        public GameObject Character { get; private set; }
        public PredictedPlayerGhost Health => m_Health;
        public void Configure(GameObject prefab, Transform spawn, TextMesh readout, AirlockController airlock)
        { m_CharacterPrefab = prefab; m_Spawn = spawn; m_SuitReadout = readout; m_Airlock = airlock; }
        public void ConfigureAudioMixer(UnityEngine.Audio.AudioMixer mixer) => m_ValidationMixer = mixer;
        private void Start()
        {
            if (!m_CharacterPrefab || !m_Spawn) return;
            var staging = new GameObject("EVA player staging"); staging.SetActive(false);
            Character = Instantiate(m_CharacterPrefab, m_Spawn.position, m_Spawn.rotation, staging.transform);
            Character.name = "EVA_Validation_ActualPlayer";
            if (Character.GetComponent<PlayerGhost>())
            {
                foreach (var behaviour in Character.GetComponents<MonoBehaviour>())
                    if (behaviour is PlayerGhost || behaviour is DollSingerNetworkPresentation || behaviour.GetType().Name == "GhostGameObject") behaviour.enabled = false;
                Character.AddComponent<AirlockGameplayPlayer>().Configure(m_CharacterPrefab);
            }
            Character.transform.SetParent(null, true); Destroy(staging);
            var view = Character.GetComponentInChildren<DollSingerView>(true); if (view) view.SetFirstPerson(true);
            if (!GameManager.Instance && m_ValidationMixer && view && view.camera)
            {
                ConfigVar.Init();
                m_ValidationPool = new SoundGameObjectPool("EVA validation audio pool", 16);
                m_ValidationSound = new SoundSystem(); m_ValidationSound.Init(view.camera.transform, 16, m_ValidationPool, m_ValidationMixer);
                m_Airlock.GetComponent<AirlockAudio>()?.BindSoundSystem(m_ValidationSound);
            }
            if (view && view.camera) m_Airlock.GetComponent<AirlockAudio>()?.BindListener(view.camera.transform);
            if (m_InteractionPanelSettings && view && view.camera)
            {
                var ui = new GameObject("EVA interaction HUD"); ui.transform.SetParent(Character.transform, false);
                var document = ui.AddComponent<UnityEngine.UIElements.UIDocument>(); document.panelSettings = m_InteractionPanelSettings; document.sortingOrder = 100;
                ui.AddComponent<AirlockInteractionPrompt>().Initialize(document, view.camera, Character.transform);
            }
            RaidHealth.Initialize(ref m_Health); m_Health.Oxygen = 80;
        }
        private void Update()
        {
            if (!Character) return;
            m_ValidationSound?.UpdateSoundSystem();
            // Reuses exactly the same survival rule as the server; these values are diagnostic, not a second tank system.
            RaidHealth.TickOxygen(ref m_Health, BreathableZone.Contains(Character.transform.position + Vector3.up),
                Time.deltaTime, 600, 5, 8, 3, 0);
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
                AirlockButton.TryPressFromCamera(Camera.main, Character.transform.position);
            if (m_SuitReadout && m_Airlock && Time.unscaledTime >= m_NextReadout)
            {
                m_NextReadout = Time.unscaledTime + .1f;
                m_SuitReadout.text = $"REPLACEABLE EVA MODULE / LOCAL VALIDATION\nSUIT O2 {m_Health.Oxygen:0.0}% / {(m_Health.BreathableAir ? "BREATHABLE" : "SUIT RESERVE")}\n" +
                    $"CHAMBER {m_Airlock.State.PressureKPa:0.0} kPa\n[F] OPERATE / SCROLL SELECTS CONTROL";
            }
        }
        private void OnDestroy() { m_ValidationSound?.KillAll(); m_ValidationPool?.Dispose(); if (Character) Destroy(Character); }
    }
}
