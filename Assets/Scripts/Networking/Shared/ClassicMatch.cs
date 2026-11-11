using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum ClassicPhase : byte { Waiting, Buy, Live, Planted, RoundOver, Finished }
    public enum ClassicUse : byte { None, PickUp, PlantA, PlantB, Defuse }

    public struct ClassicSnapshotRpc : IRpcCommand
    {
        public ClassicPhase Phase;
        public ClassicUse Use;
        public int Round, Team, AttackTeam, ScoreA, ScoreB, AliveA, AliveB, Credits, Winner;
        public float Remaining, UseProgress;
        public bool HasBomb, Alive, BombVisible;
        public Unity.Mathematics.float3 BombPosition;
    }

    public struct ClassicClientState : IComponentData
    {
        public ClassicSnapshotRpc Snapshot;
        public bool Received;
    }

    public struct ClassicUseRpc : IRpcCommand
    {
        public int Round;
        public bool Held;
    }

    public sealed class ClassicMatch
    {
        public const int Version = 1, TeamSize = 5, Capacity = TeamSize * 2, Half = 12, ToWin = 13;
        public const double BuySeconds = 12, RoundSeconds = 115, FuseSeconds = 40, BreakSeconds = 5;
        public ClassicPhase Phase { get; private set; }
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

        public bool NeedsRound(double now) => Phase == ClassicPhase.Waiting || Phase == ClassicPhase.RoundOver && now >= Deadline;

        public void BeginRound(double now, int carrier)
        {
            if (!NeedsRound(now)) return;
            Round++;
            Carrier = carrier;
            BombOnGround = false;
            BombPosition = default;
            RoundWinner = -1;
            HoldActor = 0;
            Phase = ClassicPhase.Buy;
            Deadline = now + BuySeconds;
        }

        public void Advance(double now, int attackersAlive, int defendersAlive)
        {
            if (Phase == ClassicPhase.Buy && now >= Deadline)
            {
                Phase = ClassicPhase.Live;
                Deadline = now + RoundSeconds;
            }
            if (Phase == ClassicPhase.Live)
            {
                if (attackersAlive == 0 || now >= Deadline) EndRound(1 - AttackTeam, now);
                else if (defendersAlive == 0) EndRound(AttackTeam, now);
            }
            else if (Phase == ClassicPhase.Planted && (now >= Deadline || defendersAlive == 0)) EndRound(AttackTeam, now);
            if (HoldActor != 0 && now > m_HoldUntil) HoldActor = 0;
        }

        public void DropBomb(int actor, Vector3 position)
        {
            if (Carrier != actor) return;
            Carrier = 0;
            BombOnGround = true;
            BombPosition = position;
            CancelHold(actor);
        }

        public void CancelHold(int actor)
        {
            if (HoldActor == actor) HoldActor = 0;
        }

        public static double UseSeconds(ClassicUse use) => use switch
        {
            ClassicUse.PlantA or ClassicUse.PlantB => 3.2,
            ClassicUse.Defuse => 10,
            _ => 0
        };

        public float HoldProgress(int actor, double now) => HoldActor == actor && actor != 0
            ? Mathf.Clamp01((float)((now - m_HoldStarted) / UseSeconds(m_HoldUse))) : 0;

        // Position, visibility and alive state are sampled by the host every tick.
        public void Use(int actor, int team, ClassicUse use, Vector3 position, uint damage, double now)
        {
            if (actor == 0 || now >= Deadline) return;
            if (Phase == ClassicPhase.Live && use == ClassicUse.PickUp && team == AttackTeam && BombOnGround)
            {
                Carrier = actor;
                BombOnGround = false;
                return;
            }
            bool plant = Phase == ClassicPhase.Live && team == AttackTeam && Carrier == actor &&
                (use == ClassicUse.PlantA || use == ClassicUse.PlantB);
            bool defuse = Phase == ClassicPhase.Planted && team != AttackTeam && use == ClassicUse.Defuse;
            if (!plant && !defuse) { CancelHold(actor); return; }
            if (HoldActor != 0 && HoldActor != actor && now <= m_HoldUntil) return;
            if (HoldActor != actor || use != m_HoldUse || now > m_HoldUntil ||
                damage != m_HoldDamage || (position - m_HoldPosition).sqrMagnitude > .35f * .35f)
            {
                HoldActor = actor;
                m_HoldUse = use;
                m_HoldStarted = now;
                m_HoldPosition = position;
                m_HoldDamage = damage;
            }
            m_HoldUntil = now + .7;
            if (now - m_HoldStarted < UseSeconds(use)) return;
            HoldActor = 0;
            if (defuse) { EndRound(team, now); return; }
            Carrier = 0;
            Phase = ClassicPhase.Planted;
            BombPosition = position;
            Deadline = now + FuseSeconds;
        }

        private void EndRound(int team, double now)
        {
            RoundWinner = team;
            Score[team]++;
            HoldActor = 0;
            if (Score[team] >= ToWin || Round == Half * 2)
            {
                Winner = Score[team] >= ToWin ? team : -1;
                Phase = ClassicPhase.Finished;
                Deadline = now;
            }
            else
            {
                Phase = ClassicPhase.RoundOver;
                Deadline = now + BreakSeconds;
            }
        }

        public static int Price(ItemDefinition definition) => definition?.Kind switch
        {
            ItemKind.LongGun => 2700,
            ItemKind.Pistol => 500,
            ItemKind.Helmet or ItemKind.Shield => 650,
            ItemKind.Throwable when definition.Code is "smoke_grenade" or "frag_bomb" or "fire_bomb" or "choke_bomb" => 300,
            ItemKind.Cell => 100,
            _ => 0
        };

        public static InventoryGraph StartingKit()
        {
            var kit = InventoryGraph.Create(stash: false);
            kit.Items.Remove(kit.Equipped("Primary"));
            kit.Items.Remove(kit.Equipped("Secondary"));
            kit.AddSupply("cells", 6);
            return kit;
        }
    }
}
