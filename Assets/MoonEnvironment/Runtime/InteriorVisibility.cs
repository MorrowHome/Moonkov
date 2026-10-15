using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Client presentation gate for enclosed building details. Physics, doors and
    /// gameplay objects remain active for every peer. This does not unload assets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteriorVisibility : MonoBehaviour
    {
        [SerializeField] private Transform volumeSpace;
        [SerializeField] private Bounds[] interiorVolumes = Array.Empty<Bounds>();
        [SerializeField] private Renderer[] details = Array.Empty<Renderer>();
        // Dedicated light leaves only: inactive leaves preserve Light.enabled/power.
        [SerializeField] private GameObject[] lightObjects = Array.Empty<GameObject>();
        [SerializeField, Min(0)] private float exitPadding = 4;
        [SerializeField, Min(.02f)] private float refreshInterval = .1f;

        private bool[] originalForceOff, originalActive;
        private bool visible = true;
        private Camera viewer;
        private float nextRefresh;

        public void Configure(Transform space, Bounds[] volumes, Renderer[] renderers, GameObject[] lights)
        {
            Restore();
            volumeSpace = space;
            interiorVolumes = volumes ?? Array.Empty<Bounds>();
            details = renderers ?? Array.Empty<Renderer>();
            lightObjects = lights ?? Array.Empty<GameObject>();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            if (!viewer || !viewer.isActiveAndEnabled) viewer = Camera.main;
            // A missing/replaced local camera must not leave a previously hidden interior.
            SetVisible(!viewer || !viewer.isActiveAndEnabled || ShouldShow(viewer.transform.position));
        }

        private bool ShouldShow(Vector3 worldPosition)
        {
            if (!volumeSpace || interiorVolumes.Length == 0) return true;
            Vector3 position = volumeSpace.InverseTransformPoint(worldPosition);
            foreach (var source in interiorVolumes)
            {
                var bounds = source;
                if (visible) bounds.Expand(exitPadding * 2);
                if (bounds.Contains(position)) return true;
            }
            return false;
        }

        private void SetVisible(bool value)
        {
            if (originalForceOff == null)
            {
                originalForceOff = new bool[details.Length];
                originalActive = new bool[lightObjects.Length];
                for (int i = 0; i < details.Length; i++)
                    originalForceOff[i] = details[i] && details[i].forceRenderingOff;
                for (int i = 0; i < lightObjects.Length; i++)
                    originalActive[i] = lightObjects[i] && lightObjects[i].activeSelf;
            }
            if (visible == value) return;
            visible = value;
            // Writes happen only on entering/leaving, with no hierarchy scan per frame.
            for (int i = 0; i < details.Length; i++)
                if (details[i]) details[i].forceRenderingOff = !visible || originalForceOff[i];
            for (int i = 0; i < lightObjects.Length; i++)
                if (lightObjects[i]) lightObjects[i].SetActive(visible && originalActive[i]);
        }

        private void Restore()
        {
            if (originalForceOff != null)
            {
                for (int i = 0; i < details.Length; i++)
                    if (details[i]) details[i].forceRenderingOff = originalForceOff[i];
                for (int i = 0; i < lightObjects.Length; i++)
                    if (lightObjects[i]) lightObjects[i].SetActive(originalActive[i]);
            }
            originalForceOff = null;
            originalActive = null;
            visible = true;
            viewer = null;
            nextRefresh = 0;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
