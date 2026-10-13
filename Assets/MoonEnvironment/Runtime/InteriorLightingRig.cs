using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Captures room reflections on demand and invalidates them when fixture power changes.
    /// Does not create lights or modify authored fixtures. Camera proximity only schedules
    /// captures; it never changes room lighting. Class name is retained for scene compatibility.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteriorLightingRig : MonoBehaviour
    {
        [Serializable]
        public sealed class Room
        {
            public string name;
            public Vector3 localCenter;
            public Vector3 size = new Vector3(8, 4, 8);
            public Light[] fixtures = Array.Empty<Light>();
            [Min(0)] public float reflectionIntensity = 1;
        }

        private sealed class RoomState
        {
            public Room Definition;
            public ReflectionProbe Probe;
            public float[] InitialIntensity, PreviousIntensity;
            public float InitialEnergy, PowerWeight, RetryAfter;
            public int Revision, CaptureRevision;
            public bool Dirty, Queued, NearViewer, HasCapture;
        }

        [SerializeField] private LocalLightBudget budget;
        [SerializeField] private Room[] rooms = Array.Empty<Room>();
        [SerializeField, Min(1)] private float captureDistance = 80;
        [SerializeField, Min(.1f)] private float viewerCheckInterval = .5f;
        [SerializeField] private Camera viewer;
        [SerializeField] private LayerMask reflectionMask = ~0;

        [NonSerialized] private RoomState[] runtimeRooms;
        [NonSerialized] private Queue<RoomState> captureQueue;
        [NonSerialized] private RoomState capturing;
        [NonSerialized] private GameObject runtimeRoot;
        [NonSerialized] private int renderId = -1, initializeFrame;
        [NonSerialized] private float nextViewerCheck;
        [NonSerialized] private bool warnedCaptureFailure;

        public int RoomCount => rooms?.Length ?? 0;
        public bool IsCapturing => capturing != null;
        public int PendingCaptureCount => (captureQueue?.Count ?? 0) + (capturing != null ? 1 : 0);
        public int CapturedRoomCount
        {
            get
            {
                int count = 0;
                if (runtimeRooms != null)
                    foreach (var room in runtimeRooms)
                        if (room != null && room.HasCapture) count++;
                return count;
            }
        }

        public void Configure(LocalLightBudget owner, Room[] roomDefinitions)
        {
            Unsubscribe();
            ReleaseRuntime();
            budget = owner;
            rooms = roomDefinitions ?? Array.Empty<Room>();
            if (isActiveAndEnabled) BeginRuntime();
        }

        private void OnEnable() => BeginRuntime();

        private void BeginRuntime()
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (budget)
            {
                budget.PowerChanged -= RefreshPower;
                budget.PowerChanged += RefreshPower;
            }
            // Let fixture power, emission materials and the player camera settle first.
            initializeFrame = Time.frameCount + 2;
            nextViewerCheck = 0;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                Time.frameCount < initializeFrame) return;
            if (runtimeRooms == null) CreateRuntime();
            if (Time.unscaledTime >= nextViewerCheck) CheckViewer();
            // Disabled quality support can return a capture id that never finishes. Keep the
            // bounded queue pending until support returns.
            if (!QualitySettings.realtimeReflectionProbes) return;

            if (capturing != null)
            {
                if (capturing.Probe && !capturing.Probe.IsFinishedRendering(renderId)) return;
                var completed = capturing;
                capturing = null;
                renderId = -1;
                // A switch during a time-sliced capture invalidates that entire cubemap.
                if (completed.Probe && completed.CaptureRevision == completed.Revision)
                {
                    completed.Dirty = false;
                    completed.HasCapture = true;
                    completed.Probe.intensity = completed.PowerWeight > .001f
                        ? Mathf.Max(0, completed.Definition.reflectionIntensity) : 0;
                }
                else Enqueue(completed);
            }

            // Only one probe may render at once, including all of its time-sliced faces.
            while (captureQueue.Count > 0)
            {
                var room = captureQueue.Dequeue();
                room.Queued = false;
                if (!room.Probe || !room.Dirty || !room.NearViewer || room.PowerWeight <= .001f ||
                    Time.unscaledTime < room.RetryAfter) continue;
                room.CaptureRevision = room.Revision;
                int id = room.Probe.RenderProbe();
                if (id < 0)
                {
                    room.RetryAfter = Time.unscaledTime + 5;
                    if (!warnedCaptureFailure)
                    {
                        warnedCaptureFailure = true;
                        Debug.LogWarning("Interior lighting reflection capture was unavailable; retrying later.", this);
                    }
                    continue;
                }
                capturing = room;
                renderId = id;
                break;
            }
        }

        private void CreateRuntime()
        {
            runtimeRoot = new GameObject("Interior reflections (runtime)") { hideFlags = HideFlags.DontSave };
            runtimeRoot.SetActive(false);
            runtimeRoot.transform.SetParent(transform, false);
            runtimeRooms = new RoomState[RoomCount];
            captureQueue = new Queue<RoomState>(RoomCount);
            for (int i = 0; i < runtimeRooms.Length; i++)
            {
                var definition = rooms[i];
                if (definition == null) continue;
                var fixtures = definition.fixtures ?? Array.Empty<Light>();
                var room = new RoomState { Definition = definition, InitialIntensity = new float[fixtures.Length],
                    PreviousIntensity = new float[fixtures.Length] };
                runtimeRooms[i] = room;
                for (int f = 0; f < fixtures.Length; f++)
                {
                    float intensity = fixtures[f] ? Mathf.Max(0, fixtures[f].intensity) : 0;
                    room.InitialIntensity[f] = intensity;
                    room.InitialEnergy += intensity;
                }

                string label = string.IsNullOrWhiteSpace(definition.name) ? "Room " + i : definition.name;
                var probeObject = CreateChild(label + " room reflection", definition.localCenter);
                room.Probe = probeObject.AddComponent<ReflectionProbe>();
                room.Probe.hideFlags = HideFlags.DontSave;
                room.Probe.mode = ReflectionProbeMode.Realtime;
                room.Probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                room.Probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                room.Probe.resolution = 128;
                room.Probe.hdr = true;
                room.Probe.boxProjection = true;
                room.Probe.center = Vector3.zero;
                room.Probe.size = new Vector3(Mathf.Max(.1f, definition.size.x),
                    Mathf.Max(.1f, definition.size.y), Mathf.Max(.1f, definition.size.z));
                room.Probe.blendDistance = .35f;
                room.Probe.nearClipPlane = .1f;
                room.Probe.farClipPlane = Mathf.Max(20, definition.size.magnitude * 2);
                room.Probe.cullingMask = reflectionMask;
                room.Probe.intensity = 0;
            }
            RefreshPower();
            runtimeRoot.SetActive(true);
        }

        private GameObject CreateChild(string label, Vector3 localPosition)
        {
            var child = new GameObject(label) { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(runtimeRoot.transform, false);
            child.transform.localPosition = localPosition;
            return child;
        }

        private void RefreshPower()
        {
            if (runtimeRooms == null) return;
            // Only initialization and complete power commands inspect fixtures. The normal frame
            // loop does not scan lights/renderers. Experimental distance fades are not power events.
            foreach (var room in runtimeRooms)
            {
                if (room == null) continue;
                float energy = 0;
                var fixtures = room.Definition.fixtures;
                bool changed = room.Revision == 0;
                for (int i = 0; i < room.InitialIntensity.Length; i++)
                {
                    var fixture = fixtures != null && i < fixtures.Length ? fixtures[i] : null;
                    float intensity = fixture && fixture.enabled && fixture.gameObject.activeInHierarchy
                        ? Mathf.Clamp(fixture.intensity, 0, room.InitialIntensity[i]) : 0;
                    energy += intensity;
                    // Swapping which fixture is on still requires a new capture, even at equal total power.
                    changed |= !Mathf.Approximately(room.PreviousIntensity[i], intensity);
                    room.PreviousIntensity[i] = intensity;
                }
                float weight = room.InitialEnergy > .001f ? Mathf.Clamp01(energy / room.InitialEnergy) : 0;
                room.PowerWeight = weight;
                if (!changed) continue;
                room.Revision++;
                room.Dirty = true;
                room.HasCapture = false;
                // Hide old bright reflections immediately, including an in-flight pre-switch capture.
                // Do not multiply a completed cubemap by weight again: fixture brightness is captured.
                room.Probe.intensity = 0;
                Enqueue(room);
            }
        }

        private void CheckViewer()
        {
            nextViewerCheck = Time.unscaledTime + Mathf.Max(.1f, viewerCheckInterval);
            if (!viewer || !viewer.isActiveAndEnabled) viewer = Camera.main;
            bool hasViewer = viewer && viewer.isActiveAndEnabled;
            float distanceSquared = Mathf.Max(1, captureDistance) * Mathf.Max(1, captureDistance);
            foreach (var room in runtimeRooms)
            {
                if (room == null) continue;
                room.NearViewer = hasViewer && (room.Probe.transform.position - viewer.transform.position).sqrMagnitude <= distanceSquared;
                Enqueue(room);
            }
        }

        private void Enqueue(RoomState room)
        {
            if (room.Queued || room == capturing || !room.Dirty || !room.NearViewer ||
                room.PowerWeight <= .001f || Time.unscaledTime < room.RetryAfter) return;
            room.Queued = true;
            captureQueue.Enqueue(room);
        }

        private void OnDisable()
        {
            Unsubscribe();
            ReleaseRuntime();
        }

        private void Unsubscribe()
        {
            if (budget) budget.PowerChanged -= RefreshPower;
        }

        private void ReleaseRuntime()
        {
            capturing = null;
            renderId = -1;
            captureQueue?.Clear();
            captureQueue = null;
            runtimeRooms = null;
            if (runtimeRoot)
            {
                // Deactivate immediately; Destroy releases the generated probes and cubemaps.
                runtimeRoot.SetActive(false);
                if (Application.isPlaying) Destroy(runtimeRoot);
                else DestroyImmediate(runtimeRoot);
            }
            runtimeRoot = null;
            warnedCaptureFailure = false;
        }
    }
}
