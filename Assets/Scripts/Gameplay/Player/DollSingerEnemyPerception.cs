// 人形机器人的视线、听觉和危险感知。处理照明、视角、遮挡、暴露部位和声音，再更新每个联系对象的记忆与置信度。
// AI 刚才看见过、只听过和现在能看到的目标，在后续决策里意义不同。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public partial class DollSingerEnemySystem {
        public static bool HearMissionAlarm(
            DollSingerEnemyBrain brain,
            string operationId,
            Vector3 position,
            double now) {
            return PmcSoundClue.Hear(brain, operationId, position, now);
        }

        private void Sense(DollSingerEnemyBrain brain, Transform root, Vector3 eye, float range, double now) {
            float elapsed = brain.LastSenseTime < 0 ? .1f : Mathf.Max(.001f, (float)(now - brain.LastSenseTime));
            // 服务器卡顿期间记忆照常变旧，但这段时间不算持续看见对方。
            // 记忆衰减按真正经过的时间。确认目标只累加一小段，服务器卡顿不等于持续盯了一整秒。
            float observationStep = Mathf.Min(.2f, elapsed);
            brain.LastSenseTime = now;
            ObserveGrenades(brain, root, eye, now);
            ObserveHazardArea(brain, root, now);
            float light = 1;
            if (SystemAPI.TryGetSingleton<ExpeditionClockState>(out var clock) && clock.Ready) {
                double hour =
                    ExpeditionClock.HourOfDay(ExpeditionClock.HoursAt(clock,
                        SystemAPI.GetSingleton<NetworkTime>().ServerTick));
                light = Mathf.Lerp(m_Tuning.NightSightMultiplier, 1,
                    Mathf.Clamp01(Mathf.Sin(((float)hour - 6) * Mathf.PI / 12) * 2));
            }
            float memorySeconds = ContactMemorySeconds(brain);
            float glareSight = HaloLightGlareRules.SightScale(brain.LightGlare);
            bool blinded = ExpeditionFlashBlinded(brain, now);
            var facing = Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward;
            // 每轮先把上一轮的立即可见去掉，随着时间衰减可疑度和信心。下面重新观测到才恢复。
            foreach (var contact in brain.Contacts.Values) {
                contact.Visible = false;
                contact.Suspicion = Mathf.Max(0, contact.Suspicion - elapsed * .28f);
                contact.Confidence = Mathf.Max(0, contact.Confidence - elapsed / Mathf.Max(1, memorySeconds));
            }
            foreach (var candidate in m_Targets) {
                if (blinded || candidate.Root == null || candidate.Root == root ||
                    !CanObserveOpponent(brain, candidate.Entity) || !EntityManager.Exists(candidate.Entity) ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(candidate.Entity) ||
                    EntityManager.GetComponentData<PredictedPlayerGhost>(candidate.Entity).CurrentHealth <= 0)
                    continue;
                var offset = candidate.Position + candidate.AimOffset - eye;
                float distance = offset.magnitude;
                float angle = Vector3.Angle(facing, offset);
                float cone = now - brain.LastDamage < 2 ? 95 : m_Tuning.HalfViewAngle;
                // 普通远征对真人设置单独视角和识别策略，其他对象则仍按常规距离和光照处理。
                bool recognizePlayer = brain.IsExpeditionRaider && candidate.PlayerControlled;
                float sightRange = recognizePlayer
                    ? Mathf.Max(range, m_Tuning.ExpeditionPlayerSightRange) * Mathf.Max(.9f, light)
                    : range * light;
                if (recognizePlayer) cone = Mathf.Max(cone, m_Tuning.ExpeditionPlayerHalfViewAngle);
                if (distance > sightRange * glareSight || angle > cone) continue;
                // 角色可只露头、露身体或露下身。把露在哪作为候选瞄准点，而不是固定朝中心穿过掩体。
                int exposed = 0;
                var visibleAim = candidate.AimOffset;
                if (candidate.Alien || recognizePlayer) {
                    if (ClearSight(root, eye, candidate.Position + candidate.AimOffset, candidate.Entity)) {
                        exposed = 3;
                    } else if (recognizePlayer) {
                        if (ClearSight(root, eye, candidate.Position + candidate.HeadOffset, candidate.Entity)) {
                            exposed = 1;
                            visibleAim = candidate.HeadOffset;
                        } else if (ClearSight(root, eye, candidate.Position + candidate.HipOffset, candidate.Entity)) {
                            exposed = 1;
                            visibleAim = candidate.HipOffset;
                        }
                    }
                } else {
                    bool head = ClearSight(root, eye, candidate.Position + candidate.HeadOffset, candidate.Entity);
                    bool center = ClearSight(root, eye, candidate.Position + candidate.AimOffset, candidate.Entity);
                    bool hip = ClearSight(root, eye, candidate.Position + candidate.HipOffset, candidate.Entity);
                    exposed = (head ? 1 : 0) + (center ? 1 : 0) + (hip ? 1 : 0);
                    if (brain.IsExpeditionRaider && !center)
                        visibleAim = head ? candidate.HeadOffset : candidate.HipOffset;
                }
                if (exposed == 0) continue;
                if (!brain.Contacts.TryGetValue(candidate.Entity, out var contact)) {
                    contact = new PmcContact();
                    brain.Contacts.Add(candidate.Entity, contact);
                }
                float visualGap = (float)(now - contact.LastSeen);
                bool continuous = contact.HadVisualSample && visualGap > 0 && visualGap <= .25f &&
                                  visualGap <= elapsed * 1.5f;
                // 提前量来自同一个观察者的连续两次目击。视线断开后，估计的速度回到零。
                // 用当前角色自己连续看到的结果估算目标移动。
                // 共享目标资料隔着墙也会变化，不能当成这个观察者知道的证据。
                var observedVelocity = continuous
                    ? Vector3.ClampMagnitude((candidate.Position - contact.LastVisualPosition) / visualGap, 8)
                    : Vector3.zero;
                contact.Velocity = continuous ? Vector3.Lerp(contact.Velocity, observedVelocity, .65f) : Vector3.zero;
                // 真人符合当前识别条件时直接确认，其它候选根据清晰度、距离和暴露逐步积累。
                if (recognizePlayer) { contact.Suspicion = 1; } else {
                    float clarity = Mathf.Lerp(1.5f, .45f, distance / Mathf.Max(1, sightRange));
                    clarity *= exposed / 3f * Mathf.Lerp(1, .65f, angle / Mathf.Max(1, cone));
                    clarity *= contact.Velocity.magnitude > 2 ? 1.25f : 1;
                    clarity *= glareSight;
                    float recognition = m_Tuning.RecognitionSeconds;
                    if (brain.IsExpeditionRaider && exposed >= 2)
                        recognition *= Mathf.Lerp(.65f, 1, Mathf.InverseLerp(8, 30, distance));
                    contact.Suspicion = Mathf.Clamp01(contact.Suspicion + observationStep *
                        (clarity / Mathf.Max(.1f, recognition) + .28f));
                }
                contact.Position = candidate.Position;
                contact.LastVisualPosition = candidate.Position;
                contact.HadVisualSample = true;
                contact.PositionUncertainty = 1;
                contact.AimOffset = visibleAim;
                contact.PlayerControlled = candidate.PlayerControlled;
                contact.LastSeen = now;
                if (contact.Suspicion >= .99f) contact.Confirmed = true;
                // 记得以前见过某人，不代表隔了很久又见时能立即确认。
                contact.Visible = contact.Confirmed && contact.Suspicion >= .65f;
                contact.Confidence = contact.Visible ? 1 : contact.Suspicion * .6f;
            }
            if (brain.SensorBatch != m_SensorBatch) {
                brain.SensorBatch = m_SensorBatch;
                // 另处理枪声、脚步及操作等线索，听到但看不见时只更新有误差的位置和可信程度。
                foreach (var sensor in m_Events) {
                    if (!CanObserveOpponent(brain, sensor.Source) || !EntityManager.Exists(sensor.Source) ||
                        !EntityManager.HasComponent<GhostGameObjectLink>(sensor.Source))
                        continue;
                    var source = EntityManager.GetComponentObject<GhostGameObjectLink>(sensor.Source).LinkedInstance;
                    if (source == null || source.transform == root) continue;
                    float distance = Vector3.Distance(eye, sensor.Position);
                    float hearingRange = sensor.Range * (brain.IsExpeditionRaider ? sensor.Shot ? 1.7f : 1.3f : 1);
                    if (distance > hearingRange) continue;
                    bool blocked = WorldBlocked(eye, sensor.Position + Vector3.up);
                    float audibleRange = hearingRange * (blocked ? .55f : 1);
                    if (distance > audibleRange) continue;
                    if (!brain.Contacts.TryGetValue(sensor.Source, out var contact)) {
                        contact = new PmcContact();
                        brain.Contacts.Add(sensor.Source, contact);
                    }
                    // 短时间内连续的声音使用同一个带误差方向。
                    // 每一步都随机偏移会让路线抖动，多次采样平均还可能意外定位过准。
                    if (!contact.Visible && now - contact.LastSeen > .2) {
                        if (now >= contact.HearingBiasUntil) {
                            float bearing = brain.Random.NextFloat(0, Mathf.PI * 2);
                            float magnitude = brain.Random.NextFloat(.65f, 1);
                            contact.HearingBias = new Vector3(Mathf.Sin(bearing) * magnitude,
                                brain.Random.NextFloat(-.2f, .2f), Mathf.Cos(bearing) * magnitude);
                            contact.HearingBiasUntil = now + brain.Random.NextFloat(3, 5);
                        }
                        float error = Mathf.Lerp(1.5f, 6.5f, distance / Mathf.Max(1, audibleRange)) + (blocked ? 3 : 0);
                        float visualUncertainty = 1 + Mathf.Max(0, (float)(now - contact.LastSeen)) * 2;
                        if (!contact.HadVisualSample || error <= visualUncertainty) {
                            var bias = contact.HearingBias * error;
                            if (!blocked) bias.y = 0;
                            // 短时复用估计偏差。重复听到同样的枪声可以加深怀疑，但不能靠重复平均出准确落点。
                            contact.Position = sensor.Position + bias;
                            contact.PositionUncertainty = error;
                            contact.Velocity = Vector3.zero;
                        } else { contact.PositionUncertainty = visualUncertainty; }
                        contact.Confidence = Mathf.Max(Mathf.Min(contact.Confidence, .75f), sensor.Shot ? .65f : .4f);
                    }
                    contact.LastHeard = now;
                    // 近处枪声使角色紧张。远处枪响不应积出和本次中弹、
                    // 或子弹从身旁掠过同等的压制。
                    if (sensor.Shot && distance < 18)
                        brain.Suppression = Mathf.Max(brain.Suppression, blocked ? .06f : .15f);
                }
            }
            brain.ExpiredContacts.Clear();
            var selected = Entity.Null;
            float best = .15f;
            foreach (var pair in brain.Contacts) {
                var c = pair.Value;
                if (!CanObserveOpponent(brain, pair.Key) || !EntityManager.Exists(pair.Key) ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(pair.Key) ||
                    EntityManager.GetComponentData<PredictedPlayerGhost>(pair.Key).CurrentHealth <= 0 ||
                    now - System.Math.Max(c.LastReported, System.Math.Max(c.LastSeen, c.LastHeard)) > memorySeconds) {
                    brain.ExpiredContacts.Add(pair.Key);
                    continue;
                }
                float score = c.Confidence + (c.Visible ? 2 : 0) + (pair.Key == brain.Target ? .15f : 0);
                if (brain.IsExpeditionRaider && c.Visible && c.PlayerControlled) score += .45f;
                score -= Mathf.Min(.4f, Vector3.Distance(eye, c.Position) / 150);
                if (score <= best) continue;
                best = score;
                selected = pair.Key;
            }
            foreach (var entity in brain.ExpiredContacts) brain.Contacts.Remove(entity);
            bool wasVisible = brain.Visible;
            var previous = brain.Target;
            brain.Target = selected;
            brain.Visible = selected != Entity.Null && brain.Contacts[selected].Visible;
            brain.Confidence = selected == Entity.Null ? 0 : brain.Contacts[selected].Confidence;
            // 换了目标就清走旧目标的反应计时和射击计划，不把原来安稳的火线套用到新人身上。
            if (previous != selected) ResetContactTactics(brain, now);
            if (selected == Entity.Null) {
                brain.ObservedVelocity = Vector3.zero;
                brain.ContactUncertainty = 1;
                brain.LastSeenTime = brain.LastHeardTime = -100;
                return;
            }
            var memory = brain.Contacts[selected];
            if (brain.Visible) {
                if (!wasVisible || previous != selected) {
                    brain.VisibleSince = now;
                    if (brain.IsExpeditionRaider) {
                        brain.ReacquiredTarget = previous == selected && now - brain.LastConfirmedSight <= 2;
                        if (brain.ReacquiredTarget)
                            brain.NextBurst = System.Math.Min(brain.NextBurst, now + ReactionTime(brain));
                        brain.NextAimError = 0;
                    }
                }
                brain.LastConfirmedSight = now;
            } else if (wasVisible && brain.IsExpeditionRaider) { brain.BurstUntil = now; }
            brain.LastSeen = memory.Position;
            brain.ObservedVelocity = memory.Velocity;
            brain.AimOffset = memory.AimOffset;
            brain.LastSeenTime = memory.LastSeen;
            brain.LastHeardTime = System.Math.Max(memory.LastHeard, memory.LastReported);
            brain.ContactUncertainty = memory.PositionUncertainty;
            if (wasVisible != brain.Visible) brain.NextDecision = 0;
        }

        private float ContactMemorySeconds(DollSingerEnemyBrain brain) {
            return m_Tuning.MemorySeconds * (brain.IsExpeditionRaider ? 1.5f : 1);
        }

        private static bool ExpeditionFlashBlinded(DollSingerEnemyBrain brain, double now) {
            return brain.IsExpeditionRaider && brain.SelfStatus is { FlashStrength: > .25f } status &&
                   status.FlashUntil > now;
        }

        // 记录角色目前观察到的火、毒等区域危险。离开区域会分别处理记忆与即时伤害状态。
        private void ObserveHazardArea(DollSingerEnemyBrain brain, Transform root, double now) {
            uint id = 0;
            Vector3 position = default;
            float radius = 0, closest = float.MaxValue;
            if (!brain.IsClashBot && root && m_TacticalWorld != null && brain.SelfStatus != null &&
                (brain.SelfStatus.Fire > now || brain.SelfStatus.Wire > now ||
                 (brain.SelfStatus.Poison > now && brain.SelfStatus.Antidote <= now))) {
                var scene = root.gameObject.scene.GetPhysicsScene();
                if (scene.IsValid())
                    foreach (var area in m_TacticalWorld.Objects) {
                        if (area == null || area.Id == 0 || area.Definition == null ||
                            !((area.Phase == TacticalWorldPhase.Fire &&
                               area.Definition.Kind == TacticalItemKind.Fire) ||
                              (area.Phase == TacticalWorldPhase.Poison &&
                               area.Definition.Kind == TacticalItemKind.PoisonTrap) ||
                              (area.Phase == TacticalWorldPhase.Wire &&
                               area.Definition.Kind == TacticalItemKind.WireTrap)) ||
                            !PmcHazardRules.Active(false, brain.SelfStatus, area.Phase, area.Until, now, true) ||
                            !PmcGrenadeRules.Finite(area.Position) || !float.IsFinite(area.Definition.Radius) ||
                            area.Definition.Radius <= 0)
                            continue;
                        float distance = Vector3.Distance(root.position, area.Position);
                        if (distance >= closest ||
                            !ServerGameSystem.TacticalWithin(area, root.position, 0, scene, root))
                            continue;
                        id = area.Id;
                        position = area.Position;
                        radius = area.Definition.Radius;
                        closest = distance;
                    }
            }
            bool danger = id != 0;
            if (brain.HazardDanger != danger ||
                (danger && (brain.HazardId != id || (brain.HazardPosition - position).sqrMagnitude > 1)))
                brain.NextDecision = 0;
            brain.HazardDanger = danger;
            brain.HazardId = id;
            brain.HazardPosition = position;
            brain.HazardRadius = radius;
        }

        // 看见飞行或落地的手雷才进入避险记忆。随后按最后观察点避开它，不能自动追踪墙后的运动。
        private void ObserveGrenades(DollSingerEnemyBrain brain, Transform root, Vector3 eye, double now) {
            if (brain.IsClashBot || !root) {
                brain.Grenade.Miss();
                return;
            }
            var scene = root.gameObject.scene.GetPhysicsScene();
            if (!scene.IsValid()) {
                brain.Grenade.Miss();
                return;
            }
            ulong selectedId = 0;
            float closest = float.MaxValue, selectedRadius = 0;
            Vector3 selectedPosition = default;
            double selectedUntil = 0;
            var facing = Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward;

            void Consider(
                ulong id,
                PhysicsScene sourceScene,
                Vector3 position,
                float radius,
                double until,
                bool tactical,
                TacticalItemKind kind = default,
                TacticalWorldPhase phase = default) {
                if (id == 0 || sourceScene != scene || !double.IsFinite(until) || until <= now ||
                    !PmcGrenadeRules.Finite(position))
                    return;
                float distance = Vector3.Distance(root.position, position);
                var target = position + Vector3.up * .15f;
                var ray = target - eye;
                float angle = Vector3.Angle(facing, ray);
                bool candidate = tactical
                    ? PmcGrenadeRules.Candidate(false, kind, phase, distance, radius, angle, m_Tuning.HalfViewAngle)
                    : PmcGrenadeRules.VisibleDanger(false, distance, radius, angle, m_Tuning.HalfViewAngle);
                if (distance >= closest || !candidate || SmokeVisibility.Blocked(m_TacticalWorld, scene, eye, target) ||
                    (ray.sqrMagnitude > .0001f && scene.Raycast(eye, ray.normalized, out _, ray.magnitude, s_WorldMask,
                        QueryTriggerInteraction.Ignore)))
                    return;
                selectedId = id;
                selectedPosition = position;
                selectedRadius = radius;
                selectedUntil = until;
                closest = distance;
            }

            if (m_TacticalWorld != null)
                foreach (var item in m_TacticalWorld.Objects) {
                    if (item == null || item.Definition == null || item.Phase != TacticalWorldPhase.Flying ||
                        (item.Definition.Kind != TacticalItemKind.Frag &&
                         item.Definition.Kind != TacticalItemKind.Dynamite))
                        continue;
                    Consider(item.Id, item.Scene, item.Position, item.Definition.Radius, item.Until, true,
                        item.Definition.Kind, item.Phase);
                }
            if (m_EnergyGrenades == null)
                m_EnergyGrenades = World.GetExistingSystemManaged<EnergyGrenadeServerSystem>();
            if (m_EnergyGrenades != null)
                for (int i = 0; i < m_EnergyGrenades.ActiveThreatCount; i++)
                    if (m_EnergyGrenades.TryGetLiveThreat(i, out uint id, out var sourceScene, out var position,
                            out float remaining))
                        Consider(PmcGrenadeRules.EnergyId(id), sourceScene, position, EnergyGrenadeRules.Radius,
                            now + remaining, false);
            if (selectedId == 0) {
                brain.Grenade.Miss();
                return;
            }
            if (brain.Grenade.Observe(selectedId, selectedPosition, selectedRadius, selectedUntil, now))
                brain.NextDecision = 0;
        }

        private void ResetContactTactics(DollSingerEnemyBrain brain, double now) {
            // 掩体、侧绕和连发时机都是针对某一个对手选的。
            // 换目标时不能沿用上一场交火的安全角度和已准备好的射击。
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
            brain.SidestepBurst = -1;
            brain.NextAimError = 0;
            brain.LastConfirmedSight = -100;
            brain.ReacquiredTarget = false;
            brain.HealUntil = 0;
        }

        private static bool WorldBlocked(Vector3 from, Vector3 to) {
            return UnityEngine.Physics.Linecast(from, to, s_WorldMask, QueryTriggerInteraction.Ignore);
        }
    }
}