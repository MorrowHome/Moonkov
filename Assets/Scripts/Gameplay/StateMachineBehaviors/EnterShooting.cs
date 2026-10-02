using UnityEngine;

namespace Unity.MP_FPS
{
    public class EnterShooting : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsShooting", true);
        }
    }
}
