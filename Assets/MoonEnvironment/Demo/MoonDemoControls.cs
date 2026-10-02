using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Local preview controls; no networking or template gameplay dependencies.</summary>
    public sealed class MoonDemoControls : MonoBehaviour
    {
        [SerializeField] private MoonEnvironment environment;
        [SerializeField] private FirstPersonController movement;
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private StarterAssetsInputs inputs;
        private CharacterController controller;
        private CursorLockMode previousLock;
        private bool previousVisible;

        private void Awake() => controller = movement.GetComponent<CharacterController>();
        private void OnEnable()
        {
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            SetCursor(true);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) SetCursor(Cursor.lockState != CursorLockMode.Locked);
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                environment.SetDetailed(!environment.Detailed);
                PlaceOnGround(movement.transform.position);
            }
            if (keyboard.digit1Key.wasPressedThisFrame) environment.SetSun(0.5f);
            if (keyboard.digit2Key.wasPressedThisFrame) environment.SetSun(25);
            if (keyboard.digit3Key.wasPressedThisFrame) environment.SetSun(60);
            if (keyboard.digit4Key.wasPressedThisFrame) environment.SetLunarLighting(!environment.LunarLighting);
            if (keyboard.rKey.wasPressedThisFrame) PlaceOnGround(environment.SpawnPoint.position);
        }

        private void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            inputs.cursorLocked = locked;
            inputs.cursorInputForLook = locked;
            inputs.move = Vector2.zero;
            inputs.look = Vector2.zero;
            inputs.jump = inputs.sprint = false;
            movement.enabled = locked;
            if (locked) playerInput.ActivateInput();
            else playerInput.DeactivateInput();
        }

        private void PlaceOnGround(Vector3 position)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            try
            {
                if (Physics.Raycast(position + Vector3.up * 1500, Vector3.down, out RaycastHit hit,
                    3000, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    position.y = hit.point.y + controller.skinWidth + 0.05f;
                movement.transform.position = position;
                Physics.SyncTransforms();
            }
            finally { controller.enabled = wasEnabled; }
        }

        private void OnDisable()
        {
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(16, 16, 580, 112), GUIContent.none);
            GUILayout.BeginArea(new Rect(28, 24, 552, 100));
            GUILayout.Label("MOON ENVIRONMENT / Local first-person preview");
            GUILayout.Label("WASD move | Mouse look | Shift sprint | Space jump | Esc cursor");
            GUILayout.Label("Tab DEM / detail | 1/2/3 sun 0.5 / 25 / 60 degrees | 4 lighting | R reset");
            GUILayout.Label($"Detail: {environment.Detailed} | Lunar lighting: {environment.LunarLighting} | Sun: {environment.SunElevation:F1} | Gravity: 1.62 m/s²");
            GUILayout.EndArea();
        }
    }
}
