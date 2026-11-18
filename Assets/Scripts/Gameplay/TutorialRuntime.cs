// 局内教学的公共状态，包括本局有无教学标记、当前是否瞄准、以及最近使用键鼠还是手柄。
// 教学提示从这里选合适的操作键说明。
using Unity.Entities;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS {
    // Explicit per-world marker, captured on loading. Skipping prompts does not change encounter rules mid-raid.
    public struct TutorialRaidMode : IComponentData {
    }

    public static class TutorialRuntime {
        public static bool Aiming { get; private set; }
        public static bool UsingGamepad { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            Aiming = false;
            UsingGamepad = false;
            s_Pad = null;
        }

        public static void Observe(in PlayerInput input) { Aiming = input.Aim; }

        // 教学开关走当前世界的 ECS 标记。普通对局能用同一套玩家和地图逻辑，只是不走教学提示。
        public static bool Enabled(EntityManager manager) {
            using var query = manager.CreateEntityQuery(typeof(TutorialRaidMode));
            return !query.IsEmptyIgnoreFilter;
        }

        // 结合当前操作判断键鼠或手柄，切换时缓存手柄按钮的可读名字，提示能跟随设备。
        public static void UpdateDevice() {
            var pad = Gamepad.current;
            if (pad == null) UsingGamepad = false;
            if (Keyboard.current?.anyKey.isPressed == true || Mouse.current?.delta.ReadValue().sqrMagnitude > 1 ||
                Mouse.current?.leftButton.wasPressedThisFrame == true ||
                Mouse.current?.rightButton.wasPressedThisFrame == true)
                UsingGamepad = false;
            if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > .04f ||
                                pad.rightStick.ReadValue().sqrMagnitude > .04f || pad.leftTrigger.isPressed ||
                                pad.rightTrigger.isPressed || pad.buttonSouth.wasPressedThisFrame ||
                                pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
                                pad.selectButton.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame ||
                                pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame))
                UsingGamepad = true;
            if (pad != s_Pad) {
                s_Pad = pad;
                s_PadAim = "按住 " + Name(pad?.leftTrigger, "LT") + " 瞄准，" + Name(pad?.rightTrigger, "RT") + " 发射光环脉冲";
                s_PadReload = "按 " + Name(pad?.buttonNorth, "Y") + " 使用电池补能；方向键右按住防御";
                s_PadInteract = "靠近并看向设备，按住 " + Name(pad?.buttonWest, "X") + " 操作";
                s_PadHealth = "按 " + Name(pad?.selectButton, "View") + " 打开装备，切换健康页使用医疗用品。可以先退回安全处。";
            }
        }

        private static Gamepad s_Pad;
        private static string s_PadAim, s_PadReload, s_PadInteract, s_PadHealth;

        private static string Name(UnityEngine.InputSystem.Controls.ButtonControl button, string fallback) {
            return !string.IsNullOrEmpty(button?.shortDisplayName)
                ? button.shortDisplayName
                : button?.displayName ?? fallback;
        }

        // 供教学界面读已经按设备选好的操作提示。键盘改键后从现有键位中取显示文字。
        public static string HealthHint =>
            UsingGamepad
                ? s_PadHealth
                : $"按 {RaidKeyBindings.DisplayName(RaidKey.Inventory)} 打开装备，切换健康页使用医疗用品。可以先退回安全处。";

        public static string MoveHint =>
            UsingGamepad
                ? "左摇杆移动，右摇杆观察"
                : $"{RaidKeyBindings.DisplayName(RaidKey.Forward)} {RaidKeyBindings.DisplayName(RaidKey.Left)} {RaidKeyBindings.DisplayName(RaidKey.Backward)} {RaidKeyBindings.DisplayName(RaidKey.Right)} 移动，鼠标观察";

        public static string AimHint => UsingGamepad ? s_PadAim : "按住鼠标右键瞄准，左键发射光环脉冲";

        public static string ReloadHint =>
            UsingGamepad
                ? s_PadReload
                : $"光环能量会自动恢复。按 {RaidKeyBindings.DisplayName(RaidKey.Reload)} 使用电池，按住 {RaidKeyBindings.DisplayName(RaidKey.Defense)} 防御。";

        public static string InteractHint =>
            UsingGamepad ? s_PadInteract : $"靠近并看向设备，按住 {RaidKeyBindings.DisplayName(RaidKey.Interact)} 操作";
    }
}