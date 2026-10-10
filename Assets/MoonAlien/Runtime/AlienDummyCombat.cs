using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien
{
    /// <summary>Opt-in fake-health adapter, synchronously driven by AlienAmbushSandbox.
    /// No Update callback, player lookup, production health, RPC or Ghost registration.</summary>
    public sealed class AlienDummyCombat : MonoBehaviour
    {
        public const int TicksPerSecond = 100;
        public static AlienAttackTiming Timing => new AlienAttackTiming(70, 18, 120);
        public const float SweepRadius = .16f;
        private AlienCombatContract m_Contract;
        private AlienFakeHealthSink m_Target;
        private AlienGroundProbe m_Probe;
        [SerializeField] private Collider m_Hitbox;
        private AlienAttackReceipt m_Receipt;
        private Vector3 m_LockedAim, m_LockedDirection;
        private double m_Seconds;
        private long m_Tick;
        private bool m_KillAttacker, m_KillTarget, m_RespawnAttacker, m_RespawnTarget;
        private bool m_Suspended;
        private readonly RaycastHit[] m_Hits = new RaycastHit[32];
        private readonly Collider[] m_Overlaps = new Collider[32];
        private string m_Status = "Waiting for sandbox initialization";

        public bool TargetAlive => m_Target != null && m_Target.IsAlive;
        public bool AttackerAlive => m_Contract != null && m_Contract.IsAlive;
        public double TargetHealth => m_Target == null ? 0d : m_Target.Health;
        public long Tick => m_Tick;
        public AlienCombatPhase Phase => m_Contract == null ? AlienCombatPhase.Idle : m_Contract.Phase;
        public ulong AcceptedHits => m_Target == null ? 0 : m_Target.AcceptedHitCount;

        public void Configure(Collider hitbox)
        {
            if (m_Contract != null) throw new InvalidOperationException("Cannot rebind an initialized combat fixture.");
            m_Hitbox = hitbox;
        }
        public bool Initialize(AlienGroundProbe probe)
        {
            if (m_Contract != null || !probe || !m_Hitbox || m_Hitbox.gameObject.scene != gameObject.scene) return false;
            m_Probe = probe;
            m_Contract = new AlienCombatContract(new AlienActorLife(1, 1));
            m_Target = new AlienFakeHealthSink(new AlienActorLife(2, 1), 100d);
            m_Status = "Dummy combat ready; local fixture permission only";
            return true;
        }

        // Called only by the owner, before sensing. No substeps/catch-up strikes on a long frame.
        public float BeginFrame(float deltaTime, ref AlienAmbushBrain brain, out bool resetRoute)
        {
            resetRoute = false;
            if (!m_Contract.IsAlive && brain.State != AlienAmbushState.Dead) brain.Kill();
            if (!float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime) && deltaTime >= 0f)
            {
                // A bounded sandbox lifetime also keeps the observation brain float clock precise.
                m_Seconds = Math.Min(86400d, m_Seconds + deltaTime);
                m_Tick = (long)Math.Floor(m_Seconds * TicksPerSecond);
            }
            if (m_Seconds >= 86400d) m_KillAttacker = true;
            m_Contract.AdvanceTo(m_Tick);
            if (m_KillAttacker) { m_Contract.MarkDead(m_Tick); brain.Kill(); resetRoute = true; }
            if (m_KillTarget) m_Target.MarkDead(m_Tick);
            if (m_RespawnAttacker && !m_KillAttacker && m_Contract.Life.Generation < ulong.MaxValue)
            {
                m_Contract.ResetLife(new AlienActorLife(1, m_Contract.Life.Generation + 1), m_Tick);
                brain = new AlienAmbushBrain(externallyTimedCombat: true);
                m_Receipt = default; resetRoute = true;
            }
            if (m_RespawnTarget && !m_KillTarget && m_Target.Life.Generation < ulong.MaxValue)
            {
                m_Target.ResetLife(new AlienActorLife(2, m_Target.Life.Generation + 1), 100d, m_Tick);
                m_Contract.CancelAttack(m_Receipt, m_Tick);
                brain.NotifyRouteUnavailable(Now); resetRoute = true;
            }
            // Commands are sampled here, before any hit validation, not in OnGUI callback order.
            m_KillAttacker = m_KillTarget = m_RespawnAttacker = m_RespawnTarget = false;
            if (!m_Target.IsAlive && m_Contract.Phase == AlienCombatPhase.Windup)
                m_Contract.CancelAttack(m_Receipt, m_Tick);
            if (m_Suspended)
            {
                m_Contract.CancelAttack(m_Receipt, m_Tick);
                brain.NotifyRouteUnavailable(Now);
                m_Suspended = false; resetRoute = true;
            }
            // Do not mirror strike opening until this sample's eligibility can cancel wind-up.
            if (m_Contract.Phase != AlienCombatPhase.Strike) brain.ApplyCombatPhase(m_Contract.Phase, Now);
            // The fixture moves colliders through Transform. Flush them before this sample;
            // this is a transform sync, not a change to global collision/auto-sync settings.
            Physics.SyncTransforms();
            return Now;
        }
        private float Now => (float)((double)m_Tick / TicksPerSecond);

        public void ResolveFrame(AlienAmbushBrain brain, bool currentAttackEligible)
        {
            if (brain.State == AlienAmbushState.Dead) m_Contract.MarkDead(m_Tick);
            if ((m_Contract.Phase == AlienCombatPhase.Windup && brain.State != AlienAmbushState.Telegraph) ||
                (m_Contract.Phase == AlienCombatPhase.Strike && (!currentAttackEligible ||
                    (brain.State != AlienAmbushState.Telegraph && brain.State != AlienAmbushState.Strike))))
                m_Contract.CancelAttack(m_Receipt, m_Tick);
            if (brain.TryConsumeWindup(out Vector3 aim))
            {
                Vector3 origin = Eye;
                Vector3 offset = aim - origin;
                // Reject degenerate locked directions instead of inventing an attack direction.
                if (currentAttackEligible && offset.sqrMagnitude > .000001f && Finite(offset) &&
                    m_Contract.TryBeginAttack(m_Target.Life, m_Tick, Timing, 25d,
                        Evidence(false, false, false), out m_Receipt))
                { m_LockedAim = aim; m_LockedDirection = offset.normalized; m_Status = "Locked wind-up"; }
                else m_Status = "Wind-up rejected";
            }
            if (m_Contract.Phase == AlienCombatPhase.Strike)
            {
                // Only this sensor reads the live hitbox. It cannot steer locked aim or tactics.
                bool hit = SampleLockedSweep(m_Probe, m_Hitbox, Eye, m_LockedDirection,
                    AlienAmbushBrain.AttackRange, SweepRadius, m_Hits, m_Overlaps,
                    out bool clear, out bool inRange);
                if (m_Target.TryReceive(m_Contract, m_Receipt, m_Tick, Evidence(clear, inRange, hit), out var accepted))
                    m_Status = "Accepted #" + accepted.Receipt.Token.Sequence + ": " + accepted.AppliedDamage + " damage";
                else if (m_Target.IsAlive) m_Status = "Strike missed, rejected or already consumed";
            }
            brain.ApplyCombatPhase(m_Contract.Phase, Now);
        }

        private AlienHitEvidence Evidence(bool clear, bool inRange, bool swept) =>
            new AlienHitEvidence(m_Tick, true, m_Contract.IsAlive, m_Target.IsAlive, clear, inRange, swept);
        private Vector3 Eye => transform.position + transform.up * AlienAdhesionRoute.BodyOffset;
        private static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);

        /// <summary>Only a matching enabled same-scene collider can confirm the locked sweep.
        /// Filled buffers, invalid direction, terrain starting overlap or earlier cover fail closed.</summary>
        public static bool SampleLockedSweep(AlienGroundProbe probe, Collider target, Vector3 origin,
            Vector3 direction, float range, float radius, RaycastHit[] hits, Collider[] overlaps,
            out bool clear, out bool inRange)
        {
            clear = inRange = false;
            if (!probe || !target || !target.enabled || !target.gameObject.activeInHierarchy ||
                probe.gameObject.scene != target.gameObject.scene || !Finite(origin) || !Finite(direction) ||
                direction.sqrMagnitude < .000001f || !(range > 0f) || float.IsInfinity(range) ||
                !(radius > 0f) || float.IsInfinity(radius) || hits == null || overlaps == null ||
                hits.Length == 0 || overlaps.Length == 0) return false;
            var scene = probe.gameObject.scene.GetPhysicsScene();
            if (!scene.IsValid()) return false;
            Vector3 point = target.bounds.center;
            inRange = (point - origin).sqrMagnitude <= range * range;
            clear = !probe.Overlaps(origin, radius) && !probe.Obstructed(origin, point, .01f);
            if (!clear || !inRange) return false;
            int overlapsCount = scene.OverlapSphere(origin, radius, overlaps, ~0, QueryTriggerInteraction.Collide);
            if (overlapsCount == overlaps.Length) return false;
            for (int i = 0; i < overlapsCount; i++) if (overlaps[i] == target) return true;
            int count = scene.SphereCast(origin, radius, direction.normalized, hits, range, ~0, QueryTriggerInteraction.Collide);
            if (count == hits.Length) return false;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (hits[i].collider == target) distance = Mathf.Min(distance, hits[i].distance);
            return !float.IsInfinity(distance) && !probe.Obstructed(origin,
                origin + direction.normalized * distance, radius);
        }

        private void OnDisable() => Suspend();
        public void Suspend() => m_Suspended = true;
        public void RequestAttackerDeath() => m_KillAttacker = true;
        public void RequestTargetDeath() => m_KillTarget = true;
        public void RequestAttackerRespawn() => m_RespawnAttacker = true;
        public void RequestTargetRespawn() => m_RespawnTarget = true;
        private void OnGUI()
        {
            if (m_Contract == null) return;
            GUILayout.BeginArea(new Rect(16, 350, 670, 150), GUI.skin.box);
            GUILayout.Label("FAKE DUMMY COMBAT / no production damage or network authority");
            GUILayout.Label("HP " + m_Target.Health + "/100 | " + Phase + " | tick " + m_Tick + " | accepted " + AcceptedHits);
            GUILayout.Label("Lives A:" + m_Contract.Life.Generation + " T:" + m_Target.Life.Generation + " | " + m_Status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Kill attacker")) RequestAttackerDeath();
            if (GUILayout.Button("Respawn attacker")) RequestAttackerRespawn();
            if (GUILayout.Button("Kill dummy")) RequestTargetDeath();
            if (GUILayout.Button("Respawn dummy")) RequestTargetRespawn();
            GUILayout.EndHorizontal();
            GUILayout.Label("Commands apply before the next running sample. Respawn preserves pose; pause freezes clock.");
            GUILayout.EndArea();
        }
        private void OnDrawGizmos()
        {
            if (m_Contract == null || !m_Contract.HasActiveAttack) return;
            Gizmos.color = Phase == AlienCombatPhase.Windup ? Color.yellow : Color.red;
            Gizmos.DrawLine(Eye, Eye + m_LockedDirection * AlienAmbushBrain.AttackRange);
            Gizmos.DrawWireSphere(m_LockedAim, SweepRadius);
        }
    }
}
