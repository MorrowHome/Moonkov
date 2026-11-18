// 保存客户端当前主 Camera 和它的 AudioListener，方便画面和音频系统找到同一个视点。
// 如果场景加载重复创建摄像机，Awake 会处理后来的那一个。
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>
    /// This class allows the <see cref="MainCameraSystem"/> to sync its position to the current player character position in the Client World.
    /// </summary>
    [RequireComponent(typeof(Camera), typeof(AudioListener))]
    public class MainCameraSingleton : MonoBehaviour {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() {
            if (Instance != null) {
                Destroy(Instance);
                Instance = null;
            }
        }

        public static Camera Instance;
        public AudioListener AudioListener { get; private set; } = null;

        private void Awake() {
            // We already have a main camera and don't need a new one.
            if (Instance != null) {
                Destroy(gameObject);
                return;
            }
            Instance = GetComponent<Camera>();
            AudioListener = GetComponent<AudioListener>();
        }

        private void OnDestroy() {
            if (Instance == GetComponent<Camera>()) Instance = null;
        }
    }
}