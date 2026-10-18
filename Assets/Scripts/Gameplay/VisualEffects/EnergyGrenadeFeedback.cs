using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Client-only, bounded pool. No collision or damage is owned by presentation.
    public sealed class EnergyGrenadeFeedback : MonoBehaviour
    {
        private sealed class Effect
        {
            public uint Id;
            public Transform Root, Core, Shell;
            public MeshRenderer CoreRenderer, ShellRenderer;
            public LineRenderer[] Rings;
            public LineRenderer[] Rays;
            public TrailRenderer Trail;
            public ParticleSystem Sparks;
            public Light Light;
            public Vector3 Origin, Velocity;
            public float PoseTime, Age, Remaining, Expire;
            public bool Active, Exploded;
        }

        private readonly Effect[] m_Pool = new Effect[128];
        private EnergyGrenadeStyle m_Style;
        private MaterialPropertyBlock m_Properties;
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Shell = Shader.PropertyToID("_Shell");
        private static readonly Color Cyan = new(.12f, 1.3f, 2f, 1);

        public bool Warmup()
        {
            if (m_Style) return true;
            m_Style = Resources.Load<EnergyGrenadeStyle>("Moonkov/EnergyGrenade");
            m_Properties = new MaterialPropertyBlock();
            return m_Style && m_Style.Orb && m_Style.Ribbon && m_Style.Spark && m_Style.Sphere;
        }

        public void Present(in EnergyGrenadeStateRpc state, float elapsed = 0)
        {
            if (!Warmup()) return;
            Effect effect = null;
            int empty = -1;
            for (int i = 0; i < m_Pool.Length; i++)
            {
                if (m_Pool[i] != null && m_Pool[i].Active && m_Pool[i].Id == state.Id) { effect = m_Pool[i]; break; }
                if (empty < 0 && (m_Pool[i] == null || !m_Pool[i].Active)) empty = i;
            }
            if (effect == null)
            {
                if (empty < 0) return;
                effect = m_Pool[empty] ??= CreateEffect(empty);
                effect.Id = state.Id; effect.Active = true; effect.Exploded = false;
                effect.Root.gameObject.SetActive(true);
                effect.Root.position = state.Position;
                effect.Trail.Clear(); effect.Trail.emitting = true;
                effect.Sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (state.Exploded == 0 && state.Remaining > EnergyGrenadeRules.Fuse - .3f)
                    MoonkovAudio.Play(m_Style.ThrowSound, state.Position);
            }
            // Reliable ordered state means a confirmed explosion cannot be resurrected by an old pose.
            if (effect.Exploded) return;
            effect.Origin = state.Position; effect.Velocity = state.Velocity;
            effect.PoseTime = Time.time - Mathf.Clamp(elapsed, 0, .3f);
            effect.Remaining = state.Remaining;
            effect.Root.position = state.Position;
            if (state.Exploded != 0)
            {
                effect.Exploded = true; effect.Age = 0;
                effect.Trail.emitting = false;
                effect.Sparks.Play(); effect.Sparks.Emit(48);
                MoonkovAudio.Play(m_Style.ExplosionSound, state.Position);
                effect.Expire = Time.time + 1.2f;
            }
            else effect.Expire = Time.time + Mathf.Max(.5f, state.Remaining + .8f);
            Draw(effect, 0);
        }

        public void ThrowSound(Vector3 position)
        { if (Warmup()) MoonkovAudio.Play(m_Style.ThrowSound, position); }

        private void Update()
        {
            foreach (var effect in m_Pool)
            {
                if (effect == null || !effect.Active) continue;
                if (Time.time > effect.Expire)
                { effect.Active = false; effect.Root.gameObject.SetActive(false); continue; }
                if (effect.Exploded) effect.Age += Time.deltaTime;
                else
                {
                    float age = Mathf.Min(Time.time - effect.PoseTime, effect.Remaining);
                    // Resting poses have zero velocity; authoritative snapshots keep them on the floor.
                    effect.Root.position = effect.Velocity.sqrMagnitude < .0001f ? effect.Origin :
                        Ballistics.Position(effect.Origin, effect.Velocity, EnergyGrenadeRules.Gravity, age);
                }
                Draw(effect, effect.Exploded ? effect.Age : EnergyGrenadeRules.Fuse - effect.Remaining + Time.time - effect.PoseTime);
            }
        }

        private void Draw(Effect effect, float age)
        {
            float pulse = 1 + .09f * Mathf.Sin(age * (age > 2 ? 34 : 13));
            float alpha = effect.Exploded ? Mathf.Pow(Mathf.Clamp01(1 - age / .65f), 1.5f) : 1;
            effect.Core.localScale = Vector3.one * (effect.Exploded ? .4f + age * 4 : .17f * pulse);
            effect.Shell.localScale = Vector3.one * (effect.Exploded ? Mathf.Lerp(.3f, EnergyGrenadeRules.Radius * 2, Mathf.Clamp01(age / .55f)) : .31f * pulse);
            effect.CoreRenderer.enabled = !effect.Exploded || age < .16f;
            effect.ShellRenderer.enabled = alpha > 0;
            SetProperties(effect.CoreRenderer, effect.Exploded ? Mathf.Clamp01(1 - age / .16f) : 1, 0);
            SetProperties(effect.ShellRenderer, alpha * .8f, 1);
            for (int i = 0; i < effect.Rings.Length; i++)
            {
                var ring = effect.Rings[i];
                ring.enabled = alpha > 0;
                float radius = effect.Exploded ? (.25f + Mathf.Clamp01(age / (.42f + i * .09f)) * EnergyGrenadeRules.Radius) : .22f + i * .035f;
                ring.widthMultiplier = effect.Exploded ? .065f * alpha : .012f;
                ring.startColor = ring.endColor = Cyan * new Color(1, 1, 1, alpha);
                ring.transform.localRotation = effect.Exploded ? Quaternion.Euler(i * 55, 0, 0) :
                    Quaternion.Euler(35 + i * 55, age * (90 + i * 35), age * 65 + i * 70);
                for (int n = 0; n < 48; n++)
                {
                    float angle = n * Mathf.PI * 2 / 48;
                    float distortion = effect.Exploded ? 1 : 1 + .035f * Mathf.Sin(n * 2.7f + age * 18);
                    ring.SetPosition(n, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius * distortion));
                }
            }
            if (effect.Light)
            {
                effect.Light.enabled = !effect.Exploded || age < .5f;
                effect.Light.range = effect.Exploded ? 8 : 1.6f;
                effect.Light.intensity = effect.Exploded ? 4 * alpha : .65f * pulse;
            }
            for (int i = 0; i < effect.Rays.Length; i++)
            {
                var ray = effect.Rays[i];
                ray.enabled = effect.Exploded && age < .6f;
                if (!ray.enabled) continue;
                Vector3 direction = Quaternion.Euler(i * 41, i * 137.508f, 0) * Vector3.forward;
                float head = Mathf.Min(6, age * (16 + i % 4 * 3));
                ray.SetPosition(0, direction * Mathf.Max(.1f, head - 1.1f));
                ray.SetPosition(1, direction * head);
                ray.widthMultiplier = .06f * alpha;
                ray.startColor = new Color(.08f,.6f,1,0); ray.endColor = new Color(.8f,2.5f,3,alpha);
            }
        }

        private void SetProperties(Renderer renderer, float opacity, float shell)
        {
            m_Properties.Clear(); m_Properties.SetFloat(Opacity, opacity); m_Properties.SetFloat(Shell, shell);
            renderer.SetPropertyBlock(m_Properties);
        }

        private Effect CreateEffect(int index)
        {
            var root = new GameObject("Energy grenade " + index).transform;
            root.SetParent(transform, false);
            var effect = new Effect { Root = root, Rings = new LineRenderer[3], Rays = new LineRenderer[16] };
            effect.Core = Sphere("White energy core", root, out effect.CoreRenderer);
            effect.Shell = Sphere("Plasma envelope", root, out effect.ShellRenderer);
            for (int i = 0; i < effect.Rings.Length; i++)
            {
                var ring = new GameObject("Energy filament " + i).AddComponent<LineRenderer>();
                ring.transform.SetParent(root, false); ring.useWorldSpace = false; ring.loop = true;
                ring.positionCount = 48; ring.sharedMaterial = m_Style.Ribbon;
                ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
                ring.numCornerVertices = 2; effect.Rings[i] = ring;
            }
            for (int i = 0; i < effect.Rays.Length; i++)
            {
                var ray = new GameObject("Discharging arc " + i).AddComponent<LineRenderer>();
                ray.transform.SetParent(root, false); ray.useWorldSpace = false; ray.positionCount = 2;
                ray.sharedMaterial = m_Style.Ribbon; ray.shadowCastingMode = ShadowCastingMode.Off;
                ray.receiveShadows = false; ray.enabled = false; effect.Rays[i] = ray;
            }
            var trail = root.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = m_Style.Ribbon; trail.time = .22f; trail.minVertexDistance = .05f;
            trail.startWidth = .11f; trail.endWidth = 0; trail.startColor = Cyan; trail.endColor = new Color(.1f,.4f,1,0);
            trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false; effect.Trail = trail;
            var sparks = new GameObject("Energy fragments").AddComponent<ParticleSystem>();
            sparks.transform.SetParent(root, false); sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main; main.loop = false; main.playOnAwake = false; main.duration = 1;
            main.maxParticles = 48; main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3, 12); main.startSize = new ParticleSystem.MinMaxCurve(.035f,.11f);
            main.startColor = Cyan; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = sparks.emission; emission.enabled = false;
            var shape = sparks.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .2f;
            var fade = sparks.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(new Color(.1f,.5f,1),1) },
                new[] { new GradientAlphaKey(1,0), new GradientAlphaKey(0,1) }); fade.color = gradient;
            var size = sparks.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0,1,1,0));
            var renderer = sparks.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = m_Style.Spark;
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .06f; renderer.lengthScale = 2;
            renderer.shadowCastingMode = ShadowCastingMode.Off; effect.Sparks = sparks;
            // At most four additional unshadowed lights across the whole feedback pool.
            if (index < 4)
            { effect.Light = root.gameObject.AddComponent<Light>(); effect.Light.type = LightType.Point; effect.Light.color = new Color(.15f,.65f,1); effect.Light.shadows = LightShadows.None; }
            return effect;
        }

        private Transform Sphere(string name, Transform parent, out MeshRenderer renderer)
        {
            var root = new GameObject(name); root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = m_Style.Sphere;
            renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = m_Style.Orb;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return root.transform;
        }

#if UNITY_EDITOR
        public void Preview(bool exploded, float age)
        {
            Present(new EnergyGrenadeStateRpc { Id = 1, Position = transform.position, Remaining = 2, Exploded = (byte)(exploded ? 1 : 0) });
            Draw(m_Pool[0], age);
            if (exploded) m_Pool[0].Sparks.Simulate(age, true, false);
        }
#endif
    }
}
