// 为各个物种和 Boss 阶段提供攻击距离、范围、预警、命中和恢复时间。一次攻击开始时就定下类型和阶段。
// 服务端伤害、客户端预警和音效用同一套命中时刻，才不会各演各的。
using UnityEngine;

namespace Unity.MP_FPS {
    public enum AlienAttackKind : byte {
        Bite,
        Charge,
        AcidSpit,
        TentacleSweep,
        FaceLeap,
        SlimePulse,
        RootSpikes,
        TentacleSlam,
        RoofPounce,
        LeechDrain,
        StiltRake,
        MucusSpit,
        SpiderRush,
        SpiderRake
    }

    /// <summary>不同物种有各自的出招节拍和命中形状。
    /// Boss 阶段在蓄力开始时确定，后续血量变化也不会改掉已经显示的预警。</summary>
    public struct AlienCombatPattern {
        public AlienAttackKind Kind;
        public float Range, Radius, Windup, Active, Recovery, DamageScale, Speed;
        public int Pulses;

        // 服务端伤害、预警闪烁和命中音效共用同一个时钟。
        // 某些技能有好几次伤害节拍。这里把每一拍换算成攻击开始后多少秒。
        public float PulseTime(int pulse) { return .08f + pulse * Active / Mathf.Max(1, Pulses); }

        // 用当前动作经过的秒数换出现在第几拍，第一次命中以前用 -1 表示尚未开始。
        public int PulseAt(float age) {
            return age < PulseTime(0)
                ? -1
                : Mathf.Clamp(Mathf.FloorToInt((age - .08f) / Mathf.Max(.01f, Active / Mathf.Max(1, Pulses))), 0,
                    Mathf.Max(0, Pulses - 1));
        }

        // 根须尖刺的后一拍沿攻击方向移动，其他技能可以沿用原攻击中心。
        public Vector3 PulsePoint(Vector3 point, Vector3 direction, int pulse) {
            return Kind == AlienAttackKind.RootSpikes
                ? point + direction * (Mathf.Clamp(pulse, 0, Mathf.Max(0, Pulses - 1)) * Radius * 1.6f)
                : point;
        }

        public static bool SweepContains(
            Vector3 point,
            Vector3 origin,
            Vector3 direction,
            float radius,
            float bodyHeight) {
            // 扫击先在水平面上算距离和扇区，另比较高度，避免同一方向不同楼层也算命中。
            var delta = Vector3.ProjectOnPlane(point - origin, Vector3.up);
            return delta.magnitude <= radius && Vector3.Angle(direction, delta) <= 65 &&
                   Mathf.Abs(point.y - origin.y + bodyHeight) < 2;
        }

        public static AlienCombatPattern For(
            AlienCreatureProfile profile,
            byte phase,
            uint sequence,
            AlienAttackKind? committed = null) {
            float scale = Mathf.Clamp(profile.Scale, .1f, 4);
            // 巨蛛突进和扫击有自己按实际体型调好的参数。这里专门处理，喷吐仍能走通用逻辑。
            if (profile.IsSpiderBoss && committed != AlienAttackKind.MucusSpit) {
                // Boss 配置里的距离已经是世界空间数值。
                // 巨蛛的触及距离和速度无需再乘通用的异形尺寸。
                bool rush = committed == AlienAttackKind.SpiderRush;
                float active = rush ? Mathf.Max(.4f, profile.SpiderStrikeSeconds) : .45f;
                float range = rush ? profile.SpiderAmbushRange : profile.AttackReach * scale;
                return new AlienCombatPattern {
                    Kind = rush ? AlienAttackKind.SpiderRush : AlienAttackKind.SpiderRake,
                    Range = range, Radius = rush ? .38f * scale : range,
                    Windup = Mathf.Max(1.15f, (rush ? profile.SpiderWindup : profile.Windup) * (phase > 1 ? .85f : 1)),
                    Active = active,
                    Recovery = Mathf.Max(.9f,
                        (rush ? profile.SpiderRecovery : profile.Recovery) * (phase > 1 ? .8f : 1)),
                    Speed = rush ? range / active : 0, DamageScale = rush ? 1.15f : 1, Pulses = 1
                };
            }
            var p = new AlienCombatPattern {
                Range = 2.2f, Radius = .8f, Windup = .55f, Active = .3f,
                Recovery = .8f, DamageScale = 1, Speed = 0, Pulses = 1
            };
            switch (profile.Species) {
                case AlienSpecies.Spider:
                    p.Kind = AlienAttackKind.RoofPounce;
                    p.Range = profile.SpiderAmbushRange;
                    p.Windup = profile.SpiderWindup;
                    p.Active = profile.SpiderStrikeSeconds;
                    p.Recovery = profile.SpiderRecovery;
                    break;
                case AlienSpecies.Snake:
                    p.Kind = AlienAttackKind.Bite;
                    p.Range = 3.4f;
                    p.Active = .45f;
                    p.Radius = .65f;
                    p.Speed = 8;
                    break;
                case AlienSpecies.Caterpillar:
                    p.Kind = AlienAttackKind.AcidSpit;
                    p.Range = 10;
                    p.Radius = 1.6f;
                    p.Windup = 1;
                    p.Active = 2;
                    p.Recovery = 1.2f;
                    p.Pulses = 3;
                    p.DamageScale = .4f;
                    break;
                case AlienSpecies.Centipede:
                    p.Kind = AlienAttackKind.Charge;
                    p.Range = 8;
                    p.Radius = .9f;
                    p.Windup = .9f;
                    p.Active = .8f;
                    p.Recovery = 1.4f;
                    p.Speed = 10;
                    break;
                case AlienSpecies.Leech:
                    p.Kind = AlienAttackKind.LeechDrain;
                    p.Range = 1.5f;
                    p.Windup = .5f;
                    p.Active = 1.6f;
                    p.Recovery = 1;
                    p.Radius = 1.7f;
                    p.Pulses = 3;
                    p.DamageScale = .3f;
                    break;
                case AlienSpecies.FaceHugger:
                    p.Kind = AlienAttackKind.FaceLeap;
                    p.Range = 4.5f;
                    p.Windup = .65f;
                    p.Active = .45f;
                    p.Recovery = 1.4f;
                    p.Radius = .5f;
                    break;
                case AlienSpecies.StiltWalker:
                    p.Kind = AlienAttackKind.StiltRake;
                    p.Range = profile.AttackReach;
                    p.Windup = profile.Windup;
                    p.Active = .45f;
                    p.Recovery = profile.Recovery;
                    p.Radius = .65f;
                    p.Speed = 2.8f;
                    break;
                case AlienSpecies.Slime:
                    p.Kind = AlienAttackKind.SlimePulse;
                    p.Range = 4;
                    p.Radius = 3;
                    p.Windup = 1.1f;
                    p.Active = 1.5f;
                    p.Recovery = 1.4f;
                    p.Pulses = 3;
                    p.DamageScale = .5f;
                    break;
                case AlienSpecies.Roots:
                    p.Kind = AlienAttackKind.RootSpikes;
                    p.Range = profile.Boss ? 14 : 8;
                    p.Radius = phase > 1 ? 1.5f : 1.1f;
                    p.Windup = 1.25f;
                    p.Active = 1.3f;
                    p.Recovery = 1.8f;
                    p.Pulses = phase > 1 ? 3 : 1;
                    break;
                case AlienSpecies.Tentacle:
                    p.Kind = committed ?? (profile.Boss && sequence % 2 == 0
                        ? AlienAttackKind.TentacleSlam
                        : AlienAttackKind.TentacleSweep);
                    p.Range = 6;
                    p.Radius = p.Kind == AlienAttackKind.TentacleSlam ? 2 : 5;
                    p.Windup = 1.1f;
                    p.Active = .75f;
                    p.Recovery = 1.5f;
                    p.Pulses = phase > 1 ? 2 : 1;
                    break;
            }
            if (profile.Boss && profile.Species == AlienSpecies.Roots && committed == AlienAttackKind.SlimePulse)
                p = new AlienCombatPattern {
                    Kind = AlienAttackKind.SlimePulse, Range = 3, Radius = 3,
                    Windup = 1.35f, Active = .45f, Recovery = 1.4f, Pulses = 1, DamageScale = .7f
                };
            bool canSpit = profile.Species == AlienSpecies.Caterpillar || profile.Species == AlienSpecies.Slime ||
                           (profile.Boss && (profile.Species == AlienSpecies.Tentacle ||
                                             profile.Species == AlienSpecies.Roots));
            if (committed == AlienAttackKind.MucusSpit || (!committed.HasValue && canSpit &&
                                                           profile.MucusEveryAttacks > 0 &&
                                                           sequence % (uint)profile.MucusEveryAttacks == 0))
                p = new AlienCombatPattern {
                    Kind = AlienAttackKind.MucusSpit, Range = profile.MucusRange,
                    Radius = .18f, Windup = .7f, Active = 1.2f, Recovery = .75f, Speed = 0, DamageScale = .4f,
                    Pulses = 1
                };
            if (profile.Boss && p.Kind != AlienAttackKind.MucusSpit) {
                p.Windup *= .8f;
                p.Recovery *= .65f;
                if (p.Kind == AlienAttackKind.TentacleSweep) p.Range = p.Radius * .9f;
            }
            // 大型怪物也对数值设上限，同时保留能打到体外的触及范围。
            float reachScale = profile.IsUpright ? scale : Mathf.Clamp(scale / .65f, .7f, 1.8f);
            p.Range *= reachScale;
            if (p.Kind != AlienAttackKind.MucusSpit) p.Radius *= reachScale;
            if (profile.Boss && phase > 1) {
                p.Windup *= .8f;
                p.Recovery *= .85f;
            }
            if (profile.Boss) {
                // 巨型肢体需要打到画面上能看到的尖端。
                // 狂暴时也保留局部重击和地刺范围，以及人能看清的最短预警。
                float limbReach = profile.Branch.reach * scale * 1.1f;
                if (p.Kind == AlienAttackKind.TentacleSweep) p.Range = p.Radius = limbReach;
                if (p.Kind == AlienAttackKind.TentacleSlam || p.Kind == AlienAttackKind.RootSpikes)
                    p.Range = Mathf.Max(p.Range, limbReach + 8);
                p.Windup = Mathf.Max(p.Windup, p.Kind == AlienAttackKind.MucusSpit ? .85f : 1.15f);
                p.Recovery = Mathf.Max(p.Recovery, phase > 1 ? .9f : 1.2f);
                if (p.Kind == AlienAttackKind.MucusSpit) {
                    p.Range = Mathf.Max(p.Range, profile.SightRange);
                    p.Active = Mathf.Max(p.Active,
                        Mathf.Clamp(p.Range / Mathf.Max(3, profile.MucusSpeed), .2f, 4) + .2f);
                }
                if (p.Pulses > 1) p.Active = Mathf.Max(p.Active, p.Pulses * 1.25f);
            }
            return p;
        }
    }

    public enum AlienImpactKind : byte {
        None,
        World,
        Body,
        Face
    }

    // 每个客户端自己防重复。刚加入时收到的第一包也只用来建立基线。
    public struct AlienAttackEvents {
        private bool m_Observed;
        private uint m_Attack, m_Launch, m_Impact;

        // 客户端保存已见过的发射序号。刚加入先登记基线，避免补播别人在几秒前发射的声音和粒子。
        public bool Launch(in ProceduralAlienEnemy.State state) {
            if (!m_Observed) {
                m_Observed = true;
                m_Attack = state.AttackSequence;
                m_Launch = state.Action == AlienAction.Strike ? state.AttackSequence : 0;
                return false;
            }
            if (unchecked((int)(state.AttackSequence - m_Attack)) < 0) return false;
            m_Attack = state.AttackSequence;
            if (state.Action != AlienAction.Strike || state.AttackSequence == 0 || m_Launch == state.AttackSequence)
                return false;
            m_Launch = state.AttackSequence;
            return true;
        }

        private bool m_ImpactObserved;

        // 命中用独立序号去重。一次技能发出和后来打中是两个不同事件。
        public bool Impact(in ProceduralAlienEnemy.State state) {
            if (!m_ImpactObserved) {
                m_ImpactObserved = true;
                m_Impact = state.ImpactSequence;
                return false;
            }
            if (state.ImpactSequence == 0 || unchecked((int)(state.ImpactSequence - m_Impact)) <= 0) return false;
            m_Impact = state.ImpactSequence;
            return state.ImpactKind != AlienImpactKind.None;
        }
    }
}