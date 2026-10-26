using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    // A real travelled projectile segment may pressure a nearby PMC, but it
    // never grants sight or the hidden shooter's actual position.
    public static class ProjectileThreat
    {
        public const float Radius = 1.4f;
        public const double Interval = .2;
        private const int MaxHits = 64;

        public static int Observe(PhysicsScene scene, World world, Entity shooter, ClashDamageSource source,
            Vector3 from, Vector3 to, Collider impact, double now, int playerMask, int worldMask, ref Collider[] hits)
        {
            if (!scene.IsValid() || world == null || !world.IsCreated || !double.IsFinite(now)) return 0;
            Vector3 travel = to - from;
            if (!float.IsFinite(travel.x) || !float.IsFinite(travel.y) || !float.IsFinite(travel.z) || travel.sqrMagnitude < .0001f) return 0;
            var manager = world.EntityManager;
            if (!manager.Exists(shooter) || !manager.HasComponent<PredictedPlayerGhost>(shooter)) return 0;
            if (hits == null || hits.Length == 0) hits = new Collider[16];
            int count;
            while (true)
            {
                count = scene.OverlapCapsule(from, to, Radius, hits, playerMask, QueryTriggerInteraction.Ignore);
                if (count < hits.Length || hits.Length >= MaxHits) break;
                Array.Resize(ref hits, Math.Min(MaxHits, hits.Length * 2));
            }
            GhostGameObject impactGhost = null;
            if (impact) GhostGameObject.TryFindGhostGameObject(impact.gameObject, out impactGhost);
            Vector3 direction = travel.normalized;
            int observed = 0;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i];
                if (!collider || !GhostGameObject.TryFindGhostGameObject(collider.gameObject, out var ghost) ||
                    ghost == impactGhost || ghost.World != world) continue;
                var victim = ghost.LinkedEntity;
                if (victim == shooter || !manager.Exists(victim) || !manager.HasComponent<DollSingerEnemy>(victim) ||
                    !manager.HasComponent<DollSingerEnemyBrain>(victim) || !manager.HasComponent<PredictedPlayerGhost>(victim) ||
                    BountyClashDamage.IsSelf(world, victim, source)) continue;
                var health = manager.GetComponentData<PredictedPlayerGhost>(victim);
                if (health.CurrentHealth <= 0 || health.ClashEnabled) continue;
                var brain = manager.GetComponentObject<DollSingerEnemyBrain>(victim);
                if (brain == null || now < brain.NextNearMiss) continue;
                Vector3 centre = collider.bounds.center;
                float t = Vector3.Dot(centre - from, travel) / travel.sqrMagnitude;
                // Wait until the shot has actually passed the body. Otherwise a
                // shot that hits it on the next update would count as a near miss.
                if (t < 0 || t >= 1) continue;
                Vector3 closest = from + travel * t;
                Vector3 body = collider.ClosestPoint(closest);
                Vector3 probe = closest - direction * .03f;
                Vector3 gap = body - probe;
                if (gap.magnitude > Radius + .05f || gap.sqrMagnitude > .0001f &&
                    scene.Raycast(probe, gap.normalized, gap.magnitude, worldMask, QueryTriggerInteraction.Ignore)) continue;

                brain.NextNearMiss = now + Interval;
                brain.Suppression = Mathf.Clamp01(brain.Suppression + .24f);
                brain.NextDecision = 0;
                if (brain.Action == PmcAction.Scavenge || brain.Action == PmcAction.Heal) brain.CommitUntil = 0;
                if (!brain.Contacts.TryGetValue(shooter, out var contact))
                { contact = new PmcContact(); brain.Contacts.Add(shooter, contact); }
                if (!contact.Visible && now - contact.LastSeen > .3 && (!contact.HadVisualSample || now - contact.LastSeen > 4))
                {
                    // An incoming bearing is useful; the distance to an unseen shooter is not known.
                    // Reuse one error for a short burst; changing it on every round
                    // makes cover routes jitter and lets observations average to certainty.
                    if (now >= contact.HearingBiasUntil)
                    {
                        float angle = brain.Random.NextFloat(0, Mathf.PI * 2);
                        float magnitude = brain.Random.NextFloat(.65f, 1);
                        contact.HearingBias = new Vector3(Mathf.Sin(angle) * magnitude, 0, Mathf.Cos(angle) * magnitude);
                        contact.HearingBiasUntil = now + brain.Random.NextFloat(3, 5);
                    }
                    Vector3 bias = contact.HearingBias; bias.y = 0;
                    contact.Position = closest - direction * 12 + bias * 4;
                    contact.PositionUncertainty = 12;
                    contact.Velocity = Vector3.zero;
                    contact.Confidence = Mathf.Max(contact.Confidence, .45f);
                }
                contact.LastHeard = now;
                observed++;
            }
            Array.Clear(hits, 0, count);
            return observed;
        }
    }
}
