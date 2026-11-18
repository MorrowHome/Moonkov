// 把 AI 的选择整理成一拍玩家输入。按时间更新感知和决策，推进搜刮、治疗等事情，再计算路径移动、瞄准和开火意图。
// ServerPlayerMovementSystem 接收这些输入，用跟真人共用的移动和射击逻辑执行。
using Unity.Mathematics;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public partial class DollSingerEnemySystem {
        private static readonly Profiling.ProfilerMarker s_ThinkProfile = new("Moonkov.Pmc.Think");

        private PlayerInput Think(
            Entity self,
            DollSingerEnemyBrain brain,
            PlayerGhost ghost,
            LocalTransform pose,
            ref PredictedPlayerGhost health,
            MoonRaidMap map,
            double now,
            float dt) {
            using var profile = s_ThinkProfile.Auto();
            Vector3 position = pose.Position;
            var eye = ghost.ShotOrigin.position;
            brain.SelfStatus = null;
            brain.LightGlare = health.HaloLightGlare;
            // ECS 角色早于场景 GameObject 完成链接。
            // 使用查询取得的实体身份，不通过尚未绑定的组件找身份。
            // 用 ECS 角色拿它自己的持续状态。角色外观可能还没接好，所以不从模型反向猜玩法身份。
            if (m_TacticalWorld != null && m_TacticalWorld.Statuses.TryGetValue(self, out var status))
                brain.SelfStatus = status;
            brain.BleedOnlyDamage = brain.SelfStatus != null && brain.SelfStatus.Bleed > now &&
                                    brain.SelfStatus.Fire <= now && (brain.SelfStatus.Poison <= now ||
                                                                     brain.SelfStatus.Antidote > now);
            UpdateCombatPressure(brain, health, now, dt);
            // 感知和选目标按自己的节奏更新，走路和已经开始的动作继续逐拍推进。可以省掉重复的视线、路径和目标判断。
            if (now >= brain.NextSense) {
                Sense(brain, ghost.transform, eye, map.EnemySightRange, now);
                brain.NextSense = now + .1;
            }
            if (now >= brain.NextDecision) {
                Decide(brain, position, health, map, now);
                brain.NextDecision = now + m_Tuning.DecisionInterval;
            }
            // 本拍接着做跨帧的掩体、医疗和补给。需要走哪条新路线留给定期执行的决策。
            AdvanceCover(brain, position, now);
            AdvanceHealing(brain, ref health, position, now);
            AdvanceNutrition(brain, ref health, position, now);
            UpdateOxygenSupply(brain, ref health, position, now);
            UpdateShieldSupply(brain, ref health, position, now);
            if (brain.NutritionUntil <= 0) AdvanceObjective(brain, ref health, position, eye, map, now, dt);
            // 搜索读条时角色不走路。
            // 重算一条到角色当前位置的路径可能失败，结果把本来能搜的尸体或抽屉取消了。
            // 搜刮读条时人已经站好。再寻一条到自己当前位置的路反而可能失败，把正常搜刮取消掉。
            if (brain.Action != PmcAction.Scavenge || brain.LootUntil <= 0)
                UpdatePath(brain, position, brain.TacticalGoal, now);
            var move = FollowPath(brain, position);
            bool searching = (brain.Action == PmcAction.Search || brain.Action == PmcAction.Investigate) &&
                             !brain.Visible;
            bool pursuing = brain.IsExpeditionRaider && brain.Action == PmcAction.Search && !brain.Visible &&
                            now - brain.LastConfirmedSight < 12;
            bool atCorner = brain.Corner < brain.CornerCount - 1 &&
                            HorizontalDistance(position, brain.Corners[brain.Corner]) < 2.5f;
            bool checkingCorner = searching && atCorner;
            if (searching) move *= pursuing ? checkingCorner ? .55f : 1 : checkingCorner ? .45f : .7f;
            if (brain.IsPeeking) move *= .65f;
            // 在互动、治疗和撤离读条期间停住，让 计时动作 按服务端条件完成。
            if ((brain.Action == PmcAction.Scavenge && brain.LootUntil > 0) || brain.ExtractionProgress > 0 ||
                brain.HealUntil > 0 || brain.NutritionUntil > 0 || brain.Interacting ||
                (searching && now < brain.SearchPauseUntil) ||
                (HorizontalDistance(position, brain.TacticalGoal) <= TacticalArrivalDistance &&
                 Mathf.Abs(position.y - brain.TacticalGoal.y) < .6f))
                move = Vector3.zero;
            bool blinded = ExpeditionFlashBlinded(brain, now);
            if (brain.IsExpeditionRaider && !blinded && move.sqrMagnitude < .01f)
                move = SidestepBetweenBursts(brain, position, health, now);
            brain.MovementRequested = move.sqrMagnitude > .01f;
            bool hasContact = brain.Target != Entity.Null;
            // 瞄向自己记得的可见部位。清楚看见时才按连续观察到的速度给弹体做有限的提前量。
            var aim = brain.LastSeen + brain.AimOffset;
            float distance = hasContact ? Vector3.Distance(eye, aim) : 50;
            var weapon =
                WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID, health.EquippedModules);
            // 弹体要飞一段时间，连续看见目标时可按有限的速度估计提前量并补下坠。
            if (brain.Visible && weapon != null && weapon.Type == WeaponType.Projectile) {
                float flight = Mathf.Min(.6f, distance / Mathf.Max(1, weapon.ProjectileSpeed));
                aim += Vector3.ClampMagnitude(brain.ObservedVelocity * flight, brain.IsExpeditionRaider ? 4.5f : 3);
                aim.y += .5f * weapon.ProjectileGravity * flight * flight;
            }
            var facing = hasContact ? aim - eye : move;
            bool looting = brain.Action == PmcAction.Scavenge && brain.LootUntil > 0;
            bool preaimCorner = brain.IsExpeditionRaider && atCorner && !brain.Visible && !blinded &&
                                brain.MovementRequested &&
                                (brain.Action == PmcAction.Scavenge || brain.Action == PmcAction.Extract ||
                                 brain.Action == PmcAction.FollowSquad) && !RaidHealth.HandsBusy(health);
            // 搜刮者看实际物资位置。预瞄转角时先看下一段路，小队后卫或搜索又有自己的观察方向。
            if (looting)
                facing = brain.LootLookPoint - eye;
            else if (preaimCorner) facing = brain.Corners[brain.Corner + 1] + Vector3.up * 1.15f - eye;
            if (!hasContact && brain.IsClashBot && move.sqrMagnitude < .01f) facing = brain.MissionLookPoint - eye;
            if (brain.Action == PmcAction.FollowSquad && brain.HasSquadLeader && !brain.Visible)
                facing = move.sqrMagnitude < .01f
                    ? Quaternion.Euler(0, SquadWatchYaw(brain), 0) * Vector3.forward
                    : facing;
            if (searching && brain.SearchFocusValid) {
                var lookPoint = brain.SearchLookPoint;
                if (now >= brain.SearchPauseUntil && brain.Corner < brain.CornerCount) {
                    // 转过下个拐角前，先检查拐角之后空间。
                    int corner = checkingCorner ? brain.Corner + 1 : brain.Corner;
                    lookPoint = brain.Corners[corner] + Vector3.up * 1.15f;
                }
                facing = lookPoint - eye;
            }
            if (facing.sqrMagnitude > .001f) {
                float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                float pitch = hasContact || looting || preaimCorner
                    ? -Mathf.Atan2(facing.y, new Vector2(facing.x, facing.z).magnitude) * Mathf.Rad2Deg
                    : 0;
                float turn = m_Tuning.TurnSpeed * (brain.IsExpeditionRaider && brain.Visible ? 1.7f : 1f) * dt;
                brain.Yaw = Mathf.MoveTowardsAngle(brain.Yaw, yaw, turn);
                brain.Pitch = Mathf.MoveTowardsAngle(brain.Pitch, pitch, turn * .7f);
            }
            if (now >= brain.NextAimError) {
                float settled =
                    Mathf.Clamp01((float)(now - brain.VisibleSince) / (brain.IsExpeditionRaider ? 1.1f : 2f));
                float error = Mathf.Lerp(m_Tuning.FirstShotError, m_Tuning.SettledAimError, settled);
                float movingError = 1.5f;
                // 误差做成有空间上限的偏移。远距离固定角度会偏离很多米，视线仍很清楚时不该完全看不出目标形状。
                if (brain.IsExpeditionRaider && brain.Visible) {
                    // 固定角度误差会让远处的一点偏差放大到几个身体宽。
                    float aimDistance = Mathf.Max(1, distance);
                    float offset = Mathf.Lerp(.55f, .27f, settled);
                    error = Mathf.Min(error, Mathf.Atan2(offset, aimDistance) * Mathf.Rad2Deg);
                    movingError = Mathf.Min(.9f, Mathf.Atan2(.4f, aimDistance) * Mathf.Rad2Deg);
                }
                error += brain.Suppression * 4 + move.magnitude * movingError;
                error += HaloLightGlareRules.AimErrorDegrees(brain.LightGlare);
                brain.AimErrorX = brain.Random.NextFloat(-error, error);
                brain.AimErrorY = brain.Random.NextFloat(-error * .6f, error * .6f);
                brain.NextAimError = now + .25;
            }
            var look = new float2(brain.Yaw + (brain.Visible ? brain.AimErrorX : 0),
                brain.Pitch + (brain.Visible ? brain.AimErrorY : 0));
            var direction = Quaternion.Euler(look.y, look.x, 0) * Vector3.forward;
            var localMove = Quaternion.Euler(0, -look.x, 0) * move;
            var input = new PlayerInput {
                MoveInput = new float2(localMove.x, localMove.z), LookYawPitchDegrees = look,
                // AimPoint 必须沿当前受误差和转身速度限制的视线。直接给目标精确坐标会绕开这些限制。
                // 瞄准点跟随当前转到的角度，而不是直接填敌人的精确位置。射击规则因此仍会遵守转身速度与误差。
                AimPoint = eye + direction * Mathf.Max(1, distance)
            };
            input.SetFlag(PlayerInput.InputFlag.ThirdPerson, true);
            bool fighting = CanReturnFire(brain) && health.CurrentAmmo > 0 && !health.ControllerState.IsReloadingState;
            input.SetFlag(PlayerInput.InputFlag.Aim,
                !blinded && ((fighting && brain.Visible) || checkingCorner || preaimCorner));
            input.SetFlag(PlayerInput.InputFlag.Sprint,
                PmcSprintRules.Allows(brain.IsClashBot, brain.Action, health.TacticalSprintExhausted) &&
                brain.HealUntil <= 0 && brain.NutritionUntil <= 0 && !(fighting && brain.Visible) &&
                move.sqrMagnitude > .1f && (brain.Action == PmcAction.Retreat ||
                                            brain.Action == PmcAction.EvadeGrenade ||
                                            brain.Action == PmcAction.EvadeHazard ||
                                            (brain.Action == PmcAction.Flank && !brain.Visible) ||
                                            (brain.Action == PmcAction.FollowSquad &&
                                             (position - brain.SquadLeaderPosition).sqrMagnitude > 15 * 15) ||
                                            (pursuing && !checkingCorner && distance > 18) ||
                                            ((brain.Action == PmcAction.Extract ||
                                              (brain.IsClashBot && brain.Leaving)) && !hasContact) ||
                                            (brain.Action == PmcAction.Cover &&
                                             (brain.CoverPhase == PmcCoverPhase.Approaching ||
                                              brain.CoverPhase == PmcCoverPhase.Returning))));
            int preferred = 0;
            if (!brain.IsClashBot && brain.Visible && !blinded && brain.LootUntil <= 0) {
                bool returnFire = CanReturnFire(brain);
                bool emptyUnderFire = brain.IsExpeditionRaider && health.CurrentAmmo <= 0 &&
                                      (returnFire || brain.Action == PmcAction.Reload) && brain.HealUntil <= 0 &&
                                      brain.NutritionUntil <= 0 && !brain.Interacting && !brain.Grenade.Active(now) &&
                                      !brain.HazardDanger;
                if (emptyUnderFire)
                    preferred = PmcWeaponTactics.LoadedAlternate(health, distance);
                else if (returnFire && now >= brain.NextTacticalSwitch)
                    preferred = PmcWeaponTactics.PreferredSlot(health, distance);
            }
            uint preferredWeapon = preferred == 1 ? health.PrimaryWeaponID :
                preferred == 2 ? health.SecondaryWeaponID : health.PistolWeaponID;
            bool switching = preferred != 0 &&
                             WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(preferredWeapon) != null;
            if (switching) {
                input.SetFlag(
                    preferred == 1 ? PlayerInput.InputFlag.EquipHalo :
                    preferred == 2 ? PlayerInput.InputFlag.EquipRevolver : PlayerInput.InputFlag.EquipPistol, true);
                brain.NextTacticalSwitch = now + 3;
                // 普通远征人形可先找有弹的备用武器和真实电池，结算充能还走武器系统。
                if (brain.IsExpeditionRaider && brain.Action == PmcAction.Reload)
                    brain.CommitUntil = brain.NextDecision = 0;
            }
            bool reachingCover = brain.IsExpeditionRaider && brain.Visible && brain.HasCover && !brain.IsPeeking &&
                                 !brain.PathFailed && brain.MovementRequested;
            if (!switching && !reachingCover && brain.LootUntil <= 0 && brain.HealUntil <= 0 &&
                brain.NutritionUntil <= 0 && !brain.Interacting && ShouldReload(brain, health, position, now) &&
                !health.ControllerState.IsReloadingState) {
                bool blocked = weapon != null && brain.ReloadFailure.Matches(brain.Inventory.Version,
                    health.EquippedWeaponID, weapon.EnergyPerRound);
                bool cellReady = weapon != null && !blocked && PmcReloadInventory.Prepare(brain.Inventory,
                    health.HaloEnergyInitialized ? 1 : weapon.EnergyPerRound);
                if (cellReady) {
                    brain.ReloadFailure.Clear();
                    input.SetFlag(PlayerInput.InputFlag.Reload, true);
                } else {
                    if (weapon != null && !blocked) {
                        brain.ReloadFailure.Record(brain.Inventory.Version, health.EquippedWeaponID,
                            weapon.EnergyPerRound);
                        brain.NextDecision = 0;
                    }
                    if (health.CurrentAmmo == 0) {
                        if (health.EquippedWeaponSlot != 3 && health.PistolAmmo > 0)
                            input.SetFlag(PlayerInput.InputFlag.EquipPistol, true);
                        else if (health.EquippedWeaponSlot != 1 && health.PrimaryAmmo > 0)
                            input.SetFlag(PlayerInput.InputFlag.EquipHalo, true);
                        else if (health.EquippedWeaponSlot != 2 && health.SecondaryAmmo > 0)
                            input.SetFlag(PlayerInput.InputFlag.EquipRevolver, true);
                    }
                }
            }
            float firingRange = map.EnemySightRange;
            if (brain.IsExpeditionRaider && brain.Visible &&
                brain.Contacts.TryGetValue(brain.Target, out var contact) && contact.PlayerControlled)
                firingRange = Mathf.Max(firingRange, m_Tuning.ExpeditionPlayerSightRange);
            bool firingSolution = !switching && !input.Reload && fighting && brain.Visible && !blinded &&
                                  health.CurrentAmmo > 0 && !health.ControllerState.IsReloadingState &&
                                  health.WeaponEquipRemaining <= 0 && now - brain.VisibleSince >= ReactionTime(brain) &&
                                  distance < firingRange && Vector3.Angle(direction, facing) < 7 &&
                                  ClearSight(ghost.transform, eye, brain.LastSeen + brain.AimOffset, brain.Target) &&
                                  (!brain.IsExpeditionRaider || !DollSingerBotCombat.Obscured(
                                      ghost.gameObject.scene.GetPhysicsScene(), m_TacticalWorld, eye,
                                      input.AimPoint)) &&
                                  (!brain.IsClashBot ||
                                   ClashShotLaneClear(brain, eye, direction, map.EnemySightRange)) &&
                                  (brain.ExpeditionSquadId == 0 || ExpeditionShotLaneClear(brain, ghost.transform, eye,
                                      direction, distance + .2f));
            // 别在角色还没反应过来或还在转身时，就开始消耗短暂探头的连发时间。
            // 能实际开火时才开始计算这一组。
            if (now >= brain.NextBurst && firingSolution) {
                // 松开防御时光环还在低位。
                // 开始统计短连射前留足抬手时间，也考虑手伤导致的变慢。
                float raiseTime = DollSingerWeapons.IsHalo(health.EquippedWeaponID)
                    ? (1 - Mathf.Clamp01(health.WeaponRaiseProgress)) * DollSinger.DollSingerHaloAim.RaiseSeconds *
                      RaidHealth.HandlingMultiplier(health)
                    : 0;
                brain.BurstUntil = now + raiseTime + (brain.IsExpeditionRaider
                    ? brain.Random.NextFloat(.38f, .72f)
                    : brain.Random.NextFloat(.25f, .6f));
                brain.NextBurst = brain.BurstUntil + (brain.IsExpeditionRaider
                    ? brain.Random.NextFloat(.18f, .38f)
                    : brain.Random.NextFloat(.5f, 1.1f));
            }
            // 枪线失效时清走先前排队的一发，避免转进队友或掩体后发生迟到射击。
            if (brain.IsExpeditionRaider && !firingSolution && HaloModuleFire.HasPendingFire(health))
                HaloModuleFire.Reset(ref health);
            bool shoot = firingSolution && now < brain.BurstUntil;
            // 有可反击的火线就解除防御。
            // 否则护盾会无声吞掉每轮射击，让角色一直站在外面只挨打。
            bool defend = !shoot && !TutorialRuntime.Enabled(EntityManager) && brain.Visible &&
                          (brain.Action == PmcAction.Retreat ||
                           (brain.Action == PmcAction.Cover && !brain.IsPeeking)) && health.HaloEnergy > 15 &&
                          brain.HealUntil <= 0 && brain.NutritionUntil <= 0 && !brain.Interacting && !input.Reload &&
                          HaloEnergyRules.CanDefend(health);
            input.SetFlag(PlayerInput.InputFlag.HaloDefense, defend);
            input.SetFlag(PlayerInput.InputFlag.Shoot, shoot);
            return input;
        }

        private Vector3 SidestepBetweenBursts(
            DollSingerEnemyBrain brain,
            Vector3 position,
            in PredictedPlayerGhost health,
            double now) {
            if (brain.Action != PmcAction.Engage || !brain.Visible || brain.Leaving || !CanReturnFire(brain) ||
                health.CurrentAmmo <= 0 || RaidHealth.HandsBusy(health) || brain.Grenade.Active(now) ||
                brain.HazardDanger || now < brain.BurstUntil || now >= brain.NextBurst ||
                now - brain.LastDamage < .65 || brain.Suppression >= .45f)
                return Vector3.zero;
            float distance = HorizontalDistance(position, brain.LastSeen);
            if (distance > PreferredCombatRange(brain, health.EquippedWeaponID) + 4) return Vector3.zero;
            // 每次射击间歇只挑一段短横移。走到新位置之后才从那里重新开火。
            if (brain.SidestepBurst != brain.NextBurst) {
                float side = ((brain.Seed + (int)(brain.NextBurst * 1.7)) & 1) == 0 ? 1 : -1;
                brain.SidestepDirection =
                    DollSingerBotCombat.SideStep(position, brain.LastSeen - position, side, distance < 5, m_Filter);
                brain.SidestepBurst = brain.NextBurst;
            }
            if (brain.SidestepDirection.sqrMagnitude > .01f) {
                // 短横移后的新位置就是下一组连射的出发点。
                brain.TacticalGoal = position;
                brain.Corner = brain.CornerCount;
            }
            return brain.SidestepDirection * .65f;
        }

        private float ReactionTime(DollSingerEnemyBrain brain) {
            return brain.IsExpeditionRaider
                ? m_Tuning.ReactionSeconds * (brain.ReacquiredTarget ? .18f : .35f)
                : m_Tuning.ReactionSeconds;
        }

        private static bool CanReturnFire(DollSingerEnemyBrain brain) {
            return brain.HealUntil <= 0 && brain.NutritionUntil <= 0 && !brain.Interacting &&
                   (brain.Action == PmcAction.Engage || brain.Action == PmcAction.HoldAngle ||
                    brain.Action == PmcAction.Cover || brain.Action == PmcAction.Retreat ||
                    brain.Action == PmcAction.Flank);
        }

        // 原地安全且能访问库存时才给或穿护盾。用实际物品版本避免重复试同一件。
        private void UpdateShieldSupply(
            DollSingerEnemyBrain brain,
            ref PredictedPlayerGhost health,
            Vector3 position,
            double now) {
            if (!health.ShieldEquipped || health.ShieldEnergy > PmcShieldSupply.RechargeThreshold ||
                now < brain.NextShieldSupplyCheck)
                return;
            brain.NextShieldSupplyCheck = now + 1;
            if (!PmcShieldSupply.MayRecharge(brain, health, TutorialRuntime.Enabled(EntityManager),
                    SafeToTreat(brain, position, now), now))
                return;
            var held = WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID,
                health.EquippedModules);
            if (held != null && PmcShieldSupply.TryRecharge(ref health, brain.Inventory, held.EnergyPerRound,
                    held.MagazineSize, held))
                health.LastReloadTick = SystemAPI.GetSingleton<NetCode.NetworkTime>().ServerTick.TickIndexForValidTick;
        }

        // 持续用玩家共用逻辑耗氧，并尝试按安全时机用携带补给。缺东西可触发后续撤离决策。
        private void UpdateOxygenSupply(
            DollSingerEnemyBrain brain,
            ref PredictedPlayerGhost health,
            Vector3 position,
            double now) {
            if (!PmcLifeSupport.Low(health, PmcLifeSupport.RefillThreshold) || now < brain.NextOxygenSupplyCheck)
                return;
            brain.NextOxygenSupplyCheck = now + 1;
            if (!PmcResourceActions.SafeWindow(brain, health, TutorialRuntime.Enabled(EntityManager),
                    SafeToTreat(brain, position, now), now, true) || !health.HeadDomeEquipped ||
                brain.Inventory == null)
                return;
            var supply = PmcLifeSupport.FindAccessible(brain.Inventory);
            if (supply == null && !brain.OxygenSupplyFailure.Matches(brain.Inventory.Version)) {
                supply = PmcLifeSupport.Prepare(brain.Inventory);
                if (supply == null)
                    brain.OxygenSupplyFailure.Record(brain.Inventory.Version);
                else
                    brain.OxygenSupplyFailure.Clear();
            }
            if (PmcLifeSupport.TryRefill(ref health, brain.Inventory, supply)) {
                brain.OxygenSupplyFailure.Clear();
                EnergyShieldEquipment.Flush(ref health, brain.Inventory);
                brain.NextDecision = 0;
            }
        }

        private void AdvanceNutrition(
            DollSingerEnemyBrain brain,
            ref PredictedPlayerGhost health,
            Vector3 position,
            double now) {
            if (brain.NutritionUntil <= 0 &&
                (now < brain.NextNutrition || !PmcNutrition.Low(health, PmcNutrition.UseThreshold)))
                return;
            bool eligible = !PmcLifeSupport.Low(health, PmcLifeSupport.DepartureThreshold) &&
                            PmcResourceActions.SafeWindow(brain, health, brain.TutorialActor,
                                SafeToTreat(brain, position, now), now, continuingNutrition: true,
                                allowCriticalNutrition: true);
            PmcNutrition.Advance(brain, ref health, position, eligible, now);
        }

        private static void UpdateCombatPressure(
            DollSingerEnemyBrain brain,
            PredictedPlayerGhost health,
            double now,
            float dt) {
            PmcCombatPressure.Update(brain, health, now, dt);
        }
    }

    public static class PmcCombatPressure {
        public static void Update(DollSingerEnemyBrain brain, in PredictedPlayerGhost health, double now, float dt) {
            float lost = Mathf.Max(0, brain.LastHealth - health.CurrentHealth);
            bool tracked = !brain.IsClashBot && brain.SelfStatus != null;
            if (brain.ObservedSelfStatus != brain.SelfStatus) {
                brain.ObservedSelfStatus = brain.SelfStatus;
                brain.LastStatusHealthDamage = 0;
                brain.LastStatusShieldHits = 0;
            }
            double statusLoss = tracked
                ? System.Math.Max(0, brain.SelfStatus.PmcStatusHealthDamage - brain.LastStatusHealthDamage)
                : 0;
            ulong statusShieldHits = tracked && brain.SelfStatus.PmcStatusShieldHits >= brain.LastStatusShieldHits
                ? brain.SelfStatus.PmcStatusShieldHits - brain.LastStatusShieldHits
                : 0;
            uint allShieldHits = unchecked(health.ShieldHitSequence - brain.LastShieldHitSequence);
            bool shieldHit = tracked ? allShieldHits > statusShieldHits : allShieldHits != 0;
            float externalLoss = tracked ? Mathf.Max(0, lost - (float)statusLoss) : brain.BleedOnlyDamage ? 0 : lost;
            bool impact = health.LastImpactTick != brain.LastImpactTick;
            // 伤害事件本身已由服务端确认，即便同一拍治疗后，
            // 血量净变化为零或反而增加，也不能把那次撞击忽略。
            if (impact || shieldHit || externalLoss > .001f) {
                // 压制随实际伤害调整。擦伤和周期性组织损伤轻一些，
                // 不能把它们反复当成半强度的恐慌冲击来加。
                float pressureLoss = !tracked && impact ? lost : externalLoss;
                float pressure = Mathf.Clamp01(pressureLoss / RaidHealth.Maximum(BodyPart.Chest)) * .6f;
                if (shieldHit) pressure += .08f + Mathf.Clamp01(health.ShieldHitStrength) * .12f;
                brain.Suppression = Mathf.Clamp01(brain.Suppression + pressure);
                brain.LastDamage = now;
                brain.NextDecision = 0;
            }
            brain.LastHealth = health.CurrentHealth;
            brain.LastShieldHitSequence = health.ShieldHitSequence;
            brain.LastImpactTick = health.LastImpactTick;
            brain.LastStatusHealthDamage = tracked ? brain.SelfStatus.PmcStatusHealthDamage : 0;
            brain.LastStatusShieldHits = tracked ? brain.SelfStatus.PmcStatusShieldHits : 0;
            brain.Suppression = Mathf.Max(0, brain.Suppression - Mathf.Max(0, dt) * .32f);
        }
    }

    public static class PmcStatusDamage {
        // 伤害入口额外记录确认的冲击。治疗恰好同一拍发生时，不能仅从前后血量差判断是否被打。
        public static void Record(
            TacticalServerWorld.Status status,
            in PredictedPlayerGhost before,
            in PredictedPlayerGhost after) {
            if (status == null) return;
            status.PmcStatusHealthDamage += System.Math.Max(0, before.CurrentHealth - after.CurrentHealth);
            status.PmcStatusShieldHits += unchecked(after.ShieldHitSequence - before.ShieldHitSequence);
        }
    }
}