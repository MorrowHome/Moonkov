using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private const float TacticalArrivalDistance = .75f;
        private void Decide(DollSingerEnemyBrain brain, Vector3 position, PredictedPlayerGhost health, MoonRaidMap map, double now)
        {
            if (brain.IsClashBot) PlanClashMission(brain, position, health, now);
            bool contact = brain.Target != Unity.Entities.Entity.Null && brain.Confidence > .2f;
            EvaluateDecision(brain, position, health, map, now, out PmcAction best,
                out float score, out string reason, out bool emergency);
            bool changed = best != brain.Action && (now >= brain.CommitUntil || emergency || brain.PathFailed);
            if (changed)
            {
                brain.Action = best;
                brain.ActionStarted = now;
                brain.CommitUntil = now + m_Tuning.CommitmentSeconds;
                brain.SearchStep = 0;
                brain.SearchPauseUntil = 0;
                brain.SearchFocusValid = false;
                brain.IsPeeking = false;
                brain.NextCover = 0;
                brain.NextPath = 0;
                brain.LootUntil = 0;
                brain.CacheObstructedSince = -1;
                if (best == PmcAction.Flank) brain.NextFlank = now + 12;
                if (best != PmcAction.Cover && best != PmcAction.Reload && best != PmcAction.Retreat && best != PmcAction.Heal)
                { brain.HasCover = false; m_CoverReservations.Remove(brain.Seed); }
                else if (brain.HasCover)
                    SetCoverPhase(brain, PmcCoverPhase.Returning, now + 3);
            }
            if (best == brain.Action) { brain.ActionScore = score; brain.Reason = reason; }
            Vector3 goal = brain.TacticalGoal;
            switch (brain.Action)
            {
                case PmcAction.ClashObjective:
                    goal = brain.MissionGoal;
                    break;
                case PmcAction.Scavenge:
                    goal = SelectCache(brain, position, map);
                    break;
                case PmcAction.Extract:
                    goal = brain.RaidExit;
                    break;
                case PmcAction.HoldAngle:
                    goal = position;
                    break;
                case PmcAction.Investigate:
                    if (brain.Target == Unity.Entities.Entity.Null && now < brain.MissionAlarmUntil)
                    {
                        brain.LastSeen = brain.MissionAlarmPosition;
                        brain.LastHeardTime = brain.MissionAlarmHeardAt;
                        brain.ContactUncertainty = 2;
                        brain.ObservedVelocity = Vector3.zero;
                    }
                    goal = UpdateSearch(brain, position, now);
                    break;
                case PmcAction.Search:
                    goal = UpdateSearch(brain, position, now);
                    break;
                case PmcAction.Cover:
                case PmcAction.Reload:
                case PmcAction.Retreat:
                case PmcAction.Heal:
                    if (contact && now >= brain.NextCover)
                    {
                        // Keep a useful position. A rescan validates cover; it must not
                        // pull the actor to a new rock in the middle of every peek.
                        bool compromised = brain.HasCover && !CoverOccludes(brain.CoverPosition, brain.LastSeen);
                        if (!brain.HasCover || brain.PathFailed || compromised)
                        {
                            if (compromised) AvoidCurrentCover(brain, now);
                            ChooseCover(brain, position, brain.LastSeen, now);
                        }
                        brain.NextCover = now + m_Tuning.CoverRescanSeconds;
                    }
                    goal = brain.HasCover ? (brain.IsPeeking ? brain.PeekPosition : brain.CoverPosition) : position;
                    if (brain.Action == PmcAction.Retreat && contact && !brain.HasCover)
                        goal = ChooseWithdrawal(brain, position);
                    break;
                case PmcAction.Flank:
                    if (changed || brain.PathFailed)
                        goal = ChooseFlank(brain, position);
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
            if (brain.IsClashBot && brain.Mission == ClashBotGoal.Escort && brain.Action == PmcAction.Engage &&
                HorizontalDistance(position, brain.MissionGoal) > 9) goal = brain.MissionGoal;
            if (brain.IsClashBot && !InsideClashArena(goal)) goal = RecoverClashRoute(brain, position, now);
            brain.TacticalGoal = goal;
        }

        private static void SetCoverPhase(DollSingerEnemyBrain brain, PmcCoverPhase phase, double until)
        {
            brain.CoverPhase = phase;
            brain.CoverPhaseUntil = until;
            brain.IsPeeking = phase == PmcCoverPhase.Advancing || phase == PmcCoverPhase.Holding;
            brain.NextPath = 0;
        }

        private static void AvoidCurrentCover(DollSingerEnemyBrain brain, double now)
        {
            brain.AvoidCoverPosition = brain.CoverPosition;
            brain.AvoidCoverUntil = now + 12;
        }

        // Cheap arrival/interrupt checks run with the motor. Geometry queries stay
        // in the staggered decision pass, and exposure has a hard deadline.
        private void AdvanceCover(DollSingerEnemyBrain brain, Vector3 position, double now)
        {
            bool usesCover = brain.Action == PmcAction.Cover || brain.Action == PmcAction.Reload ||
                brain.Action == PmcAction.Retreat || brain.Action == PmcAction.Heal;
            if (!usesCover || !brain.HasCover) { brain.IsPeeking = false; return; }
            bool canFight = brain.Action == PmcAction.Cover;
            bool exposed = brain.CoverPhase == PmcCoverPhase.Advancing || brain.CoverPhase == PmcCoverPhase.Holding;
            if (exposed && (!canFight || brain.Suppression > .65f || now - brain.LastDamage < .8 ||
                now >= brain.CoverExposureUntil || brain.PathFailed))
            {
                if (brain.Suppression > .65f || now - brain.LastDamage < .8 || brain.PathFailed ||
                    brain.CoverPhase == PmcCoverPhase.Advancing && now >= brain.CoverExposureUntil)
                    AvoidCurrentCover(brain, now);
                SetCoverPhase(brain, PmcCoverPhase.Returning, now + 3);
                brain.BurstUntil = now;
            }
            float toCover = HorizontalDistance(position, brain.CoverPosition);
            switch (brain.CoverPhase)
            {
                case PmcCoverPhase.Approaching:
                case PmcCoverPhase.Returning:
                    if (toCover <= TacticalArrivalDistance && Mathf.Abs(position.y - brain.CoverPosition.y) < .6f)
                    {
                        bool relocate = canFight && (brain.CoverPeeks >= 2 ||
                            brain.AvoidCoverUntil > now && HorizontalDistance(brain.CoverPosition, brain.AvoidCoverPosition) < 2);
                        if (relocate)
                        {
                            AvoidCurrentCover(brain, now);
                            brain.HasCover = brain.IsPeeking = false;
                            brain.TacticalGoal = position;
                            brain.NextCover = brain.NextDecision = 0;
                            m_CoverReservations.Remove(brain.Seed);
                            return;
                        }
                        SetCoverPhase(brain, PmcCoverPhase.Settling,
                            now + brain.Random.NextFloat(.8f, 1.6f) + brain.Caution * .6f);
                    }
                    else if (now >= brain.CoverPhaseUntil)
                    {
                        brain.HasCover = brain.IsPeeking = false;
                        brain.NextCover = brain.NextDecision = 0;
                        brain.TacticalGoal = position;
                        m_CoverReservations.Remove(brain.Seed);
                        return;
                    }
                    break;
                case PmcCoverPhase.Settling:
                    if (canFight && now >= brain.CoverPhaseUntil && brain.Suppression < .4f &&
                        now - brain.LastDamage > 1.2 && HorizontalDistance(brain.CoverPosition, brain.PeekPosition) > .8f)
                    {
                        brain.CoverExposureUntil = now + 2.5;
                        SetCoverPhase(brain, PmcCoverPhase.Advancing, brain.CoverExposureUntil);
                    }
                    break;
                case PmcCoverPhase.Advancing:
                    if (HorizontalDistance(position, brain.PeekPosition) <= TacticalArrivalDistance && Mathf.Abs(position.y - brain.PeekPosition.y) < .6f)
                        SetCoverPhase(brain, PmcCoverPhase.Holding, now + brain.Random.NextFloat(.55f, 1.05f));
                    break;
                case PmcCoverPhase.Holding:
                    if (now >= brain.CoverPhaseUntil)
                    {
                        brain.CoverPeeks++;
                        SetCoverPhase(brain, PmcCoverPhase.Returning, now + 3);
                    }
                    break;
            }
            brain.IsPeeking = canFight && (brain.CoverPhase == PmcCoverPhase.Advancing || brain.CoverPhase == PmcCoverPhase.Holding);
            brain.TacticalGoal = brain.IsPeeking ? brain.PeekPosition : brain.CoverPosition;
            m_CoverReservations[brain.Seed] = (brain.CoverPosition, now + 4);
        }

        private Vector3 UpdateSearch(DollSingerEnemyBrain brain, Vector3 position, double now)
        {
            double observation = System.Math.Max(brain.LastSeenTime, brain.LastHeardTime);
            bool freshClue = observation > brain.SearchObservationTime && HorizontalDistance(brain.LastSeen, brain.SearchAnchor) > 2;
            if (!brain.SearchFocusValid || freshClue)
            {
                brain.SearchFocusValid = true;
                brain.SearchObservationTime = observation;
                brain.SearchAnchor = brain.LastSeen;
                Vector3 heading = brain.ObservedVelocity;
                heading.y = 0;
                if (heading.sqrMagnitude < .5f) heading = brain.LastSeen - position;
                heading.y = 0;
                brain.SearchHeading = heading.sqrMagnitude > .01f ? heading.normalized : Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward;
                brain.SearchStep = 0;
                brain.SearchPauseUntil = 0;
                return ChooseSearchPoint(brain, position, now);
            }
            Vector3 goal = brain.TacticalGoal;
            if (brain.SearchPauseUntil > 0)
            {
                if (now < brain.SearchPauseUntil) return goal;
                brain.SearchPauseUntil = 0;
                brain.SearchStep++;
                int budget = brain.Action == PmcAction.Investigate ? 3 : 6;
                if (brain.SearchStep >= budget)
                {
                    // Exhaust the remembered hypothesis instead of orbiting it forever.
                    if (brain.Contacts.TryGetValue(brain.Target, out var memory))
                    { memory.Confidence = 0; memory.Suspicion = 0; }
                    if (brain.Target == Unity.Entities.Entity.Null) brain.MissionAlarmUntil = 0;
                    brain.Target = Unity.Entities.Entity.Null;
                    brain.Confidence = 0;
                    brain.SearchFocusValid = false;
                    brain.NextDecision = 0;
                    brain.CommitUntil = 0;
                    return position;
                }
                return ChooseSearchPoint(brain, position, now);
            }
            if (Vector3.Distance(position, goal) < 1 || now >= brain.SearchUntil || brain.PathFailed)
            {
                // Stop, check an exit, then advance. No constant spinning or blind
                // three-second goal churn while still travelling to the first clue.
                brain.SearchPauseUntil = now + brain.Random.NextFloat(.65f, 1.2f);
                Vector3 sector = Quaternion.Euler(0, brain.SearchStep % 2 == 0 ? 35 : -35, 0) * brain.SearchHeading;
                brain.SearchLookPoint = position + sector * 8 + Vector3.up * 1.15f;
                return position;
            }
            return goal;
        }

        private Vector3 ChooseSearchPoint(DollSingerEnemyBrain brain, Vector3 position, double now)
        {
            Vector3 bestPoint = position;
            float bestScore = float.MinValue, travel = 0;
            float radius = Mathf.Clamp(brain.Uncertainty(now), 3, 11);
            float desiredAngle = brain.SearchStep == 0 ? 180 :
                brain.SearchStep == 1 ? 0 : (brain.SearchStep % 2 == 0 ? 1 : -1) * (35 + brain.SearchStep * 14);
            for (int i = 0; i < 9; i++)
            {
                float angle = desiredAngle + (i == 0 ? 0 : (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 22);
                float offset = brain.SearchStep == 0 ? 2 : radius;
                Vector3 candidate = brain.SearchAnchor + Quaternion.Euler(0, angle, 0) * brain.SearchHeading * offset;
                if (!NavMesh.SamplePosition(candidate, out var sample, 2, m_Filter) ||
                    HorizontalDistance(position, sample.position) < 1.2f ||
                    !CompletePath(brain, position, sample.position, out float length)) continue;
                bool machineClue = brain.Action == PmcAction.Investigate && brain.Target == Unity.Entities.Entity.Null && now < brain.MissionAlarmUntil;
                if (length > (machineClue ? 55 : 30)) continue;
                float value = 15 - i * 1.5f - length * .35f;
                // Search exits with a useful view of the clue, and prefer a sheltered approach.
                if (!WorldBlocked(sample.position + Vector3.up * 1.4f, brain.SearchAnchor + Vector3.up * 1.15f)) value += 5;
                value -= PathExposure(brain, brain.SearchAnchor) * .3f;
                if (value <= bestScore) continue;
                bestScore = value; bestPoint = sample.position; travel = length;
            }
            bool investigatingMachine = brain.Action == PmcAction.Investigate && brain.Target == Unity.Entities.Entity.Null && now < brain.MissionAlarmUntil;
            brain.SearchUntil = now + Mathf.Clamp(travel / 2 + 2, 3, investigatingMachine ? 26 : 14);
            brain.SearchLookPoint = brain.SearchAnchor + brain.SearchHeading * radius + Vector3.up * 1.15f;
            brain.NextPath = 0;
            return bestPoint;
        }

        private Vector3 ChooseFlank(DollSingerEnemyBrain brain, Vector3 position)
        {
            Vector3 away = position - brain.LastSeen; away.y = 0;
            float distance = away.magnitude;
            if (distance < .1f) return position;
            away /= distance;
            float best = float.MinValue;
            Vector3 goal = position;
            int preferredSide = (brain.IsClashBot ? brain.ClashSlot : brain.Seed) % 2 == 0 ? 1 : -1;
            for (int i = 0; i < 6; i++)
            {
                int side = i % 2 == 0 ? preferredSide : -preferredSide;
                Vector3 offset = Vector3.Cross(Vector3.up, away) * side * (7 + i / 2 * 3) + away * 2;
                if (!NavMesh.SamplePosition(position + offset, out var sample, 2, m_Filter) ||
                    HorizontalDistance(sample.position, brain.LastSeen) < Mathf.Max(5, distance * .65f) ||
                    !CompletePath(brain, position, sample.position, out float length) || length > 26) continue;
                float score = 20 - length * .5f - PathExposure(brain, brain.LastSeen) * .8f;
                if (!WorldBlocked(sample.position + Vector3.up * 1.4f, brain.LastSeen + brain.AimOffset)) score += 5;
                if (score <= best) continue;
                best = score; goal = sample.position;
            }
            return goal;
        }

        private static bool CoverOccludes(Vector3 cover, Vector3 threat) =>
            WorldBlocked(threat + Vector3.up * 1.5f, cover + Vector3.up * 1.15f) &&
            WorldBlocked(threat + Vector3.up * 1.5f, cover + Vector3.up * 1.55f);

        private static float PathExposure(DollSingerEnemyBrain brain, Vector3 threat)
        {
            float exposure = 0;
            int corners = brain.ProbePath.GetCornersNonAlloc(brain.ProbeCorners);
            for (int i = 1; i < corners; i++)
            {
                Vector3 from = brain.ProbeCorners[i - 1], to = brain.ProbeCorners[i];
                float length = Vector3.Distance(from, to);
                if (!WorldBlocked(threat + Vector3.up * 1.5f, (from + to) * .5f + Vector3.up * 1.15f)) exposure += length * .5f;
                if (!WorldBlocked(threat + Vector3.up * 1.5f, to + Vector3.up * 1.15f)) exposure += length * .5f;
            }
            return exposure;
        }

        private Vector3 ReachableOffset(DollSingerEnemyBrain brain, Vector3 position, Vector3 offset)
        {
            if (NavMesh.SamplePosition(position + offset, out var end, 3, m_Filter) &&
                CompletePath(brain, position, end.position, out _)) return end.position;
            if (NavMesh.SamplePosition(position - offset * .5f, out end, 3, m_Filter) &&
                CompletePath(brain, position, end.position, out _)) return end.position;
            return position;
        }

        private Vector3 ChooseWithdrawal(DollSingerEnemyBrain brain, Vector3 position)
        {
            Vector3 away = position - brain.LastSeen;
            away.y = 0;
            float currentDistance = away.magnitude;
            if (currentDistance < .1f) away = -(Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward);
            else away /= currentDistance;
            float best = float.MinValue;
            Vector3 goal = position;
            // A blocked escape must never reverse its offset and send a wounded
            // raider toward the opponent. Compare rear and lateral routes instead.
            for (int i = 0; i < 5; i++)
            {
                float angle = i == 0 ? 0 : (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 40;
                Vector3 desired = position + Quaternion.Euler(0, angle, 0) * away * 10;
                if (!NavMesh.SamplePosition(desired, out var sample, 2, m_Filter) ||
                    HorizontalDistance(sample.position, brain.LastSeen) <= currentDistance ||
                    !CompletePath(brain, position, sample.position, out float length) || length > 24) continue;
                float value = HorizontalDistance(sample.position, brain.LastSeen) - length * .4f -
                    PathExposure(brain, brain.LastSeen) * (1 + brain.Caution);
                if (value <= best) continue;
                best = value;
                goal = sample.position;
            }
            return goal;
        }

        private bool CompletePath(DollSingerEnemyBrain brain, Vector3 position, Vector3 goal, out float length)
        {
            length = 0;
            if (!NavMesh.SamplePosition(position, out var start, 3, m_Filter) ||
                !NavMesh.CalculatePath(start.position, goal, m_Filter, brain.ProbePath) ||
                brain.ProbePath.status != NavMeshPathStatus.PathComplete) return false;
            int count = brain.ProbePath.GetCornersNonAlloc(brain.ProbeCorners);
            for (int i = 1; i < count; i++) length += Vector3.Distance(brain.ProbeCorners[i - 1], brain.ProbeCorners[i]);
            return count > 0 && count < brain.ProbeCorners.Length &&
                (!brain.IsClashBot || InsideClashArena(goal) && SafeClashRoute(brain.ProbeCorners, count));
        }

        private void ChooseCover(DollSingerEnemyBrain brain, Vector3 position, Vector3 threat, double now)
        {
            float best = float.MinValue;
            bool found = false;
            Vector3 cover = position, peek = position;
            float approachLength = 0;
            // A bounded local geometry search, rather than treating every waypoint as cover.
            for (int i = 0; i < 16; i++)
            {
                Vector3 offset = Quaternion.Euler(0, i * 137.5f + brain.Seed * 43, 0) * Vector3.forward * (3 + i % 4 * 3);
                if (!NavMesh.SamplePosition(position + offset, out var sample, 2, m_Filter)) continue;
                if (brain.IsClashBot && (Mathf.Abs(sample.position.y - position.y) > 1.2f ||
                    brain.FailedGoalUntil > now && HorizontalDistance(sample.position, brain.FailedGoal) < 2)) continue;
                Vector3 candidate = sample.position;
                if (brain.AvoidCoverUntil > now && HorizontalDistance(candidate, brain.AvoidCoverPosition) < 3) continue;
                if (!CoverOccludes(candidate, threat)) continue;
                bool occupied = false;
                foreach (var reservation in m_CoverReservations)
                    if (reservation.Key != brain.Seed && reservation.Value.expiry > now && HorizontalDistance(candidate, reservation.Value.position) < 2)
                    { occupied = true; break; }
                if (occupied || !CompletePath(brain, position, candidate, out float length)) continue;
                // ProbePath is reused for peek validation below. Score the actual
                // approach now, before that second path query overwrites it.
                float exposure = PathExposure(brain, threat);
                Vector3 toward = threat - candidate; toward.y = 0; toward.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, toward) * 1.6f;
                Vector3 candidatePeek = candidate;
                bool canPeek = false;
                int firstSide = brain.Seed % 2 == 0 ? 1 : -1;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int sign = attempt == 0 ? firstSide : -firstSide;
                    if (NavMesh.SamplePosition(candidate + side * sign, out var edge, 1, m_Filter) &&
                        HorizontalDistance(candidate, edge.position) > .8f &&
                        !WorldBlocked(edge.position + Vector3.up * 1.3f, threat + Vector3.up * 1.15f) &&
                        CompletePath(brain, candidate, edge.position, out float peekLength) && peekLength < 3)
                    { candidatePeek = edge.position; canPeek = true; break; }
                }
                float distance = HorizontalDistance(candidate, threat);
                float value = 50 - length * 1.5f - exposure * (1 + brain.Caution) +
                    (canPeek && brain.Action == PmcAction.Cover ? 14 : 0) - Mathf.Abs(distance - m_Tuning.PreferredRange) * .4f;
                if (brain.Action == PmcAction.Retreat) value += distance * 1.5f;
                if (value <= best) continue;
                best = value; found = true; cover = candidate; peek = candidatePeek; approachLength = length;
            }
            brain.HasCover = found;
            brain.CoverPosition = cover;
            brain.PeekPosition = peek;
            brain.CoverThreatPosition = threat;
            brain.CoverPeeks = 0;
            brain.IsPeeking = false;
            if (found)
            {
                SetCoverPhase(brain, PmcCoverPhase.Approaching, now + Mathf.Clamp(approachLength / 2 + 2, 3, 14));
                m_CoverReservations[brain.Seed] = (cover, now + 4);
            }
            else m_CoverReservations.Remove(brain.Seed);
        }
    }
}
