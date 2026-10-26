using System;
using Unity.MP_FPS;

public static class TacticalSelfTreatmentChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}

    public static string Run()
    {
        s_Checks=0;
        var health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);
        health.Energy=0;health.Hydration=0;
        uint damage=health.NonMetabolicDamageSequence,shield=health.ShieldHitSequence;
        float start=health.CurrentHealth;
        RaidNutrition.Tick(ref health,.5f,1000,1000,300,0,2,2,10);
        Check(health.CurrentHealth<start && health.LastHitTick==10,"metabolic damage still applies");
        Check(RaidHealth.SelfTreatmentDamageUnchanged(health,damage,shield),"metabolic damage permits treatment");
        var beforeOwn=health;
        RaidHealth.CombatDamage(ref health,BodyPart.Auto,2,11,RaidDamageKind.Bleed);
        Check(health.CurrentHealth<beforeOwn.CurrentHealth && !RaidHealth.SelfTreatmentDamageUnchanged(health,damage,shield),"bleeding still deals damage");
        Check(RaidHealth.SelfTreatmentDamageUnchanged(beforeOwn,damage,shield),"only known status damage can advance treatment baseline");
        damage=health.NonMetabolicDamageSequence;
        RaidNutrition.Tick(ref health,.5f,1000,1000,300,0,2,2,12);
        Check(RaidHealth.SelfTreatmentDamageUnchanged(health,damage,shield),"treatment can continue after known bleed and metabolic damage");
        var beforeAttack=health;
        RaidHealth.CombatDamage(ref health,BodyPart.RightArm,5,13,RaidDamageKind.Bullet);
        RaidNutrition.Tick(ref health,.5f,1000,1000,300,0,2,2,13);
        Check(health.LastHitTick==13 && !RaidHealth.SelfTreatmentDamageUnchanged(health,damage,shield),"same-tick metabolic damage cannot hide a bullet");
        Check(RaidHealth.SelfTreatmentDamageUnchanged(beforeAttack,damage,shield),"external hit changed the damage sequence");
        RaidHealth.CombatDamage(ref health,BodyPart.Auto,1,13,RaidDamageKind.Bleed);
        Check(!RaidHealth.SelfTreatmentDamageUnchanged(health,damage,shield),"later bleed cannot hide an unaccepted bullet");
        var protectedHealth=new PredictedPlayerGhost();RaidHealth.Initialize(ref protectedHealth);
        protectedHealth.ShieldEquipped=true;protectedHealth.ShieldCharge=100;
        damage=protectedHealth.NonMetabolicDamageSequence;shield=protectedHealth.ShieldHitSequence;
        RaidHealth.CombatDamage(ref protectedHealth,BodyPart.RightArm,5,20,RaidDamageKind.Bullet);
        Check(protectedHealth.NonMetabolicDamageSequence==damage && !RaidHealth.SelfTreatmentDamageUnchanged(protectedHealth,damage,shield),
            "shield-only hit interrupts treatment");
        return "PASS "+s_Checks+" self-treatment damage checks";
    }
}
