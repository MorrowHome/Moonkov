using UnityEngine;

namespace Unity.MP_FPS
{
    public class EnterInAir : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsInAir", true);
        }
    }
}
