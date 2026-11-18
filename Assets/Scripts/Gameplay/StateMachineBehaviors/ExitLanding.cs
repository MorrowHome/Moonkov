// Animator 退出落地状态时清掉 IsInAir，让接下来的动画回到地面分支。
using UnityEngine;

namespace Unity.MP_FPS {
    public class ExitLanding : StateMachineBehaviour {
        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
            // 落地片段播放完才退出空中姿态，让过渡时能完整播完动作。
            animator.SetBool("IsInAir", false);
        }
    }
}