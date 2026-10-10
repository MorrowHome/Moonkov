using System;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class MoonkovRaidHealthChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static PredictedPlayerGhost Fresh()
    { var state = default(PredictedPlayerGhost); RaidHealth.Initialize(ref state); return state; }

    [MenuItem("Tools/Moonkov/Check Raid Health")]
    public static void Run()
    {
        var health = Fresh();
        Require(health.CurrentHealth == 440 && health.MaxHealth == 440 && health.Oxygen == 100, "Spawn/reset values differ.");
        var points = new[] { new Vector3(0,.8f,0), new Vector3(0,.45f,0), new Vector3(0,.15f,0),
            new Vector3(-.25f,.3f,0), new Vector3(.25f,.3f,0), new Vector3(-.12f,-.5f,0), new Vector3(.12f,-.5f,0) };
        for (int i = 0; i < points.Length; i++) Require(RaidHealth.ResolveCapsuleHit(points[i], 1.8f, .3f) == (BodyPart)i, "Authoritative hit region or anatomical side differs.");
        var critical = Fresh(); RaidHealth.Damage(ref critical, BodyPart.Chest, 80, 1);
        Require(critical.CurrentHealth > 350 && RaidHealth.DangerFraction(critical) < .1f, "Limb sum hides critical chest from HUD/AI.");
        RaidHealth.Damage(ref health, BodyPart.LeftLeg, 80, 1);
        Require(health.LeftLegHealth == 0 && Mathf.Abs(health.CurrentHealth - 360) < .001f && health.CurrentHealth > 0, "Limb overflow must conserve damage without instant death.");
        var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.Sprint, true);
        Require(!RaidHealth.RestrictInput(health, input).Sprint, "Disabled leg still sprints.");
        var constants = new FirstPersonController.ControllerConsts(); constants.Walk.Speed = 4; constants.Sprint.Speed = 8;
        Require(Mathf.Abs(RaidHealth.Movement(health, constants).Walk.Speed - 2.6f) < .001f, "Leg injury missing from movement.");
        foreach (var vital in new[] { BodyPart.Head, BodyPart.Chest })
        {
            var victim = Fresh(); RaidHealth.Damage(ref victim, vital, RaidHealth.Maximum(vital), 2);
            Require(victim.CurrentHealth == 0 && !RaidHealth.CanHeal(victim, vital), "Vital zero must die, not be revived by injector.");
        }

        var graph = InventoryGraph.Create(false); graph.AddSupply("medkit", 2);
        var medicine = graph.Items.Find(graph.MedicalAccessible); int version = graph.Version;
        Require(RaidHealth.UseMedical(ref health, graph, medicine.Id, version, BodyPart.LeftLeg) == InventoryError.None &&
            health.LeftLegHealth == 40 && medicine.Quantity == 1 && graph.Version == version + 1, "Targeted treatment/consumption not atomic.");
        float before = health.CurrentHealth;
        Require(RaidHealth.UseMedical(ref health, graph, medicine.Id, version, BodyPart.LeftLeg) == InventoryError.Stale &&
            health.CurrentHealth == before && medicine.Quantity == 1, "Replayed version healed or consumed again.");
        Require(RaidHealth.UseMedical(ref health, graph, medicine.Id, graph.Version, (BodyPart)9) == InventoryError.Invalid && medicine.Quantity == 1, "Invalid part consumed medicine.");
        var dead = Fresh(); RaidHealth.Damage(ref dead, BodyPart.Head, 35, 3);
        Require(RaidHealth.UseMedical(ref dead, graph, medicine.Id, graph.Version, BodyPart.LeftLeg) == InventoryError.Invalid && medicine.Quantity == 1, "Dead player consumed medicine.");
        var full = Fresh(); Require(RaidHealth.UseMedical(ref full, graph, medicine.Id, graph.Version, BodyPart.Auto) == InventoryError.Invalid, "Full-health quick use consumed medicine.");
        // Only the targeted part changes, including a previously blacked-out limb.
        var selected = Fresh(); RaidHealth.Damage(ref selected, BodyPart.RightArm, 60, 4);
        float chest = selected.ChestHealth;
        Require(RaidHealth.UseMedical(ref selected, graph, medicine.Id, graph.Version, BodyPart.Auto) == InventoryError.None &&
            selected.RightArmHealth == 40 && selected.ChestHealth == chest, "Automatic treatment picked another part.");

        var a = Fresh(); a.Oxygen = 1;
        var b = a;
        RaidHealth.TickOxygen(ref a, false, 10, 100, 5, 2, 3, 5);
        for (int i = 0; i < 10; i++) RaidHealth.TickOxygen(ref b, false, 1, 100, 5, 2, 3, 5);
        Require(a.Oxygen == 0 && Mathf.Abs(a.ChestHealth - 64) < .001f && Mathf.Abs(a.ChestHealth - b.ChestHealth) < .001f, "Oxygen crossing/grace depends on frame size.");
        RaidHealth.TickOxygen(ref a, true, 2, 100, 5, 2, 3, 6);
        Require(a.Oxygen == 10 && a.ChestHealth == 64 && a.HypoxiaSeconds == 0, "Indoor refill resets reserve or continues damage.");
        RaidHealth.TickOxygen(ref a, false, 1, 100, 5, 2, 3, 6);
        Require(a.Oxygen == 9 && a.ChestHealth == 64, "Leaving air loses residual oxygen.");

        var zone = new GameObject("Health check air zone");
        try
        {
            zone.transform.position = new Vector3(10000, 10000, 10000); zone.transform.rotation = Quaternion.Euler(0, 37, 0);
            var box = zone.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(4, 3, 2);
            var air = zone.AddComponent<BreathableZone>();
            Require(BreathableZone.Contains(zone.transform.position) && !BreathableZone.Contains(zone.transform.TransformPoint(new Vector3(3, 0, 0))), "Rotated authored volume boundary differs.");
            air.enabled = false; Require(!BreathableZone.Contains(zone.transform.position), "Disabled air zone remains active.");
        }
        finally { UnityEngine.Object.DestroyImmediate(zone); }

        var serializerType = typeof(RaidMedicalUseRpc).Assembly.GetTypes().Single(type => type.IsValueType && typeof(IRpcCommandSerializer<RaidMedicalUseRpc>).IsAssignableFrom(type));
        var serializer = (IRpcCommandSerializer<RaidMedicalUseRpc>)Activator.CreateInstance(serializerType);
        using var bytes = new NativeArray<byte>(512, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        serializer.Serialize(ref writer, new RpcSerializerState { CompressionModel = StreamCompressionModel.Default },
            new RaidMedicalUseRpc { RaidId = 2, RequestId = 3, ExpectedVersion = 4, ItemId = "check", Part = BodyPart.RightLeg });
        var reader = new DataStreamReader(writer.AsNativeArray()); var decoded = default(RaidMedicalUseRpc);
        serializer.Deserialize(ref reader, new RpcDeserializerState { CompressionModel = StreamCompressionModel.Default }, ref decoded);
        Require(!writer.HasFailedWrites && decoded.Part == BodyPart.RightLeg && decoded.RequestId == 3, "Generated medical RPC lost target/sequence.");
        Debug.Log("[Raid health checks] Damage, death, targeted medical, replay, movement, oxygen, air zones and generated RPC passed.");
    }
}
