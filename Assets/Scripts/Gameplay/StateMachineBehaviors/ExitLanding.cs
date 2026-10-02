using UnityEngine;

namespace Unity.MP_FPS
{
    public class ExitLanding : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsInAir", false);
        }
    }
}
