using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Owned input response. The resulting look angle is sent through the existing
    // command path, so the camera and the following shot share the same direction.
    public struct HaloRecoilResponse
    {
        private float pending, recoverable, age, recoveryDelay, recoveryRate;
        public float Remaining => pending + recoverable;
        public static float KickDegrees(uint weapon) => weapon switch
        {
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
        public void Kick(uint weapon, float magnification, float shotInterval = .35f, bool aiming = true)
        {
            float scale = aiming ? 1f / Mathf.Clamp(magnification, 1f, 12f) : 1f;
            float strength = aiming ? 1f : 2.8f;
            pending += Mathf.Min(KickDegrees(weapon) * scale * strength, Mathf.Max(0, (aiming ? 3f : 9f) * scale - Remaining));
            // Begin settling between automatic/burst shots. A fixed long hold lets
            // fast weapons reach the travel limit and then lose every later kick.
            recoveryDelay = aiming ? Mathf.Min(.12f, Mathf.Max(0, shotInterval) * .35f) :
                Mathf.Min(.22f, Mathf.Max(0, shotInterval) * .65f);
            recoveryRate = aiming ? 13f : 4f;
            age = 0;
        }
        public void Apply(ref float pitch, float downwardInput, float deltaTime)
        {
            if (deltaTime <= 0) return;
            // Only retain recovery for movement that actually passed the pitch limit.
            float kick = Mathf.Min(pending, Mathf.Max(0, pitch + 80f));
            pending = 0; pitch -= kick; recoverable += kick;
            recoverable = Mathf.Max(0, recoverable - Mathf.Max(0, downwardInput));
            float previous = age; age += deltaTime;
            float recoveryTime = Mathf.Max(0, age - recoveryDelay) - Mathf.Max(0, previous - recoveryDelay);
            float recovery = recoverable * (1f - Mathf.Exp(-recoveryRate * recoveryTime));
            recoverable -= recovery; pitch = Mathf.Min(80f, pitch + recovery);
        }
        public void Reset() { pending = recoverable = age = recoveryDelay = 0; recoveryRate = 13; }
    }

    /// <summary>Owned-player look and lean rules, shared by gameplay and the offline map preview.</summary>
    public static class DollSingerLookController
    {
        public static float2 ReturnFreeLook(float2 offset, float step) => new float2(
            Mathf.MoveTowards(offset.x, 0f, step), Mathf.MoveTowards(offset.y, 0f, step));
        public static bool Update(ref float2 look, ref float2 freeLookOffset, ref float lean,
            out Quaternion viewRotation, DollSingerInput input, DollSingerMovement model,
            DollSingerView view, bool blocked, float deltaTime)
        {
            bool thirdPerson = !view.IsFirstPerson;
            bool freeLooking = !blocked && !thirdPerson && input.AltHeld;
            if (freeLooking)
            {
                freeLookOffset += new float2(input.Look.x, -input.Look.y);
                freeLookOffset = math.clamp(freeLookOffset,
                    new float2(-model.m_FreeLookYawLimit, -model.m_FreeLookPitchLimit),
                    new float2(model.m_FreeLookYawLimit, model.m_FreeLookPitchLimit));
            }
            else
            {
                if (!blocked) look += new float2(input.Look.x, -input.Look.y) * view.AimLookSensitivity;
                float step = model.m_FreeLookReturnSpeed * deltaTime;
                freeLookOffset = ReturnFreeLook(freeLookOffset, step);
            }
            if (thirdPerson) freeLookOffset = float2.zero;
            look.y = math.clamp(look.y, -80f, 80f);
            freeLookOffset.y = math.clamp(look.y + freeLookOffset.y, -80f, 80f) - look.y;
            lean = blocked || thirdPerson || model.Stance == DollSingerStance.Prone || input.Stance == DollSingerStance.Prone ? 0f :
                Mathf.MoveTowards(lean, input.LeanTarget, deltaTime * model.m_LeanSpeed);
            Quaternion bodyRotation = freeLooking ? model.transform.rotation : Quaternion.Euler(0f, look.x, 0f);
            if (!blocked && !thirdPerson) lean = view.LimitLeanAmount(lean, bodyRotation);
            model.SetNetworkLeanAmount(lean);
            var viewLook = look + freeLookOffset;
            viewRotation = Quaternion.Euler(viewLook.y, viewLook.x, 0f);
            // Restore this world rotation again in LateUpdate, after the motor rotates the parent.
            view.SetNetworkLookRotation(viewRotation);
            return freeLooking || math.lengthsq(freeLookOffset) > 0.01f;
        }
    }
}
