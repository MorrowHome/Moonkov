using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class AlienAttackDemo
    {
        public readonly struct AttackEntry
        {
            public readonly int ProfileIndex;
            public readonly AlienAttackKind Kind;
            public readonly string Name;
            public readonly float Distance;
            public AttackEntry(int profile, AlienAttackKind kind, string name, float distance)
            { ProfileIndex = profile; Kind = kind; Name = name; Distance = distance; }
        }

        private readonly List<AttackEntry> m_Attacks = new();
        public IReadOnlyList<AttackEntry> Attacks => m_Attacks;
        public AlienCreatureProfile[] Profiles => m_Profiles;

        private void BuildCatalog()
        {
            m_Attacks.Clear();
            for (int i = 0; i < m_Profiles.Length; i++)
            {
                var p = m_Profiles[i];
                if (!p) throw new System.InvalidOperationException("Missing demo profile " + i);
                if (p.IsSpiderBoss)
                {
                    Add(AlienAttackKind.SpiderRake, "前足重击", 3);
                    Add(AlienAttackKind.SpiderRush, "直线扑冲", 9);
                    Add(AlienAttackKind.MucusSpit, "远距黏液", 18);
                    continue;
                }
                switch (p.Species)
                {
                    case AlienSpecies.Spider: Add(AlienAttackKind.RoofPounce, "伏顶扑击", 2); break;
                    case AlienSpecies.Snake: Add(AlienAttackKind.Bite, "突进撕咬", 2.3f); break;
                    case AlienSpecies.Caterpillar: Add(AlienAttackKind.AcidSpit, "酸液连喷", 6); break;
                    case AlienSpecies.Tentacle: Add(AlienAttackKind.TentacleSweep, "触肢横扫", 3); break;
                    case AlienSpecies.Centipede: Add(AlienAttackKind.Charge, "直线冲撞", 5); break;
                    case AlienSpecies.Leech: Add(AlienAttackKind.LeechDrain, "吸血", 1); break;
                    case AlienSpecies.Slime: Add(AlienAttackKind.SlimePulse, "近身三连脉冲", 2); break;
                    case AlienSpecies.Roots: Add(AlienAttackKind.RootSpikes, "地面根刺", 5); break;
                    case AlienSpecies.FaceHugger: Add(AlienAttackKind.FaceLeap, "跃起覆面", 2.5f); break;
                    case AlienSpecies.StiltWalker: Add(AlienAttackKind.StiltRake, "长肢抓击", Mathf.Min(2.5f, p.AttackReach * .7f)); break;
                }
                if (p.MucusEveryAttacks > 0 && (p.Species == AlienSpecies.Caterpillar || p.Species == AlienSpecies.Slime))
                    Add(AlienAttackKind.MucusSpit, "黏液喷吐", 9);
                void Add(AlienAttackKind kind, string name, float distance) => m_Attacks.Add(new AttackEntry(i, kind, name, distance));
            }
        }
    }
}
