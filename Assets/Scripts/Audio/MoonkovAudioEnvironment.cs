// GameManager 在有画面的进程里加上这段脚本。回到主菜单时，它恢复主相机的听者，并根据登录画面是否已收起切换船舱底噪。
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public sealed class MoonkovAudioEnvironment : MonoBehaviour {
        private SoundSystem.SoundInfo m_Interior;
        private ISoundSystem m_System;
        private bool m_OnShip;
        private bool m_AccessVisible = true, m_MenuActive;

        public void SetAccessVisible(bool visible) {
            m_AccessVisible = visible;
            if (visible) {
                StopInterior();
                m_OnShip = false;
            }
        }

        private void Start() {
            m_System = GameManager.Instance != null ? GameManager.Instance.SoundSystem : null;
            MoonkovAudio.ApplyVolumes();
            _ = MoonkovAudio.Library;
        }

        private void Update() {
            if (GameSettings.Instance == null) return;
            // 常驻对象可能同时陪客户端经过主菜单和战局。只有现在处在菜单并且登陆动画退开，才挂船舱循环音。
            bool menu = SceneManager.GetActiveScene().name == GameManager.MainMenuSceneName &&
                        GameSettings.Instance.GameState != GlobalGameState.InGame;
            if (menu && !m_MenuActive) EnsureListener();
            m_MenuActive = menu;
            bool ship = menu && !m_AccessVisible;
            // 同一个房间里反复 Update 只复用当前的声音。场景或登陆状态切换才重新播放或让底音淡出。
            if (ship == m_OnShip) return;
            m_OnShip = ship;
            if (ship) {
                // Back on the ship the player-owned listener is gone, so make sure the camera
                // listener is enabled again before playing the interior bed.
                EnsureListener();
                m_Interior = MoonkovAudio.Play(MoonkovAudio.Library?.ShipInterior, Vector3.zero);
            } else { StopInterior(); }
        }

        /// <summary>
        /// Unity is completely silent while no AudioListener in the scene is enabled, so the
        /// ship always re-arms the persistent camera listener.
        /// </summary>
        private void EnsureListener() {
            var camera = MainCameraSingleton.Instance;
            if (camera == null) return;
            var listener = camera.GetComponent<AudioListener>();
            if (listener == null) return;
            // 退出战局时本机角色的听者可能被销毁，恢复菜单相机的听者后才能重新收听船内声音。
            listener.enabled = true;
            m_System?.SetListenerTransform(listener.transform);
        }

        private void StopInterior() {
            if (m_Interior != null) m_System?.Stop(m_Interior, .5f);
            m_Interior = null;
        }

        private void OnDisable() {
            StopInterior();
            m_OnShip = false;
            m_MenuActive = false;
        }

        private void OnApplicationQuit() { PlayerPrefs.Save(); }
    }
}