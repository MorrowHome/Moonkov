// 经典爆破的回合状态和算分规则。这里处理开战、装拆包、时间、换边、记分和购买规则，ServerGameSystem.Classic 把它接到实际地图和玩家。
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public enum ClassicPhase : byte {
        Waiting,
        Buy,
        Live,
        Planted,
        RoundOver,
        Finished
    }

    public enum ClassicUse : byte {
        None,
        PickUp,
        PlantA,
        PlantB,
        Defuse
    }

    public struct ClassicScoreboardEntry {
        public FixedString64Bytes Name;
        public ushort Kills, Deaths;
        public int AverageDamage;
    }

    public struct ClassicSnapshotV4Rpc : IRpcCommand {
        public ClassicPhase Phase;
        public ClassicUse Use;
        public int Round, Team, Slot, AttackTeam, ScoreA, ScoreB, AliveA, AliveB, Credits, Winner;
        public float Remaining, UseProgress;
        public bool HasBomb, Alive, BombVisible;
        public byte AliveMaskA, AliveMaskB, CacheMask;
        public ClassicScoreboardEntry SeatA0, SeatA1, SeatA2, SeatA3, SeatA4;
        public ClassicScoreboardEntry SeatB0, SeatB1, SeatB2, SeatB3, SeatB4;
        public Mathematics.float3 BombPosition;

        public readonly ClassicScoreboardEntry GetSeat(int index) {
            return index switch {
                0 => SeatA0,
                1 => SeatA1,
                2 => SeatA2,
                3 => SeatA3,
                4 => SeatA4,
                5 => SeatB0,
                6 => SeatB1,
                7 => SeatB2,
                8 => SeatB3,
                9 => SeatB4,
                _ => default
            };
        }
    }

    public struct ClassicClientState : IComponentData {
        public ClassicSnapshotV4Rpc Snapshot;
        public uint Sequence;
        public bool Received;
    }

    public struct ClassicUseRpc : IRpcCommand {
        public int Round;
        public bool Held;
    }

    public sealed class ClassicMatch {
        public const int Version = 4, TeamSize = 5, Capacity = TeamSize * 2, Half = 12, ToWin = 13;
        public const double BuySeconds = 12, RoundSeconds = 115, FuseSeconds = 40, BreakSeconds = 5;
        public ClassicPhase Phase { get; private set; }
        internal bool LootOpen => Phase is ClassicPhase.Buy or ClassicPhase.Live or ClassicPhase.Planted;
        public double Deadline { get; private set; }
        public int Round { get; private set; }
        public int AttackTeam => Round <= Half ? 0 : 1;
        public int Winner { get; private set; } = -1;
        public int RoundWinner { get; private set; } = -1;
        public readonly int[] Score = new int[2];
        public int Carrier { get; private set; }
        public bool BombOnGround { get; private set; }
        public Vector3 BombPosition { get; private set; }
        public int HoldActor { get; private set; }
        private ClassicUse m_HoldUse;
        private Vector3 m_HoldPosition;
        private uint m_HoldDamage;
        private double m_HoldStarted, m_HoldUntil;
        private readonly Dictionary<int, CombatStats> m_CombatStats = new(Capacity);

        private struct CombatStats {
            public int Rounds, LatestRound;
            public ushort Kills, Deaths;
            public float Damage;
        }

        private static readonly string[][] s_CacheContents = {
            new[] { "flash_grenade", "smoke_grenade", "frag_bomb", "fire_bomb", "choke_bomb" },
            new[] { "smoke_grenade", "frag_bomb", "fire_bomb", "choke_bomb", "flash_grenade" },
            new[] { "frag_bomb", "fire_bomb", "choke_bomb", "flash_grenade", "smoke_grenade" }
        };

        public bool NeedsRound(double now) {
            return Phase == ClassicPhase.Waiting || Phase == ClassicPhase.RoundOver && now >= Deadline;
        }

        // 只有实际进入这回合作战的席位才增加回合参与计数，用来算长期均伤。
        internal void EnterCombat(int actor) {
            m_CombatStats.TryGetValue(actor, out var stats);
            if (stats.LatestRound == Round) return;
            stats.LatestRound = Round;
            stats.Rounds++;
            m_CombatStats[actor] = stats;
        }

        // 根据最终真实扣血记敌我伤害和死亡。不能把护盾挡住或已死后溢出的数值也加进分数。
        internal void RecordCombatDamage(int attacker, int victim, float damage, bool lethal) {
            if (Phase is not (ClassicPhase.Live or ClassicPhase.Planted) ||
                !m_CombatStats.TryGetValue(victim, out var target) || target.LatestRound != Round)
                return;
            if (lethal) {
                target.Deaths++;
                m_CombatStats[victim] = target;
            }
            if (attacker == victim || !m_CombatStats.TryGetValue(attacker, out var source) ||
                source.LatestRound != Round)
                return;
            source.Damage += damage;
            if (lethal) source.Kills++;
            m_CombatStats[attacker] = source;
        }

        // 对局成绩按席位累计，换边不重置已经打过的回合和战绩。
        internal ClassicScoreboardEntry ScoreboardEntry(int actor, FixedString64Bytes name) {
            m_CombatStats.TryGetValue(actor, out var stats);
            return new ClassicScoreboardEntry {
                Name = name,
                Kills = stats.Kills,
                Deaths = stats.Deaths,
                AverageDamage = stats.Rounds > 0 ? Mathf.RoundToInt(stats.Damage / stats.Rounds) : 0
            };
        }

        public void BeginRound(double now, int carrier) {
            if (!NeedsRound(now)) return;
            // 每回合重置炸弹持有、回合赢家和读条，先进入购买期。历史比分仍由比赛对象保留。
            Round++;
            Carrier = carrier;
            BombOnGround = false;
            BombPosition = default;
            RoundWinner = -1;
            HoldActor = 0;
            Phase = ClassicPhase.Buy;
            Deadline = now + BuySeconds;
        }

        public void Advance(double now, int attackersAlive, int defendersAlive) {
            // 按房主时间从购买切换到开火阶段。没有安装时进攻方限时，安装后胜负条件另由引信和守方状态决定。
            if (Phase == ClassicPhase.Buy && now >= Deadline) {
                Phase = ClassicPhase.Live;
                Deadline = now + RoundSeconds;
            }
            if (Phase == ClassicPhase.Live) {
                if (attackersAlive == 0 || now >= Deadline)
                    EndRound(1 - AttackTeam, now);
                else if (defendersAlive == 0) EndRound(AttackTeam, now);
            } else if (Phase == ClassicPhase.Planted && (now >= Deadline || defendersAlive == 0)) {
                EndRound(AttackTeam, now);
            }
            if (HoldActor != 0 && now > m_HoldUntil) HoldActor = 0;
        }

        // 只有此刻的携带者死亡或离手才在地图设掉落位置，别人重复报告不能把炸弹再放一份。
        public void DropBomb(int actor, Vector3 position) {
            if (Carrier != actor) return;
            Carrier = 0;
            BombOnGround = true;
            BombPosition = position;
            CancelHold(actor);
        }

        public void CancelHold(int actor) {
            if (HoldActor == actor) HoldActor = 0;
        }

        public static double UseSeconds(ClassicUse use) {
            return use switch {
                ClassicUse.PlantA or ClassicUse.PlantB => 3.2,
                ClassicUse.Defuse => 10,
                _ => 0
            };
        }

        public float HoldProgress(int actor, double now) {
            return HoldActor == actor && actor != 0
                ? Mathf.Clamp01((float)((now - m_HoldStarted) / UseSeconds(m_HoldUse)))
                : 0;
        }

        internal bool RestockCache(int id, InventoryGraph cache) {
            string[] rotation = s_CacheContents[id];
            // 这回合的补给箱内容按固定轮换取一项。箱里仍有同款时不重复塞。
            string code = rotation[(Round - 1) % rotation.Length];
            foreach (var item in cache.Children(LootInventoryExchange.Root))
                if (item.Code == code)
                    return false;
            return cache.AddSupply(code, 1, LootInventoryExchange.Root) == InventoryError.None;
        }

        // Position, visibility and alive state are sampled by the host every tick.
        public void Use(int actor, int team, ClassicUse use, Vector3 position, uint damage, double now) {
            if (actor == 0 || now >= Deadline) return;
            // 进攻方正在本回合作战且地上确实有炸弹，才拿到携带权。人物距离和视野已经由服务器入口查过。
            if (Phase == ClassicPhase.Live && use == ClassicUse.PickUp && team == AttackTeam && BombOnGround) {
                Carrier = actor;
                BombOnGround = false;
                return;
            }
            // 安装和拆除需要不同的阶段和队伍。只有携带者能安装，反方等成功安装后才可拆除。
            bool plant = Phase == ClassicPhase.Live && team == AttackTeam && Carrier == actor &&
                         (use == ClassicUse.PlantA || use == ClassicUse.PlantB);
            bool defuse = Phase == ClassicPhase.Planted && team != AttackTeam && use == ClassicUse.Defuse;
            if (!plant && !defuse) {
                CancelHold(actor);
                return;
            }
            // 同一时刻只认一个有效操作人。受击、走动、换按钮或请求到期会以新动作重新进度。
            if (HoldActor != 0 && HoldActor != actor && now <= m_HoldUntil) return;
            if (HoldActor != actor || use != m_HoldUse || now > m_HoldUntil || damage != m_HoldDamage ||
                (position - m_HoldPosition).sqrMagnitude > .35f * .35f) {
                HoldActor = actor;
                m_HoldUse = use;
                m_HoldStarted = now;
                m_HoldPosition = position;
                m_HoldDamage = damage;
            }
            // 按住期间持续用新请求续这段短租约。松手或请求断掉会清读条，不能留在后台自己完成。
            m_HoldUntil = now + .7;
            if (now - m_HoldStarted < UseSeconds(use)) return;
            HoldActor = 0;
            // 拆完立即得出回合结果。安装成功则开始引信，进入守方仍可抢救的阶段。
            if (defuse) {
                EndRound(team, now);
                return;
            }
            Carrier = 0;
            Phase = ClassicPhase.Planted;
            BombPosition = position;
            Deadline = now + FuseSeconds;
        }

        // 锁定本回合赢家、累计队伍比分，再按回合或总胜分决定开下一回合或完成比赛。
        private void EndRound(int team, double now) {
            RoundWinner = team;
            Score[team]++;
            HoldActor = 0;
            if (Score[team] >= ToWin || Round == Half * 2) {
                Winner = Score[team] >= ToWin ? team : -1;
                Phase = ClassicPhase.Finished;
                Deadline = now;
            } else {
                Phase = ClassicPhase.RoundOver;
                Deadline = now + BreakSeconds;
            }
        }

        public static int Price(ItemDefinition definition) {
            if (definition == null) return 0;
            if (definition.Kind == ItemKind.LongGun) return 2700;
            if (definition.Kind == ItemKind.Pistol) return 500;
            if (definition.Kind is ItemKind.Helmet or ItemKind.Shield) return 650;
            if (definition.Kind == ItemKind.Cell) return 100;
            if (definition.Kind == ItemKind.Medical)
                return definition.Code switch {
                    "medkit" => 400,
                    "trauma_kit" => 900,
                    "rescue_kit" => 700,
                    "med_gel" => 200,
                    "hemostatic_bandage" => 150,
                    _ => 0
                };
            if (definition.Kind == ItemKind.Food) return 100;
            if (definition.Kind == ItemKind.Drink) return 150;
            if (definition.Kind == ItemKind.Oxygen) return 250;
            if (definition.Kind != ItemKind.Throwable || TacticalItemRules.Find(definition.Code) == null) return 0;
            return definition.Code switch {
                "vitality_shot" => 400,
                "stamina_shot" => 200,
                "antidote_shot" => 200,
                "regeneration_shot" => 500,
                "flash_grenade" => 200,
                "smoke_grenade" => 300,
                "frag_bomb" => 300,
                "fire_bomb" => 400,
                "choke_bomb" => 100,
                "poison_bomb" => 300,
                "dynamite_bundle" => 400,
                "alert_trip_mine" => 200,
                "concertina_trip_mine" => 300,
                "poison_trip_mine" => 250,
                "ammo_box" => 600,
                "energy_grenade" => 400,
                _ => 0
            };
        }

        private static bool IsBulkSupply(ItemDefinition definition) {
            return definition.Kind is ItemKind.Cell or ItemKind.Throwable or ItemKind.Medical or ItemKind.Food
                or ItemKind.Drink or ItemKind.Oxygen;
        }

        // 购买受钱包、单次限制和物品种类限制。电池等少数补给允许成批选择，非堆叠装备按件。
        public static int PurchaseLimit(ItemDefinition definition, int credits) {
            int price = Price(definition);
            if (price <= 0) return 0;
            int affordable = credits / price;
            return IsBulkSupply(definition) ? affordable : Math.Min(1, affordable);
        }

        // 在临时库存用版本检查和放置规则购买。成功回调后房主上层再扣席位的购买额度。
        public static InventoryError Purchase(InventoryGraph graph, string code, int version, int quantity = 1) {
            var definition = InventoryCatalog.Get(code);
            if (Price(definition) <= 0 || quantity < 1) return InventoryError.Invalid;
            if (graph == null || graph.Version != version)
                return graph == null ? InventoryError.Invalid : InventoryError.Stale;
            if (IsBulkSupply(definition)) return graph.AddSupply(code, quantity);
            return quantity == 1 ? ArenaArmory.Issue(graph, code, version) : InventoryError.Invalid;
        }

        public static InventoryGraph StartingKit() {
            var kit = InventoryGraph.Create(false);
            kit.Items.Remove(kit.Equipped("Primary"));
            kit.Items.Remove(kit.Equipped("Secondary"));
            kit.AddSupply("cells", 6);
            return kit;
        }
    }
}