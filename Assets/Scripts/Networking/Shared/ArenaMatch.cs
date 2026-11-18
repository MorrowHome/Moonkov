// 临时装备玩法的比赛状态与补给选择。记录加入的玩家、击杀和时间，给房主使用。经典爆破的细分回合由 ClassicMatch 负责。
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public enum ArenaPhase : byte {
        Waiting,
        Fighting,
        Finished
    }

    public struct ArenaSnapshotRpc : IRpcCommand {
        public ArenaPhase Phase;
        public int Players, Kills, BestScore;
        public float Remaining, Respawn;
        public bool Won, Draw;
    }

    public struct ArenaClientState : IComponentData {
        public ArenaSnapshotRpc Snapshot;
        public bool Received;
    }

    public struct ArenaSupplyRpc : IRpcCommand {
        public FixedString64Bytes Code;
        public uint RequestId;
        public int ExpectedVersion, Quantity;
    }

    public sealed class ArenaMatch : IComponentData {
        public const int Version = 2, Capacity = 5, TargetScore = 30;
        public const float Duration = 600, RespawnSeconds = 3, LoadSeconds = 45;

        public sealed class Player {
            public string MemberId;
            public int Id, Score, Character, Team, Slot, Credits = 800;
            public double UseUntil;
            public UnityEngine.Vector3 LastPosition;
            public FixedString64Bytes Name;
            public double RespawnAt;
            public bool Spawned;
        }

        public readonly RaidGameMode Mode;
        public readonly ClassicMatch Classic;
        internal readonly Dictionary<int, ClassicBot> Bots = new();
        internal readonly ClassicBotTactics ClassicTactics;
        internal readonly HashSet<Entity> ClassicDeaths = new();
        public readonly Dictionary<Entity, Player> Players = new();

        // 首次开赛等本轮允许的玩家数量，或等加入窗口走完。这样先到的房主不会太早进入首回合。
        public bool FirstRoundReady(double now, int expected) {
            return Players.Count > 0 && (Players.Count >= expected || now >= m_LoadDeadline);
        }

        public ArenaMatch() : this(RaidGameMode.Arena) { }

        public ArenaMatch(RaidGameMode mode) {
            Mode = mode;
            if (mode == RaidGameMode.Classic) {
                Classic = new ClassicMatch();
                ClassicTactics = new ClassicBotTactics();
            }
        }

        // 竞技场和经典爆破都使用比赛临时装备。退房不会把临时配发物写进远征仓库。
        public static bool UsesTemporaryKit(RaidGameMode mode) {
            return mode is RaidGameMode.Arena or RaidGameMode.Classic;
        }

        public int RulesVersion => Classic == null ? Version : ClassicMatch.Version;
        public int Epoch => Classic?.Round ?? 1;

        public bool IsAlly(int actor, int team) {
            if (Classic == null || actor == 0) return false;
            if (Bots.TryGetValue(actor, out var bot)) return bot.Team == team;
            return Players.Values.Any(player => player.Id == actor && player.Team == team);
        }

        public readonly HashSet<Mathematics.float3> ReservedSpawns = new();
        public ArenaPhase Phase { get; private set; }
        public double Deadline { get; private set; }
        public int BestScore { get; private set; }
        public int Winner { get; private set; }
        public double NextSnapshot;
        internal int CreditedRound;
        private double m_LoadDeadline;
        private int m_NextId;

        // 登记房间成员后才分席位及队伍。经典按空位排队伍，竞技场的参赛者是各自对抗。
        public Player Join(Entity connection, string memberId, string name, int character, double now) {
            if (Phase == ArenaPhase.Finished || Classic?.Round > 0 ||
                Players.Count >= (Classic == null ? Capacity : ClassicMatch.Capacity) ||
                Players.ContainsKey(connection) || Players.Values.Any(player => player.MemberId == memberId))
                return null;
            var seat = new Player { MemberId = memberId, Id = ++m_NextId, Character = character };
            seat.Name.CopyFromTruncated(name);
            if (Classic != null) {
                int firstTeam = Players.Values.Count(player => player.Team == 0);
                seat.Team = firstTeam <= Players.Count - firstTeam ? 0 : 1;
                seat.Slot = Enumerable.Range(0, ClassicMatch.TeamSize).First(slot =>
                    !Players.Values.Any(player => player.Team == seat.Team && player.Slot == slot));
            } else { seat.Team = seat.Id; }
            if (Players.Count == 0 && Phase == ArenaPhase.Waiting)
                m_LoadDeadline = now + (Classic == null ? LoadSeconds : 120);
            Players.Add(connection, seat);
            return seat;
        }

        // 有充分玩家或等候条件满足后开场。实时计时到期时，按当前分数决定最后结果。
        public void Tick(double now, int expectedPlayers) {
            if (Phase == ArenaPhase.Waiting && Players.Count > 0 &&
                (Players.Count >= Math.Max(2, expectedPlayers) || now >= m_LoadDeadline)) {
                if (Players.Count < 2) {
                    Phase = ArenaPhase.Finished;
                    return;
                }
                Phase = ArenaPhase.Fighting;
                Deadline = now + Duration;
            }
            if (Phase == ArenaPhase.Fighting && (Players.Count < 2 || now >= Deadline)) Finish();
        }

        // 记录观战和重生时刻，并给符合条件的攻击者算击杀分。比赛已结束时不再改变结果。
        public void RecordKill(int attacker, Player victim) {
            if (Phase != ArenaPhase.Fighting || attacker == victim.Id) return;
            foreach (var player in Players.Values) {
                if (player.Id != attacker) continue;
                player.Score++;
                BestScore = Math.Max(BestScore, player.Score);
                if (player.Score >= TargetScore) Finish();
                return;
            }
        }

        // 收局时固定赢家或平局，供全体客户端结算。不存在跨局长期加分。
        private void Finish() {
            Phase = ArenaPhase.Finished;
            if (Players.Count == 0) return;
            BestScore = Players.Values.Max(player => player.Score);
            var leaders = Players.Values.Where(player => player.Score == BestScore).Take(2).ToArray();
            Winner = leaders.Length == 1 ? leaders[0].Id : 0;
        }
    }

    public static class ArenaArmory {
        public static bool Available(ItemDefinition item) {
            return item != null && item.Kind != ItemKind.Root && item.Kind != ItemKind.Material &&
                   !InventoryCatalog.IsResearchSupply(item.Code);
        }

        public static InventoryGraph NewKit() {
            var graph = InventoryGraph.Create(false);
            graph.AddSupply("cells", RaidRules.BagCapacity);
            return graph;
        }

        // 免费补给仍走背包版本和实际容量。临时装备模式也不能发无定义或超过包能装的物资。
        public static InventoryError Issue(InventoryGraph graph, string code, int version) {
            if (graph.Version != version) return InventoryError.Stale;
            var definition = InventoryCatalog.Get(code);
            if (!Available(definition)) return InventoryError.Invalid;
            if (definition.Kind is ItemKind.Cell or ItemKind.Medical or ItemKind.Food or ItemKind.Drink
                or ItemKind.Oxygen or ItemKind.Throwable or ItemKind.StorageModule or ItemKind.ShapingModule
                or ItemKind.UtilityModule)
                return graph.AddSupply(code, definition.MaxStack);
            var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code };
            foreach (string parent in graph.CarryContainers().Prepend(InventoryCatalog.Equipment)) {
                if (!graph.FindSpace(item, parent, out string region, out int x, out int y)) continue;
                if (graph.CarriedWeight + definition.Weight > InventoryCatalog.CarryWeightLimit)
                    return InventoryError.Overweight;
                item.Parent = parent;
                item.Region = region;
                item.X = x;
                item.Y = y;
                graph.Items.Add(item);
                graph.Version++;
                return InventoryError.None;
            }
            return InventoryError.Full;
        }
    }
}