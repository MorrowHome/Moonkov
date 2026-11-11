using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    internal readonly struct ClassicBotOrder
    {
        public readonly Vector3 Goal, Watch;
        public readonly Vector3? Approach;
        public readonly bool Guard;

        public ClassicBotOrder(Vector3 goal, Vector3 watch, bool guard = false, Vector3? approach = null)
        {
            Goal = goal;
            Watch = watch;
            Guard = guard;
            Approach = approach;
        }
    }

    internal sealed class ClassicBotTactics
    {
        // Dust II coordinates on the imported NavMesh. The hold positions watch
        // different entrances even when the round objective is on the same site.
        private static readonly Vector3 s_Long = new(39.50f, 99.80f, 36.40f);
        private static readonly Vector3 s_Short = new(52.22f, 102.44f, 11.60f);
        private static readonly Vector3 s_Tunnels = new(30.60f, 100.85f, -46.80f);
        private static readonly Vector3 s_MidToB = new(58.00f, 98.25f, -23.40f);
        private static readonly ClassicBotOrder[] s_GuardPosts =
        {
            new(new Vector3(61.00f, 102.51f, 30.50f), new Vector3(49, 99.80f, 36.50f), true), // A Long
            new(new Vector3(65.50f, 100.82f, -47.90f), new Vector3(49, 100.00f, -47.90f), true), // B Tunnels
            new(new Vector3(54.70f, 97.03f, -15.80f), new Vector3(43.70f, 96.83f, -12.20f), true), // Mid
            new(new Vector3(64.20f, 102.51f, 22.80f), s_Short, true), // A Short
            new(new Vector3(60.00f, 100.06f, -35.42f), new Vector3(56.70f, 98.80f, -29.60f), true) // B Doors
        };
        private static readonly ClassicBotOrder[] s_AfterPlantPosts =
        {
            new(new Vector3(61.00f, 102.51f, 30.50f), s_Long, true),
            new(new Vector3(64.20f, 102.51f, 22.80f), s_Short, true),
            new(new Vector3(60.99f, 100.83f, 33.88f), new Vector3(69, 97.5f, 8), true),
            new(new Vector3(63.50f, 102.51f, 25.06f), s_Short, true),
            new(new Vector3(61.60f, 102.47f, 28.90f), new Vector3(69, 97.5f, 8), true),
            new(new Vector3(65.50f, 100.82f, -47.90f), s_Tunnels, true),
            new(new Vector3(60.00f, 100.06f, -35.42f), s_MidToB, true),
            new(new Vector3(63.28f, 100.18f, -41.59f), new Vector3(47.60f, 100.08f, -47.90f), true),
            new(new Vector3(62.40f, 100.06f, -37.70f), s_MidToB, true),
            new(new Vector3(67.30f, 100.18f, -42.25f), s_MidToB, true)
        };

        internal struct Combatant
        {
            public Entity Actor;
            public int Id, Team, Slot;
            public bool Bot, Alive;
            public Vector3 Position, ShotPosition;
            public uint Shot;
            public double ShotAt;
        }

        private struct Contact
        {
            public int Site;
            public double Until;
        }

        public readonly Combatant[] Roster = new Combatant[ClassicMatch.Capacity];
        private readonly Contact[] m_Contacts = new Contact[2 * ClassicMatch.Capacity];
        private readonly int[] m_Pressure = new int[4];
        private readonly NavMeshPath m_ObjectivePath = new();
        private readonly Vector3[] m_ObjectiveCorners = new Vector3[96];
        private int m_AttackSite, m_CarrierSlot, m_RecoveryBot, m_DefuseBot;
        private double m_DefuseChoiceUntil, m_RecoveryChoiceUntil;

        public void BeginRound(int round, int carrierSlot)
        {
            Array.Clear(Roster, 0, Roster.Length);
            Array.Clear(m_Contacts, 0, m_Contacts.Length);
            m_AttackSite = (round - 1) % 2;
            m_CarrierSlot = carrierSlot;
            m_DefuseBot = m_RecoveryBot = 0;
            m_DefuseChoiceUntil = m_RecoveryChoiceUntil = 0;
        }

        public void BeginTick()
        {
            for (int i = 0; i < Roster.Length; i++) Roster[i].Alive = false;
        }

        private static int SeatIndex(int team, int slot) => team * ClassicMatch.TeamSize + slot;

        public void Observe(Entity actor, int id, int team, int slot, bool bot, Vector3 position, uint shot, double now)
        {
            ref var entry = ref Roster[SeatIndex(team, slot)];
            if (entry.Actor != actor) entry = default;
            if (entry.Shot != shot && shot != 0)
            {
                entry.Shot = shot;
                entry.ShotAt = now;
                entry.ShotPosition = position;
            }
            entry.Actor = actor;
            entry.Id = id;
            entry.Team = team;
            entry.Slot = slot;
            entry.Bot = bot;
            entry.Alive = true;
            entry.Position = position;
        }

        public bool Alive(ClassicBot bot) => Roster[SeatIndex(bot.Team, bot.Slot)].Alive;

        public void Report(int team, int observedSeat, Vector3 position, ClassicDust2Map map, double now)
        {
            float distanceA = (position - map.Site(0)).sqrMagnitude;
            float distanceB = (position - map.Site(1)).sqrMagnitude;
            if (Mathf.Min(distanceA, distanceB) > 32 * 32) return;
            m_Contacts[team * ClassicMatch.Capacity + observedSeat] = new Contact
            {
                Site = distanceA < distanceB ? 0 : 1,
                Until = now + 10
            };
        }

        public void UpdateOrders(ClassicMatch rules, ClassicDust2Map map, double now)
        {
            Array.Clear(m_Pressure, 0, m_Pressure.Length);
            for (int i = 0; i < m_Contacts.Length; i++)
                if (m_Contacts[i].Until > now && Roster[i % Roster.Length].Alive)
                    m_Pressure[i / Roster.Length * 2 + m_Contacts[i].Site]++;

            if (rules.BombOnGround && (now >= m_RecoveryChoiceUntil || m_RecoveryBot != 0 && !IsAlive(m_RecoveryBot)))
            {
                m_RecoveryBot = NearestBot(rules.AttackTeam, rules.BombPosition, m_RecoveryBot);
                m_RecoveryChoiceUntil = now + 3;
            }
            if (!rules.BombOnGround)
            {
                m_RecoveryBot = 0;
                m_RecoveryChoiceUntil = 0;
            }

            if (rules.Phase == ClassicPhase.Planted)
            {
                if (rules.HoldActor != 0)
                {
                    m_DefuseBot = rules.HoldActor < 0 ? rules.HoldActor : 0;
                    m_DefuseChoiceUntil = 0;
                }
                else if (now >= m_DefuseChoiceUntil || m_DefuseBot != 0 && !IsAlive(m_DefuseBot))
                {
                    m_DefuseBot = NearestBot(1 - rules.AttackTeam, rules.BombPosition, m_DefuseBot);
                    m_DefuseChoiceUntil = now + 3;
                }
                return;
            }

            foreach (var entry in Roster)
            {
                if (!entry.Alive || rules.Carrier != entry.Id) continue;
                m_CarrierSlot = entry.Slot;
                // Respect a human carrier who takes the bomb to the other site.
                if (!entry.Bot && map.InSite(1 - m_AttackSite, entry.Position)) m_AttackSite = 1 - m_AttackSite;
                break;
            }
        }

        private bool IsAlive(int id)
        {
            foreach (var entry in Roster)
                if (entry.Alive && entry.Id == id) return true;
            return false;
        }

        private int NearestBot(int team, Vector3 goal, int current)
        {
            float nearest = float.PositiveInfinity;
            float nearestFallback = float.PositiveInfinity;
            int result = 0, fallback = 0;
            bool sampled = NavMesh.SamplePosition(goal, out var to, 2.25f, NavMesh.AllAreas);
            foreach (var entry in Roster)
            {
                if (!entry.Alive || !entry.Bot || entry.Team != team) continue;
                float distance = (entry.Position - goal).sqrMagnitude;
                if (distance < nearestFallback) { nearestFallback = distance; fallback = entry.Id; }
                if (!sampled || !NavMesh.SamplePosition(entry.Position, out var from, 1.65f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, m_ObjectivePath) ||
                    m_ObjectivePath.status != NavMeshPathStatus.PathComplete) continue;
                int count = m_ObjectivePath.GetCornersNonAlloc(m_ObjectiveCorners);
                float cost = 0;
                for (int i = 1; i < count; i++) cost += Vector3.Distance(m_ObjectiveCorners[i - 1], m_ObjectiveCorners[i]);
                // Keep a runner moving when another route is only slightly shorter.
                if (entry.Id == current) cost *= .75f;
                if (cost >= nearest) continue;
                nearest = cost;
                result = entry.Id;
            }
            return result != 0 ? result : fallback;
        }

        public bool HasBombAssignment(int id, ClassicUse use) => use switch
        {
            ClassicUse.PickUp => m_RecoveryBot == id,
            ClassicUse.Defuse => m_DefuseBot == id,
            ClassicUse.PlantA or ClassicUse.PlantB => true,
            _ => false
        };

        public ClassicBotOrder Order(ClassicBot bot, ClassicMatch rules, ClassicDust2Map map)
        {
            bool attacking = bot.Team == rules.AttackTeam;
            if (rules.Phase == ClassicPhase.Planted)
            {
                int site = map.InSite(0, rules.BombPosition) ? 0 : 1;
                if (!attacking && bot.Id == m_DefuseBot) return new ClassicBotOrder(rules.BombPosition, rules.BombPosition);
                return s_AfterPlantPosts[site * ClassicMatch.TeamSize + bot.Slot];
            }
            if (attacking)
            {
                if (bot.Id == m_RecoveryBot && rules.BombOnGround) return new ClassicBotOrder(rules.BombPosition, rules.BombPosition);
                var post = s_AfterPlantPosts[m_AttackSite * ClassicMatch.TeamSize + bot.Slot];
                int rank = (bot.Slot - m_CarrierSlot + ClassicMatch.TeamSize) % ClassicMatch.TeamSize;
                bool mainLane = rank <= 2;
                Vector3 approach = m_AttackSite == 0 ? (mainLane ? s_Short : s_Long) : (mainLane ? s_Tunnels : s_MidToB);
                if (bot.Id == rules.Carrier) return new ClassicBotOrder(map.Site(m_AttackSite), post.Watch, approach: approach);
                return new ClassicBotOrder(post.Goal, post.Watch, true, approach);
            }

            int pressureA = m_Pressure[bot.Team * 2], pressureB = m_Pressure[bot.Team * 2 + 1];
            if (pressureA > pressureB && (bot.Slot == 2 || bot.Slot == 4 && pressureA >= 2 && pressureB == 0))
                return s_AfterPlantPosts[bot.Slot];
            if (pressureB > pressureA && (bot.Slot == 2 || bot.Slot == 3 && pressureB >= 2 && pressureA == 0))
                return s_AfterPlantPosts[ClassicMatch.TeamSize + bot.Slot];
            return s_GuardPosts[bot.Slot];
        }
    }
}
