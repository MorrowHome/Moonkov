using UnityEngine;

namespace Unity.MP_FPS {
    public class ExitHit : StateMachineBehaviour {
        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
            animator.SetBool("IsHit", false);
        }
    }
}