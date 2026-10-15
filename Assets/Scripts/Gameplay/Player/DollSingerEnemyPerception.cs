using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private void Sense(DollSingerEnemyBrain brain, Transform root, Vector3 eye, float range, double now)
        {
            float elapsed = brain.LastSenseTime < 0 ? .1f : Mathf.Max(.001f, (float)(now - brain.LastSenseTime));
            // A server hitch must age memory, but must not count as uninterrupted visual exposure.
            float observationStep = Mathf.Min(.2f, elapsed);
            brain.LastSenseTime = now;
            float light = 1;
            if (SystemAPI.TryGetSingleton<ExpeditionClockState>(out var clock) && clock.Ready)
            {
                double hour = ExpeditionClock.HourOfDay(ExpeditionClock.HoursAt(clock, SystemAPI.GetSingleton<NetworkTime>().ServerTick));
                light = Mathf.Lerp(m_Tuning.NightSightMultiplier, 1,
                    Mathf.Clamp01(Mathf.Sin(((float)hour - 6) * Mathf.PI / 12) * 2));
            }
            foreach (var contact in brain.Contacts.Values)
            {
                contact.Visible = false;
                contact.Suspicion = Mathf.Max(0, contact.Suspicion - elapsed * .28f);
                contact.Confidence = Mathf.Max(0, contact.Confidence - elapsed / Mathf.Max(1, m_Tuning.MemorySeconds));
            }
            foreach (var candidate in m_Targets)
            {
                if (candidate.Root == null || candidate.Root == root || !EntityManager.Exists(candidate.Entity) ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(candidate.Entity) ||
                    EntityManager.GetComponentData<PredictedPlayerGhost>(candidate.Entity).CurrentHealth <= 0) continue;
                Vector3 offset = candidate.Position + candidate.AimOffset - eye;
                float distance = offset.magnitude;
                float angle = Vector3.Angle(Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward, offset);
                float cone = now - brain.LastDamage < 2 ? 95 : m_Tuning.HalfViewAngle;
                if (distance > range * light || angle > cone) continue;
                int exposed = 0;
                if (candidate.Alien)
                {
                    // Scaled crawling creatures cannot be aimed at an imaginary human chest.
                    if (ClearSight(root, eye, candidate.Position + candidate.AimOffset, candidate.Entity)) exposed = 3;
                }
                else
                {
                    if (ClearSight(root, eye, candidate.Position + Vector3.up * 1.55f, candidate.Entity)) exposed++;
                    if (ClearSight(root, eye, candidate.Position + Vector3.up * 1.15f, candidate.Entity)) exposed++;
                    if (ClearSight(root, eye, candidate.Position + Vector3.up * .85f, candidate.Entity)) exposed++;
                }
                if (exposed == 0) continue;
                if (!brain.Contacts.TryGetValue(candidate.Entity, out var contact))
                { contact = new PmcContact(); brain.Contacts.Add(candidate.Entity, contact); }
                float visualGap = (float)(now - contact.LastSeen);
                bool continuous = contact.HadVisualSample && visualGap > 0 && visualGap <= .25f &&
                    visualGap <= elapsed * 1.5f;
                // Estimate motion from our own consecutive sightings. The shared target track
                // also updates behind walls and is not evidence this observer may use.
                Vector3 observedVelocity = continuous ?
                    Vector3.ClampMagnitude((candidate.Position - contact.LastVisualPosition) / visualGap, 8) : Vector3.zero;
                contact.Velocity = continuous ? Vector3.Lerp(contact.Velocity, observedVelocity, .65f) : Vector3.zero;
                float clarity = Mathf.Lerp(1.5f, .45f, distance / Mathf.Max(1, range * light));
                clarity *= exposed / 3f * Mathf.Lerp(1, .65f, angle / Mathf.Max(1, cone));
                clarity *= contact.Velocity.magnitude > 2 ? 1.25f : 1;
                contact.Suspicion = Mathf.Clamp01(contact.Suspicion + observationStep *
                    (clarity / Mathf.Max(.1f, m_Tuning.RecognitionSeconds) + .28f));
                contact.Position = candidate.Position;
                contact.LastVisualPosition = candidate.Position;
                contact.HadVisualSample = true;
                contact.PositionUncertainty = 1;
                contact.AimOffset = candidate.AimOffset;
                contact.LastSeen = now;
                if (contact.Suspicion >= .99f) contact.Confirmed = true;
                // Remembering an identity does not grant instant recognition after a long absence.
                contact.Visible = contact.Confirmed && contact.Suspicion >= .65f;
                contact.Confidence = contact.Visible ? 1 : contact.Suspicion * .6f;
            }
            if (brain.SensorBatch != m_SensorBatch)
            {
                brain.SensorBatch = m_SensorBatch;
                foreach (var sensor in m_Events)
                {
                    if (!EntityManager.Exists(sensor.Source) || !EntityManager.HasComponent<GhostGameObjectLink>(sensor.Source)) continue;
                    var source = EntityManager.GetComponentObject<GhostGameObjectLink>(sensor.Source).LinkedInstance;
                    if (source == null || source.transform == root) continue;
                    float distance = Vector3.Distance(eye, sensor.Position);
                    if (distance > sensor.Range) continue;
                    bool blocked = WorldBlocked(eye, sensor.Position + Vector3.up);
                    float audibleRange = sensor.Range * (blocked ? .55f : 1);
                    if (distance > audibleRange) continue;
                    if (!brain.Contacts.TryGetValue(sensor.Source, out var contact))
                    { contact = new PmcContact(); brain.Contacts.Add(sensor.Source, contact); }
                    // Keep an uncertain bearing for a short sound sequence. Independent random
                    // offsets on every footstep cause jitter and can average into perfect tracking.
                    if (!contact.Visible && now - contact.LastSeen > .2)
                    {
                        if (now >= contact.HearingBiasUntil)
                        {
                            float bearing = brain.Random.NextFloat(0, Mathf.PI * 2);
                            float magnitude = brain.Random.NextFloat(.65f, 1);
                            contact.HearingBias = new Vector3(Mathf.Sin(bearing) * magnitude,
                                brain.Random.NextFloat(-.2f, .2f), Mathf.Cos(bearing) * magnitude);
                            contact.HearingBiasUntil = now + brain.Random.NextFloat(3, 5);
                        }
                        float error = Mathf.Lerp(1.5f, 6.5f, distance / Mathf.Max(1, audibleRange)) + (blocked ? 3 : 0);
                        float visualUncertainty = 1 + Mathf.Max(0, (float)(now - contact.LastSeen)) * 2;
                        if (!contact.HadVisualSample || error <= visualUncertainty)
                        {
                            Vector3 bias = contact.HearingBias * error;
                            if (!blocked) bias.y = 0;
                            contact.Position = sensor.Position + bias;
                            contact.PositionUncertainty = error;
                            contact.Velocity = Vector3.zero;
                        }
                        else contact.PositionUncertainty = visualUncertainty;
                        contact.Confidence = Mathf.Max(Mathf.Min(contact.Confidence, .75f), sensor.Shot ? .65f : .4f);
                    }
                    contact.LastHeard = now;
                    // Nearby shots create unease; distant shooting must not build the same
                    // pressure as taking damage or a projectile passing this actor.
                    if (sensor.Shot && distance < 18)
                        brain.Suppression = Mathf.Max(brain.Suppression, blocked ? .06f : .15f);
                }
            }
            brain.ExpiredContacts.Clear();
            Entity selected = Entity.Null;
            float best = .15f;
            foreach (var pair in brain.Contacts)
            {
                var c = pair.Value;
                if (!EntityManager.Exists(pair.Key) || !EntityManager.HasComponent<PredictedPlayerGhost>(pair.Key) ||
                    EntityManager.GetComponentData<PredictedPlayerGhost>(pair.Key).CurrentHealth <= 0 ||
                    now - System.Math.Max(c.LastSeen, c.LastHeard) > m_Tuning.MemorySeconds)
                { brain.ExpiredContacts.Add(pair.Key); continue; }
                float score = c.Confidence + (c.Visible ? 2 : 0) + (pair.Key == brain.Target ? .15f : 0);
                score -= Mathf.Min(.4f, Vector3.Distance(eye, c.Position) / 150);
                if (score <= best) continue;
                best = score;
                selected = pair.Key;
            }
            foreach (var entity in brain.ExpiredContacts) brain.Contacts.Remove(entity);
            bool wasVisible = brain.Visible;
            Entity previous = brain.Target;
            brain.Target = selected;
            brain.Visible = selected != Entity.Null && brain.Contacts[selected].Visible;
            brain.Confidence = selected == Entity.Null ? 0 : brain.Contacts[selected].Confidence;
            if (previous != selected) ResetContactTactics(brain, now);
            if (selected == Entity.Null)
            {
                brain.ObservedVelocity = Vector3.zero;
                brain.ContactUncertainty = 1;
                brain.LastSeenTime = brain.LastHeardTime = -100;
                return;
            }
            var memory = brain.Contacts[selected];
            if (brain.Visible && (!wasVisible || previous != selected)) brain.VisibleSince = now;
            brain.LastSeen = memory.Position;
            brain.ObservedVelocity = memory.Velocity;
            brain.AimOffset = memory.AimOffset;
            brain.LastSeenTime = memory.LastSeen;
            brain.LastHeardTime = memory.LastHeard;
            brain.ContactUncertainty = memory.PositionUncertainty;
            if (wasVisible != brain.Visible) brain.NextDecision = 0;
        }

        private void ResetContactTactics(DollSingerEnemyBrain brain, double now)
        {
            // Cover, flank routes and bursts were chosen relative to a particular opponent.
            // A new contact must not inherit a safe angle or a settled trigger from that duel.
            brain.HasCover = false;
            brain.IsPeeking = false;
            brain.CoverPhase = PmcCoverPhase.Approaching;
            brain.CoverPeeks = 0;
            m_CoverReservations.Remove(brain.Seed);
            brain.NextCover = 0;
            brain.NextPath = 0;
            brain.PathFailed = true;
            brain.CommitUntil = 0;
            brain.NextDecision = 0;
            brain.SearchStep = 0;
            brain.SearchFocusValid = false;
            brain.SearchPauseUntil = 0;
            brain.SearchObservationTime = -100;
            brain.SearchUntil = now;
            brain.BurstUntil = 0;
            brain.NextBurst = now;
            brain.NextAimError = 0;
            brain.HealUntil = 0;
        }

        private static bool WorldBlocked(Vector3 from, Vector3 to) => UnityEngine.Physics.Linecast(from, to,
            s_WorldMask, QueryTriggerInteraction.Ignore);
    }
}
