using UnityEngine;

namespace Unity.MP_FPS
{
    public class ExitShooting : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsShooting", false);
        }
    }
}
