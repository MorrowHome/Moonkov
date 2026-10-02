using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Local input ownership shared by movement, view and halo presentation.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DollSingerInput : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.15f;
        [SerializeField, Min(1f)] private float gamepadLookSpeed = 120f;
        [SerializeField] private bool captureOnEnable = true;
        private CursorLockMode previousLock;
        private bool previousVisible;
        private bool captured;

        public bool CanReadPlayerInput => captured && Cursor.lockState == CursorLockMode.Locked;
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool AimHeld { get; private set; }
        public bool FirePressed { get; private set; }
        public bool FireHeld { get; private set; }
        public bool ViewPressed { get; private set; }
        public bool LightPressed { get; private set; }
        public bool ReloadPressed { get; private set; }
        public float Scroll { get; private set; }

        private void OnEnable()
        {
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            SetCapture(captureOnEnable);
        }

        private void Update()
        {
            ClearFrame();
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCapture(false);
                return;
            }
            if (!CanReadPlayerInput)
            {
                if ((keyboard != null && keyboard.f1Key.wasPressedThisFrame) ||
                    (mouse != null && mouse.leftButton.wasPressedThisFrame)) SetCapture(true);
                return; // Consume the click that reacquires the Game view.
            }
            if (keyboard != null)
            {
                Move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                JumpPressed = keyboard.spaceKey.wasPressedThisFrame;
                SprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                ViewPressed = keyboard.vKey.wasPressedThisFrame;
                LightPressed = keyboard.lKey.wasPressedThisFrame;
                ReloadPressed = keyboard.rKey.wasPressedThisFrame;
            }
            if (mouse != null)
            {
                Look = mouse.delta.ReadValue() * mouseSensitivity;
                AimHeld = mouse.rightButton.isPressed;
                FirePressed = mouse.leftButton.wasPressedThisFrame;
                FireHeld = mouse.leftButton.isPressed;
                ViewPressed |= mouse.middleButton.wasPressedThisFrame;
                Scroll = mouse.scroll.ReadValue().y;
            }
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftStick.ReadValue().sqrMagnitude > Move.sqrMagnitude) Move = gamepad.leftStick.ReadValue();
                Look += gamepad.rightStick.ReadValue() * (gamepadLookSpeed * Time.deltaTime);
                JumpPressed |= gamepad.buttonSouth.wasPressedThisFrame;
                SprintHeld |= gamepad.leftStickButton.isPressed;
                AimHeld |= gamepad.leftTrigger.isPressed;
                FirePressed |= gamepad.rightTrigger.wasPressedThisFrame;
                FireHeld |= gamepad.rightTrigger.isPressed;
                ViewPressed |= gamepad.rightStickButton.wasPressedThisFrame;
            }
            Move = Vector2.ClampMagnitude(Move, 1f);
        }

        private void ClearFrame()
        {
            Move = Look = Vector2.zero;
            JumpPressed = SprintHeld = AimHeld = FirePressed = ViewPressed = LightPressed = ReloadPressed = false;
            FireHeld = false;
            Scroll = 0f;
        }

        private void SetCapture(bool value)
        {
            captured = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
            ClearFrame();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) SetCapture(false);
        }

        private void OnDisable()
        {
            captured = false;
            ClearFrame();
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }
    }
}
