using System;

namespace Unity.MP_FPS
{
    public struct TacticalSprintState
    {
        public bool Initialized,Exhausted;
        public float Value,RestDelay;
    }
    // Moonkov v1 adaptation. This is separate from hunger, thirst, oxygen and
    // the unimplemented Hunt melee/combat stamina pool.
    public static class TacticalSprintRules
    {
        public const float Capacity=100,DrainPerSecond=2,RecoveryPerSecond=20,RecoveryDelay=1.5f,ResumeThreshold=25;
        public static void Reset(ref TacticalSprintState state)
        {state.Initialized=true;state.Value=Capacity;state.RestDelay=0;state.Exhausted=false;}
        public static void Tick(ref TacticalSprintState state,float dt,bool sprinting,bool stimulated,bool canAct=true,float carriedWeight=0)
        {
            if(!state.Initialized || !float.IsFinite(state.Value))Reset(ref state);
            if(!float.IsFinite(dt)||dt<=0)return;
            if(!canAct || stimulated){Reset(ref state);return;}
            if(sprinting)
            {
                state.Value=Math.Max(0,state.Value-DrainPerSecond*RaidEncumbrance.SprintDrain(carriedWeight)*dt);state.RestDelay=RecoveryDelay;
                if(state.Value<=.001f){state.Value=0;state.Exhausted=true;}
                return;
            }
            float usable=Math.Max(0,dt-state.RestDelay);
            state.RestDelay=Math.Max(0,state.RestDelay-dt);
            state.Value=Math.Min(Capacity,state.Value+RecoveryPerSecond*RaidEncumbrance.SprintRecovery(carriedWeight)*usable);
            if(state.Exhausted && state.Value+.001f>=ResumeThreshold)state.Exhausted=false;
        }
    }
}
