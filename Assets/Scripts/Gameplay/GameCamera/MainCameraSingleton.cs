// 保存客户端当前主 Camera 和它的 AudioListener，方便画面和音频系统找到同一个视点。
// 如果场景加载重复创建摄像机，Awake 会处理后来的那一个。
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>
    /// <see cref="MainCameraSystem"/> 通过这个类把摄像机移到客户端 World 中当前角色的位置。
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

        // 摄像机对象实例放进静态入口。若已经登记了一个，后创建的对象销毁，避免出现重复声音监听器。
        private void Awake() {
            // 已经有一个主摄像机时，销毁重复创建的对象。
            if (Instance != null) {
                Destroy(gameObject);
                return;
            }
            Instance = GetComponent<Camera>();
            AudioListener = GetComponent<AudioListener>();
        }

        // 仅销毁者正是已登记的 Camera 时才清除入口。重复摄像机销毁不会把有效实例清掉。
        private void OnDestroy() {
            if (Instance == GetComponent<Camera>()) Instance = null;
        }
    }
}