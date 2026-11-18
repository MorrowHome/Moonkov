// 局内设备交互用的输入绑定和瞄准计算。这里提供当前设备的按键提示，并按距离、摄像机方向和视线筛选可操作设备。
// 具体选中哪一个由 RaidInteractionSelection 决定。
using System;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS {
    // 远征 HUD 的输入只有这一个入口。界面显示的按键来自实际处理输入的绑定。
    // 两个动作共用 F 或手柄左侧面键。哪项消费按键由当前选中的目标决定。
    public sealed class RaidInteractionBindings : IDisposable {
        private readonly InputActionMap m_Map = new("RaidInteraction");
        public InputAction Operate { get; }
        public InputAction Loot { get; }
        public InputAction Inspect { get; }
        private InputDevice m_Device;

        public RaidInteractionBindings() {
            // 设备操作和搜刮可以共用一颗键，但保留两个动作给上层根据准星目标决定消费；查看走独立按键。
            Operate = m_Map.AddAction("Operate", InputActionType.Button);
            Operate.AddBinding(KeyboardPath(RaidKey.Interact));
            Operate.AddBinding("<Gamepad>/buttonWest");
            Loot = m_Map.AddAction("Loot", InputActionType.Button);
            Loot.AddBinding(KeyboardPath(RaidKey.Interact));
            Loot.AddBinding("<Gamepad>/buttonWest");
            Inspect = m_Map.AddAction("Inspect", InputActionType.Button);
            Inspect.AddBinding(KeyboardPath(RaidKey.Inspect));
            Inspect.AddBinding("<Gamepad>/leftShoulder");
            m_Map.Enable();
            RaidKeyBindings.Changed += RefreshKeys;
        }

        private static string KeyboardPath(RaidKey action) {
            var key = RaidKeyBindings.GetKey(action);
            return "<Keyboard>/" + (Keyboard.current?[key].name ?? (key == Key.Digit0 ? "0" : key.ToString()));
        }

        // 改键后只覆盖每个动作的键盘绑定，手柄的绑定继续可用。
        private void RefreshKeys() {
            string interact = KeyboardPath(RaidKey.Interact);
            Operate.ApplyBindingOverride(0, interact);
            Loot.ApplyBindingOverride(0, interact);
            Inspect.ApplyBindingOverride(0, KeyboardPath(RaidKey.Inspect));
        }

        // 有明显手柄或键鼠输入时记录最后使用的设备；微小摇杆漂移不会自动切换提示。
        public void ObserveDevice() {
            if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame &&
                (Gamepad.current.buttonWest.wasPressedThisFrame || Gamepad.current.leftShoulder.wasPressedThisFrame ||
                 Gamepad.current.leftStick.ReadValue().sqrMagnitude > .04f ||
                 Gamepad.current.rightStick.ReadValue().sqrMagnitude > .04f))
                m_Device = Gamepad.current;
            if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) || (Mouse.current != null &&
                    (Mouse.current.delta.ReadValue().sqrMagnitude > 1 || Mouse.current.leftButton.wasPressedThisFrame)))
                m_Device = Keyboard.current;
        }

        // 从当前设备实际绑定的控件生成显示名。设备暂时没被识别时退回动作的通用显示名。
        public string Label(InputAction action) {
            var device = m_Device ?? (InputDevice)Keyboard.current ?? Gamepad.current;
            for (int i = 0; i < action.controls.Count; i++) {
                var control = action.controls[i];
                if (control.device != device) continue;
                int binding = action.GetBindingIndexForControl(control);
                if (binding >= 0)
                    return action.GetBindingDisplayString(binding,
                        InputBinding.DisplayStringOptions.DontIncludeInteractions);
            }
            return action.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        public void Dispose() {
            RaidKeyBindings.Changed -= RefreshKeys;
            m_Map.Disable();
            m_Map.Dispose();
        }
    }

    public static class RaidInteractionAim {
        public const float AssistDegrees = 8;
        private static readonly int s_Mask = LayerMask.GetMask("Default", "Ground");
        private static int s_RayFrame = -1;
        private static Camera s_RayCamera;
        private static Vector3 s_RayOrigin, s_RayForward;
        private static RaycastHit s_RayHit;
        private static bool s_HasRayHit;

        // 准星直接打中配置好的交互表面时，优先于通过辅助范围选中的目标。
        // 辅助范围按空间角度计算，宽屏或窄屏不会改变目标选择。
        // 模型动画可以改变准星瞄点，服务端的交互距离仍以原锚点为准。
        public static float Score(
            Camera camera,
            Vector3 actor,
            Vector3 point,
            float range,
            Transform surface = null,
            Vector3? rangeAnchor = null) {
            // 准星对准点和角色实际可到达的锚点可以不同，比如动画模型外形会动，服务端的交互距离仍围着固定位置算。
            var anchor = rangeAnchor ?? point;
            if (!camera || !camera.isActiveAndEnabled || Vector3.Distance(actor, anchor) > range) return -1;
            var delta = point - camera.transform.position;
            float angle = Vector3.Angle(camera.transform.forward, delta);
            // 同一帧用同一个镜头和朝向挑多个目标时复用准星射线结果。
            if (s_RayFrame != Time.frameCount || s_RayCamera != camera || s_RayOrigin != camera.transform.position ||
                s_RayForward != camera.transform.forward) {
                s_RayFrame = Time.frameCount;
                s_RayCamera = camera;
                s_RayOrigin = camera.transform.position;
                s_RayForward = camera.transform.forward;
                s_HasRayHit = UnityEngine.Physics.Raycast(s_RayOrigin, s_RayForward, out s_RayHit, 8, s_Mask,
                    QueryTriggerInteraction.Ignore);
            }
            // 射线正中目标给高分；擦边才用小角度辅助。角度按三维方向量，换屏幕比例不影响可选范围。
            bool direct = s_HasRayHit && surface &&
                          (s_RayHit.transform == surface || s_RayHit.transform.IsChildOf(surface));
            if (!direct && angle > AssistDegrees) return -1;
            // 镜头看得见还不够，角色身边也得有条看得到锚点的线。第三人称镜头探出墙角时就不会隔墙操作。
            if (!Visible(camera.transform.position, point, surface) ||
                !Visible(actor + Vector3.up * 1.4f, anchor, surface))
                return -1;
            return (direct ? 1000 : 100) - angle - Vector3.Distance(actor, anchor) * .01f;
        }

        public static bool Visible(Vector3 eye, Vector3 point, Transform surface = null) {
            var delta = point - eye;
            if (delta.sqrMagnitude < .0001f) return true;
            if (!UnityEngine.Physics.Raycast(eye, delta.normalized, out var hit, Mathf.Max(0, delta.magnitude - .025f),
                    s_Mask, QueryTriggerInteraction.Ignore))
                return true;
            return surface && (hit.transform == surface || hit.transform.IsChildOf(surface));
        }

        public static Transform MissionSurface(string anchor) {
            foreach (var device in RaidMissionDevice.Active)
                if (device && device.AnchorId == anchor)
                    return device.transform;
            return null;
        }
    }
}