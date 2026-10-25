using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS
{
    // Explicit per-world marker, captured on loading. Skipping prompts does not change encounter rules mid-raid.
    public struct TutorialRaidMode : IComponentData { }
    public static class TutorialRuntime
    {
        public static bool Aiming { get; private set; }
        public static bool UsingGamepad { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Aiming = false; UsingGamepad = false; s_Pad = null; }
        public static void Observe(in PlayerInput input) => Aiming = input.Aim;
        public static bool Enabled(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(typeof(TutorialRaidMode));
            return !query.IsEmptyIgnoreFilter;
        }
        public static void UpdateDevice()
        {
            var pad = Gamepad.current;
            if (pad == null) UsingGamepad = false;
            if (Keyboard.current?.anyKey.isPressed == true || Mouse.current?.delta.ReadValue().sqrMagnitude > 1 ||
                Mouse.current?.leftButton.wasPressedThisFrame == true || Mouse.current?.rightButton.wasPressedThisFrame == true) UsingGamepad = false;
            if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > .04f || pad.rightStick.ReadValue().sqrMagnitude > .04f ||
                pad.leftTrigger.isPressed || pad.rightTrigger.isPressed || pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame ||
                pad.buttonNorth.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame)) UsingGamepad = true;
            if (pad != s_Pad)
            {
                s_Pad = pad;
                s_PadAim = "按住 " + Name(pad?.leftTrigger, "LT") + " 瞄准，" + Name(pad?.rightTrigger, "RT") + " 发射光环脉冲";
                s_PadReload = "按 " + Name(pad?.buttonNorth, "Y") + " 为光环充能，电池需放入口袋或胸挂";
                s_PadInteract = "靠近并看向设备，按住 " + Name(pad?.buttonWest, "X") + " 操作";
                s_PadHealth = "按 " + Name(pad?.selectButton, "View") + " 打开装备，切换健康页使用医疗用品。可以先退回安全处。";
            }
        }
        private static Gamepad s_Pad;
        private static string s_PadAim, s_PadReload, s_PadInteract, s_PadHealth;
        private static string Name(UnityEngine.InputSystem.Controls.ButtonControl button, string fallback)
            => !string.IsNullOrEmpty(button?.shortDisplayName) ? button.shortDisplayName : button?.displayName ?? fallback;
        public static string HealthHint => UsingGamepad ? s_PadHealth : "按 Tab 打开装备，切换健康页使用医疗用品。可以先退回安全处。";
        public static string MoveHint => UsingGamepad ? "左摇杆移动，右摇杆观察" : "W A S D 移动，鼠标观察";
        public static string AimHint => UsingGamepad ? s_PadAim : "按住鼠标右键瞄准，左键发射光环脉冲";
        public static string ReloadHint => UsingGamepad ? s_PadReload : "按 R 为光环充能，电池需放入口袋或胸挂";
        public static string InteractHint => UsingGamepad ? s_PadInteract : "靠近并看向设备，按住 F 操作";
    }
}
