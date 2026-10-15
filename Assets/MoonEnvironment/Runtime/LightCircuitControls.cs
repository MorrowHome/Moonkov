using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    public enum LightCommand { SetOn, SetOff, Toggle }
    public enum LightGroupState { Unavailable, AllOff, AllOn, Mixed }

    /// <summary>
    /// Building-local ID adapter. The configured lighting owner owns power; this
    /// component owns bindings and presentation, not a second copy of state. A future
    /// authoritative network handler should validate commands before invoking this adapter.
    /// No Update, global registration, save data or network authority is implied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LightCircuitControls : MonoBehaviour
    {
        [Serializable] public sealed class EmissionTarget
        {
            public Renderer renderer;
            public int materialSlot;
            public Material onMaterial, offMaterial;
            [NonSerialized] internal Material[] slots;
        }
        [Serializable] public sealed class Binding
        {
            public string lightId;
            public Light fixture;
            public EmissionTarget[] emissiveTargets = Array.Empty<EmissionTarget>();
        }

        [SerializeField] private LocalLightBudget budget;
        [SerializeField] private BuildingBakedLighting bakedLighting;
        [SerializeField] private Binding[] lights = Array.Empty<Binding>();
        private readonly Dictionary<string, Binding> byId = new Dictionary<string, Binding>(StringComparer.Ordinal);
        // Unity hot reload serializes private fields too; these must reset alongside
        // the non-serialized dictionary rather than claiming an empty lookup is ready.
        [NonSerialized] private bool initialized, valid;
        public event Action StateChanged;
        public string ConfigurationError { get; private set; }

        public void Configure(LocalLightBudget owner, Binding[] bindings)
        {
            Unsubscribe();
            budget = owner;
            bakedLighting = null;
            lights = bindings ?? Array.Empty<Binding>();
            initialized = false;
            if (isActiveAndEnabled) Subscribe();
        }
        public void ConfigureBaked(BuildingBakedLighting owner, Binding[] bindings)
        {
            Unsubscribe();
            bakedLighting = owner;
            budget = null;
            lights = bindings ?? Array.Empty<Binding>();
            initialized = false;
            if (isActiveAndEnabled) Subscribe();
        }
        public void ConfigureBaked(BuildingBakedLighting owner) => ConfigureBaked(owner, lights);
        private void OnEnable() => Subscribe();
        private void OnDisable() { Unsubscribe(); StateChanged?.Invoke(); }
        private void Subscribe()
        {
            if (bakedLighting)
            {
                bakedLighting.PowerChanged -= Refresh;
                bakedLighting.PowerChanged += Refresh;
            }
            else if (budget)
            {
                budget.PowerChanged -= Refresh;
                budget.PowerChanged += Refresh;
            }
            else return;
            Refresh();
        }
        private void Unsubscribe()
        {
            if (budget) budget.PowerChanged -= Refresh;
            if (bakedLighting) bakedLighting.PowerChanged -= Refresh;
        }

        private bool TryGetPowered(Light fixture, out bool on)
        {
            if (bakedLighting) return bakedLighting.TryGetPowered(fixture, out on);
            on = false;
            return budget && budget.TryGetPowered(fixture, out on);
        }

        private bool Initialize()
        {
            if (initialized) return valid;
            initialized = true;
            valid = false;
            byId.Clear();
            var fixtures = new HashSet<Light>();
            var renderers = new Dictionary<Renderer, Material[]>();
            var occupiedSlots = new HashSet<(Renderer, int)>();
            if ((!bakedLighting && !budget) || lights.Length == 0) return Fail("Missing owner or light bindings.");
            foreach (var binding in lights)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.lightId) || !binding.fixture ||
                    byId.ContainsKey(binding.lightId) || !fixtures.Add(binding.fixture) ||
                    !TryGetPowered(binding.fixture, out _)) return Fail("Invalid, duplicated or unmanaged light binding.");
                byId.Add(binding.lightId, binding);
                foreach (var target in binding.emissiveTargets)
                {
                    if (target == null || !target.renderer || !target.onMaterial || !target.offMaterial ||
                        !occupiedSlots.Add((target.renderer, target.materialSlot))) return Fail("Invalid or shared emission slot binding.");
                    if (!renderers.TryGetValue(target.renderer, out var slots))
                        renderers.Add(target.renderer, slots = target.renderer.sharedMaterials);
                    if (target.materialSlot < 0 || target.materialSlot >= slots.Length ||
                        slots[target.materialSlot] != target.onMaterial && slots[target.materialSlot] != target.offMaterial)
                        return Fail("Emission material slot no longer matches the imported binding.");
                    target.slots = slots;
                }
            }
            ConfigurationError = null;
            return valid = true;
        }
        private bool Fail(string error) { ConfigurationError = error; return false; }

        public bool TryGetPowered(string lightId, out bool on)
        {
            on = false;
            return Initialize() && lightId != null && byId.TryGetValue(lightId, out var binding) &&
                TryGetPowered(binding.fixture, out on);
        }

        public LightGroupState GetState(string[] targetIds)
        {
            if (targetIds == null || targetIds.Length == 0) return LightGroupState.Unavailable;
            bool anyOn = false, anyOff = false;
            foreach (string id in targetIds)
            {
                if (!TryGetPowered(id, out bool on)) return LightGroupState.Unavailable;
                anyOn |= on; anyOff |= !on;
            }
            return anyOn && anyOff ? LightGroupState.Mixed : anyOn ? LightGroupState.AllOn : LightGroupState.AllOff;
        }

        public bool TryApply(string[] targetIds, LightCommand command)
        {
            if (!isActiveAndEnabled || !Enum.IsDefined(typeof(LightCommand), command)) return false;
            var state = GetState(targetIds);
            if (state == LightGroupState.Unavailable) return false;
            bool on = command == LightCommand.SetOn || command == LightCommand.Toggle && state != LightGroupState.AllOn;
            // Allocate only on user commands. Duplicate IDs never double-toggle a fixture.
            var targets = new HashSet<Light>();
            foreach (string id in targetIds) targets.Add(byId[id].fixture);
            var batch = new Light[targets.Count]; targets.CopyTo(batch);
            // The baked owner expands fixture targets to complete room circuits.
            return bakedLighting ? bakedLighting.SetPoweredBatch(batch, on) : budget.SetPoweredBatch(batch, on);
        }

        private void Refresh()
        {
            if (!Initialize()) { StateChanged?.Invoke(); return; }
            foreach (var binding in lights)
            {
                if (!TryGetPowered(binding.fixture, out bool on)) continue;
                foreach (var target in binding.emissiveTargets)
                {
                    if (!target.renderer) continue;
                    var material = on ? target.onMaterial : target.offMaterial;
                    if (target.slots[target.materialSlot] == material) continue;
                    // Detach the bounded rendering representation before changing a source.
                    var cluster = target.renderer.GetComponentInParent<BuildingRenderClusters>();
                    if (cluster) cluster.ReleaseFor(target.renderer);
                    target.slots[target.materialSlot] = material;
                    target.renderer.sharedMaterials = target.slots;
                }
            }
            StateChanged?.Invoke();
        }
    }
}
