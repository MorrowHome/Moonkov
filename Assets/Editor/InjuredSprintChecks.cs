using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class InjuredSprintChecks
{
    [MenuItem("Tools/Moonkov/Check Injured Sprint")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); }
        PredictedPlayerGhost Runner(int legs)
        {
            var state = new PredictedPlayerGhost();
            RaidHealth.Initialize(ref state);
            if (legs > 0) RaidHealth.Set(ref state, BodyPart.LeftLeg, 0);
            if (legs > 1) RaidHealth.Set(ref state, BodyPart.RightLeg, 0);
            state.ControllerState.Sprinting = true;
            state.ControllerState.MovementSpeed = 3.5f;
            state.ControllerState.MovementType = FirstPersonController.MovementType.Standing;
            return state;
        }
        var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.Sprint, true);
        var constants = new FirstPersonController.ControllerConsts
        {
            Walk = new FirstPersonController.ControllerConsts.StateConsts { Speed = 2 },
            Sprint = new FirstPersonController.ControllerConsts.StateConsts { Speed = 6 }, JumpHeight = 1.2f
        };
        for (int legs = 0; legs <= 2; legs++)
        {
            var runner = Runner(legs);
            Check(RaidHealth.RestrictInput(runner, input).Sprint, "Leg injury blocked sprint input.");
            var movement = RaidHealth.Movement(runner, constants);
            Check(movement.Sprint.Speed > movement.Walk.Speed, "Sprint is only walking.");
            Check(Mathf.Abs(movement.Sprint.Speed - (legs > 0 ? 3.9f : 6f)) < .001f &&
                Mathf.Abs(movement.JumpHeight - (legs > 0 ? .6f : 1.2f)) < .001f, "Injury movement penalties changed.");
            foreach (int hz in new[] { 30, 60, 144 })
            {
                runner = Runner(legs); float before = runner.CurrentHealth;
                runner.ShieldEquipped = true; runner.ShieldCharge = 100;
                runner.LastImpactTick = 7; uint damageSequence = runner.NonPhysiologicalDamageSequence;
                for (int i = 0; i < hz * 2; i++)
                    RaidHealth.ApplyInjuredSprintDamage(ref runner, Vector3.forward * (3.5f / hz), true, 1f / hz, (uint)(i + 10));
                Check(Mathf.Abs(before - runner.CurrentHealth - legs * 4) < .02f, "Sprint cost depends on frame rate or leg count.");
                Check(runner.ShieldCharge == 100 && runner.LastImpactTick == 7 &&
                    runner.NonPhysiologicalDamageSequence == damageSequence, "Exertion used shields or emitted a combat impact.");
            }
        }
        foreach (string reason in new[] { "rest", "wall", "walk", "air", "landing", "vertical", "teleport", "pause", "stall", "nan", "dead", "clash", "uninitialized" })
        {
            var runner = Runner(1); var travel = Vector3.forward * .35f; float dt = .1f; bool grounded = true;
            switch (reason)
            {
                case "rest": runner.ControllerState.MovementSpeed = 0; break;
                case "wall": travel = Vector3.zero; break;
                case "walk": runner.ControllerState.Sprinting = false; break;
                case "air": runner.ControllerState.MovementType = FirstPersonController.MovementType.Falling; break;
                case "landing": grounded = false; break;
                case "vertical": travel = Vector3.up; break;
                case "teleport": travel *= 100; break;
                case "pause": dt = 0; break;
                case "stall": dt = 1; break;
                case "nan": travel.x = float.NaN; break;
                case "dead": RaidHealth.Set(ref runner, BodyPart.Head, 0); break;
                case "clash": runner.ClashEnabled = true; break;
                case "uninitialized": runner.BodyHealthInitialized = false; break;
            }
            float before = runner.CurrentHealth;
            Check(RaidHealth.ApplyInjuredSprintDamage(ref runner, travel, grounded, dt, 8) == 0 &&
                runner.CurrentHealth == before, "Invalid sprint charged health: " + reason);
        }
        foreach (string reason in new[] { "energy", "water", "oxygen", "supply" })
        {
            var runner = Runner(1);
            if (reason == "energy") runner.Energy = 0;
            if (reason == "water") runner.Hydration = 0;
            if (reason == "oxygen") runner.Oxygen = 0;
            if (reason == "supply") runner.ConsumingSupply = true;
            Check(!RaidHealth.RestrictInput(runner, input).Sprint, "Other sprint restriction lost: " + reason);
        }
        var healed = Runner(1); RaidHealth.Set(ref healed, BodyPart.LeftLeg, 1);
        Check(RaidHealth.ApplyInjuredSprintDamage(ref healed, Vector3.forward * .35f, true, .1f, 8) == 0,
            "Healed leg continued costing health.");
        var dying = Runner(2);
        for (int i = 0; i < RaidHealth.PartCount; i++)
            RaidHealth.Set(ref dying, (BodyPart)i, Mathf.Min(RaidHealth.Get(dying, (BodyPart)i), .01f));
        RaidHealth.ApplyInjuredSprintDamage(ref dying, Vector3.forward * .35f, true, .1f, 8);
        Check(dying.CurrentHealth == 0, "Exertion bypassed normal death rules.");
        return $"PASS {checks} injured sprint checks";
    }
}
