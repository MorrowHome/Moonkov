// VisualEffectManager 创造一次性的枪口特效后用这个组件约定寿命。时间到了自动销毁，避免场景里留下用过的特效对象。
using UnityEngine;

namespace Unity.MP_FPS {
    public class DestroyAfterDelay : MonoBehaviour {
        public float Lifetime = 1f;
        private void Start() { Destroy(gameObject, Lifetime); }
    }
}