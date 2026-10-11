using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS.Moon.Preview
{
    /// <summary>Local preview adapter only; never add this to a network environment prefab.</summary>
    public sealed class LightSwitchPreviewInteractor : MonoBehaviour
    {
        [SerializeField] private Camera viewer;
        [SerializeField] private DollSingerInput input;
        [SerializeField] private float reach = 3f;
        [SerializeField] private LayerMask interactionMask = Physics.DefaultRaycastLayers;
        private LightSwitchButton focused;
        private float nextCheck;
        public void Configure(Camera camera, DollSingerInput owner) { viewer = camera; input = owner; }

        private bool CanInteract => isActiveAndEnabled && viewer && viewer.isActiveAndEnabled &&
            input && input.isActiveAndEnabled && input.CanReadPlayerInput && Time.timeScale > 0;

        // Exposed for a focused physics-path Play check; still obeys the same input/pause gates.
        public bool TryInteract(Ray ray)
        {
            if (!CanInteract) return false;
            var button = FindButton(ray);
            return button && button.Press();
        }
        private LightSwitchButton FindButton(Ray ray)
        {
            // First solid hit wins: walls and the panel housing block interaction.
            return Physics.Raycast(ray, out var hit, reach, interactionMask, QueryTriggerInteraction.Ignore)
                ? hit.collider.GetComponentInParent<LightSwitchButton>() : null;
        }
        private void Update()
        {
            if (!CanInteract) { focused = null; return; }
            bool pressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame ||
                Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame;
            if (pressed || Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + .1f;
                var ray = new Ray(viewer.transform.position, viewer.transform.forward);
                focused = FindButton(ray);
                if (pressed && focused) focused.Press();
            }
        }
        private void OnGUI()
        {
            if (!CanInteract) return;
            GUI.Label(new Rect(Screen.width * .5f - 4, Screen.height * .5f - 9, 18, 18), "+");
            if (focused)
                GUI.Box(new Rect(Screen.width * .5f - 185, Screen.height * .65f, 370, 32),
                    "F / Gamepad X: " + focused.Label + "  [" + focused.State + "]");
        }
    }
}
