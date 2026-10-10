using System;
using System.Linq;
using Unity.Collections;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class MoonkovRaidNutritionChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static PredictedPlayerGhost Fresh()
    { var state = default(PredictedPlayerGhost); RaidHealth.Initialize(ref state); return state; }
    private static void Tick(ref PredictedPlayerGhost state, float dt) => RaidNutrition.Tick(ref state, dt, 100, 100, 100, 2, .5f, 1, 1);
    [MenuItem("Tools/Moonkov/Check Raid Nutrition")]
    public static void Run()
    {
        var fresh = Fresh(); Require(fresh.Energy == 100 && fresh.Hydration == 100 && fresh.Satiety == 25, "Deployment resources not reset.");
        var resting = fresh; Tick(ref resting, 10);
        var sprinting = fresh; sprinting.ControllerState.MovementSpeed = 6; sprinting.ControllerState.Sprinting = true; Tick(ref sprinting, 10);
        Require(Mathf.Abs(resting.Energy - 90) < .001f && Mathf.Abs(sprinting.Energy - 82.5f) < .001f && sprinting.Hydration == 86, "Activity drain differs.");
        var abdomen = fresh; RaidHealth.Set(ref abdomen, BodyPart.Abdomen, 0); Tick(ref abdomen, 10);
        Require(abdomen.Energy == 80 && abdomen.Hydration == 80, "Black abdomen does not double drain.");
        var a = fresh; a.Energy = a.Hydration = 4; var b = a; Tick(ref a, 10);
        for (int i = 0; i < 10; i++) Tick(ref b, 1);
        Require(Mathf.Abs(a.CurrentHealth - 434) < .01f && Mathf.Abs(a.CurrentHealth - b.CurrentHealth) < .01f && a.Energy == 0 && a.Hydration == 0, "Exhaustion crossing/grace damage depends on frame size.");
        var grace = fresh; grace.Energy = grace.Hydration = 0; Tick(ref grace, 2);
        Require(grace.CurrentHealth == 440, "Exhaustion grace causes immediate damage.");
        var stopped = a; RaidNutrition.Tick(ref stopped, 0, 100, 100, 100, 2, .5f, 1, 1);
        Require(stopped.CurrentHealth == a.CurrentHealth, "Zero simulation time damages resources.");
        var graph = InventoryGraph.Create(false); graph.AddSupply("ration", 3); graph.AddSupply("water", 2);
        Require(graph.Validate() == InventoryError.None, "New supplies cannot fit starter carried kit.");
        var ration = graph.Items.Find(i => i.Code == "ration"); var water = graph.Items.Find(i => i.Code == "water");
        int version = graph.Version;
        Require(RaidNutrition.Consume(ref fresh, graph, ration.Id, version) == InventoryError.Invalid && graph.Version == version && ration.Quantity == 3, "Full energy wastes food.");
        a.Satiety = 0;
        Require(RaidNutrition.Consume(ref a, graph, ration.Id, version) == InventoryError.None && a.Energy == 45 && a.Hydration == 0 && a.Satiety == 35 && ration.Quantity == 2 && a.StarvationSeconds == 0, "Food did not apply atomic recovery/side effects.");
        Require(RaidNutrition.Consume(ref a, graph, ration.Id, version) == InventoryError.Stale && a.Energy == 45 && ration.Quantity == 2, "Stale/replayed consumption applies twice.");
        a.Satiety = 95;
        Require(RaidNutrition.Consume(ref a, graph, ration.Id, graph.Version) == InventoryError.Invalid && ration.Quantity == 2, "Insufficient stomach space consumes food.");
        Require(RaidNutrition.Consume(ref a, graph, water.Id, graph.Version) == InventoryError.None && a.Hydration == 50 && a.Satiety == 100 && a.DehydrationSeconds == 0, "Full stomach blocks lifesaving water.");
        Require(!RaidNutrition.Effects(a).HasFlag(SurvivalEffects.Dehydrated), "Drinking did not remove dehydration.");
        var blocked = fresh; RaidHealth.Set(ref blocked, BodyPart.Head, 0);
        Require(RaidNutrition.Consume(ref blocked, graph, ration.Id, graph.Version) == InventoryError.Invalid && ration.Quantity == 2, "Dead player consumed ration.");
        var cache = LootInventoryExchange.CreateCache(0); var joined = LootInventoryExchange.Snapshot(graph, cache);
        var loot = joined.Items.Find(i => i.Code == "ration" && joined.RootOf(i.Id) == "loot");
        Require(RaidNutrition.Consume(ref a, joined, loot.Id, joined.Version) == InventoryError.Inaccessible, "World loot was consumed before transfer.");
        Require(cache.Validate() == InventoryError.None && LootInventoryExchange.CreateCache(1).Items.Any(i => i.Code == "water"), "Food/water loot graph invalid.");
        var profile = InventoryGraph.Create(); var shop = new ShopCommand { RequestId = Guid.NewGuid().ToString(), Operation = ShopOperation.Buy, Code = "water", Quantity = 2, ExpectedVersion = profile.Version };
        Require(ShopRules.Apply(profile, shop, out var bought) == null && bought.Count("water") == 2 && bought.Validate() == InventoryError.None, "Supplier cannot sell new consumables.");
        var low = fresh; low.Energy = low.Hydration = 20;
        var constants = new FirstPersonController.ControllerConsts(); constants.Walk.Speed = 4; constants.Sprint.Speed = 6; constants.JumpHeight = 1;
        var slower = RaidHealth.Movement(low, constants);
        Require(slower.Walk.Speed < 4 && slower.Sprint.Speed < 6 && slower.JumpHeight < 1 && RaidHealth.HandlingMultiplier(low) > 1, "Low reserves have no gameplay effect.");
        low.Energy = 0; var input = default(PlayerInput); input.SetFlag(PlayerInput.InputFlag.Sprint, true);
        Require(!RaidHealth.RestrictInput(low, input).Sprint && RaidHealth.Movement(low, constants).Sprint.Speed == RaidHealth.Movement(low, constants).Walk.Speed, "Starving player can sprint.");
        var full = fresh; full.Satiety = 90;
        Require(RaidNutrition.Movement(full, constants).Sprint.Speed < 6 && RaidNutrition.Movement(full, constants).Walk.Speed == 4, "Full-stomach effect differs.");
        var fatal = fresh; fatal.Energy = fatal.Hydration = 0; Tick(ref fatal, 1000);
        Require(fatal.CurrentHealth == 0, "Metabolic death bypasses death state.");
        var serializerType = typeof(RaidConsumableUseRpc).Assembly.GetTypes().Single(t => t.IsValueType && typeof(IRpcCommandSerializer<RaidConsumableUseRpc>).IsAssignableFrom(t));
        var serializer = (IRpcCommandSerializer<RaidConsumableUseRpc>)Activator.CreateInstance(serializerType);
        using var bytes = new NativeArray<byte>(512, Allocator.Temp); var writer = new DataStreamWriter(bytes);
        serializer.Serialize(ref writer, new RpcSerializerState { CompressionModel = StreamCompressionModel.Default },
            new RaidConsumableUseRpc { RaidId = 2, RequestId = 7, ExpectedVersion = 9, ItemId = "nutrition" });
        var reader = new DataStreamReader(writer.AsNativeArray()); var decoded = default(RaidConsumableUseRpc);
        serializer.Deserialize(ref reader, new RpcDeserializerState { CompressionModel = StreamCompressionModel.Default }, ref decoded);
        Require(!writer.HasFailedWrites && decoded.RequestId == 7 && decoded.ItemId.ToString() == "nutrition" && decoded.ExpectedVersion == 9 && decoded.RaidId == 2, "Generated consumable RPC lost intent fields.");
        Debug.Log("[Raid nutrition checks] Activity/abdomen drain, grace, recovery, inventory transaction, loot, supplier, penalties, death and generated RPC passed.");
    }
}
