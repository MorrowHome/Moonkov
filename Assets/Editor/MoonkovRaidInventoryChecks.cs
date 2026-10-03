using System;
using System.Linq;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MoonkovRaidInventoryChecks
{
    [BurstCompile(CompileSynchronously = true)]
    private struct CreateChunkJob : IJob
    {
        public EntityCommandBuffer Commands;
        public RaidInventoryChunkV2Rpc Chunk;
        public void Execute() => Commands.AddComponent(Commands.CreateEntity(), Chunk);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [MenuItem("Tools/Moonkov/Check Raid Inventory")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        var chunk = new RaidInventoryChunkV2Rpc { RaidId = 3, Sequence = 7, RequestId = 4, Index = 0, Count = 1, Json = new string('x', RaidInventoryTransport.ChunkCharacters) };
        var serializerType = typeof(RaidInventoryChunkV2Rpc).Assembly.GetTypes().Single(type =>
            type.IsValueType && typeof(IRpcCommandSerializer<RaidInventoryChunkV2Rpc>).IsAssignableFrom(type));
        var serializer = (IRpcCommandSerializer<RaidInventoryChunkV2Rpc>)Activator.CreateInstance(serializerType);
        using var bytes = new NativeArray<byte>(1378, Allocator.Temp);
        var writeStream = new DataStreamWriter(bytes);
        serializer.Serialize(ref writeStream, new RpcSerializerState { CompressionModel = StreamCompressionModel.Default }, chunk);
        Require(!writeStream.HasFailedWrites && writeStream.Length + 13 < 1378, "Chunk exceeds transport packet.");
        var reader = new DataStreamReader(writeStream.AsNativeArray());
        var decoded = default(RaidInventoryChunkV2Rpc);
        serializer.Deserialize(ref reader, new RpcDeserializerState { CompressionModel = StreamCompressionModel.Default }, ref decoded);
        Require(decoded.Json.Equals(chunk.Json) && decoded.RequestId == chunk.RequestId, "Generated serializer changed chunk data.");
        using (var world = new World("Inventory RPC check"))
        using (var commands = new EntityCommandBuffer(Allocator.TempJob))
        {
            new CreateChunkJob { Commands = commands, Chunk = decoded }.Schedule().Complete();
            commands.Playback(world.EntityManager);
            using var query = world.EntityManager.CreateEntityQuery(typeof(RaidInventoryChunkV2Rpc));
            Require(query.GetSingleton<RaidInventoryChunkV2Rpc>().Json.Equals(chunk.Json), "Burst ECB uses an incompatible component layout.");
            Require(UnsafeUtility.SizeOf<RaidInventoryChunkV2Rpc>() == TypeManager.GetTypeInfo<RaidInventoryChunkV2Rpc>().SizeInChunk, "RPC/ECS sizes differ.");
        }

        var graph = InventoryGraph.Create(false);
        Require(graph.AddSupply("cells", 2) == InventoryError.None, "Fixture failed.");
        var json = RaidInventoryTransport.Encode(graph);
        var state = new RaidInventoryClientState();
        int count = (json.Length + RaidInventoryTransport.ChunkCharacters - 1) / RaidInventoryTransport.ChunkCharacters;
        // Reverse order and repeat a fragment: incomplete snapshots must not reach the view.
        for (int index = count - 1; index >= 0; index--)
        {
            var fragment = new RaidInventoryChunkV2Rpc { RaidId = 3, Sequence = 1, Count = count, Index = index,
                Json = json.Substring(index * RaidInventoryTransport.ChunkCharacters, Math.Min(RaidInventoryTransport.ChunkCharacters, json.Length - index * RaidInventoryTransport.ChunkCharacters)) };
            state.Receive(fragment, RaidInventoryTransport.Decode);
            state.Receive(fragment, RaidInventoryTransport.Decode);
            if (index > 0) Require(state.Graph == null, "Partial inventory was exposed.");
        }
        Require(state.Graph?.Count("cells", true) == 2, "Chunk assembly lost inventory.");

        var root = new VisualElement();
        using var view = new ContainerInventoryView(root, false, _ => { });
        Require(root.Query<Label>(className: "inventory-section-title").ToList().Count == 3, "Loading inventory hides container headings.");
        Require(root.Q<Label>("item").text == "LOADING", "Unknown equipment is presented as empty.");
        view.Present(graph);
        var method = typeof(ContainerInventoryView).GetMethod("Command", BindingFlags.Instance | BindingFlags.NonPublic);
        var cell = graph.Items.Single(item => item.Code == "cells");
        var move = new InventoryCommand { ItemId = cell.Id, Parent = "pockets", Region = "2" };
        view.SetReadOnly("Raid owns this equipment.");
        method.Invoke(view, new object[] { move });
        Require(!view.Busy, "Locked account still sends equipment moves.");
        view.SetReadOnly(null);
        method.Invoke(view, new object[] { move });
        Require(view.Busy, "Move does not lock the view.");
        view.Present(graph.Clone(), operationCompleted: false);
        Require(view.Busy, "Unrelated snapshot unlocks pending move.");
        view.Present(graph, "Move rejected.");
        Require(!view.Busy, "Move response does not unlock the view.");
        Debug.Log($"Raid inventory checks passed: generated serializer ({writeStream.Length} bytes), Burst ECB layout, out-of-order/duplicate chunks, loading headings, pending-move locking.");
    }
}
