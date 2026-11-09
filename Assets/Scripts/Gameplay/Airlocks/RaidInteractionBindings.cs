using System;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS
{
    // One action owner for the raid HUD. Prompts read the same bindings that consume input.
    // Separate actions share F / the west face button; target selection owns each press.
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
            Operate.AddBinding(KeyboardPath(RaidKey.Interact)); Operate.AddBinding("<Gamepad>/buttonWest");
            Loot = m_Map.AddAction("Loot", InputActionType.Button);
            Loot.AddBinding(KeyboardPath(RaidKey.Interact)); Loot.AddBinding("<Gamepad>/buttonWest");
            Inspect = m_Map.AddAction("Inspect", InputActionType.Button);
            Inspect.AddBinding(KeyboardPath(RaidKey.Inspect)); Inspect.AddBinding("<Gamepad>/leftShoulder");
            m_Map.Enable();
            RaidKeyBindings.Changed += RefreshKeys;
        }

        private static string KeyboardPath(RaidKey action)
        {
            Key key = RaidKeyBindings.GetKey(action);
            return "<Keyboard>/" + (Keyboard.current?[key].name ?? (key == Key.Digit0 ? "0" : key.ToString()));
        }

        private void RefreshKeys()
        {
            string interact = KeyboardPath(RaidKey.Interact);
            Operate.ApplyBindingOverride(0, interact);
            Loot.ApplyBindingOverride(0, interact);
            Inspect.ApplyBindingOverride(0, KeyboardPath(RaidKey.Inspect));
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

        public void Dispose()
        {
            RaidKeyBindings.Changed -= RefreshKeys;
            m_Map.Disable(); m_Map.Dispose();
        }
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
        // Animated bodies can move their aim point without moving the server's access anchor.
        public static float Score(Camera camera, Vector3 actor, Vector3 point, float range, Transform surface = null, Vector3? rangeAnchor = null)
        {
            Vector3 anchor = rangeAnchor ?? point;
            if (!camera || !camera.isActiveAndEnabled || Vector3.Distance(actor, anchor) > range) return -1;
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
            if (!Visible(camera.transform.position, point, surface) || !Visible(actor + Vector3.up * 1.4f, anchor, surface)) return -1;
            return (direct ? 1000 : 100) - angle - Vector3.Distance(actor, anchor) * .01f;
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
