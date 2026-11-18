// 给悬赏机器人的小队目标安排路线和分工，比如接近目标、响应求救、守位或撤离。
// 走到合适位置后通过对局的正式入口提交动作，由规则验证能否完成。
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS {
    public partial class DollSingerEnemySystem {
        private sealed class ClashSquadPlan {
            public int Exit = -1;
            public double NextExitChoice, BlockedSince = -1;
        }

        private readonly Dictionary<int, ClashSquadPlan> m_ClashSquads = new(3);

        // 任务依赖对局本轮有效的机器人信息。注册表取不到时不继续提交旧动作。
        private bool TryClashView(DollSingerEnemyBrain brain, out ClashBotView view) {
            view = null;
            return brain.IsClashBot && m_ClashRegistry != null && brain.ClashEpoch == m_ClashRegistry.Epoch &&
                   m_ClashRegistry.TryGetView(brain.ClashParticipantId, out view) && view.Epoch == brain.ClashEpoch;
        }

        // 根据队友、对局阶段和当前优先目标选择任务。队友位置是允许共享的，敌人的即时位置仍由感知提供。
        private void PlanClashMission(
            DollSingerEnemyBrain brain,
            Vector3 position,
            PredictedPlayerGhost health,
            double now) {
            if (now < brain.NextMission || !TryClashView(brain, out var view)) return;
            brain.NextMission = now + .6;
            brain.RescueTargetId = -1;
            brain.MissionUrgent = false;
            bool runner = true;
            foreach (var ally in m_ClashAgents)
                if (ally.View.TeamId == brain.ClashTeamId && ally.View.Life == ClashLife.Alive &&
                    ally.Brain.ClashParticipantId < brain.ClashParticipantId && ally.Brain.PathFailures < 3)
                    runner = false;
            // 队友位置可以作为小队信息，这里不读对手位置。
            bool canRescue = SafeToTreat(brain, position, now) && RaidHealth.DangerFraction(health) > .25f &&
                             view.Deadline - view.ServerNow > 8 &&
                             (!view.IsCarrier || view.Deadline - view.ServerNow > 45);
            float rescueDistance = 35;
            if (canRescue)
                foreach (var ally in view.Teammates) {
                    if (ally.ParticipantId == brain.ClashParticipantId || ally.Life != ClashLife.Downed) continue;
                    float distance = Vector3.Distance(position, ally.Position);
                    if (distance >= rescueDistance ||
                        !TryClashApproach(brain, position, ally.Position, 1.4f, now, out var access))
                        continue;
                    rescueDistance = distance;
                    brain.RescueTargetId = ally.ParticipantId;
                    brain.Mission = ClashBotGoal.Rescue;
                    brain.MissionGoal = access;
                    brain.MissionLookPoint = (Vector3)ally.Position + Vector3.up * .5f;
                }
            if (brain.RescueTargetId >= 0) {
                brain.MissionUrgent = true;
                return;
            }
            bool evacuate = view.IsCarrier || view.TeamHasBounty || view.Bounty == ClashBountyState.Extracted ||
                            view.Deadline - view.ServerNow < 55;
            brain.Leaving = evacuate;
            if (evacuate) {
                brain.Mission = view.TeamHasBounty && !view.IsCarrier ? ClashBotGoal.Escort : ClashBotGoal.Extract;
                brain.MissionGoal = SelectClashExit(brain, position, view, now);
                if (brain.Mission == ClashBotGoal.Escort)
                    foreach (var ally in m_ClashAgents)
                        if (ally.View.TeamId == view.TeamId && ally.View.IsCarrier &&
                            Vector3.Distance(position, ally.Position) > 9 &&
                            TryClashApproach(brain, position, ally.Position, 4, now, out var escort))
                            brain.MissionGoal = escort;
                brain.MissionLookPoint = brain.Target != Entity.Null
                    ? brain.LastSeen + brain.AimOffset
                    : position + (position - brain.MissionGoal).normalized * 10 + Vector3.up;
                return;
            }
            var objective = view.Bounty == ClashBountyState.Ground || view.Bounty == ClashBountyState.Carried
                ? (Vector3)view.BountyPosition
                : (Vector3)view.Target;
            if (!InsideClashArena(objective)) objective = view.Target;
            brain.Mission = view.Bounty == ClashBountyState.Ground && runner
                ? ClashBotGoal.GroundBounty
                : ClashBotGoal.Target;
            float distanceToObjective = HorizontalDistance(position, objective);
            float radius = distanceToObjective > 22 ? 14 :
                runner && view.Bounty != ClashBountyState.Banishing ? 1.4f : 6;
            if (view.Bounty == ClashBountyState.Carried)
                radius = 9; // 这是公开的粗略线索，无法定位隐蔽的持有者。
            if (!TryClashApproach(brain, position, objective, radius, now, out var goal)) {
                brain.Mission = ClashBotGoal.Hold;
                goal = RecoverClashRoute(brain, position, now);
            }
            brain.MissionGoal = goal;
            // 后卫看另一个扇区，别和前面队友盯同一个方向。
            var outward = goal - objective;
            outward.y = 0;
            brain.MissionLookPoint = brain.Target != Entity.Null
                ? brain.LastSeen + brain.AimOffset
                : goal + (outward.sqrMagnitude > .1f ? outward.normalized : Vector3.forward) * 10 + Vector3.up * 1.15f;
            if (!runner && distanceToObjective < 20 && view.Bounty != ClashBountyState.Carried)
                brain.Mission = ClashBotGoal.Hold;
        }

        // 围着任务锚点取多个可走的接近位置，还要避开另一队友已占用的整段通道。
        private bool TryClashApproach(
            DollSingerEnemyBrain brain,
            Vector3 position,
            Vector3 anchor,
            float radius,
            double now,
            out Vector3 goal) {
            goal = position;
            var entry = BountyClashArenaSettings.Position(m_ClashArena.Spawns[brain.ClashTeamId * 3]) - anchor;
            entry.y = 0;
            if (entry.sqrMagnitude < .1f) entry = Vector3.forward;
            entry.Normalize();
            float roleAngle = brain.ClashSlot == 0 ? -32 : 32;
            float best = float.MaxValue;
            for (int i = 0; i < 8; i++) {
                float angle = roleAngle + (i == 0 ? 0 : (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 35);
                var desired = anchor + Quaternion.Euler(0, angle, 0) * entry * radius;
                if (!NavMesh.SamplePosition(desired, out var sample, 2, m_Filter) ||
                    Mathf.Abs(sample.position.y - anchor.y) > 1.3f || !InsideClashArena(sample.position) ||
                    (radius <= 1.5f && (Vector3.Distance(sample.position, anchor) > 2.4f ||
                                        WorldBlocked(sample.position + Vector3.up * 1.6f,
                                            anchor + Vector3.up * .4f))) ||
                    (brain.FailedGoalUntil > now && HorizontalDistance(sample.position, brain.FailedGoal) < 2) ||
                    !CompletePath(brain, position, sample.position, out float length))
                    continue;
                float score = length + i * 2;
                if (brain.Target != Entity.Null) score += PathExposure(brain, brain.LastSeen) * .7f;
                // 要避开队友已经占用的通道，不能只分配不同终点。
                foreach (var ally in m_ClashAgents)
                    if (ally.Brain != brain && ally.View.TeamId == brain.ClashTeamId &&
                        ally.View.Life == ClashLife.Alive &&
                        HorizontalDistance(sample.position, ally.Brain.MissionGoal) < 3)
                        score += 20;
                if (score >= best) continue;
                best = score;
                goal = sample.position;
            }
            return best < float.MaxValue;
        }

        // 优先沿当前小队已经选好的出口协同撤离。环境变化或路不通时再重新选。
        private Vector3 SelectClashExit(DollSingerEnemyBrain brain, Vector3 position, ClashBotView view, double now) {
            if (!m_ClashSquads.TryGetValue(brain.ClashTeamId, out var squad)) {
                squad = new ClashSquadPlan();
                m_ClashSquads.Add(brain.ClashTeamId, squad);
            }
            var selectedBlock = squad.Exit == 0 ? view.ExitBlockA : view.ExitBlockB;
            if (squad.Exit >= 0 && selectedBlock != ClashExitBlock.None) {
                if (squad.BlockedSince < 0) squad.BlockedSince = now;
            } else { squad.BlockedSince = -1; }
            if (now >= squad.NextExitChoice || brain.PathFailures >= 2 ||
                (squad.BlockedSince >= 0 && now - squad.BlockedSince > 3)) {
                squad.NextExitChoice = now + 5;
                float best = float.MaxValue;
                int selected = -1;
                for (int exit = 0; exit < 2; exit++) {
                    var point = exit == 0 ? (Vector3)view.ExitA : (Vector3)view.ExitB;
                    if (!NavMesh.SamplePosition(point, out var sample, 2, m_Filter) ||
                        Mathf.Abs(sample.position.y - point.y) > 1.2f ||
                        !CompletePath(brain, position, sample.position, out float length))
                        continue;
                    var block = exit == 0 ? view.ExitBlockA : view.ExitBlockB;
                    float score = length + (block == ClashExitBlock.None ? 0 : 1000);
                    if (brain.Target != Entity.Null) score += PathExposure(brain, brain.LastSeen);
                    if (exit == brain.SelectedExit && brain.PathFailures >= 2) score += 500;
                    if (exit == squad.Exit && block == ClashExitBlock.None) score -= 15;
                    if (score >= best) continue;
                    selected = exit;
                    best = score;
                }
                squad.Exit = selected;
            }
            brain.SelectedExit = squad.Exit;
            if (squad.Exit < 0) {
                brain.Reason = "both exit routes unavailable; bounded local recovery";
                return RecoverClashRoute(brain, position, now);
            }
            var anchor = squad.Exit == 0 ? (Vector3)view.ExitA : (Vector3)view.ExitB;
            return TryClashApproach(brain, position, anchor, 2, now, out var goal)
                ? goal
                : RecoverClashRoute(brain, position, now);
        }

        private Vector3 RecoverClashRoute(DollSingerEnemyBrain brain, Vector3 position, double now) {
            if (brain.RecoveryAttempts >= 3) {
                if (now < brain.FailedGoalUntil) return position;
                brain.RecoveryAttempts = 0;
            }
            brain.RecoveryAttempts++;
            var away = brain.Target != Entity.Null
                ? position - brain.LastSeen
                : BountyClashArenaSettings.Position(m_ClashArena.Center) - position;
            away.y = 0;
            if (away.sqrMagnitude < .1f) away = Vector3.forward;
            for (int i = 0; i < 5; i++) {
                var desired = position + Quaternion.Euler(0, (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 45, 0) *
                    away.normalized * 3;
                if (NavMesh.SamplePosition(desired, out var sample, 1.5f, m_Filter) &&
                    Mathf.Abs(sample.position.y - position.y) < .8f && InsideClashArena(sample.position) &&
                    RaidSpawnSafety.TryGroundPosition(sample.position, .35f, 1.8f, 45, out _, out _) &&
                    CompletePath(brain, position, sample.position, out float length) && length < 8)
                    return sample.position;
            }
            return position;
        }

        // 站位、阶段、当前身体可操作等条件都通过后，才发送按下交互。离目标太远会继续调整路线。
        private void AdvanceClashObjective(DollSingerEnemyBrain brain, Vector3 position, Vector3 eye, double now) {
            brain.Interacting = false;
            if (!TryClashView(brain, out var view) || view.Phase != ClashPhase.Active || view.Life != ClashLife.Alive)
                return;
            bool rescue = brain.Action == PmcAction.ClashObjective && brain.Mission == ClashBotGoal.Rescue &&
                          brain.RescueTargetId >= 0 && SafeToTreat(brain, position, now);
            var point = view.Bounty == ClashBountyState.Ground ? (Vector3)view.BountyPosition : (Vector3)view.Target;
            if (rescue)
                foreach (var ally in view.Teammates)
                    if (ally.ParticipantId == brain.RescueTargetId)
                        point = ally.Position;
            // 移动马达可能在 1.4 米接近点前 .75 米就停下。
            // 在服务器 3 米限制内保留余量，避免救援 AI 停在距目标 2.15 米却不能操作。
            bool accessible = Vector3.Distance(position, point) <= 2.6f && Mathf.Abs(position.y - point.y) < 1.5f &&
                              !WorldBlocked(eye, point + Vector3.up * .4f);
            if (rescue && accessible) {
                brain.Interacting = true;
                SubmitClashIntent(brain,
                    view.ReviveTarget == brain.RescueTargetId ? ClashIntent.ReviveRenew : ClashIntent.ReviveBegin,
                    brain.RescueTargetId, now);
                return;
            }
            if (view.ReviveTarget >= 0) SubmitClashIntent(brain, ClashIntent.ReviveCancel, view.ReviveTarget, now);
            if (brain.Action != PmcAction.ClashObjective || brain.HealUntil > 0 || !accessible) return;
            if (view.Bounty == ClashBountyState.Unbanished && brain.Mission == ClashBotGoal.Target)
                SubmitClashIntent(brain, ClashIntent.Banish, -1, now);
            else if (view.Bounty == ClashBountyState.Ground && brain.Mission == ClashBotGoal.GroundBounty)
                SubmitClashIntent(brain, ClashIntent.Pickup, -1, now);
        }

        // 机器人能表达想操作什么，但目标是否真的完成由同一对局规则负责。
        private void SubmitClashIntent(DollSingerEnemyBrain brain, ClashIntent intent, int target, double now) {
            if (now < brain.NextIntent || m_ClashRegistry == null) return;
            brain.NextIntent = now + .2;
            brain.IntentSequence++;
            if (brain.IntentSequence == 0) brain.IntentSequence = 1;
            m_ClashRegistry.Submit(new ClashBotIntent {
                ParticipantId = brain.ClashParticipantId, Epoch = brain.ClashEpoch,
                Sequence = brain.IntentSequence, Intent = intent, TargetParticipantId = target
            });
        }
    }
}