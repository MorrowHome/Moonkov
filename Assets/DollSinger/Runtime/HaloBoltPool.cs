using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Client-only visual flights. Each shot owns its trajectory, line renderers and moving light.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class HaloBoltPool : MonoBehaviour
    {
        private sealed class Bolt
        {
            public GameObject Root;
            public LineRenderer Core, Glow;
            public Light Light;
            public Vector3 Start, Direction;
            public float Distance, Speed, Length, Age;
        }

        private readonly List<Bolt> m_Active = new List<Bolt>(16);
        private readonly Stack<Bolt> m_Free = new Stack<Bolt>(16);
        private LineRenderer m_CoreTemplate, m_GlowTemplate;

        public int ActiveCount => m_Active.Count;
        public int CreatedCount { get; private set; }

        public void Configure(LineRenderer core, LineRenderer glow)
        {
            m_CoreTemplate = core;
            m_GlowTemplate = glow;
        }

        public void Play(Vector3 start, Vector3 direction, float distance, float speed, float length,
            Color color, float lightIntensity, float lightRange)
        {
            if (distance <= 0.01f || direction.sqrMagnitude < 0.0001f) return;
            // Only reuse flights that finished; an active shot is never replaced by a later one.
            var bolt = m_Free.Count > 0 ? m_Free.Pop() : CreateBolt();
            bolt.Start = start;
            bolt.Direction = direction.normalized;
            bolt.Distance = distance;
            bolt.Speed = Mathf.Max(1f, speed);
            bolt.Length = Mathf.Max(0.01f, length);
            bolt.Age = 0;
            bolt.Light.color = color;
            bolt.Light.intensity = Mathf.Max(0, lightIntensity);
            bolt.Light.range = Mathf.Max(0.1f, lightRange);
            bolt.Light.enabled = lightIntensity > 0;
            bolt.Root.SetActive(true);
            UpdatePose(bolt);
            m_Active.Add(bolt);
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        // Explicit elapsed time also permits focused checks without starting the game.
        public void Advance(float deltaTime)
        {
            for (int i = m_Active.Count - 1; i >= 0; i--)
            {
                var bolt = m_Active[i];
                bolt.Age += Mathf.Max(0, deltaTime);
                if (bolt.Age * bolt.Speed >= bolt.Distance + bolt.Length)
                {
                    bolt.Root.SetActive(false);
                    m_Free.Push(bolt);
                    m_Active.RemoveAt(i);
                }
                else UpdatePose(bolt);
            }
        }

        private static void UpdatePose(Bolt bolt)
        {
            float travelled = bolt.Age * bolt.Speed;
            float front = Mathf.Min(travelled, bolt.Distance);
            float tail = Mathf.Clamp(travelled - bolt.Length, 0, bolt.Distance);
            Vector3 from = bolt.Start + bolt.Direction * tail;
            Vector3 to = bolt.Start + bolt.Direction * front;
            if (bolt.Core) { bolt.Core.SetPosition(0, from); bolt.Core.SetPosition(1, to); }
            if (bolt.Glow) { bolt.Glow.SetPosition(0, from); bolt.Glow.SetPosition(1, to); }
            // Reapply world pose after character turns; illumination follows the projectile, not its shooter.
            bolt.Light.transform.position = to;
        }

        private Bolt CreateBolt()
        {
            var root = new GameObject("Halo Bolt " + (++CreatedCount));
            root.transform.SetParent(transform, false);
            var bolt = new Bolt
            {
                Root = root,
                Core = CreateLine("Core", root.transform, m_CoreTemplate),
                Glow = CreateLine("Glow", root.transform, m_GlowTemplate)
            };
            var lightObject = new GameObject("Flight Light");
            lightObject.transform.SetParent(root.transform, false);
            bolt.Light = lightObject.AddComponent<Light>();
            bolt.Light.type = LightType.Point;
            bolt.Light.renderMode = LightRenderMode.ForcePixel;
            bolt.Light.shadows = LightShadows.None;
            return bolt;
        }

        internal static LineRenderer CreateLine(string name, Transform parent, LineRenderer template)
        {
            if (!template) return null;
            var go = new GameObject(name);
            go.layer = template.gameObject.layer;
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.sharedMaterial = template.sharedMaterial;
            line.colorGradient = template.colorGradient;
            line.widthCurve = template.widthCurve;
            line.widthMultiplier = template.widthMultiplier;
            line.alignment = template.alignment;
            line.textureMode = template.textureMode;
            line.numCapVertices = template.numCapVertices;
            line.numCornerVertices = template.numCornerVertices;
            line.sortingOrder = template.sortingOrder;
            line.renderingLayerMask = template.renderingLayerMask;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return line;
        }

        public void ClearActive()
        {
            foreach (var bolt in m_Active)
            {
                bolt.Root.SetActive(false);
                m_Free.Push(bolt);
            }
            m_Active.Clear();
        }

        private void OnDisable() => ClearActive();
    }
}
