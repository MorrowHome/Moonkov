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
            m_System = GameManager.Instance.SoundSystem;
            MoonkovAudio.ApplyVolumes();
            _ = MoonkovAudio.Library;
        }
        private void Update()
        {
            bool ship = SceneManager.GetActiveScene().name == GameManager.MainMenuSceneName &&
                GameSettings.Instance.GameState != GlobalGameState.InGame;
            if (ship == m_OnShip) return;
            m_OnShip = ship;
            if (ship) m_Interior = MoonkovAudio.Play(MoonkovAudio.Library?.ShipInterior, Vector3.zero);
            else StopInterior();
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
