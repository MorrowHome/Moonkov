// Animator 进入射击状态时打开 IsShooting，退出时关闭。它标记的是动画状态，射击判定和伤害在玩法代码里。
using UnityEngine;

namespace Unity.MP_FPS
{
    public class EnterExitShooting : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsShooting", true);
        }

        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsShooting", false);
        }
    }
}
