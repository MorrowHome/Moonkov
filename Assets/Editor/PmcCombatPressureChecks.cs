using System;
using Unity.MP_FPS;

public static class PmcCombatPressureChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    public static string Run()
    {
        s_Checks = 0;
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        RaidHealth.Damage(ref health, BodyPart.LeftArm, 20, 1);
        // The pressure rule needs only scalar state. Avoid constructing a native
        // NavMeshPath when this check runs outside the Unity editor.
        var brain = (DollSingerEnemyBrain)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        brain.LastHealth = health.CurrentHealth; brain.LastDamage = -100;
        brain.NextDecision = 100; brain.BleedOnlyDamage = true;
        float before = health.CurrentHealth;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 2, RaidDamageKind.Bullet);
        RaidHealth.Set(ref health, BodyPart.LeftArm, health.LeftArmHealth + 5);
        Check(health.CurrentHealth > before && health.LastImpactTick == 2, "the real damage and healing fixture is valid");
        PmcCombatPressure.Update(brain, health, 10, 0);
        Check(brain.LastDamage == 10 && brain.NextDecision == 0, "a bullet hidden by healing still triggers a response");
        Check(PmcUnknownAttack.NeedsEvasion(false, false, brain.LastDamage, 10.1), "a hidden bullet starts unseen-attack evasion");
        brain.NextDecision = 100;
        PmcCombatPressure.Update(brain, health, 11, 0);
        Check(brain.LastDamage == 10 && brain.NextDecision == 100, "the same impact is not replayed");

        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 3, RaidDamageKind.Bleed);
        PmcCombatPressure.Update(brain, health, 12, 0);
        Check(brain.LastDamage == 10 && brain.NextDecision == 100, "blood loss alone does not restart panic");
        RaidHealth.CombatDamage(ref health, BodyPart.RightArm, 4, 4, RaidDamageKind.Melee);
        PmcCombatPressure.Update(brain, health, 13, 0);
        Check(brain.LastDamage == 13 && brain.NextDecision == 0 && brain.Suppression > 0,
            "a fresh melee impact during bleeding creates pressure");
        float pressure = brain.Suppression;
        PmcCombatPressure.Update(brain, health, 14, 1);
        Check(brain.Suppression < pressure && brain.LastDamage == 13, "pressure decays without a new impact");

        health.ShieldEquipped = true; health.ShieldCharge = 100;
        brain.NextDecision = 100; before = health.CurrentHealth;
        RaidHealth.CombatDamage(ref health, BodyPart.Chest, 5, 5, RaidDamageKind.Bullet);
        Check(health.CurrentHealth == before && health.LastImpactTick == 4 && health.ShieldHitSequence > 0,
            "the real shield absorbs the next shot without tissue damage");
        PmcCombatPressure.Update(brain, health, 15, 0);
        Check(brain.LastDamage == 15 && brain.NextDecision == 0 && brain.Suppression > 0,
            "a shield-only hit still triggers pressure and a decision");
        pressure = brain.Suppression; brain.NextDecision = 100;
        PmcCombatPressure.Update(brain, health, 16, 0);
        Check(brain.LastDamage == 15 && brain.NextDecision == 100 && brain.Suppression == pressure,
            "the same shield hit does not repeat");

        health.ShieldEquipped = false; brain.BleedOnlyDamage = false;
        RaidHealth.Damage(ref health, BodyPart.LeftLeg, 2, 6);
        PmcCombatPressure.Update(brain, health, 17, 0);
        Check(brain.LastDamage == 17 && brain.NextDecision == 0, "other external injury keeps its existing response");
        return "PASS " + s_Checks + " PMC combat pressure checks";
    }
}
