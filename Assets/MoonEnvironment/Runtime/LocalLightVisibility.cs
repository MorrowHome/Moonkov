using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Distance culling for explicitly assigned, standalone light leaves.
    /// Does not own power, intensity or shadows; circuit changes survive culling.</summary>
    [DisallowMultipleComponent]
    public sealed class LocalLightVisibility : MonoBehaviour
    {
        [SerializeField] private Light[] fixtures = Array.Empty<Light>();
        [SerializeField, Min(1)] private float visibilityDistance = 120;
        [SerializeField, Min(0)] private float exitPadding = 20;
        [SerializeField, Min(.02f)] private float refreshInterval = .25f;

        private struct Entry
        {
            public Light Light;
            public bool Culled;
        }

        private Entry[] entries;
        private float nextRefresh;

        public void Configure(Light[] lights)
        {
            Restore();
            fixtures = lights ?? Array.Empty<Light>();
        }

        private void Initialize()
        {
            var collected = new List<Entry>(fixtures.Length);
            var seen = new HashSet<Light>();
            foreach (var light in fixtures)
            {
                if (!light || light.type == LightType.Directional || !seen.Add(light) ||
                    !light.gameObject.activeSelf || light.transform.childCount != 0) continue;
                // Never deactivate an emissive mesh, interaction or gameplay component.
                bool leaf = true;
                foreach (var component in light.GetComponents<Component>())
                    if (!(component is Transform) && !(component is Light) &&
                        !(component is UniversalAdditionalLightData)) { leaf = false; break; }
                if (leaf) collected.Add(new Entry { Light = light });
            }
            entries = collected.ToArray();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            if (entries == null) Initialize();
            RefreshVisibility(Camera.main);
        }

        private void RefreshVisibility(Camera viewer)
        {
            // Keep the same apparent-size cutoff while looking through a scope.
            float zoom = viewer && !viewer.orthographic ? Mathf.Max(1,
                Mathf.Tan(22.5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Max(1, viewer.fieldOfView) * .5f * Mathf.Deg2Rad)) : 1;
            for (int i = 0; i < entries.Length; i++)
            {
                ref var entry = ref entries[i];
                if (!entry.Light) continue;
                float distance = visibilityDistance * zoom + entry.Light.range + (entry.Culled ? 0 : exitPadding);
                bool culled = viewer && viewer.isActiveAndEnabled && !viewer.orthographic &&
                    (viewer.transform.position - entry.Light.transform.position).sqrMagnitude > distance * distance;
                if (culled == entry.Culled) continue;
                entry.Culled = culled;
                entry.Light.gameObject.SetActive(!culled);
            }
        }

        private void Restore()
        {
            if (entries != null)
                foreach (var entry in entries)
                    if (entry.Culled && entry.Light) entry.Light.gameObject.SetActive(true);
            entries = null;
            nextRefresh = 0;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
