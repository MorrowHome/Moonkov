using UnityEngine;

namespace Unity.MP_FPS
{
    // Local presentation history only. Does not mutate replicated health or survival rules.
    internal sealed class InjuryFeedbackState
    {
        internal float Critical, Hunger, Thirst, Impact, Blood, Time, Variation, Visibility;
        private bool m_Observed, m_Alive;
        private float m_Health, m_DeathAge;
        private uint m_Hit;
        internal bool Active => Visibility > .001f && Critical + Hunger + Thirst + Impact + Blood > .005f;

        internal void Reset()
        {
            Critical=Hunger=Thirst=Impact=Blood=Time=Variation=Visibility=m_DeathAge=m_Health=0;
            m_Observed=m_Alive=false;m_Hit=0;
        }

        internal static bool Systemic(in PredictedPlayerGhost state) => state.LastHitPart==BodyPart.Auto &&
            state.LastDamageAmount<8 && (!state.ClashEnabled && (state.Energy<=0 || state.Hydration<=0 || state.HypoxiaSeconds>0) ||
            state.AlienFoodEffect==AlienFoodEffect.VenomPower);

        internal void Present(in PredictedPlayerGhost state,float dt,float fadeSpeed)
        {
            dt=Mathf.Clamp(dt,0,.25f);Time+=dt;
            Impact=Mathf.MoveTowards(Impact,0,Mathf.Max(.1f,fadeSpeed)*dt);
            Blood=Mathf.MoveTowards(Blood,0,dt*.15f);
            bool alive=state.CurrentHealth>0 && (!state.ClashEnabled || state.ClashLife==ClashLife.Alive);
            bool fresh=!m_Observed || alive&&!m_Alive;
            if(fresh)
            {
                Critical=Hunger=Thirst=Impact=Blood=0;m_Hit=state.LastHitTick;m_Health=state.CurrentHealth;m_DeathAge=0;
                Visibility=alive?1:0;
            }
            else if((alive||m_Alive) && unchecked((int)(state.LastHitTick-m_Hit))>0 &&
                state.LastDamageAmount>0 && state.CurrentHealth<m_Health-.001f && !Systemic(state))
            {
                float strength=Mathf.Clamp01(state.LastDamageAmount/RaidHealth.Maximum(BodyPart.Chest));
                Impact=Mathf.Clamp01(Impact+.3f+strength*.7f);
                Blood=Mathf.Clamp01(Blood+.18f+strength*.6f);
                Variation=(state.LastHitTick%997)/997f;
            }
            m_Observed=true;if(unchecked((int)(state.LastHitTick-m_Hit))>0)m_Hit=state.LastHitTick;m_Health=state.CurrentHealth;
            if(alive)
            {
                Visibility=1;m_DeathAge=0;
                float fraction=state.CurrentHealth/Mathf.Max(1,state.MaxHealth);
                if(state.BodyHealthInitialized)
                    fraction=Mathf.Min(fraction,Mathf.Min(state.HeadHealth/Mathf.Max(.001f,RaidHealth.Maximum(state,BodyPart.Head)),
                        state.ChestHealth/Mathf.Max(.001f,RaidHealth.Maximum(state,BodyPart.Chest))));
                float critical=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.08f,.55f,fraction));
                float hunger=state.BodyHealthInitialized&&!state.ClashEnabled?Mathf.Clamp01((RaidNutrition.LowThreshold-state.Energy)/RaidNutrition.LowThreshold):0;
                float thirst=state.BodyHealthInitialized&&!state.ClashEnabled?Mathf.Clamp01((RaidNutrition.LowThreshold-state.Hydration)/RaidNutrition.LowThreshold):0;
                float blend=1-Mathf.Exp(-dt*5);
                Critical=Mathf.Lerp(Critical,critical,blend);Hunger=Mathf.Lerp(Hunger,hunger,blend);Thirst=Mathf.Lerp(Thirst,thirst,blend);
            }
            else if(m_Alive || m_DeathAge>0)
            {
                // Briefly retain the last dying state, then release the result/spectator view.
                if(m_Alive)Critical=Mathf.Max(Critical,.85f);
                m_DeathAge+=dt;Visibility=1-Mathf.SmoothStep(0,1,m_DeathAge/1.1f);
            }
            m_Alive=alive;
        }

        internal void Trigger(float intensity)
        {
            Impact=Mathf.Max(Impact,Mathf.Clamp01(intensity));
            Blood=Mathf.Max(Blood,Mathf.Clamp01(intensity)*.5f);
        }
    }
}
