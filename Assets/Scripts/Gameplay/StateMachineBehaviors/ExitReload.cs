using UnityEngine;

namespace Unity.MP_FPS
{
    public class ExitReload : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsReloading", false);
        }
    }
}