// 把实际飞过的子弹路径变成附近人形 AI 感到的压制和大致来向。子弹必须已掠过身体附近且没有墙挡，才记为擦过。
// AI 可以因此找掩护，感知里保存的是带误差的方向线索。
using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    // 实际飞过的弹道能使附近的 PMC 感到压制。
    // 这不会让角色看穿遮挡或知道发射者的精确位置。
    public static class ProjectileThreat {
        public const float Radius = 1.4f;
        public const double Interval = .2;
        private const int MaxHits = 64;

        public static int Observe(
            PhysicsScene scene,
            World world,
            Entity shooter,
            ClashDamageSource source,
            Vector3 from,
            Vector3 to,
            Collider impact,
            double now,
            int playerMask,
            int worldMask,
            ref Collider[] hits) {
            if (!scene.IsValid() || world == null || !world.IsCreated || !double.IsFinite(now)) return 0;
            var travel = to - from;
            if (!float.IsFinite(travel.x) || !float.IsFinite(travel.y) || !float.IsFinite(travel.z) ||
                travel.sqrMagnitude < .0001f)
                return 0;
            var manager = world.EntityManager;
            if (!manager.Exists(shooter) || !manager.HasComponent<PredictedPlayerGhost>(shooter)) return 0;
            if (hits == null || hits.Length == 0) hits = new Collider[16];
            int count;
            while (true) {
                // 用这段实际弹道和擦过半径去找附近身体。只处理同一场景中的敌对 AI，撞到的那个目标直接走伤害。
                count = scene.OverlapCapsule(from, to, Radius, hits, playerMask, QueryTriggerInteraction.Ignore);
                if (count < hits.Length || hits.Length >= MaxHits) break;
                Array.Resize(ref hits, Math.Min(MaxHits, hits.Length * 2));
            }
            GhostGameObject impactGhost = null;
            if (impact) GhostGameObject.TryFindGhostGameObject(impact.gameObject, out impactGhost);
            var direction = travel.normalized;
            int observed = 0;
            for (int i = 0; i < count; i++) {
                var collider = hits[i];
                if (!collider || !GhostGameObject.TryFindGhostGameObject(collider.gameObject, out var ghost) ||
                    ghost == impactGhost || ghost.World != world)
                    continue;
                var victim = ghost.LinkedEntity;
                if (victim == shooter || !manager.Exists(victim) || !manager.HasComponent<DollSingerEnemy>(victim) ||
                    !manager.HasComponent<DollSingerEnemyBrain>(victim) ||
                    !manager.HasComponent<PredictedPlayerGhost>(victim) ||
                    BountyClashDamage.IsSelf(world, victim, source) ||
                    BountyClashDamage.TeamProtected(manager, victim, source))
                    continue;
                var health = manager.GetComponentData<PredictedPlayerGhost>(victim);
                if (health.CurrentHealth <= 0 || health.ClashEnabled) continue;
                var brain = manager.GetComponentObject<DollSingerEnemyBrain>(victim);
                if (brain == null || now < brain.NextNearMiss) continue;
                var centre = collider.bounds.center;
                float t = Vector3.Dot(centre - from, travel) / travel.sqrMagnitude;
                // 等弹体确实掠过身体才发信号。
                // 否则下一帧本就会打中的一发会被提前当成擦弹。
                // 把身体位置投影到弹道线上，只有已经飞过身体时才算擦过。下一小段要打中的弹体不用提前制造擦弹。
                if (t < 0 || t >= 1) continue;
                var closest = from + travel * t;
                var body = collider.ClosestPoint(closest);
                var probe = closest - direction * .03f;
                var gap = body - probe;
                if (gap.magnitude > Radius + .05f || (gap.sqrMagnitude > .0001f && scene.Raycast(probe, gap.normalized,
                        gap.magnitude, worldMask, QueryTriggerInteraction.Ignore)))
                    continue;
                brain.NextNearMiss = now + Interval;
                // 真实擦过会增加压制并提前触发新决定。搜刮或疗伤的 AI 可以及时转身避险。
                brain.Suppression = Mathf.Clamp01(brain.Suppression + .24f);
                brain.NextDecision = 0;
                if (brain.Action == PmcAction.Scavenge || brain.Action == PmcAction.Heal) brain.CommitUntil = 0;
                if (!brain.Contacts.TryGetValue(shooter, out var contact)) {
                    contact = new PmcContact();
                    brain.Contacts.Add(shooter, contact);
                }
                if (!contact.Visible && now - contact.LastSeen > .3 &&
                    (!contact.HadVisualSample || now - contact.LastSeen > 4)) {
                    // 从来弹很难知道隐蔽射手离多远，只能提供来向线索。
                    // 短连射用同一个误差。如果每发都改变偏移，
                    // 掩体路线会抖，反复观察还可能把随机偏差平均成精确位置。
                    if (now >= contact.HearingBiasUntil) {
                        float angle = brain.Random.NextFloat(0, Mathf.PI * 2);
                        float magnitude = brain.Random.NextFloat(.65f, 1);
                        contact.HearingBias =
                            new Vector3(Mathf.Sin(angle) * magnitude, 0, Mathf.Cos(angle) * magnitude);
                        contact.HearingBiasUntil = now + brain.Random.NextFloat(3, 5);
                    }
                    var bias = contact.HearingBias;
                    bias.y = 0;
                    // 只知道来球方向时，给一处带误差的线索。短时间内共用误差，防止扫几发就推算出射手精确位置。
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