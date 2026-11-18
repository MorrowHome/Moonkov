// 角色动画片段上的事件入口。脚落地时先记一个标志，Update 用角色位置播放脚步声音。换弹动画也可在指定帧触发一段声音。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    public class EventHandler : MonoBehaviour {
        public float minFootstepInterval = 0.2f;
        public SoundDef footstep;
        public SoundDef reloadSFX;
        [NonSerialized] public bool onFootDown;
        [NonSerialized] public bool onLand;
        [NonSerialized] public bool onDoubleJump;
        [NonSerialized] public bool onJumpStart;
        // 动画回调先记落脚，Update 再从角色当前的位置发声音。收到多个落脚标记也只在这一帧触发一次。
        public void OnCharEvent(AnimationEvent e) { onFootDown = true; }
        public void OnLand(AnimationEvent e) { onFootDown = true; }

        // 有配置装填声音才创建声源。动画事件只驱动听感，何时能再次开火仍看装填规则。
        public void ReloadAnimationSFXTrigger(AnimationEvent e) {
            if (reloadSFX != null) GameManager.Instance.SoundSystem.CreateEmitter(reloadSFX, transform);
        }

        private void Update() {
            // 消耗动作标记之后立刻清掉，下帧不会重播刚才的脚步。
            if (onFootDown) {
                GameManager.Instance.SoundSystem.CreateEmitter(footstep, transform);
                onFootDown = false;
            }
        }
    }
}