using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
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
