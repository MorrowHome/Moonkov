using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class RaidFallDamageChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
    private static PredictedPlayerGhost Fresh(float height)
    {
        var health = default(PredictedPlayerGhost);
        RaidHealth.Initialize(ref health);
        health.ControllerState.FallHeight = height;
        health.ControllerState.LandTriggered = true;
        return health;
    }

    public static string Run()
    {
        s_Checks = 0;
        const float gravity = -RaidHealth.MoonGravity;
        Check(RaidHealth.FallDamage(0, gravity) == 0, "Standing is safe");
        Check(RaidHealth.FallDamage(1.2f, gravity) == 0, "A normal jump is safe");
        Check(RaidHealth.FallDamage(4.5f, gravity) == 0, "Safe height includes the boundary");
        Check(Near(RaidHealth.FallDamage(6, gravity), 18), "Six-meter lunar landing");
        Check(Near(RaidHealth.FallDamage(10, gravity), 66), "Ten-meter lunar landing");
        Check(Near(RaidHealth.FallDamage(6, gravity * 2), 36), "Impact scales with gravity");
        Check(RaidHealth.FallDamage(20, 0) == 0 && RaidHealth.FallDamage(20, 1) == 0, "Zero or upward gravity");
        Check(RaidHealth.FallDamage(float.NaN, gravity) == 0 && RaidHealth.FallDamage(float.PositiveInfinity, gravity) == 0,
            "Invalid height cannot damage");
        Check(RaidHealth.FallDamage(20, float.NaN) == 0 && RaidHealth.FallDamage(20, float.NegativeInfinity) == 0,
            "Invalid gravity cannot damage");
        Check(RaidHealth.FallDamage(float.MaxValue, -float.MaxValue) == RaidHealth.MaximumFallDamage, "Extreme finite values are capped");

        var health = Fresh(6);
        health.ShieldEquipped = true; health.ShieldCharge = 100; health.ShieldEnergy = 100;
        float applied = RaidHealth.ApplyLandingDamage(ref health, false, gravity, 17);
        Check(Near(applied, 18) && Near(health.CurrentHealth, 422), "Landing damages once");
        Check(Near(health.LeftLegHealth, 56) && Near(health.RightLegHealth, 56), "Both legs absorb impact");
        Check(health.ShieldCharge == 100 && health.ShieldEnergy == 100 && health.ShieldHitSequence == 0, "A shield cannot absorb a fall");
        Check(health.LastHitTick == 17 && Near(health.LastDamageAmount, 18) && health.ControllerState.IsHit,
            "Existing injury and action interruption signals update");
        Check(RaidHealth.ApplyLandingDamage(ref health, true, gravity, 18) == 0 && Near(health.CurrentHealth, 422),
            "An uncleared landing event cannot damage twice");
        health.ControllerState.LandTriggered = false;
        Check(RaidHealth.ApplyLandingDamage(ref health, false, gravity, 19) == 0 && Near(health.CurrentHealth, 422),
            "Movement without a landing cannot damage");
        health.ControllerState.FallHeight = 1.2f; health.ControllerState.LandTriggered = true;
        Check(RaidHealth.ApplyLandingDamage(ref health, false, gravity, 20) == 0 && health.LastHitTick == 17,
            "A later safe landing cannot replay the prior fall");

        health = Fresh(16);
        Check(RaidHealth.ApplyLandingDamage(ref health, false, gravity, 21) > 0 &&
              health.LeftLegHealth == 0 && health.RightLegHealth == 0 && !RaidNutrition.CanSprint(health),
            "A hard landing disables legs and sprint through existing rules");
        var injured = Fresh(6);
        RaidHealth.Set(ref injured, BodyPart.LeftLeg, 0);
        float before = injured.CurrentHealth;
        Check(RaidHealth.ApplyLandingDamage(ref injured, false, gravity, 22) > 0 && injured.CurrentHealth < before &&
              injured.LeftLegHealth == 0 && injured.RightLegHealth < 65, "An already injured leg does not discard impact");

        var gel=Fresh(6);gel.AlienFoodEffect=AlienFoodEffect.GelArmor;gel.AlienFoodEffectSeconds=10;
        Check(Near(RaidHealth.ApplyLandingDamage(ref gel,false,gravity,22),18) && Near(gel.LeftLegHealth,56) && Near(gel.RightLegHealth,56),
            "Combat gel armor does not reduce fall damage");
        gel=Fresh(0);gel.AlienFoodEffect=AlienFoodEffect.GelArmor;gel.AlienFoodEffectSeconds=10;
        Check(Near(RaidHealth.CombatDamage(ref gel,BodyPart.RightArm,20,23,RaidDamageKind.Bullet),15) && Near(gel.RightArmHealth,45),
            "Combat gel armor still reduces incoming tissue damage");
        gel=Fresh(0);gel.AlienFoodEffect=AlienFoodEffect.GelArmor;gel.AlienFoodEffectSeconds=10;gel.ShieldEquipped=true;gel.ShieldCharge=10;
        Check(Near(RaidHealth.CombatDamage(ref gel,BodyPart.RightArm,20,23,RaidDamageKind.Bullet),7.5f) && gel.ShieldCharge==0,
            "Gel armor preserves shield-first damage ordering");
        gel=Fresh(0);gel.AlienFoodEffect=AlienFoodEffect.GelArmor;gel.AlienFoodEffectSeconds=10;gel.Oxygen=0;
        RaidHealth.TickOxygen(ref gel,false,1,600,0,0,3,24);
        Check(Near(gel.ChestHealth,82),"Combat gel armor does not reduce hypoxia damage");

        var graph = InventoryGraph.Create(false);
        Check(graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None, "Create carried medicine");
        var medicine = graph.Items.Find(item => item.Code == "medkit");
        var actor = new RaidInventoryState { Graph = graph };
        health = Fresh(6); RaidHealth.Set(ref health, BodyPart.LeftArm, 20);
        Check(actor.BeginMedical(health, medicine.Id, graph.Version, BodyPart.LeftArm, float3.zero, 0) == InventoryError.None,
            "Treatment starts before landing");
        RaidHealth.ApplyLandingDamage(ref health, false, gravity, 29);
        actor.RefreshMedical(ref health, true, float3.zero, 3);
        Check(!actor.ActionPending && graph.Count("medkit", true) == 1 && health.LeftArmHealth == 20,
            "Fall damage interrupts treatment without consuming medicine");

        var severe = Fresh(100);
        RaidHealth.ApplyLandingDamage(ref severe, false, gravity, 23);
        Check(severe.CurrentHealth == 0, "A sufficiently high fall can be fatal");
        health = Fresh(10); health.ClashEnabled = true;
        Check(RaidHealth.ApplyLandingDamage(ref health, false, gravity, 24) == 0 && health.CurrentHealth == 440,
            "Bounty mode keeps its existing rules");
        health = Fresh(10); health.BodyHealthInitialized = false;
        Check(RaidHealth.ApplyLandingDamage(ref health, false, gravity, 25) == 0 && health.CurrentHealth == 440,
            "Uninitialized health cannot be changed");
        health = Fresh(10); RaidHealth.Damage(ref health, BodyPart.Head, 35, 26);
        Check(RaidHealth.ApplyLandingDamage(ref health, false, gravity, 27) == 0 && health.LastHitTick == 26,
            "A dead player cannot take another fall");

        return "PASS " + s_Checks + " fall damage checks";
    }
}
