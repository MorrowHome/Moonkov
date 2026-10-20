using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Laser presentation driven by the gameplay projectile's current and recent ballistic positions.</summary>
    public sealed class HaloProjectileVisual : MonoBehaviour
    {
        private LineRenderer m_Core, m_Glow;
        private Light m_Light;
        private bool m_Illuminate;
        private float m_MaxTrailLength;
        private static AnimationCurve s_Taper;
        public static float TrailLengthLimit(bool revolver, bool shotgun, bool sniper) =>
            sniper ? 6.5f : shotgun ? 1.3f : revolver ? 1.8f : 2.6f;

        public void Configure(DollSingerHaloAim source, bool revolver, bool shotgun = false, bool sniper = false, bool illuminate = true)
        {
            m_Core = HaloBoltPool.CreateLine("Laser core", transform, source.beamCore);
            m_Glow = HaloBoltPool.CreateLine("Laser glow", transform, source.beamGlow);
            if (s_Taper == null) s_Taper = new AnimationCurve(new Keyframe(0, .02f),
                new Keyframe(.65f, .75f), new Keyframe(.92f, 1), new Keyframe(1, .55f));
            float width = shotgun ? .5f : revolver ? .7f : sniper ? .85f : .8f;
            foreach (var line in new[] { m_Core, m_Glow })
                if (line) { line.widthCurve = s_Taper; line.widthMultiplier *= width; line.numCapVertices = 2; }
            m_MaxTrailLength = TrailLengthLimit(revolver, shotgun, sniper);
            m_Illuminate = illuminate && source.boltLightIntensity > 0;
            var lightObject = new GameObject("Projectile light");
            lightObject.transform.SetParent(transform, false);
            m_Light = lightObject.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.renderMode = LightRenderMode.ForcePixel;
            m_Light.shadows = LightShadows.None;
            m_Light.color = revolver && source.revolverVisual ? source.revolverVisual.lightColor :
                sniper && source.sniperVisual ? source.sniperVisual.lightColor : shotgun && source.shotgunVisual ? source.shotgunVisual.lightColor : source.WeaponColor;
            m_Light.intensity = Mathf.Min(6f, Mathf.Max(0f, source.boltLightIntensity)) * (sniper ? 1.25f : .8f);
            if (m_Core)
            {
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(m_Light.color, 0),
                    new GradientColorKey(Color.Lerp(m_Light.color, Color.white, .8f), 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.7f, .45f), new GradientAlphaKey(1, 1) });
                m_Core.colorGradient = gradient;
            }
            if (m_Glow)
            {
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(m_Light.color, 0), new GradientColorKey(m_Light.color, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(source.glowAlpha * .55f, .6f),
                        new GradientAlphaKey(source.glowAlpha * .3f, 1) });
                m_Glow.colorGradient = gradient;
            }
            m_Light.range = Mathf.Clamp(source.boltLightRange, .1f, 3.2f);
            m_Light.enabled = false;
        }

        public void SetFlight(Vector3 tail, Vector3 head)
        {
            Vector3 travel = head - tail;
            if (travel.sqrMagnitude > m_MaxTrailLength * m_MaxTrailLength && m_MaxTrailLength > 0)
                tail = head - travel.normalized * m_MaxTrailLength;
            bool visible = (head - tail).sqrMagnitude > .000001f;
            if (m_Core) m_Core.enabled = visible;
            if (m_Glow) m_Glow.enabled = visible;
            if (m_Light) m_Light.enabled = visible && m_Illuminate;
            if (m_Core) { m_Core.SetPosition(0, tail); m_Core.SetPosition(1, head); }
            if (m_Glow) { m_Glow.SetPosition(0, tail); m_Glow.SetPosition(1, head); }
            if (m_Light) m_Light.transform.position = head;
        }

        public void Stop() => gameObject.SetActive(false);
    }
}
