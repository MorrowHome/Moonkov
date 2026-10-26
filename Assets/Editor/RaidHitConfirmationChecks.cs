using System;
using Unity.MP_FPS;

public static class RaidHitConfirmationChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static PredictedPlayerGhost Fresh()
    {
        var health=default(PredictedPlayerGhost); RaidHealth.Initialize(ref health); return health;
    }

    public static string Run()
    {
        s_Checks=0;
        var health=Fresh(); var before=health;
        Check(RaidHealth.ImpactFlags(before,health)==0, "Rejected impact must not confirm damage");
        health.LastHitTick++;health.LastDamageAmount=0;
        Check(RaidHealth.ImpactFlags(before,health)==0, "A tick change alone is not a hit");
        health=before;health.ShieldRestoreSequence++;
        Check(RaidHealth.ImpactFlags(before,health)==0, "Shield restoration is not a hit");
        health=before;health.CurrentHealth=float.NaN;
        Check(RaidHealth.ImpactFlags(before,health)==0, "Invalid health is not a hit");

        health=Fresh();before=health;
        Check(RaidHealth.CombatDamage(ref health,BodyPart.Chest,15,10,RaidDamageKind.Bullet)>0 &&
              RaidHealth.ImpactFlags(before,health)==1, "Actual body damage confirms once");
        before=health;
        RaidHealth.CombatDamage(ref health,BodyPart.Chest,100,11,RaidDamageKind.Bullet);
        Check(health.CurrentHealth==0 && RaidHealth.ImpactFlags(before,health)==3, "Lethal body hit confirms death");
        before=health;
        Check(RaidHealth.ImpactFlags(before,health)==0, "A dead target cannot confirm another hit");

        health=Fresh();health.ShieldEquipped=true;health.ShieldCharge=100;health.ShieldEnergy=100;
        before=health;
        Check(RaidHealth.CombatDamage(ref health,BodyPart.Chest,20,20,RaidDamageKind.Bullet)==0 &&
              health.CurrentHealth==before.CurrentHealth && health.ShieldCharge==80 && RaidHealth.ImpactFlags(before,health)==1,
            "A fully absorbed shot confirms a real shield hit");
        before=health;
        RaidHealth.CombatDamage(ref health,BodyPart.Chest,20,20,RaidDamageKind.Bullet);
        Check(health.LastHitTick==before.LastHitTick && RaidHealth.ImpactFlags(before,health)==1,
            "A second shield hit in the same tick still confirms");
        before=health;
        RaidHealth.CombatDamage(ref health,BodyPart.Chest,80,21,RaidDamageKind.Bullet);
        Check(health.ShieldCharge==0 && health.CurrentHealth<before.CurrentHealth && RaidHealth.ImpactFlags(before,health)==1,
            "A penetrating shield hit confirms body damage");

        health=Fresh();health.LastHitTick=55;health.LastHitPart=BodyPart.LeftLeg;health.LastDamageAmount=12;
        before=health;
        // Friendly-fire and mode guards return without entering CombatDamage.
        Check(RaidHealth.ImpactFlags(before,health)==0 && health.LastHitTick==55 &&
              health.LastHitPart==BodyPart.LeftLeg && health.LastDamageAmount==12,
            "Rejected damage preserves the preceding hit record");
        return "PASS " + s_Checks + " combat confirmation checks";
    }
}
