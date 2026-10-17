using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    /// <summary>Client presentation for the authored raid route. Never drives enemy AI or spawning.</summary>
    [DisallowMultipleComponent]
    public sealed class RaidThreatAtmosphere : MonoBehaviour
    {
        [SerializeField] private SoundDef environmentBed;
        [SerializeField] private SoundDef structureCreak;
        [SerializeField] private SoundDef alienRustle;
        [SerializeField, Min(5)] private float creatureSoundRange = 42;
        private MoonRaidMap m_Map;
        private ISoundSystem m_System;
        private SoundSystem.SoundInfo m_Bed, m_Creak;
        private Volume m_Volume;
        private VolumeProfile m_Profile;
        private Vignette m_Vignette;
        private UniversalAdditionalCameraData m_CameraData;
        private bool m_PreviousPostProcessing;
        private LayerMask m_PreviousVolumeMask;
        private System.Random m_Random;
        private float m_NextSample, m_NextCreak, m_Pressure;

        private void Start()
        {
            var game = GameManager.Instance;
            if (!game || game.IsHeadless) { enabled = false; return; }
            m_Map = GetComponent<MoonRaidMap>();
            m_System = game.SoundSystem;
            m_Random = new System.Random(System.Guid.NewGuid().GetHashCode());
            // Runtime profile leaves authored sky, exposure, bloom and other maps untouched.
            var root = new GameObject("Raid atmosphere volume");
            root.transform.SetParent(transform, false);
            m_Volume = root.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = 40;
            m_Volume.weight = 0;
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.hideFlags = HideFlags.DontSave;
            m_Volume.sharedProfile = m_Profile;
            var color = m_Profile.Add<ColorAdjustments>();
            color.saturation.Override(-12);
            color.contrast.Override(5);
            var white = m_Profile.Add<WhiteBalance>();
            white.temperature.Override(-8);
            m_Vignette = m_Profile.Add<Vignette>();
            m_Vignette.intensity.Override(.18f);
            m_Vignette.smoothness.Override(.45f);
        }

        private void Update()
        {
            if (Time.time < m_NextSample) return;
            m_NextSample = Time.time + .5f;
            if (!m_Map || m_System == null ||
                !PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientOwned, out var manager) ||
                !manager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players) || players.Count == 0 ||
                !IsLiving(players[0]))
            {
                StopPresentation();
                return;
            }
            var player = players[0];
            BindCamera(player.GetPlayerCamera());
            Vector3 listener = player.CameraTarget ? player.CameraTarget.position : player.transform.position;
            bool nearby = ProceduralAlienEnemy.TryGetNearbySoundSource(player.World, listener,
                creatureSoundRange, out var source);
            float pressure = nearby ? 1 - Mathf.Clamp01(Vector3.Distance(listener, source) / creatureSoundRange) : 0;
            m_Pressure = Mathf.MoveTowards(m_Pressure, pressure, .15f);
            m_Volume.weight = Mathf.MoveTowards(m_Volume.weight, 1, .15f);
            m_Vignette.intensity.value = Mathf.Lerp(.18f, .28f, m_Pressure);

            if (!Valid(m_Bed))
            {
                m_Bed = MoonkovAudio.Play(environmentBed, listener, .1f);
                m_NextCreak = Time.time + Range(9, 16);
            }
            if (Valid(m_Bed)) m_Bed.Handle.Emitter.SetVolume(Mathf.Lerp(.45f, .8f, m_Pressure));
            // Creature-local feedback owns idle vocals; the atmosphere layer only supplies the bed/structure.
            if (Time.time < m_NextCreak) return;
            m_NextCreak = Time.time + Range(14, 25);
            var anchors = m_Map.LootPositions;
            if (anchors.Length == 0) return;
            int first = m_Random.Next(anchors.Length);
            for (int i = 0; i < anchors.Length; i++)
            {
                Vector3 point = anchors[(first + i) % anchors.Length] + Vector3.up * 2;
                float distance = Vector3.Distance(listener, point);
                if (distance < 8 || distance > 55) continue;
                Stop(ref m_Creak);
                m_Creak = MoonkovAudio.Play(structureCreak, point, .65f);
                break;
            }
        }

        private static bool IsLiving(PlayerGhost player) => player && player.GhostGameObject &&
            player.GhostGameObject.GhostEntityExists() && player.GhostGameObject.GhostHasComponent<PredictedPlayerGhost>() &&
            player.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>().CurrentHealth > 0;
        private static bool Valid(SoundSystem.SoundInfo info) => info?.Handle != null && info.Handle.IsValid();
        private float Range(float min, float max) => min + (float)m_Random.NextDouble() * (max - min);
        private void Stop(ref SoundSystem.SoundInfo info)
        {
            if (Valid(info)) m_System?.Stop(info, .4f);
            info = null;
        }
        private void StopPresentation()
        {
            Stop(ref m_Bed); Stop(ref m_Creak);
            if (m_Volume) m_Volume.weight = 0;
            RestoreCamera();
            m_Pressure = 0;
        }
        private void BindCamera(Camera camera)
        {
            var data = camera ? camera.GetUniversalAdditionalCameraData() : null;
            if (data == m_CameraData) return;
            RestoreCamera();
            if (!data) return;
            m_CameraData = data;
            m_PreviousPostProcessing = data.renderPostProcessing;
            m_PreviousVolumeMask = data.volumeLayerMask;
            data.renderPostProcessing = true;
            data.volumeLayerMask |= 1; // The runtime atmosphere volume uses Default.
        }
        private void RestoreCamera()
        {
            if (m_CameraData)
            {
                m_CameraData.renderPostProcessing = m_PreviousPostProcessing;
                m_CameraData.volumeLayerMask = m_PreviousVolumeMask;
            }
            m_CameraData = null;
        }
        private void OnDisable() => StopPresentation();
        private void OnDestroy()
        {
            StopPresentation();
            if (m_Volume) Destroy(m_Volume.gameObject);
            if (!m_Profile) return;
            foreach (var component in m_Profile.components) Destroy(component);
            Destroy(m_Profile);
        }
    }
}
