using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum ArenaPhase : byte { Waiting, Fighting, Finished }

    public struct ArenaSnapshotRpc : IRpcCommand
    {
        public ArenaPhase Phase;
        public int Players, Kills, BestScore;
        public float Remaining, Respawn;
        public bool Won, Draw;
    }

    public struct ArenaClientState : IComponentData
    {
        public ArenaSnapshotRpc Snapshot;
        public bool Received;
    }

    public struct ArenaSupplyRpc : IRpcCommand
    {
        public FixedString64Bytes Code;
        public uint RequestId;
        public int ExpectedVersion;
    }

    public sealed class ArenaMatch : IComponentData
    {
        public const int Version = 1, Capacity = 5, TargetScore = 30;
        public const float Duration = 600, RespawnSeconds = 3, LoadSeconds = 45;

        public sealed class Player
        {
            public string MemberId;
            public int Id, Score, Character;
            public FixedString64Bytes Name;
            public double RespawnAt;
            public bool Spawned;
        }

        public readonly Dictionary<Entity, Player> Players = new();
        public readonly HashSet<Unity.Mathematics.float3> ReservedSpawns = new();
        public ArenaPhase Phase { get; private set; }
        public double Deadline { get; private set; }
        public int BestScore { get; private set; }
        public int Winner { get; private set; }
        public double NextSnapshot;
        private double m_LoadDeadline;
        private int m_NextId;

        public Player Join(Entity connection, string memberId, string name, int character, double now)
        {
            if (Phase == ArenaPhase.Finished || Players.Count >= Capacity ||
                Players.ContainsKey(connection) || Players.Values.Any(player => player.MemberId == memberId)) return null;
            var seat = new Player { MemberId = memberId, Id = ++m_NextId, Character = character };
            seat.Name.CopyFromTruncated(name);
            if (Players.Count == 0 && Phase == ArenaPhase.Waiting) m_LoadDeadline = now + LoadSeconds;
            Players.Add(connection, seat);
            return seat;
        }

        public void Tick(double now, int expectedPlayers)
        {
            if (Phase == ArenaPhase.Waiting && Players.Count > 0 &&
                (Players.Count >= Math.Max(2, expectedPlayers) || now >= m_LoadDeadline))
            {
                if (Players.Count < 2) { Phase = ArenaPhase.Finished; return; }
                Phase = ArenaPhase.Fighting;
                Deadline = now + Duration;
            }
            if (Phase == ArenaPhase.Fighting && (Players.Count < 2 || now >= Deadline)) Finish();
        }

        public void RecordKill(int attacker, Player victim)
        {
            if (Phase != ArenaPhase.Fighting || attacker == victim.Id) return;
            foreach (var player in Players.Values)
            {
                if (player.Id != attacker) continue;
                player.Score++;
                BestScore = Math.Max(BestScore, player.Score);
                if (player.Score >= TargetScore) Finish();
                return;
            }
        }

        private void Finish()
        {
            Phase = ArenaPhase.Finished;
            if (Players.Count == 0) return;
            BestScore = Players.Values.Max(player => player.Score);
            var leaders = Players.Values.Where(player => player.Score == BestScore).Take(2).ToArray();
            Winner = leaders.Length == 1 ? leaders[0].Id : 0;
        }
    }

    public static class ArenaArmory
    {
        public static bool Available(ItemDefinition item) => item != null && item.Kind != ItemKind.Root &&
            item.Kind != ItemKind.Material && !InventoryCatalog.IsResearchSupply(item.Code);

        public static InventoryGraph NewKit()
        {
            var graph = InventoryGraph.Create(stash: false);
            graph.AddSupply("cells", RaidRules.BagCapacity);
            return graph;
        }

        public static InventoryError Issue(InventoryGraph graph, string code, int version)
        {
            if (graph.Version != version) return InventoryError.Stale;
            var definition = InventoryCatalog.Get(code);
            if (!Available(definition)) return InventoryError.Invalid;
            if (definition.Kind is ItemKind.Cell or ItemKind.Medical or ItemKind.Food or ItemKind.Drink or
                ItemKind.Oxygen or ItemKind.Throwable or ItemKind.StorageModule or ItemKind.ShapingModule or ItemKind.UtilityModule)
                return graph.AddSupply(code, definition.MaxStack);

            var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code };
            foreach (var parent in graph.CarryContainers().Prepend(InventoryCatalog.Equipment))
            {
                if (!graph.FindSpace(item, parent, out var region, out var x, out var y)) continue;
                if (graph.CarriedWeight + definition.Weight > InventoryCatalog.CarryWeightLimit) return InventoryError.Overweight;
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
