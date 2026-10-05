using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private void Decide(DollSingerEnemyBrain brain, Vector3 position, PredictedPlayerGhost health, MoonRaidMap map, double now)
        {
            bool contact = brain.Target != Unity.Entities.Entity.Null && brain.Confidence > .2f;
            bool lowHealth = health.CurrentHealth < health.MaxHealth * m_Tuning.RetreatHealthFraction;
            bool empty = health.CurrentAmmo == 0;
            int reserveAmmo = (health.EquippedWeaponSlot != 1 ? health.PrimaryAmmo : 0) +
                (health.EquippedWeaponSlot != 2 ? health.SecondaryAmmo : 0) + (health.EquippedWeaponSlot != 3 ? health.PistolAmmo : 0);
            var weapon = WeaponManager.Instance?.WeaponRegistry.GetWeaponData(health.EquippedWeaponID);
            int energyPerRound = weapon?.EnergyPerRound ?? 1;
            bool hasEnergy = brain.Inventory.CellEnergy(carriedOnly: true) >= energyPerRound;
            bool noResources = empty && reserveAmmo == 0 && !hasEnergy;
            if (lowHealth || noResources || brain.LootedCaches >= m_Tuning.DesiredCaches ||
                now - brain.SpawnTime > map.RaidDuration * (m_Tuning.DepartureFraction + (brain.Seed % 3 - 1) * .08f))
                brain.Leaving = true;

            PmcAction best = brain.Leaving ? PmcAction.Extract : PmcAction.Scavenge;
            float score = brain.Leaving ? 28 : 12;
            string reason = brain.Leaving ? "bank loot / leave before raid timeout" : "search supplies";
            void Consider(PmcAction action, float value, string why)
            {
                if (brain.Action == action) value += 8; // Hysteresis avoids changing tactics every decision tick.
                if (value <= score) return;
                best = action; score = value; reason = why;
            }
            if (contact)
            {
                if (brain.Visible)
                {
                    Consider(PmcAction.Engage, 50 + brain.Aggression * 12, "confirmed opponent; deliberate bursts");
                    if ((brain.Action == PmcAction.Flank && now - brain.ActionStarted < 5 && HorizontalDistance(position, brain.TacticalGoal) > 2 ||
                         brain.Action != PmcAction.Flank && now >= brain.NextFlank && now - brain.ActionStarted > 5) && brain.Suppression < .5f)
                        Consider(PmcAction.Flank, 62 + brain.Aggression * 8, "change angle instead of repeating a duel");
                }
                else if (now - brain.LastSeenTime < m_Tuning.MemorySeconds && brain.Contacts[brain.Target].Confirmed)
                    Consider(PmcAction.Search, 38 + brain.Confidence * 10, "search last observation and likely exits");
                else Consider(PmcAction.Investigate, 26 + brain.Confidence * 10, "investigate uncertain sensor contact");
                if (brain.Suppression > .3f || now - brain.LastDamage < 3 || brain.HasCover && now - brain.LastSeenTime < 4)
                    Consider(PmcAction.Cover, 55 + brain.Suppression * 30 + brain.Caution * 8, "pressure: break line of sight, peek briefly");
                if (lowHealth || noResources)
                    Consider(PmcAction.Retreat, 110, "survival exceeds value of this fight");
            }
            if (empty && hasEnergy || health.ControllerState.IsReloadingState)
                Consider(PmcAction.Reload, 95, "reload using carried cells, behind cover when available");
            bool emergency = lowHealth || noResources || empty || brain.Suppression > .7f;
            bool changed = best != brain.Action && (now >= brain.CommitUntil || emergency || brain.PathFailed);
            if (changed)
            {
                brain.Action = best;
                brain.ActionStarted = now;
                brain.CommitUntil = now + m_Tuning.CommitmentSeconds;
                brain.SearchStep = 0;
                brain.SearchUntil = now + 2;
                brain.NextCover = 0;
                brain.NextPath = 0;
                brain.LootUntil = 0;
                if (best == PmcAction.Flank) brain.NextFlank = now + 12;
                if (best != PmcAction.Cover && best != PmcAction.Reload && best != PmcAction.Retreat)
                { brain.HasCover = false; m_CoverReservations.Remove(brain.Seed); }
            }
            if (best == brain.Action) { brain.ActionScore = score; brain.Reason = reason; }
            Vector3 goal = brain.TacticalGoal;
            switch (brain.Action)
            {
                case PmcAction.Scavenge:
                    goal = SelectCache(brain, position, map);
                    break;
                case PmcAction.Extract:
                    goal = map.ExtractionPosition;
                    break;
                case PmcAction.Investigate:
                    goal = brain.LastSeen;
                    break;
                case PmcAction.Search:
                    if (brain.SearchStep == 0) goal = brain.LastSeen;
                    if (HorizontalDistance(position, goal) < 2 || now >= brain.SearchUntil || brain.PathFailed)
                    {
                        brain.SearchStep++;
                        float radius = Mathf.Min(12, 2 + brain.SearchStep * 2);
                        Vector3 prediction = Vector3.ClampMagnitude(brain.ObservedVelocity * 1.5f, 5);
                        Vector3 offset = Quaternion.Euler(0, brain.Seed * 97 + brain.SearchStep * 137, 0) * Vector3.forward * radius;
                        goal = brain.LastSeen + prediction + offset;
                        brain.SearchUntil = now + 3;
                        if (brain.SearchStep > 5 && brain.Contacts.TryGetValue(brain.Target, out var memory)) memory.Confidence = 0;
                    }
                    break;
                case PmcAction.Cover:
                case PmcAction.Reload:
                case PmcAction.Retreat:
                    if (contact && (now >= brain.NextCover || brain.PathFailed))
                    {
                        ChooseCover(brain, position, brain.LastSeen, now);
                        brain.NextCover = now + m_Tuning.CoverRescanSeconds;
                    }
                    brain.IsPeeking = brain.Action == PmcAction.Cover && brain.HasCover &&
                        brain.Suppression < .7f && (now - brain.ActionStarted) % 3.6 > 2.5;
                    goal = brain.HasCover ? (brain.IsPeeking ? brain.PeekPosition : brain.CoverPosition) : position;
                    if (brain.Action == PmcAction.Retreat && contact && !brain.HasCover)
                        goal = ReachableOffset(brain, position, (position - brain.LastSeen).normalized * 12);
                    break;
                case PmcAction.Flank:
                    if (changed || brain.PathFailed)
                    {
                        Vector3 away = position - brain.LastSeen; away.y = 0; away.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, away) * (brain.Seed % 2 == 0 ? 1 : -1);
                        goal = ReachableOffset(brain, position, side * 12 + away * 3);
                    }
                    if (HorizontalDistance(position, goal) < 2 && now - brain.ActionStarted > 2)
                        brain.CommitUntil = 0;
                    break;
                case PmcAction.Engage:
                    if (changed || now >= brain.NextCover || brain.PathFailed)
                    {
                        Vector3 away = position - brain.LastSeen; away.y = 0; away.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, away) * brain.Random.NextFloat(-3, 3);
                        float range = m_Tuning.PreferredRange + brain.Caution * 5;
                        Vector3 desired = brain.LastSeen + away * range + side;
                        goal = ReachableOffset(brain, position, Vector3.ClampMagnitude(desired - position, 8));
                        brain.NextCover = now + 2;
                    }
                    break;
            }
            brain.TacticalGoal = goal;
        }

        private Vector3 ReachableOffset(DollSingerEnemyBrain brain, Vector3 position, Vector3 offset)
        {
            if (NavMesh.SamplePosition(position + offset, out var end, 3, m_Filter) &&
                CompletePath(brain, position, end.position, out _)) return end.position;
            if (NavMesh.SamplePosition(position - offset * .5f, out end, 3, m_Filter) &&
                CompletePath(brain, position, end.position, out _)) return end.position;
            return position;
        }

        private bool CompletePath(DollSingerEnemyBrain brain, Vector3 position, Vector3 goal, out float length)
        {
            length = 0;
            if (!NavMesh.SamplePosition(position, out var start, 3, m_Filter) ||
                !NavMesh.CalculatePath(start.position, goal, m_Filter, brain.ProbePath) ||
                brain.ProbePath.status != NavMeshPathStatus.PathComplete) return false;
            int count = brain.ProbePath.GetCornersNonAlloc(brain.ProbeCorners);
            for (int i = 1; i < count; i++) length += Vector3.Distance(brain.ProbeCorners[i - 1], brain.ProbeCorners[i]);
            return count > 0 && count < brain.ProbeCorners.Length;
        }

        private void ChooseCover(DollSingerEnemyBrain brain, Vector3 position, Vector3 threat, double now)
        {
            float best = float.MinValue;
            bool found = false;
            Vector3 cover = position, peek = position;
            // A bounded local geometry search, rather than treating every waypoint as cover.
            for (int i = 0; i < 16; i++)
            {
                Vector3 offset = Quaternion.Euler(0, i * 137.5f + brain.Seed * 43, 0) * Vector3.forward * (3 + i % 4 * 3);
                if (!NavMesh.SamplePosition(position + offset, out var sample, 2, m_Filter)) continue;
                Vector3 candidate = sample.position;
                if (!WorldBlocked(threat + Vector3.up * 1.15f, candidate + Vector3.up * 1.15f)) continue;
                bool occupied = false;
                foreach (var reservation in m_CoverReservations)
                    if (reservation.Key != brain.Seed && reservation.Value.expiry > now && HorizontalDistance(candidate, reservation.Value.position) < 2)
                    { occupied = true; break; }
                if (occupied || !CompletePath(brain, position, candidate, out float length)) continue;
                Vector3 toward = threat - candidate; toward.y = 0; toward.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, toward) * 1.6f;
                Vector3 candidatePeek = candidate;
                bool canPeek = false;
                for (int sign = -1; sign <= 1; sign += 2)
                    if (NavMesh.SamplePosition(candidate + side * sign, out var edge, 1, m_Filter) &&
                        HorizontalDistance(candidate, edge.position) > .8f &&
                        !WorldBlocked(edge.position + Vector3.up * 1.3f, threat + Vector3.up * 1.15f) &&
                        CompletePath(brain, candidate, edge.position, out _))
                    { candidatePeek = edge.position; canPeek = true; break; }
                float distance = HorizontalDistance(candidate, threat);
                float value = 50 - length * 1.5f + (canPeek ? 14 : 0) - Mathf.Abs(distance - m_Tuning.PreferredRange) * .4f;
                if (brain.Action == PmcAction.Retreat) value += distance * 1.5f;
                int corners = brain.ProbePath.GetCornersNonAlloc(brain.ProbeCorners);
                for (int c = 1; c < corners; c++)
                    if (!WorldBlocked(threat + Vector3.up, brain.ProbeCorners[c] + Vector3.up)) value -= 2;
                if (value <= best) continue;
                best = value; found = true; cover = candidate; peek = candidatePeek;
            }
            brain.HasCover = found;
            brain.CoverPosition = cover;
            brain.PeekPosition = peek;
            if (found) m_CoverReservations[brain.Seed] = (cover, now + 4);
            else m_CoverReservations.Remove(brain.Seed);
        }
    }
}
