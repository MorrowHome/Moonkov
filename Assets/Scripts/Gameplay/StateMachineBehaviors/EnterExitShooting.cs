// Animator 进入射击状态时打开 IsShooting，退出时关闭。它标记的是动画状态，射击判定和伤害在玩法代码里。
using UnityEngine;

namespace Unity.MP_FPS {
    public class EnterExitShooting : StateMachineBehaviour {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
            // 这个标记供动画层使用，说明现在正处在这段射击姿势。
            animator.SetBool("IsShooting", true);
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
            animator.SetBool("IsShooting", false);
        }
    }
}