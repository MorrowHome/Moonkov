using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.Transforms;
using UnityEngine;

public partial class FirstPersonController {
    private static class AnimationParameters {
        public static readonly int IsMoving = Animator.StringToHash("IsMoving"); //1P
        public static readonly int Speed = Animator.StringToHash("Speed"); //3P
        public static readonly int StrafeSpeed = Animator.StringToHash("SpeedStrafe"); //3P
        public static readonly int Shoot = Animator.StringToHash("Shoot");
        public static readonly int IsShooting = Animator.StringToHash("IsShooting");
        public static readonly int Reload = Animator.StringToHash("Reload");
        public static readonly int Jump = Animator.StringToHash("Jump");
        public static readonly int Land = Animator.StringToHash("Land");
        public static readonly int IsHit = Animator.StringToHash("IsHit");
        public static readonly int IsDead = Animator.StringToHash("IsDead"); //3P
    }

    // 其他玩家已经有插值后的位姿，直接把它送进显示对象。他们的移动不用本机重新碰撞。
    public void ApplyInterpolatedClientState(
        ref ControllerState state,
        in ControllerConsts consts,
        in LocalTransform localTransform,
        float deltaTime,
        bool applyAnimation = false) {
        // 应用已平滑过的旋转。
        state.CurrentPosition = localTransform.Position;
        state.CurrentRotation = localTransform.Rotation;
        transform.SetPositionAndRotation(state.CurrentPosition, state.CurrentRotation);
    }

    // 先消费只播一遍的事件，再更新每帧连续变化的动画参数。这样连续的移动姿态和开火、落地不会互相覆盖。
    public void ApplyAnimatorState(in ControllerState state, in ControllerConsts consts, float deltaTime) {
        if (m_PlayerGhost == null || m_PlayerGhost.GhostGameObject == null)
            return; //Ghost 还没链接时先返回。
        var predictedPlayerGhost = m_PlayerGhost.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>();
        bool isClientOwned = m_PlayerGhost.Role == MultiplayerRole.ClientOwned;
        var animator = isClientOwned ? m_Animator_1P : m_Animator_3P;
        if (animator == null) return;

        // 先处理只触发一次的动画事件。
        HandleAnimationEvents(animator, predictedPlayerGhost);

        // 再更新持续变化的动画参数。
        if (isClientOwned)
            ApplyFirstPersonMovementAnimation(animator, state);
        else
            ApplyThirdPersonMovementAnimation(animator, state, predictedPlayerGhost.CurrentHealth);
    }

    private void HandleAnimationEvents(Animator animator, in PredictedPlayerGhost ghostState) {
        // 射击动作
        if (ghostState.LastShotTick > _lastAnimatedShotTick) {
            if (!ghostState.ControllerState.IsReloadingState && !animator.GetBool(AnimationParameters.IsShooting)) {
                if (m_EnableAnimationLogging)
                    Debug.Log($"[ANIMATION] Firing SHOOT trigger at Tick: {ghostState.LastShotTick.ToString()}");
                animator.SetTrigger(AnimationParameters.Shoot);
            }
            _lastAnimatedShotTick = ghostState.LastShotTick;
        }

        // 装填动作
        if (ghostState.LastReloadTick > _lastAnimatedReloadTick) {
            if (m_EnableAnimationLogging)
                Debug.Log($"[ANIMATION] Firing RELOAD trigger at Tick: {ghostState.LastReloadTick.ToString()}");
            animator.SetTrigger(AnimationParameters.Reload);
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghostState.EquippedWeaponID);
            if (weaponData != null && weaponData.WeaponReloadSfx != null) {
                var eventHandler = animator.GetComponent<Unity.MP_FPS.EventHandler>();
                eventHandler.reloadSFX = weaponData.WeaponReloadSfx;
            }
            _lastAnimatedReloadTick = ghostState.LastReloadTick;
        }

        // 起跳动作
        if (ghostState.LastJumpTick > _lastAnimatedJumpTick) {
            if (m_EnableAnimationLogging)
                Debug.Log($"[ANIMATION] Firing JUMP trigger at Tick: {ghostState.LastJumpTick.ToString()}");
            animator.SetTrigger(AnimationParameters.Jump);
            _lastAnimatedJumpTick = ghostState.LastJumpTick;
        }

        // 落地动作
        if (ghostState.LastLandTick > _lastAnimatedLandTick) {
            if (m_EnableAnimationLogging)
                Debug.Log($"[ANIMATION] Firing LAND trigger at Tick: {ghostState.LastLandTick.ToString()}");
            animator.SetTrigger(AnimationParameters.Land);
            _lastAnimatedLandTick = ghostState.LastLandTick;
        }

        // 受击动作
        if (ghostState.LastHitTick > _lastProcessedHitTick) {
            if (m_PlayerGhost.Role == MultiplayerRole.ClientOwned) {
                if (m_EnableAnimationLogging)
                    Debug.Log($"[ANIMATION] Firing 1P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");

                // 自己的镜头反馈独立观察网络确认的组织伤害，
                // 不依赖动画刷新。护盾或代谢变化不应重复播放枪伤血效。
            } else {
                if (m_EnableAnimationLogging)
                    Debug.Log($"[ANIMATION] Firing 3P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");

                // 触发第三人称模型的受击动作。
                animator.SetTrigger(AnimationParameters.IsHit);
            }
            if (PlayerHitSFX != null && ghostState.LastDamageAmount > 0 && !InjuryFeedbackState.Systemic(ghostState))
                GameManager.Instance.SoundSystem.CreateEmitter(PlayerHitSFX, transform.position);
            _lastProcessedHitTick = ghostState.LastHitTick;
        }
    }

    private void ApplyFirstPersonMovementAnimation(Animator animator, in ControllerState state) {
        float targetIsMovingValue = state.AnimatorTargetSpeed != 0.0f ? 1.0f : 0.0f;
        animator.SetFloat(AnimationParameters.IsMoving, targetIsMovingValue);
    }

    private void ApplyThirdPersonMovementAnimation(Animator animator, in ControllerState state, float currentHealth) {
        var relativeSpeed = math.mul(math.inverse(state.CurrentRotation), state.MovementRequest);
        float speedZ = math.abs(relativeSpeed.z) < 0.01f ? 0.0f : math.sign(relativeSpeed.z);
        float speedX = math.abs(relativeSpeed.x) < 0.01f ? 0.0f : math.sign(relativeSpeed.x);
        animator.SetFloat(AnimationParameters.Speed, speedZ);
        animator.SetFloat(AnimationParameters.StrafeSpeed, speedX);
        bool isDead = currentHealth <= 0;
        animator.SetBool(AnimationParameters.IsDead, isDead);
    }
}
