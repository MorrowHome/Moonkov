using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class AlienLiquidMotion
    {
        public static LiquidReaction Evaluate(AlienCreatureProfile profile, AlienAction action, float age,
            float windup, float active, float recovery, Vector3 centre, Vector3 direction)
        {
            var reaction = new LiquidReaction { Centre = centre, Direction = direction,
                Strength = profile.VoidLiquid ? profile.LiquidReactionStrength : 0 };
            if (action == AlienAction.Windup) reaction.Charge = Mathf.SmoothStep(0, 1, age / Mathf.Max(.05f, windup));
            else if (action == AlienAction.Strike || action == AlienAction.Attached)
            {
                int pulses = profile.Species == AlienSpecies.Slime || profile.Species == AlienSpecies.Caterpillar || profile.Species == AlienSpecies.Leech ? 3 : 1;
                float t = Mathf.Clamp01(age / Mathf.Max(.05f, active));
                reaction.Burst = Mathf.Sin(Mathf.Repeat(t * pulses, 1) * Mathf.PI);
                reaction.Charge = .25f;
            }
            else if (action == AlienAction.Recover) reaction.Recovery = 1 - Mathf.Clamp01(age / Mathf.Max(.05f, recovery));
            else if (action == AlienAction.Dead) reaction.Burst = .5f * Mathf.Exp(-age * 5);
            return reaction;
        }
    }
}
