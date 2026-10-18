using UnityEngine;

namespace Unity.MP_FPS
{
    public enum AlienAttackKind : byte { Bite, Charge, AcidSpit, TentacleSweep, FaceLeap, SlimePulse, RootSpikes, TentacleSlam, RoofPounce, LeechDrain, StiltRake }

    /// <summary>Species define different timing and hit geometry. Boss phase is
    /// committed at windup, so a health change cannot silently resize a warning.</summary>
    public struct AlienCombatPattern
    {
        public AlienAttackKind Kind;
        public float Range, Radius, Windup, Active, Recovery, DamageScale, Speed;
        public int Pulses;
        public static AlienCombatPattern For(AlienCreatureProfile profile, byte phase, uint sequence, AlienAttackKind? committed = null)
        {
            float scale = Mathf.Clamp(profile.Scale, .1f, 4);
            var p = new AlienCombatPattern { Range = 2.2f, Radius = .8f, Windup = .55f, Active = .3f,
                Recovery = .8f, DamageScale = 1, Speed = 0, Pulses = 1 };
            switch (profile.Species)
            {
                case AlienSpecies.Spider: p.Kind = AlienAttackKind.RoofPounce; p.Range = profile.SpiderAmbushRange; p.Windup = profile.SpiderWindup; p.Active = profile.SpiderStrikeSeconds; p.Recovery = profile.SpiderRecovery; break;
                case AlienSpecies.Snake: p.Kind = AlienAttackKind.Bite; p.Range = 3.4f; p.Active = .45f; p.Radius = .65f; p.Speed = 8; break;
                case AlienSpecies.Caterpillar: p.Kind = AlienAttackKind.AcidSpit; p.Range = 10; p.Radius = 1.6f; p.Windup = 1; p.Active = 2; p.Recovery = 1.2f; p.Pulses = 3; p.DamageScale = .4f; break;
                case AlienSpecies.Centipede: p.Kind = AlienAttackKind.Charge; p.Range = 8; p.Radius = .9f; p.Windup = .9f; p.Active = .8f; p.Recovery = 1.4f; p.Speed = 10; break;
                case AlienSpecies.Leech: p.Kind = AlienAttackKind.LeechDrain; p.Range = 1.5f; p.Windup = .5f; p.Active = 1.6f; p.Recovery = 1; p.Radius = 1.7f; p.Pulses = 3; p.DamageScale = .3f; break;
                case AlienSpecies.FaceHugger: p.Kind = AlienAttackKind.FaceLeap; p.Range = 4.5f; p.Windup = .65f; p.Active = .45f; p.Recovery = 1.4f; p.Radius = .5f; break;
                case AlienSpecies.StiltWalker: p.Kind = AlienAttackKind.StiltRake; p.Range = profile.AttackReach; p.Windup = profile.Windup; p.Active = .45f; p.Recovery = profile.Recovery; p.Radius = .65f; p.Speed = 2.8f; break;
                case AlienSpecies.Slime: p.Kind = AlienAttackKind.SlimePulse; p.Range = 4; p.Radius = 3; p.Windup = 1.1f; p.Active = 1.5f; p.Recovery = 1.4f; p.Pulses = 3; p.DamageScale = .5f; break;
                case AlienSpecies.Roots: p.Kind = AlienAttackKind.RootSpikes; p.Range = profile.Boss ? 14 : 8; p.Radius = phase > 1 ? 1.5f : 1.1f; p.Windup = 1.25f; p.Active = 1.3f; p.Recovery = 1.8f; p.Pulses = phase > 1 ? 3 : 1; break;
                case AlienSpecies.Tentacle:
                    p.Kind = committed ?? (profile.Boss && sequence % 2 == 0 ? AlienAttackKind.TentacleSlam : AlienAttackKind.TentacleSweep);
                    p.Range = 6; p.Radius = p.Kind == AlienAttackKind.TentacleSlam ? 2 : 5;
                    p.Windup = 1.1f; p.Active = .75f; p.Recovery = 1.5f; p.Pulses = phase > 1 ? 2 : 1; break;
            }
            // Bound large profiles without shrinking their attacks into their own body.
            float reachScale = profile.IsUpright ? scale : Mathf.Clamp(scale / .65f, .7f, 1.8f);
            p.Range *= reachScale;
            p.Radius *= reachScale;
            if (profile.Boss && phase > 1) { p.Windup *= .8f; p.Recovery *= .85f; }
            return p;
        }
    }
}
