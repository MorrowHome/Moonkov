using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public sealed class MoonkovAudioEnvironment : MonoBehaviour
    {
        private SoundSystem.SoundInfo m_Interior;
        private ISoundSystem m_System;
        private bool m_OnShip;
        private void Start()
        {
            m_System = GameManager.Instance != null ? GameManager.Instance.SoundSystem : null;
            MoonkovAudio.ApplyVolumes();
            _ = MoonkovAudio.Library;
        }
        private void Update()
        {
            if (GameSettings.Instance == null) return;
            bool ship = SceneManager.GetActiveScene().name == GameManager.MainMenuSceneName &&
                GameSettings.Instance.GameState != GlobalGameState.InGame;
            if (ship == m_OnShip) return;
            m_OnShip = ship;
            if (ship)
            {
                // Back on the ship the player-owned listener is gone, so make sure the camera
                // listener is enabled again before playing the interior bed.
                EnsureListener();
                m_Interior = MoonkovAudio.Play(MoonkovAudio.Library?.ShipInterior, Vector3.zero);
            }
            else StopInterior();
        }

        /// <summary>
        /// Unity is completely silent while no AudioListener in the scene is enabled, so the
        /// ship always re-arms the persistent camera listener.
        /// </summary>
        private void EnsureListener()
        {
            var camera = MainCameraSingleton.Instance;
            if (camera == null) return;
            var listener = camera.GetComponent<AudioListener>();
            if (listener == null) return;
            listener.enabled = true;
            m_System?.SetListenerTransform(listener.transform);
        }
        private void StopInterior()
        {
            if (m_Interior != null) m_System?.Stop(m_Interior, .5f);
            m_Interior = null;
        }
        private void OnDisable() { StopInterior(); m_OnShip = false; }
        private void OnApplicationQuit() => PlayerPrefs.Save();
    }
}
