using System;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Owns fixture power, with optional distance budgeting for profiling only.
    /// Normal lighting keeps authored intensity regardless of camera position.
    /// Power and render eligibility are separate: gameplay must use SetPowered,
    /// not Light.enabled/intensity (owned here), so returning cannot undo a switch.
    /// This is local presentation; a networked switch must replicate power itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalLightBudget : MonoBehaviour
    {
        [SerializeField] private Light[] fixtures = Array.Empty<Light>();
        [SerializeField, Tooltip("Experimental A/B only: nearby-light selection can darken visible rooms. Leave off for normal gameplay.")]
        private bool limitLightsByDistance = false;
        [SerializeField] private Camera viewer;
        [SerializeField, Min(1)] private int maxActiveLights = 24;
        [SerializeField, Min(1)] private float activationDistance = 60;
        [SerializeField, Min(.02f)] private float refreshInterval = .2f;
        [SerializeField, Min(.05f)] private float fadeSeconds = .6f;
        [SerializeField, Min(1)] private float distanceFade = 12;
        [SerializeField, Min(0)] private int transitionSlots = 4;

        private bool[] powered;
        private Candidate[] candidates;
        private float[] authoredIntensity;
        private float[] weight;
        private float[] targetWeight;
        private int candidateCount;
        private float nextRefresh;
        private bool wasDistanceLimited;

        // Notification is sent after a complete command, never for distance fades.
        public event Action PowerChanged;

        public bool TryGetPowered(Light fixture, out bool on)
        {
            Initialize();
            int index = Array.IndexOf(fixtures, fixture);
            on = fixture && index >= 0 && powered[index];
            return fixture && index >= 0;
        }

        private struct Candidate : IComparable<Candidate>
        {
            public int Index;
            public float Distance;
            public int CompareTo(Candidate other)
            {
                int order = Distance.CompareTo(other.Distance);
                return order != 0 ? order : Index.CompareTo(other.Index);
            }
        }

        private void Initialize()
        {
            if (powered != null) return;
            powered = new bool[fixtures.Length];
            candidates = new Candidate[fixtures.Length];
            authoredIntensity = new float[fixtures.Length];
            weight = new float[fixtures.Length];
            targetWeight = new float[fixtures.Length];
            for (int i = 0; i < fixtures.Length; i++)
            {
                powered[i] = fixtures[i] && fixtures[i].enabled;
                authoredIntensity[i] = fixtures[i] ? fixtures[i].intensity : 0;
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            Initialize();
            nextRefresh = 0;
            wasDistanceLimited = limitLightsByDistance;
            if (!limitLightsByDistance)
            {
                ApplyUnbudgeted();
                return;
            }
            // No unbounded first frame while the player camera initializes.
            for (int i = 0; i < fixtures.Length; i++)
                if (fixtures[i])
                {
                    fixtures[i].enabled = false;
                    fixtures[i].intensity = 0;
                }
        }

        private void LateUpdate()
        {
            Initialize();
            if (!limitLightsByDistance)
            {
                // Power setters apply changes immediately. Only leaving the
                // experimental mode requires a full restore; normal frames are idle.
                if (wasDistanceLimited) ApplyUnbudgeted();
                wasDistanceLimited = false;
                return;
            }
            if (!wasDistanceLimited) nextRefresh = 0;
            wasDistanceLimited = true;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Mathf.Max(.02f, refreshInterval);
                if (!viewer || !viewer.isActiveAndEnabled) viewer = Camera.main;
                if (viewer && viewer.isActiveAndEnabled) RefreshBudget(viewer.transform.position);
                else Array.Clear(targetWeight, 0, targetWeight.Length);
            }
            // Selection is infrequent, but brightness must change every frame.
            ApplyFades(Time.unscaledDeltaTime);
        }

        private void ApplyUnbudgeted()
        {
            // Let URP cull light influence. Camera distance is not a power switch.
            // Preserve authored shadows: removing them can light through walls.
            for (int i = 0; i < fixtures.Length; i++)
            {
                var light = fixtures[i];
                if (!light) continue;
                weight[i] = targetWeight[i] = powered[i] ? 1 : 0;
                if (light.intensity != authoredIntensity[i]) light.intensity = authoredIntensity[i];
                if (light.enabled != powered[i]) light.enabled = powered[i];
            }
            // If the experimental mode is enabled at runtime, refresh immediately.
            nextRefresh = 0;
        }

        private void RefreshBudget(Vector3 position)
        {
            Initialize();
            int count = 0;
            for (int i = 0; i < fixtures.Length; i++)
            {
                var light = fixtures[i];
                if (!light || !powered[i] || authoredIntensity[i] <= 0 || !light.gameObject.activeInHierarchy) continue;
                float distance = Vector3.Distance(position, light.transform.position);
                if (distance > activationDistance + light.range) continue;
                candidates[count++] = new Candidate { Index = i, Distance = distance };
            }
            Array.Sort(candidates, 0, count);
            candidateCount = count;
            Array.Clear(targetWeight, 0, targetWeight.Length);
            // Reserve room for outgoing and incoming lights to coexist. Fades must
            // never temporarily double the shadow budget in a crowded corridor.
            int steadyBudget = Mathf.Max(1, maxActiveLights - Mathf.Clamp(transitionSlots, 0, maxActiveLights - 1));
            bool crowded = count > steadyBudget;
            // The first excluded light defines a continuous spatial budget edge.
            // A fixture must approach zero BEFORE crossing the ranking cutoff;
            // fading only after removal makes entire lit patches visibly switch.
            float edge = crowded ? candidates[steadyBudget].Distance : 0;
            float inner = crowded ? Mathf.Max(candidates[0].Distance,
                edge - Mathf.Min(distanceFade, Mathf.Max(1, edge * .35f))) : 0;
            float band = edge - inner;
            for (int i = 0; i < Mathf.Min(count, steadyBudget); i++)
            {
                int index = candidates[i].Index;
                float distance = candidates[i].Distance;
                float budgetWeight = 1;
                if (crowded)
                {
                    // Coincident/equidistant fixtures have no spatial band. Use a
                    // stable rank taper in that degenerate case, not an all-dark room.
                    budgetWeight = band > .001f ? Mathf.SmoothStep(0, 1, (edge - distance) / band) :
                        Mathf.SmoothStep(0, 1, (steadyBudget - i) / (float)Mathf.Min(steadyBudget, 6));
                }
                targetWeight[index] = budgetWeight * Mathf.SmoothStep(0, 1,
                    (activationDistance + fixtures[index].range - distance) / Mathf.Max(1, distanceFade));
            }
        }

        private void ApplyFades(float deltaTime)
        {
            float step = Mathf.Min(Mathf.Max(0, deltaTime), .05f) / Mathf.Max(.05f, fadeSeconds);
            int active = 0;
            // Release fully faded slots first. Retain shadows through the entire
            // fade, rather than disabling shadows and leaking through walls.
            for (int i = 0; i < fixtures.Length; i++)
            {
                if (!fixtures[i]) continue;
                if (!powered[i] || !fixtures[i].gameObject.activeInHierarchy) weight[i] = 0;
                else if (weight[i] > 0) weight[i] = Mathf.MoveTowards(weight[i], targetWeight[i], step);
                if (weight[i] > 0) active++;
            }
            // Admit new fixtures in proximity order, only into available slots.
            for (int c = 0; c < candidateCount && active < Mathf.Max(1, maxActiveLights); c++)
            {
                int i = candidates[c].Index;
                if (!fixtures[i] || !powered[i] || !fixtures[i].gameObject.activeInHierarchy ||
                    targetWeight[i] <= 0 || weight[i] > 0) continue;
                weight[i] = Mathf.Min(targetWeight[i], step);
                if (weight[i] > 0) active++;
            }
            for (int i = 0; i < fixtures.Length; i++)
            {
                if (!fixtures[i]) continue;
                float intensity = authoredIntensity[i] * weight[i];
                if (fixtures[i].intensity != intensity) fixtures[i].intensity = intensity;
                bool visible = weight[i] > 0;
                if (fixtures[i].enabled != visible) fixtures[i].enabled = visible;
            }
        }

        /// <summary>Call from a switch/interaction. Power persists while culled.</summary>
        public bool SetPowered(Light fixture, bool on)
        {
            Initialize();
            int index = Array.IndexOf(fixtures, fixture);
            if (!fixture || index < 0) return false;
            bool changed = powered[index] != on;
            ApplyPower(index, on);
            if (changed) PowerChanged?.Invoke();
            return true;
        }

        /// <summary>Validate every target before writing; observers see only the final batch.</summary>
        public bool SetPoweredBatch(Light[] targets, bool on)
        {
            Initialize();
            if (targets == null || targets.Length == 0) return false;
            var indices = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                indices[i] = Array.IndexOf(fixtures, targets[i]);
                if (!targets[i] || indices[i] < 0) return false;
            }
            bool changed = false;
            foreach (int index in indices)
            {
                changed |= powered[index] != on;
                ApplyPower(index, on);
            }
            if (changed) PowerChanged?.Invoke();
            return true;
        }

        private void ApplyPower(int index, bool on)
        {
            var fixture = fixtures[index];
            powered[index] = on;
            if (!on)
            {
                weight[index] = targetWeight[index] = 0;
                fixture.enabled = false;
            }
            else if (!isActiveAndEnabled || !limitLightsByDistance)
            {
                weight[index] = targetWeight[index] = 1;
                fixture.intensity = authoredIntensity[index];
                fixture.enabled = true;
            }
            nextRefresh = 0;
        }

        [ContextMenu("Runtime/All Lights Off")]
        public void AllLightsOff() => SetAllPowered(false);

        [ContextMenu("Runtime/All Lights On")]
        public void AllLightsOn() => SetAllPowered(true);

        private void SetAllPowered(bool on)
        {
            if (!Application.isPlaying) return;
            Initialize();
            bool changed = false;
            for (int i = 0; i < fixtures.Length; i++)
            {
                changed |= fixtures[i] && powered[i] != on;
                powered[i] = on;
                if (!on) weight[i] = targetWeight[i] = 0;
                if (fixtures[i] && (!on || !isActiveAndEnabled || !limitLightsByDistance))
                {
                    weight[i] = targetWeight[i] = on ? 1 : 0;
                    fixtures[i].intensity = authoredIntensity[i];
                    fixtures[i].enabled = on;
                }
            }
            nextRefresh = 0;
            if (changed) PowerChanged?.Invoke();
        }

        private void OnDisable()
        {
            if (powered == null) return;
            // Relinquish render ownership but retain the current gameplay power.
            // Authored shadows are never changed by this component.
            for (int i = 0; i < fixtures.Length; i++)
                if (fixtures[i])
                {
                    fixtures[i].intensity = authoredIntensity[i];
                    fixtures[i].enabled = powered[i];
                }
            powered = null;
        }
    }
}
