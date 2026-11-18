using UnityEngine;

namespace Unity.MP_FPS {
    public class EnterShooting : StateMachineBehaviour {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
            animator.SetBool("IsShooting", true);
        }
    }
}