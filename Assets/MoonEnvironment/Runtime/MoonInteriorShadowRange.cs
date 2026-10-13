using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Conservative enclosed-view shadow range for a scene-owned pipeline copy.
    /// At most six nonallocating raycasts per 0.25 s; opening the view restores range immediately.
    /// </summary>
    internal sealed class MoonInteriorShadowRange
    {
        private static readonly Vector2[] ViewportSamples =
        {
            new(.5f, .5f), new(.08f, .08f), new(.08f, .92f), new(.92f, .08f), new(.92f, .92f)
        };

        private readonly UniversalRenderPipelineAsset pipeline;
        private readonly float exteriorDistance;
        private Camera viewer;
        private float nextSample, targetDistance, transitionStartDistance, transitionStartTime;

        public MoonInteriorShadowRange(UniversalRenderPipelineAsset runtimePipeline, float originalDistance)
        {
            pipeline = runtimePipeline;
            exteriorDistance = originalDistance;
            targetDistance = transitionStartDistance = originalDistance;
        }

        public void Tick(LayerMask occluders)
        {
            float now = Time.unscaledTime;
            if (now >= nextSample)
            {
                nextSample = now + .25f;
                viewer = Camera.main;
                float measuredDistance = Measure(occluders);
                if (!Mathf.Approximately(measuredDistance, targetDistance))
                {
                    targetDistance = measuredDistance;
                    transitionStartDistance = pipeline.shadowDistance;
                    transitionStartTime = now;
                }
            }
            if (!viewer || !viewer.isActiveAndEnabled)
            {
                Restore();
                return;
            }
            // Expansion is immediate, including a doorway or an unobstructed viewport corner.
            // Only contraction may interpolate; unscaled time also supports paused A/B captures.
            float distance = targetDistance >= pipeline.shadowDistance ? targetDistance :
                Mathf.Lerp(transitionStartDistance, targetDistance, Mathf.Clamp01((now - transitionStartTime) / .5f));
            if (!Mathf.Approximately(pipeline.shadowDistance, distance)) pipeline.shadowDistance = distance;
        }

        private float Measure(LayerMask occluders)
        {
            if (!viewer || !viewer.isActiveAndEnabled || viewer.cameraType != CameraType.Game ||
                !Physics.Raycast(viewer.transform.position, Vector3.up, 12, occluders, QueryTriggerInteraction.Ignore))
                return exteriorDistance;

            float farthest = 0;
            foreach (Vector2 sample in ViewportSamples)
            {
                Ray ray = viewer.ViewportPointToRay(new Vector3(sample.x, sample.y, 0));
                if (!Physics.Raycast(ray, out RaycastHit hit, 60, occluders, QueryTriggerInteraction.Ignore))
                    return exteriorDistance;
                farthest = Mathf.Max(farthest, hit.distance);
            }
            // Never increase a source preset that already uses a shorter distance.
            return Mathf.Min(exteriorDistance, Mathf.Clamp(farthest + 10, 40, 75));
        }

        public void Restore()
        {
            targetDistance = transitionStartDistance = exteriorDistance;
            if (!Mathf.Approximately(pipeline.shadowDistance, exteriorDistance)) pipeline.shadowDistance = exteriorDistance;
        }
    }
}
