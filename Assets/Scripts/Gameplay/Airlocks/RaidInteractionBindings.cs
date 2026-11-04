using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS
{
    // One action owner for the raid HUD. Prompts read the same bindings that consume input.
    // Separate actions preserve the existing keyboard controls while the pad shares one button.
    public sealed class RaidInteractionBindings : IDisposable
    {
        private readonly InputActionMap m_Map = new InputActionMap("RaidInteraction");
        public InputAction Operate { get; }
        public InputAction Loot { get; }
        public InputAction Inspect { get; }
        private InputDevice m_Device;

        public RaidInteractionBindings()
        {
            Operate = m_Map.AddAction("Operate", InputActionType.Button);
            Operate.AddBinding("<Keyboard>/f"); Operate.AddBinding("<Gamepad>/buttonWest");
            Loot = m_Map.AddAction("Loot", InputActionType.Button);
            Loot.AddBinding("<Keyboard>/e"); Loot.AddBinding("<Gamepad>/buttonWest");
            Inspect = m_Map.AddAction("Inspect", InputActionType.Button);
            Inspect.AddBinding("<Keyboard>/i"); Inspect.AddBinding("<Gamepad>/leftShoulder");
            m_Map.Enable();
        }

        public void ObserveDevice()
        {
            if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame &&
                (Gamepad.current.buttonWest.wasPressedThisFrame || Gamepad.current.leftShoulder.wasPressedThisFrame ||
                 Gamepad.current.leftStick.ReadValue().sqrMagnitude > .04f ||
                 Gamepad.current.rightStick.ReadValue().sqrMagnitude > .04f)) m_Device = Gamepad.current;
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame ||
                Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 1 || Mouse.current.leftButton.wasPressedThisFrame))
                m_Device = Keyboard.current;
        }

        public string Label(InputAction action)
        {
            var device = m_Device ?? (InputDevice)Keyboard.current ?? Gamepad.current;
            for (int i = 0; i < action.controls.Count; i++)
            {
                var control = action.controls[i];
                if (control.device != device) continue;
                int binding = action.GetBindingIndexForControl(control);
                if (binding >= 0) return action.GetBindingDisplayString(binding,
                    InputBinding.DisplayStringOptions.DontIncludeInteractions);
            }
            return action.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        public void Dispose() { m_Map.Disable(); m_Map.Dispose(); }
    }

    public static class RaidInteractionAim
    {
        public const float AssistDegrees = 8;
        private static readonly int s_Mask = LayerMask.GetMask("Default", "Ground");
        private static int s_RayFrame = -1;
        private static Camera s_RayCamera;
        private static Vector3 s_RayOrigin, s_RayForward;
        private static RaycastHit s_RayHit;
        private static bool s_HasRayHit;

        // A direct hit on the authored interaction surface beats every assisted candidate.
        // The small cone is angular, so aspect ratio does not change target selection.
        public static float Score(Camera camera, Vector3 actor, Vector3 point, float range, Transform surface = null)
        {
            if (!camera || !camera.isActiveAndEnabled || Vector3.Distance(actor, point) > range) return -1;
            Vector3 delta = point - camera.transform.position;
            float angle = Vector3.Angle(camera.transform.forward, delta);
            if (s_RayFrame != Time.frameCount || s_RayCamera != camera || s_RayOrigin != camera.transform.position || s_RayForward != camera.transform.forward)
            {
                s_RayFrame = Time.frameCount; s_RayCamera = camera;
                s_RayOrigin = camera.transform.position; s_RayForward = camera.transform.forward;
                s_HasRayHit = UnityEngine.Physics.Raycast(s_RayOrigin, s_RayForward, out s_RayHit, 8, s_Mask, QueryTriggerInteraction.Ignore);
            }
            bool direct = s_HasRayHit && surface && (s_RayHit.transform == surface || s_RayHit.transform.IsChildOf(surface));
            if (!direct && angle > AssistDegrees) return -1;
            if (!Visible(camera.transform.position, point, surface) || !Visible(actor + Vector3.up * 1.4f, point, surface)) return -1;
            return (direct ? 1000 : 100) - angle - Vector3.Distance(actor, point) * .01f;
        }

        public static bool Visible(Vector3 eye, Vector3 point, Transform surface = null)
        {
            var delta = point - eye;
            if (delta.sqrMagnitude < .0001f) return true;
            if (!UnityEngine.Physics.Raycast(eye, delta.normalized, out var hit,
                Mathf.Max(0, delta.magnitude - .025f), s_Mask, QueryTriggerInteraction.Ignore)) return true;
            return surface && (hit.transform == surface || hit.transform.IsChildOf(surface));
        }

        public static Transform MissionSurface(string anchor)
        {
            foreach (var device in RaidMissionDevice.Active)
                if (device && device.AnchorId == anchor) return device.transform;
            return null;
        }
    }
}
