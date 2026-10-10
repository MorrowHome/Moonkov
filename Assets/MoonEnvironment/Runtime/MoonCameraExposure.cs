using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Bounded camera adaptation without a renderer feature or GPU readback.
    /// Meters visible geometry and sunlight occlusion at 4 Hz; this is an exposure
    /// approximation, not a luminance histogram. The Volume affects the entire image.
    /// </summary>
    internal sealed class MoonCameraExposure
    {
        private readonly GameObject owner;
        private readonly VolumeProfile profile;
        private readonly ColorAdjustments color;
        private Camera camera;
        private float nextMeter, targetEV, exposureEV;
        private static readonly Vector2[] MeterPoints =
        {
            new(.5f,.5f), new(.3f,.35f), new(.7f,.35f), new(.5f,.2f), new(.5f,.7f)
        };

        public float DarkAdaptation(float maximumEV) => Mathf.InverseLerp(0, Mathf.Max(.01f, maximumEV), exposureEV);

        public MoonCameraExposure(Transform parent)
        {
            owner = new GameObject("Moon camera exposure (runtime)") { hideFlags = HideFlags.DontSave, layer = 0 };
            owner.transform.SetParent(parent, false);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            color = profile.Add<ColorAdjustments>(false);
            color.postExposure.overrideState = true;
            var volume = owner.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20;
            volume.sharedProfile = profile;
        }

        public void Tick(Light sun, float maximumEV, float darkSeconds, float brightSeconds, LayerMask occluders)
        {
            maximumEV = Mathf.Clamp(maximumEV, 0, 6);
            if (Time.unscaledTime >= nextMeter)
            {
                nextMeter = Time.unscaledTime + .25f;
                camera = Camera.main;
                if (!camera || camera.cameraType != CameraType.Game) return;
                // Gameplay cameras use Default in their volume mask. Follow the
                // camera's scene lifetime rather than retaining a despawned view.
                float brightest = 0;
                Vector3 toSun = sun ? -sun.transform.forward : Vector3.up;
                bool day = sun && sun.isActiveAndEnabled && sun.intensity > 0;
                foreach (Vector2 point in MeterPoints)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3(point.x, point.y, 0));
                    if (!Physics.Raycast(ray, out RaycastHit hit, 2000, occluders, QueryTriggerInteraction.Ignore))
                    {
                        if (day && Vector3.Dot(ray.direction, toSun) > .98f) brightest = 1;
                        continue;
                    }
                    if (day && !Physics.Raycast(hit.point + hit.normal * .06f, toSun, 2500,
                            occluders, QueryTriggerInteraction.Ignore))
                        brightest = Mathf.Max(brightest, .5f + .5f * Mathf.Max(0, Vector3.Dot(hit.normal, toSun)));
                }
                // Do not open the iris fully just because the player looks at black
                // sky while standing in sunlight. A short downward sample provides context.
                if (day && Physics.Raycast(camera.transform.position, Vector3.down, out RaycastHit floor, 5,
                        occluders, QueryTriggerInteraction.Ignore)
                    && !Physics.Raycast(floor.point + floor.normal * .06f, toSun, 2500,
                        occluders, QueryTriggerInteraction.Ignore))
                    brightest = Mathf.Max(brightest, .5f);
                targetEV = Mathf.Clamp(-Mathf.Log(Mathf.Max(.0001f, brightest), 2), 0, maximumEV);
            }
            float seconds = targetEV > exposureEV ? darkSeconds : brightSeconds;
            exposureEV = Mathf.Lerp(exposureEV, targetEV, 1 - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.05f, seconds)));
            color.postExposure.value = exposureEV;
        }

        public void Dispose()
        {
            if (owner) { owner.SetActive(false); Object.Destroy(owner); }
            if (profile)
            {
                foreach (VolumeComponent component in profile.components) Object.Destroy(component);
                Object.Destroy(profile);
            }
        }
    }
}
