using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Server-owned combat resource. Internal physiological damage never calls Absorb.
    public static class EnergyShieldRules
    {
        public const float Capacity=100, EnergyCapacity=100, RecoveryDelay=6, RecoveryPerSecond=8, EnergyPerShield=.5f;
        public static bool Blocks(RaidDamageKind kind) => kind==RaidDamageKind.Bullet || kind==RaidDamageKind.Explosion ||
            kind==RaidDamageKind.Melee || kind==RaidDamageKind.Fire;
        public static float Resource(float value) => float.IsFinite(value)?Mathf.Clamp(value,0,Capacity):0;
        public static bool Alive(in PredictedPlayerGhost state) => state.CurrentHealth>0 &&
            (!state.ClashEnabled || state.ClashLife==ClashLife.Alive);

        // Returns remaining body damage. One external impact calls this once before any body split.
        public static float Absorb(ref PredictedPlayerGhost state,float incoming,uint tick,RaidDamageKind kind,float3 impact=default)
        {
            if(!float.IsFinite(incoming) || incoming<=0)return 0;
            if(!state.HaloDefending || !HaloEnergyRules.CanDefend(state) || !Alive(state) || !Blocks(kind))return incoming;
            float absorbed=HaloEnergyRules.Spend(ref state,incoming);
            if(absorbed<=0)return incoming;
            state.ShieldHitSequence++;
            state.ShieldHitStrength=Mathf.Clamp01(absorbed/Capacity*3);
            state.ShieldHitPoint=math.all(math.isfinite(impact)) && math.lengthsq(impact)>.001f ? impact :
                state.ControllerState.CurrentPosition+new float3(0,1,0)+math.mul(state.ControllerState.CurrentRotation,new float3(0,0,.4f));
            // Full absorption still interrupts actions and raises a shield event, without fake tissue loss.
            state.LastHitTick=tick;state.ControllerState.IsHit=true;
            if(absorbed>=incoming){state.LastDamageAmount=0;state.LastHitPart=BodyPart.Auto;}
            return incoming-absorbed;
        }
        public static void Tick(ref PredictedPlayerGhost state,float dt,bool canRecover=true) =>
            HaloEnergyRules.Tick(ref state,dt,canRecover);
    }
}
