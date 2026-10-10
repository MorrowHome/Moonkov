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
        public uint WeaponId;
        public int PelletIndex;
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
            public uint Weapon;
            public int Pellet;
            public float Until;
            public bool Confirmed;
            public Vector3 Position;
            public byte Kind;
        }
        private struct ShotReceipt
        {
            public int Owner;
            public uint Tick, Weapon;
            public float Until, LastSurfaceSound;
            public byte Confirmation;
        }
        private readonly Receipt[] m_Receipts = new Receipt[256];
        private readonly ShotReceipt[] m_Shots = new ShotReceipt[128];
        private readonly ParticleSystem[] m_Pool = new ParticleSystem[16];
        private RifleFeedbackLibrary m_Library;
        private int m_NextReceipt, m_NextEffect, m_NextShot;

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
            var profile = m_Library?.ForWeapon(impact.WeaponId);
            int shot = FindShot(impact);
            int receipt = -1;
            for (int i = 0; i < m_Receipts.Length; i++)
                if (m_Receipts[i].Until > Time.unscaledTime && m_Receipts[i].Owner == impact.OwnerNetworkId &&
                    m_Receipts[i].Tick == impact.ShotTick && m_Receipts[i].Weapon == impact.WeaponId &&
                    m_Receipts[i].Pellet == impact.PelletIndex) { receipt = i; break; }
            if (receipt < 0)
            {
                receipt = m_NextReceipt++ % m_Receipts.Length;
                m_Receipts[receipt] = new Receipt { Owner = impact.OwnerNetworkId, Tick = impact.ShotTick,
                    Weapon = impact.WeaponId, Pellet = impact.PelletIndex,
                    Until = Time.unscaledTime + 6f, Position = impact.Position, Kind = impact.Kind };
                Emit((RifleImpactKind)impact.Kind, impact.Position, impact.Normal, profile, shot);
            }
            else if (confirmed && !m_Receipts[receipt].Confirmed &&
                (m_Receipts[receipt].Kind != impact.Kind || Vector3.Distance(m_Receipts[receipt].Position, impact.Position) > .25f))
            {
                // A corrected trajectory may hit a different surface. Show the actual server landing.
                Emit((RifleImpactKind)impact.Kind, impact.Position, impact.Normal, profile, shot);
            }
            if (!confirmed || m_Receipts[receipt].Confirmed) return;
            m_Receipts[receipt].Confirmed = true;
            if (!owned || (impact.DamageFlags & 1) == 0) return;
            bool killed = (impact.DamageFlags & 2) != 0;
            byte confirmation = (byte)(killed ? 2 : 1);
            // Eight shotgun pellets are one trigger pull. A later lethal pellet may upgrade it once.
            if (confirmation <= m_Shots[shot].Confirmation) return;
            m_Shots[shot].Confirmation = confirmation;
            InGameHUD.ConfirmRifleHit(killed, profile);
            MoonkovAudio.Play(killed ? profile?.KillConfirm ?? m_Library?.KillConfirm :
                profile?.HitConfirm ?? m_Library?.HitConfirm, Vector3.zero);
        }

        private int FindShot(in ClientRifleImpactRpc impact)
        {
            for (int i = 0; i < m_Shots.Length; i++)
                if (m_Shots[i].Until > Time.unscaledTime && m_Shots[i].Owner == impact.OwnerNetworkId &&
                    m_Shots[i].Tick == impact.ShotTick && m_Shots[i].Weapon == impact.WeaponId) return i;
            int next = m_NextShot++ % m_Shots.Length;
            m_Shots[next] = new ShotReceipt { Owner = impact.OwnerNetworkId, Tick = impact.ShotTick,
                Weapon = impact.WeaponId, Until = Time.unscaledTime + 6f, LastSurfaceSound = float.NegativeInfinity };
            return next;
        }

        private void Emit(RifleImpactKind kind, Vector3 position, Vector3 normal,
            HaloWeaponFeedbackProfile profile, int shot)
        {
            if (m_Library == null) return;
            // Surface particles remain per pellet; nearby simultaneous sounds share a short gate.
            if (Time.unscaledTime - m_Shots[shot].LastSurfaceSound >= (profile?.SurfaceSoundInterval ?? 0))
            {
                MoonkovAudio.Play(kind == RifleImpactKind.Body ? profile?.BodyImpact ?? m_Library.BodyImpact :
                    kind == RifleImpactKind.Metal ? profile?.MetalImpact ?? m_Library.MetalImpact :
                    profile?.RockImpact ?? m_Library.RockImpact, position);
                m_Shots[shot].LastSurfaceSound = Time.unscaledTime;
            }
            var particles = m_Pool[m_NextEffect++ % m_Pool.Length];
            if (particles == null) return;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.transform.SetPositionAndRotation(position + normal * .015f,
                Quaternion.LookRotation(normal.sqrMagnitude > .001f ? normal : Vector3.up));
            bool metal = kind == RifleImpactKind.Metal, body = kind == RifleImpactKind.Body;
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.10f, metal ? .24f : .38f);
            float speed = profile?.FragmentSpeed ?? 1, scale = profile?.FragmentScale ?? 1;
            main.startSpeed = new ParticleSystem.MinMaxCurve((metal ? 1.4f : .3f) * speed, (metal ? 3f : .8f) * speed);
            main.startSize = new ParticleSystem.MinMaxCurve((body ? .018f : .012f) * scale, (metal ? .028f : .055f) * scale);
            main.startColor = body ? new Color(1f, .34f, .5f, .65f) :
                metal ? new Color(1f, .85f, .4f) : new Color(.58f, .52f, .45f, .65f);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = metal ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = metal ? 2.5f : 1f; renderer.velocityScale = metal ? .06f : 0;
            particles.Play();
            particles.Emit(Mathf.Clamp(Mathf.RoundToInt((metal ? 10 : body ? 6 : 12) *
                (profile?.FragmentCount ?? 1)), 1, profile?.ImpactFlash == true ? 15 : 16));
            if (profile?.ImpactFlash == true && !body)
                particles.Emit(new ParticleSystem.EmitParams { startSize = .13f, startLifetime = .065f,
                    velocity = Vector3.zero, startColor = new Color(.65f, .9f, 1f, .8f) }, 1);
        }
    }
}
