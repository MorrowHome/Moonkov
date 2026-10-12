using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Character-style orbit and camera-relative intent. The surface motor owns motion;
    /// this camera retains a world-up horizon while the creature crosses walls and ceilings.</summary>
    public sealed class SpiderCharacterView
    {
        private readonly Camera m_Camera;
        private readonly int m_Mask;
        private float m_Yaw, m_Pitch = 18, m_Distance = 5;
        private Vector3 m_Tangent;
        private Vector3 m_LastUp;
        public bool HasControl => Application.isFocused && Cursor.lockState == CursorLockMode.Locked;

        public SpiderCharacterView(Camera camera, SurfaceMotor.Pose pose, int mask)
        {
            m_Camera = camera; m_Mask = mask;
            Vector3 forward = Vector3.ProjectOnPlane(pose.Forward, Vector3.up).normalized;
            m_Yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            m_Tangent = pose.Forward; m_LastUp = pose.Up;
        }

        public void ReadInput()
        {
            var mouse = Mouse.current; var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Unlock();
            else if (Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (mouse == null || !HasControl) return;
            Vector2 delta = mouse.delta.ReadValue();
            m_Yaw += delta.x * .13f;
            m_Pitch = Mathf.Clamp(m_Pitch - delta.y * .13f, -65, 75);
            m_Distance = Mathf.Clamp(m_Distance - mouse.scroll.ReadValue().y * .003f, 2.8f, 8);
        }

        public Vector3 TangentForward(SurfaceMotor.Pose pose)
        {
            // Transport the last valid tangent through a surface transition. When looking along
            // its normal, projecting the view becomes singular; preserve the transported tangent.
            m_Tangent = Quaternion.FromToRotation(m_LastUp, pose.Up) * m_Tangent;
            m_LastUp = pose.Up;
            // Orbit pitch only changes inspection. Including it in movement would reverse W
            // when the horizontal view points into the wall during a floor-to-wall transition.
            Vector3 view = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward;
            Vector3 projected = Vector3.ProjectOnPlane(view, pose.Up);
            if (projected.sqrMagnitude > .025f) m_Tangent = projected.normalized;
            return m_Tangent;
        }

        public void Draw(SurfaceMotor.Pose pose)
        {
            if (!m_Camera) return;
            Quaternion rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0);
            Vector3 target = pose.Position + pose.Up * .20f;
            Vector3 backwards = rotation * Vector3.back;
            float distance = m_Distance;
            if (Physics.SphereCast(target, .18f, backwards, out var hit, distance, m_Mask,
                QueryTriggerInteraction.Ignore)) distance = Mathf.Max(.25f, hit.distance - .20f);
            m_Camera.transform.SetPositionAndRotation(target + backwards * distance, rotation);
        }

        public static void Unlock() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
