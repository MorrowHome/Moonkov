using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    public static class AlienSurfaceFrame
    {
        // Parallel transport keeps the tangent coherent through walls and inverted ceilings.
        public static Quaternion Transport(Quaternion frame, Vector3 nextUp)
        {
            Vector3 up = nextUp.sqrMagnitude > .00001f ? nextUp.normalized : frame * Vector3.up;
            Vector3 previousUp = frame * Vector3.up;
            Quaternion turn = Quaternion.FromToRotation(previousUp, up);
            Vector3 tangent = Vector3.ProjectOnPlane(turn * (frame * Vector3.forward), up);
            if (tangent.sqrMagnitude < .00001f) tangent = Vector3.Cross(frame * Vector3.right, up);
            return Quaternion.LookRotation(tangent.normalized, up);
        }

        public static Vector3 StepNormal(Vector3 current, Vector3 target, float degreesPerSecond, float dt) =>
            Vector3.RotateTowards(current.normalized, target.normalized,
                Mathf.Max(0f, degreesPerSecond) * Mathf.Deg2Rad * Mathf.Max(0f, dt), 0f).normalized;
    }
}
