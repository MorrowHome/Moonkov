// Animator 进入腾空状态时打开 IsInAir，供后续动画过渡使用。落地动画退出时由 ExitLanding 清掉。
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
