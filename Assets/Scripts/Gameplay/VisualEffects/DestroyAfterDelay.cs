// VisualEffectManager 创造一次性的枪口特效后用这个组件约定寿命。时间到了自动销毁，避免场景里留下用过的特效对象。
using UnityEngine;

namespace Unity.MP_FPS {
    public class DestroyAfterDelay : MonoBehaviour {
        public float Lifetime = 1f;
        // 生成枪口短特效时传入寿命，Start 用 Unity 的延迟销毁安排收回。其他脚本不需要每帧检查这一个临时物件。
        private void Start() { Destroy(gameObject, Lifetime); }
    }
}