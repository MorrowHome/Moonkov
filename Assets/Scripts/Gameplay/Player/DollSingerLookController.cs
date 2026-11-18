// 本机控制的角色如何看向目标。把输入、自由观察和枪口抬升合成视角，并逐步恢复反冲。
// 合成后的角度会经输入指令送出去，让镜头方向与随后的射击共用方向。
using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS {
    // 处理自己控制角色的输入。算出的视角通过现有指令流程发送，
    // 因此摄像机看向的方向和随后那一发共用同一朝向。
    public struct HaloRecoilResponse {
        // 刚产生的反冲先在待应用值，镜头真正抬起的部分才记入可恢复角度。两者分开可避免到达镜头上限后又往回多压。
        private float pending, recoverable, age, recoveryDelay, recoveryRate;
        private float returnFraction, pendingYaw, recoverableYaw;
        private uint sequence;
        public float Remaining => pending + recoverable;

        public static float KickDegrees(uint weapon) {
            return weapon switch {
                DollSingerWeapons.Cicada => .18f,
                DollSingerWeapons.Trine or DollSingerWeapons.Rondo => .28f,
                DollSingerWeapons.Aegis => .42f,
                DollSingerWeapons.Revolver => .9f,
                DollSingerWeapons.Shotgun or DollSingerWeapons.Sundown => 1.25f,
                DollSingerWeapons.Tempest => .48f,
                DollSingerWeapons.Thorn or DollSingerWeapons.Vesper => .8f,
                DollSingerWeapons.Sniper or DollSingerWeapons.Nocturne => 1.2f,
                DollSingerWeapons.Halo => .44f,
                _ => 0f
            };
        }

        // 每次实际开火按武器、瞄准与放大倍率给相机加短促抬升。射速高时恢复也提前，不让多发都挤到画面上沿。
        public void Kick(uint weapon, float magnification, float shotInterval = .35f, bool aiming = true) {
            returnFraction = 1;
            float scale = aiming ? 1f / Mathf.Clamp(magnification, 1f, 12f) : 1f;
            float strength = aiming ? 1f : 2.8f;
            pending += Mathf.Min(KickDegrees(weapon) * scale * strength,
                Mathf.Max(0, (aiming ? 3f : 9f) * scale - Remaining));
            // 全自动或连发的两发之间就开始回收反冲。
            // 固定等待太久会让高速枪撞到视角上限，之后再开枪看不出新的反冲。
            recoveryDelay = aiming
                ? Mathf.Min(.12f, Mathf.Max(0, shotInterval) * .35f)
                : Mathf.Min(.22f, Mathf.Max(0, shotInterval) * .65f);
            recoveryRate = aiming ? 13f : 4f;
            age = 0;
        }

        // 有专门手感配置时用它确定强度、回收速度和左右跳动。横向位移由射击序号复算。
        public void Kick(
            uint weapon,
            float magnification,
            float shotInterval,
            bool aiming,
            HaloWeaponFeedbackProfile profile) {
            if (profile == null || profile.RecoilPitch <= 0) {
                Kick(weapon, magnification, shotInterval, aiming);
                return;
            }
            float scale = aiming ? 1f / Mathf.Clamp(magnification, 1f, 12f) : 1f;
            float strength = aiming ? 1f : 2.8f;
            float limit = (aiming ? Mathf.Max(3, profile.RecoilPitch * 2) : 12) * scale;
            pending += Mathf.Min(profile.RecoilPitch * scale * strength, Mathf.Max(0, limit - Remaining));
            // 左右冲量小且可复算，无需求随机摆动镜头。
            pendingYaw += profile.RecoilYaw * scale * (aiming ? 1 : 1.5f) * ((++sequence & 1) == 0 ? -.65f : 1);
            returnFraction = Mathf.Clamp01(profile.RecoilReturnFraction);
            recoveryDelay = Mathf.Max(0, profile.RecoilHoldSeconds);
            recoveryRate = Mathf.Max(.1f, profile.RecoilSettleSpeed);
            age = 0;
        }

        public void Apply(ref float pitch, float downwardInput, float deltaTime) {
            if (deltaTime <= 0) return;
            // 只对俯仰上限以内实际用掉的偏移保留回收量。
            // 接近俯仰上限时只应用剩余空间。已经被裁掉的反冲不给恢复逻辑使用。
            float kick = Mathf.Min(pending, Mathf.Max(0, pitch + 80f));
            pending = 0;
            pitch -= kick;
            recoverable += kick * returnFraction;
            recoverable = Mathf.Max(0, recoverable - Mathf.Max(0, downwardInput));
            float previous = age;
            age += deltaTime;
            float recoveryTime = Mathf.Max(0, age - recoveryDelay) - Mathf.Max(0, previous - recoveryDelay);
            float recovery = recoverable * (1f - Mathf.Exp(-recoveryRate * recoveryTime));
            recoverable -= recovery;
            pitch = Mathf.Min(80f, pitch + recovery);
        }

        // 把反冲与手动压枪叠在同一份本地视角。后续采样的射击方向因此和画面方向一致。
        public void Apply(ref float2 look, float2 playerDelta, float deltaTime) {
            if (deltaTime <= 0) return;
            float previousAge = age;
            Apply(ref look.y, playerDelta.y, deltaTime);
            look.x += pendingYaw;
            recoverableYaw += pendingYaw * returnFraction;
            pendingYaw = 0;
            if (playerDelta.x * recoverableYaw < 0)
                recoverableYaw = Mathf.MoveTowards(recoverableYaw, 0, Mathf.Abs(playerDelta.x));
            float recoveryTime = Mathf.Max(0, age - recoveryDelay) - Mathf.Max(0, previousAge - recoveryDelay);
            float recovery = recoverableYaw * (1 - Mathf.Exp(-recoveryRate * recoveryTime));
            recoverableYaw -= recovery;
            look.x -= recovery;
        }

        public void Reset() {
            pending = recoverable = age = recoveryDelay = pendingYaw = recoverableYaw = 0;
            recoveryRate = 13;
            returnFraction = 1;
            sequence = 0;
        }
    }

    /// <summary>自己控制角色的观察和探身规则，正式玩法与离线地图预览共用。</summary>
    public static class DollSingerLookController {
        // 松开自由观察键时让头部偏角逐步回零，切回常规观察不瞬间跳视角。
        public static float2 ReturnFreeLook(float2 offset, float step) {
            return new float2(Mathf.MoveTowards(offset.x, 0f, step), Mathf.MoveTowards(offset.y, 0f, step));
        }

        // 用按键区分普通转视角与只转头。计算探身后把镜头需要的旋转单独输出，身体随后移动也能保持同一视角。
        public static bool Update(
            ref float2 look,
            ref float2 freeLookOffset,
            ref float lean,
            out Quaternion viewRotation,
            DollSingerInput input,
            DollSingerMovement model,
            DollSingerView view,
            bool blocked,
            float deltaTime) {
            bool thirdPerson = !view.IsFirstPerson;
            bool freeLooking = !blocked && !thirdPerson && input.AltHeld;
            if (freeLooking) {
                freeLookOffset += new float2(input.Look.x, -input.Look.y);
                freeLookOffset = math.clamp(freeLookOffset,
                    new float2(-model.m_FreeLookYawLimit, -model.m_FreeLookPitchLimit),
                    new float2(model.m_FreeLookYawLimit, model.m_FreeLookPitchLimit));
            } else {
                if (!blocked) look += new float2(input.Look.x, -input.Look.y) * view.AimLookSensitivity;
                float step = model.m_FreeLookReturnSpeed * deltaTime;
                freeLookOffset = ReturnFreeLook(freeLookOffset, step);
            }
            if (thirdPerson) freeLookOffset = float2.zero;
            look.y = math.clamp(look.y, -80f, 80f);
            freeLookOffset.y = math.clamp(look.y + freeLookOffset.y, -80f, 80f) - look.y;
            lean = blocked || thirdPerson || model.Stance == DollSingerStance.Prone ||
                   input.Stance == DollSingerStance.Prone
                ? 0f
                : Mathf.MoveTowards(lean, input.LeanTarget, deltaTime * model.m_LeanSpeed);
            var bodyRotation = freeLooking ? model.transform.rotation : Quaternion.Euler(0f, look.x, 0f);
            if (!blocked && !thirdPerson) lean = view.LimitLeanAmount(lean, bodyRotation);
            model.SetNetworkLeanAmount(lean);
            var viewLook = look + freeLookOffset;
            viewRotation = Quaternion.Euler(viewLook.y, viewLook.x, 0f);
            // 移动系统转了镜头的父物体后，在 LateUpdate 里再应用这个世界空间旋转。
            view.SetNetworkLookRotation(viewRotation);
            return freeLooking || math.lengthsq(freeLookOffset) > 0.01f;
        }
    }
}