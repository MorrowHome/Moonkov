using System;
using System.Runtime.Serialization;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;

// Exercises the production status-action state machine without a scene or profile.
// The separate editor fixture also checks decisions and the actual server.
public static class PmcSelfTreatmentChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/AI/Check Self Treatment Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        var brain = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        brain.Inventory = InventoryGraph.Create(false, false);
        brain.SelfStatus = new TacticalServerWorld.Status { Fire = 40, Bleed = 40 };
        brain.LastDamage = -100; brain.Action = PmcAction.Heal;
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        brain.LastHealth = health.CurrentHealth;
        void Advance(double now)
        {
            bool bleeding = brain.SelfStatus != null && brain.SelfStatus.Bleed > now;
            bool burning = !brain.IsClashBot && brain.SelfStatus != null && brain.SelfStatus.Fire > now;
            bool eligible = brain.Action == PmcAction.Heal && (!brain.FireDanger || brain.IsClashBot) &&
                now - brain.LastDamage >= 3 && (bleeding || burning || brain.HealUntil > 0);
            PmcSelfTreatment.Advance(brain, eligible, bleeding, burning, now);
        }
        void StatusHit(RaidDamageKind kind, uint tick)
        {
            var before = health;
            RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, tick, kind);
            PmcStatusDamage.Record(brain.SelfStatus, before, health);
        }

        Advance(10);
        Check(brain.HealStatus && brain.HealUntil == 13 && brain.SelfStatus.Fire == 40 && brain.SelfStatus.Bleed == 40,
            "self-treatment starts without clearing either status early");
        StatusHit(RaidDamageKind.Fire, 1); PmcCombatPressure.Update(brain, health, 11, .1f); Advance(11);
        StatusHit(RaidDamageKind.Bleed, 2); PmcCombatPressure.Update(brain, health, 12, .1f); Advance(12);
        Check(brain.HealStatus && brain.HealUntil == 13 && brain.LastDamage == -100 && health.CurrentHealth < 440,
            "own fire and blood loss continue without interrupting self-treatment");
        Advance(13);
        Check(!brain.HealStatus && brain.HealUntil == 0 && brain.SelfStatus.Fire == 0 && brain.SelfStatus.Bleed == 0,
            "one completed action stops both active statuses");

        brain.SelfStatus.Fire = 50; brain.LastDamage = -100; brain.NextHeal = 0; brain.Action = PmcAction.Heal;
        health.ShieldEquipped = true; health.ShieldCharge = 100; Advance(20);
        StatusHit(RaidDamageKind.Fire, 3);
        RaidHealth.CombatDamage(ref health, BodyPart.Chest, 5, 3, RaidDamageKind.Bullet);
        PmcCombatPressure.Update(brain, health, 21, .1f); Advance(21);
        Check(brain.HealUntil == 0 && brain.SelfStatus.Fire == 50 && brain.LastDamage == 21,
            "a shielded bullet in the same update interrupts the action");

        brain.LastDamage = -100; brain.NextHeal = 0; brain.Action = PmcAction.Heal; Advance(24);
        StatusHit(RaidDamageKind.Fire, 4);
        RaidHealth.CombatDamage(ref health, BodyPart.Chest, 2, 4, RaidDamageKind.Poison);
        PmcCombatPressure.Update(brain, health, 25, .1f); Advance(25);
        Check(brain.HealUntil == 0 && brain.SelfStatus.Fire == 50 && brain.LastDamage == 25,
            "poison interrupts self-treatment even during burning");

        brain.LastDamage = -100; brain.NextHeal = 0; brain.Action = PmcAction.Heal; brain.FireDanger = true; Advance(30);
        Check(brain.HealUntil == 0 && brain.SelfStatus.Fire == 50, "the actor cannot extinguish while still inside active fire");
        brain.FireDanger = false;
        Check(brain.Inventory.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None, "medical fixture");
        var med = brain.Inventory.Items.Find(i => i.Code == "medkit");
        brain.HealStatus = false; brain.HealUntil = 31; brain.HealItemId = med.Id;
        brain.HealInventoryVersion = brain.Inventory.Version; brain.LastDamage = -100; Advance(30);
        Check(brain.HealUntil == 0 && brain.HealItemId == null && brain.Inventory.Find(med.Id)?.Quantity == 1,
            "new burning cancels an unfinished medical item without consuming it");

        brain.ClashParticipantId = 1; brain.SelfStatus.Fire = 60; brain.SelfStatus.Bleed = 60;
        brain.LastDamage = -100; brain.NextHeal = 0; brain.Action = PmcAction.Heal; Advance(40); Advance(43);
        Check(brain.SelfStatus.Bleed == 0 && brain.SelfStatus.Fire == 60,
            "bounty bots retain their original bleed-only action");
        return "PASS " + s_Checks + " PMC self-treatment checks";
    }
}
