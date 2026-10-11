using System;
using System.Collections;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>One command endpoint; several buttons may observe/control the same lights.</summary>
    [DisallowMultipleComponent]
    public sealed class LightSwitchButton : MonoBehaviour
    {
        [SerializeField] private string switchId, label;
        [SerializeField] private LightCircuitControls controls;
        [SerializeField] private string[] targets = Array.Empty<string>();
        [SerializeField] private LightCommand command;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private Renderer indicator;
        [SerializeField] private Material onIndicator, offIndicator, mixedIndicator, unavailableIndicator;
        [SerializeField, Min(0)] private int indicatorSlot;
        [SerializeField] private Vector3 pressOffsetInParent;
        [NonSerialized] private Coroutine pressRoutine;
        [NonSerialized] private Vector3 restPosition;
        [NonSerialized] private bool pressing;
        public string SwitchId => switchId;
        public string Label => label;
        public LightGroupState State => controls && controls.isActiveAndEnabled ? controls.GetState(targets) : LightGroupState.Unavailable;
        public bool Press()
        {
            if (!isActiveAndEnabled || !controls || !controls.TryApply(targets, command)) return false;
            if (Application.isPlaying && pressOffsetInParent.sqrMagnitude > 0)
            {
                RestoreCap();
                restPosition = transform.localPosition;
                pressing = true;
                pressRoutine = StartCoroutine(AnimatePress());
            }
            return true;
        }

        public void Configure(string id, string display, LightCircuitControls owner, string[] ids, LightCommand action,
            TextMesh text = null, Renderer lamp = null, Material on = null, Material off = null, Material mixed = null, Material unavailable = null,
            int materialSlot = 0, Vector3 pressOffset = default)
        {
            OnDisable();
            switchId = id; label = display; controls = owner; targets = ids; command = action;
            statusText = text; indicator = lamp; onIndicator = on; offIndicator = off; mixedIndicator = mixed; unavailableIndicator = unavailable;
            indicatorSlot = materialSlot; pressOffsetInParent = pressOffset;
            if (isActiveAndEnabled) OnEnable();
        }
        private void OnEnable()
        {
            if (controls) { controls.StateChanged -= Refresh; controls.StateChanged += Refresh; }
            Refresh();
        }
        private void OnDisable() { if (controls) controls.StateChanged -= Refresh; RestoreCap(); }
        private void RestoreCap()
        {
            if (pressRoutine != null) StopCoroutine(pressRoutine);
            pressRoutine = null;
            if (pressing) transform.localPosition = restPosition;
            pressing = false;
        }
        // Runs only during a successful press. Pausing freezes the feedback with gameplay;
        // disabling or replacing a button always restores its authored rest position.
        private IEnumerator AnimatePress()
        {
            const float duration = .18f;
            float elapsed = 0;
            while (elapsed < duration)
            {
                float phase = elapsed / duration;
                float amount = phase < .3f ? phase / .3f : (1 - phase) / .7f;
                transform.localPosition = restPosition + pressOffsetInParent * Mathf.SmoothStep(0, 1, amount);
                yield return null;
                elapsed += Time.deltaTime;
            }
            transform.localPosition = restPosition;
            pressing = false;
            pressRoutine = null;
        }
        private void Refresh()
        {
            var state = State;
            if (statusText) statusText.text = label + "\n" +
                (state == LightGroupState.AllOn ? "ON" : state == LightGroupState.AllOff ? "OFF" : state == LightGroupState.Mixed ? "MIXED" : "UNAVAILABLE");
            var material = state == LightGroupState.AllOn ? onIndicator : state == LightGroupState.AllOff ? offIndicator :
                state == LightGroupState.Mixed ? mixedIndicator : unavailableIndicator;
            if (indicator && material)
            {
                var slots = indicator.sharedMaterials;
                if (indicatorSlot >= 0 && indicatorSlot < slots.Length && slots[indicatorSlot] != material)
                {
                    slots[indicatorSlot] = material;
                    indicator.sharedMaterials = slots;
                }
            }
        }
    }
}
