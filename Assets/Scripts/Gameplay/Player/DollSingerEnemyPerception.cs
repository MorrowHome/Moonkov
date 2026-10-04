using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private void Sense(DollSingerEnemyBrain brain, Transform root, Vector3 eye, float range, double now)
        {
            const float interval = .1f;
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
                contact.Suspicion = Mathf.Max(0, contact.Suspicion - interval * .2f);
                contact.Confidence = Mathf.Max(0, contact.Confidence - interval / m_Tuning.MemorySeconds);
            }
            foreach (var candidate in m_Targets)
            {
                if (candidate.Root == null || candidate.Root == root || !EntityManager.Exists(candidate.Entity) ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(candidate.Entity) ||
                    EntityManager.GetComponentData<PredictedPlayerGhost>(candidate.Entity).CurrentHealth <= 0) continue;
                Vector3 offset = candidate.Position + Vector3.up * 1.15f - eye;
                float distance = offset.magnitude;
                float angle = Vector3.Angle(Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward, offset);
                float cone = now - brain.LastDamage < 2 ? 95 : m_Tuning.HalfViewAngle;
                if (distance > range * light || angle > cone) continue;
                int exposed = 0;
                if (ClearSight(root, eye, candidate.Position + Vector3.up * 1.55f, candidate.Entity)) exposed++;
                if (ClearSight(root, eye, candidate.Position + Vector3.up * 1.15f, candidate.Entity)) exposed++;
                if (ClearSight(root, eye, candidate.Position + Vector3.up * .85f, candidate.Entity)) exposed++;
                if (exposed == 0) continue;
                if (!brain.Contacts.TryGetValue(candidate.Entity, out var contact))
                { contact = new PmcContact(); brain.Contacts.Add(candidate.Entity, contact); }
                float clarity = Mathf.Lerp(1.5f, .45f, distance / Mathf.Max(1, range * light));
                clarity *= exposed / 3f * Mathf.Lerp(1, .65f, angle / cone);
                clarity *= candidate.Velocity.magnitude > 2 ? 1.25f : 1;
                contact.Suspicion = Mathf.Clamp01(contact.Suspicion + interval * (clarity / m_Tuning.RecognitionSeconds + .2f));
                contact.Position = candidate.Position;
                contact.Velocity = candidate.Velocity;
                contact.LastSeen = now;
                if (contact.Suspicion >= .99f) contact.Confirmed = true;
                contact.Visible = contact.Confirmed;
                contact.Confidence = contact.Confirmed ? 1 : contact.Suspicion * .6f;
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
                    bool blocked = WorldBlocked(eye, sensor.Position + Vector3.up);
                    float audibleRange = sensor.Range * (blocked ? .55f : 1);
                    if (distance > audibleRange) continue;
                    if (!brain.Contacts.TryGetValue(sensor.Source, out var contact))
                    { contact = new PmcContact(); brain.Contacts.Add(sensor.Source, contact); }
                    // Suit/acoustic gameplay sensors produce uncertain locations, not wall vision.
                    if (!contact.Visible && now - contact.LastSeen > .2)
                    {
                        float error = Mathf.Lerp(1.5f, 6, distance / Mathf.Max(1, audibleRange));
                        contact.Position = sensor.Position + new Vector3(brain.Random.NextFloat(-error, error), 0, brain.Random.NextFloat(-error, error));
                        contact.Velocity = Vector3.zero;
                        contact.Confidence = Mathf.Max(contact.Confidence, sensor.Shot ? .65f : .4f);
                    }
                    contact.LastHeard = now;
                    if (sensor.Shot && distance < 18) brain.Suppression = Mathf.Clamp01(brain.Suppression + .12f);
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
            if (selected == Entity.Null) return;
            var memory = brain.Contacts[selected];
            if (brain.Visible && (!wasVisible || previous != selected)) brain.VisibleSince = now;
            brain.LastSeen = memory.Position;
            brain.ObservedVelocity = memory.Velocity;
            brain.LastSeenTime = memory.LastSeen;
            brain.LastHeardTime = memory.LastHeard;
        }

        private static bool WorldBlocked(Vector3 from, Vector3 to) => UnityEngine.Physics.Linecast(from, to,
            s_WorldMask, QueryTriggerInteraction.Ignore);
    }
}
