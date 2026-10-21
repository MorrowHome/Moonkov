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
        private bool wasBlocked;

        // Assigned by the host game; the standalone character demo has no UI dependency.
        public System.Func<bool> GameplayInputBlocked { get; set; }
        public System.Func<bool> TimedActionInputActive { get; set; }
        private bool IsGameplayBlocked => GameplayInputBlocked?.Invoke() == true;
        private bool suppressTiptoeUntilRelease;
        private int settingsRevision = -1;

        public void ApplyViewSettings()
        {
            if (settingsRevision == DollSingerViewSettings.Revision) return;
            mouseSensitivity = DollSingerViewSettings.MouseSensitivity;
            settingsRevision = DollSingerViewSettings.Revision;
        }

        // Cancellation owns this press even after the server acknowledges it. Holding
        // X must not turn into tiptoe halfway through the same physical key gesture.
        public bool ReadTiptoe(bool pressed, bool held, bool timedActionActive)
        {
            if (!held) suppressTiptoeUntilRelease = false;
            if (pressed && timedActionActive) suppressTiptoeUntilRelease = true;
            return held && !suppressTiptoeUntilRelease;
        }

        public bool CanReadPlayerInput => !IsGameplayBlocked && captured && Cursor.lockState == CursorLockMode.Locked;
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool SprintHeld { get; private set; }
        public DollSingerStance Stance { get; private set; }
        private DollSingerStance toggledStance;
        public bool AimHeld { get; private set; }
        public bool FirePressed { get; private set; }
        public bool FireHeld { get; private set; }
        public bool ViewPressed { get; private set; }
        public bool LightPressed { get; private set; }
        private HaloLightPress lightPress;
        private bool suppressFireUntilRelease;
        public bool LightAdjusting => lightPress.Adjusting;
        public bool LightPreviewAiming { get; set; }
        public uint LightingSettings { get; set; }
        private uint savedLightingSettings;
        private const string LightingPreference = "Moonkov.HaloLighting";

        public void CancelLightAdjustment()
        {
            if (LightAdjusting) suppressFireUntilRelease = true;
            lightPress.Cancel();
            SaveLighting();
        }

        private void SaveLighting()
        {
            LightingSettings = HaloLighting.Sanitize(LightingSettings);
            if (savedLightingSettings == LightingSettings) return;
            savedLightingSettings = LightingSettings;
            PlayerPrefs.SetInt(LightingPreference, (int)LightingSettings);
            PlayerPrefs.Save();
        }
        public bool ReloadPressed { get; private set; }
        public int WeaponSlotPressed { get; private set; }
        public bool AltHeld { get; private set; }
        public float LeanTarget { get; private set; }
        public bool IsLeanLocked => lockedLean != 0f;
        private bool leanEnabled;
        private float lockedLean;
        public float Scroll { get; private set; }

        private void OnEnable()
        {
            ApplyViewSettings();
            LightingSettings = savedLightingSettings = HaloLighting.Sanitize((uint)PlayerPrefs.GetInt(LightingPreference, 0));
            lightPress.Cancel();
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            wasBlocked = IsGameplayBlocked;
            SetCapture(captureOnEnable && !wasBlocked);
        }

        public void SetLeanEnabled(bool enabled)
        {
            leanEnabled = enabled;
            if (enabled) return;
            lockedLean = 0f;
            LeanTarget = 0f;
        }

        private void Update()
        {
            ClearFrame();
            ApplyViewSettings();
            var keyboard = Keyboard.current;
            bool tiptoe = ReadTiptoe(keyboard != null && keyboard.xKey.wasPressedThisFrame,
                keyboard != null && keyboard.xKey.isPressed, TimedActionInputActive?.Invoke() == true);
            if (IsGameplayBlocked)
            {
                CancelLightAdjustment();
                wasBlocked = true;
                SetCapture(false);
                return;
            }
            if (wasBlocked)
            {
                wasBlocked = false;
                SetCapture(captureOnEnable && Application.isFocused);
                return; // Closing UI must not also aim, shoot or rotate the character.
            }
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelLightAdjustment();
                SetCapture(false);
                return;
            }
            bool wasAdjusting = LightAdjusting;
            bool toggleLight = lightPress.Step(keyboard != null && keyboard.lKey.wasPressedThisFrame,
                keyboard != null && keyboard.lKey.isPressed, Time.unscaledTime,
                Application.isFocused && (CanReadPlayerInput || wasAdjusting));
            if (LightAdjusting)
            {
                if (!wasAdjusting)
                {
                    LightPreviewAiming = mouse != null && mouse.rightButton.isPressed;
                    SetCapture(false);
                }
                AimHeld = LightPreviewAiming;
                return;
            }
            if (wasAdjusting)
            {
                SaveLighting();
                suppressFireUntilRelease = true;
                SetCapture(true);
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
                if (keyboard.cKey.wasPressedThisFrame)
                    toggledStance = toggledStance == DollSingerStance.Crouching ? DollSingerStance.Standing : DollSingerStance.Crouching;
                if (keyboard.zKey.wasPressedThisFrame)
                    toggledStance = toggledStance == DollSingerStance.Prone ? DollSingerStance.Standing : DollSingerStance.Prone;
                if (JumpPressed) toggledStance = DollSingerStance.Standing;
                Stance = keyboard.leftCtrlKey.isPressed ? DollSingerStance.Crouching :
                    tiptoe && toggledStance == DollSingerStance.Standing ? DollSingerStance.Tiptoe : toggledStance;
                ViewPressed = keyboard.vKey.wasPressedThisFrame;
                LightPressed = toggleLight;
                ReloadPressed = keyboard.rKey.wasPressedThisFrame;
                WeaponSlotPressed = keyboard.digit1Key.wasPressedThisFrame ? 1 :
                    keyboard.digit2Key.wasPressedThisFrame ? 2 : keyboard.digit3Key.wasPressedThisFrame ? 3 : 0;
                AltHeld = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
                if (leanEnabled && AltHeld && keyboard.qKey.wasPressedThisFrame)
                    lockedLean = lockedLean < 0f ? 0f : -1f;
                else if (leanEnabled && AltHeld && keyboard.eKey.wasPressedThisFrame)
                    lockedLean = lockedLean > 0f ? 0f : 1f;
                float heldLean = !leanEnabled || AltHeld ? 0f :
                    (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                LeanTarget = leanEnabled ? (heldLean != 0f ? heldLean : lockedLean) : 0f;
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
                if (gamepad.buttonEast.wasPressedThisFrame)
                    toggledStance = toggledStance == DollSingerStance.Crouching ? DollSingerStance.Standing : DollSingerStance.Crouching;
                if (gamepad.dpad.down.wasPressedThisFrame)
                    toggledStance = toggledStance == DollSingerStance.Prone ? DollSingerStance.Standing : DollSingerStance.Prone;
                if (gamepad.buttonSouth.wasPressedThisFrame) toggledStance = DollSingerStance.Standing;
                if (gamepad.buttonEast.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame || gamepad.buttonSouth.wasPressedThisFrame)
                    Stance = toggledStance;
                if (gamepad.rightShoulder.isPressed && toggledStance == DollSingerStance.Standing) Stance = DollSingerStance.Tiptoe;
            }
            if (suppressFireUntilRelease)
            {
                bool held = (mouse != null && mouse.leftButton.isPressed) ||
                    (gamepad != null && gamepad.rightTrigger.isPressed);
                FirePressed = FireHeld = false;
                if (!held) suppressFireUntilRelease = false;
            }
            Move = Vector2.ClampMagnitude(Move, 1f);
        }

        private void ClearFrame()
        {
            Move = Look = Vector2.zero;
            JumpPressed = SprintHeld = AimHeld = FirePressed = ViewPressed = LightPressed = ReloadPressed = false;
            FireHeld = false;
            Stance = toggledStance;
            AltHeld = false;
            LeanTarget = 0f;
            Scroll = 0f;
            WeaponSlotPressed = 0;
        }

        private void SetCapture(bool value)
        {
            captured = value;
            if (!value) lockedLean = 0f;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
            ClearFrame();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { CancelLightAdjustment(); toggledStance = DollSingerStance.Standing; SetCapture(false); }
        }

        private void OnDisable()
        {
            CancelLightAdjustment();
            captured = false;
            lockedLean = 0f;
            ClearFrame();
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }
    }
}
