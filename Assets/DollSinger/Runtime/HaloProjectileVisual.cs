using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Laser presentation driven by the gameplay projectile's current and recent ballistic positions.</summary>
    public sealed class HaloProjectileVisual : MonoBehaviour
    {
        private LineRenderer m_Core, m_Glow;
        private Light m_Light;

        public void Configure(DollSingerHaloAim source, bool revolver)
        {
            m_Core = HaloBoltPool.CreateLine("Laser core", transform, source.beamCore);
            m_Glow = HaloBoltPool.CreateLine("Laser glow", transform, source.beamGlow);
            var lightObject = new GameObject("Projectile light");
            lightObject.transform.SetParent(transform, false);
            m_Light = lightObject.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.renderMode = LightRenderMode.ForcePixel;
            m_Light.shadows = LightShadows.None;
            m_Light.color = revolver && source.revolverVisual ? source.revolverVisual.lightColor : source.aimedHaloColor;
            m_Light.intensity = Mathf.Max(0f, source.boltLightIntensity);
            m_Light.range = Mathf.Max(0.1f, source.boltLightRange);
            m_Light.enabled = source.boltLightIntensity > 0f;
        }

        public void SetFlight(Vector3 tail, Vector3 head)
        {
            if (m_Core) { m_Core.SetPosition(0, tail); m_Core.SetPosition(1, head); }
            if (m_Glow) { m_Glow.SetPosition(0, tail); m_Glow.SetPosition(1, head); }
            if (m_Light) m_Light.transform.position = head;
        }

        public void Stop() => gameObject.SetActive(false);
    }
}
