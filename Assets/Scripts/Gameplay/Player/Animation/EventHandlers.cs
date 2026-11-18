// 角色动画片段上的事件入口。脚落地时先记一个标志，Update 用角色位置播放脚步声音。换弹动画也可在指定帧触发一段声音。
using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class EventHandler : MonoBehaviour
    {
        public float minFootstepInterval = 0.2f;
        public SoundDef footstep;
        public SoundDef reloadSFX;

        [NonSerialized] public bool onFootDown;
        [NonSerialized] public bool onLand;
        [NonSerialized] public bool onDoubleJump;
        [NonSerialized] public bool onJumpStart;

        public void OnCharEvent(AnimationEvent e)
        {
            onFootDown = true;
        }

        public void OnLand(AnimationEvent e)
        {
            onFootDown = true;
        }

        public void ReloadAnimationSFXTrigger(AnimationEvent e)
        {
            if (reloadSFX != null)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(reloadSFX, transform);
            }
        }

        void Update()
        {
            if (onFootDown)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(footstep, transform);
                onFootDown = false;
            }
        }
    }
}