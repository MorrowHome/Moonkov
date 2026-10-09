using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    public struct ClientRifleImpactRpc : IRpcCommand
    {
        public int OwnerNetworkId;
        public uint ShotTick;
        public float3 Position, Normal;
        public byte Kind, DamageFlags; // bit 0: applied damage; bit 1: lethal damage
    }

    // A client-world child of its existing VFX manager. All fragments reuse a bounded pool.
    public sealed class RifleImpactFeedback : MonoBehaviour
    {
        private struct Receipt
        {
            public int Owner;
            public uint Tick;
            public float Until;
            public bool Confirmed;
            public Vector3 Position;
            public byte Kind;
        }
        private readonly Receipt[] m_Receipts = new Receipt[256];
        private readonly ParticleSystem[] m_Pool = new ParticleSystem[16];
        private RifleFeedbackLibrary m_Library;
        private int m_NextReceipt, m_NextEffect;

        public void Warmup()
        {
            if (m_Library != null) return;
            m_Library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
            if (m_Library == null || m_Library.ImpactMaterial == null) return;
            for (int i = 0; i < m_Pool.Length; i++)
            {
                var child = new GameObject("Rifle impact " + i);
                child.transform.SetParent(transform, false);
                var particles = child.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false; main.loop = false; main.duration = .5f;
                main.maxParticles = 16; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 1.62f / Mathf.Max(.001f, Mathf.Abs(UnityEngine.Physics.gravity.y));
                var emission = particles.emission; emission.enabled = false;
                var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 42f; shape.radius = .01f;
                var size = particles.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = m_Library.ImpactMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                m_Pool[i] = particles;
            }
        }

        // Predicted collision gives immediate surface feedback. Confirmation never invents damage.
        public void Present(in ClientRifleImpactRpc impact, bool confirmed, bool owned)
        {
            Warmup();
            int receipt = -1;
            for (int i = 0; i < m_Receipts.Length; i++)
                if (m_Receipts[i].Until > Time.unscaledTime && m_Receipts[i].Owner == impact.OwnerNetworkId &&
                    m_Receipts[i].Tick == impact.ShotTick) { receipt = i; break; }
            if (receipt < 0)
            {
                receipt = m_NextReceipt++ % m_Receipts.Length;
                m_Receipts[receipt] = new Receipt { Owner = impact.OwnerNetworkId, Tick = impact.ShotTick,
                    Until = Time.unscaledTime + 6f, Position = impact.Position, Kind = impact.Kind };
                Emit((RifleImpactKind)impact.Kind, impact.Position, impact.Normal);
            }
            else if (confirmed && !m_Receipts[receipt].Confirmed &&
                (m_Receipts[receipt].Kind != impact.Kind || Vector3.Distance(m_Receipts[receipt].Position, impact.Position) > .25f))
            {
                // A corrected trajectory may hit a different surface. Show the actual server landing.
                Emit((RifleImpactKind)impact.Kind, impact.Position, impact.Normal);
            }
            if (!confirmed || m_Receipts[receipt].Confirmed) return;
            m_Receipts[receipt].Confirmed = true;
            if (!owned || (impact.DamageFlags & 1) == 0) return;
            bool killed = (impact.DamageFlags & 2) != 0;
            InGameHUD.ConfirmRifleHit(killed);
            MoonkovAudio.Play(killed ? m_Library?.KillConfirm : m_Library?.HitConfirm, Vector3.zero);
        }

        private void Emit(RifleImpactKind kind, Vector3 position, Vector3 normal)
        {
            if (m_Library == null) return;
            MoonkovAudio.Play(kind == RifleImpactKind.Body ? m_Library.BodyImpact :
                kind == RifleImpactKind.Metal ? m_Library.MetalImpact : m_Library.RockImpact, position);
            var particles = m_Pool[m_NextEffect++ % m_Pool.Length];
            if (particles == null) return;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.transform.SetPositionAndRotation(position + normal * .015f,
                Quaternion.LookRotation(normal.sqrMagnitude > .001f ? normal : Vector3.up));
            bool metal = kind == RifleImpactKind.Metal, body = kind == RifleImpactKind.Body;
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.10f, metal ? .24f : .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(metal ? 1.4f : .3f, metal ? 3f : .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(body ? .018f : .012f, metal ? .028f : .055f);
            main.startColor = body ? new Color(1f, .34f, .5f, .65f) :
                metal ? new Color(1f, .85f, .4f) : new Color(.58f, .52f, .45f, .65f);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = metal ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = metal ? 2.5f : 1f; renderer.velocityScale = metal ? .06f : 0;
            particles.Play(); particles.Emit(metal ? 10 : body ? 6 : 12);
        }
    }
}
