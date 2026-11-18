// 把同类 ECS 系统按 World 分别记下来。主机同时有服务端和客户端 World，两边能各取自己的实例，销毁 World 时只清自己的记录。
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[ResetOnPlayMode("ResetStaticState")]
public abstract partial class ClientServerSingletonSystem<T> : SystemBase where T : SystemBase {
#pragma warning disable UDR0001
    // Reset by ResetStaticState method
    // 即便系统类型一样，房主的客户端和服务器各有自己的状态。以 World 当键，避免查到另一端的实体管理器。
    private static Dictionary<World, T> s_InstanceLookup = new();
#pragma warning restore UDR0001
    protected static void ResetStaticState() { s_InstanceLookup.Clear(); }

    public static T ClientInstance =>
        ClientServerBootstrap.ClientWorld != null &&
        s_InstanceLookup.TryGetValue(ClientServerBootstrap.ClientWorld, out var instance)
            ? instance
            : null;

    public static T ServerInstance =>
        ClientServerBootstrap.ServerWorld != null &&
        s_InstanceLookup.TryGetValue(ClientServerBootstrap.ServerWorld, out var instance)
            ? instance
            : null;

    public static bool TryGetClientInstance(out T clientInstance) {
        if (ClientServerBootstrap.ClientWorld != null)
            return s_InstanceLookup.TryGetValue(ClientServerBootstrap.ClientWorld, out clientInstance);
        clientInstance = null;
        return false;
    }

    public static bool TryGetServerInstance(out T serverInstance) {
        // invert this if
        if (ClientServerBootstrap.ServerWorld != null)
            return s_InstanceLookup.TryGetValue(ClientServerBootstrap.ServerWorld, out serverInstance);
        serverInstance = null;
        return false;
    }

    public static T Instance(World world) {
        return world != null && s_InstanceLookup.TryGetValue(world, out var instance) ? instance : null;
    }

    public static bool TryGetInstance(World world, out T worldInstance) {
        Debug.Assert(world != null);
        return s_InstanceLookup.TryGetValue(world, out worldInstance);
    }

    // 系统加入哪个 World，就只登记这一个对应实例。比如客户端销毁时，服务端的同类系统还可以继续运行。
    protected override void OnCreate() {
        Debug.Assert(!s_InstanceLookup.ContainsKey(World),
            $"{typeof(T)} ClientServerSingletonSystem instance already exists for world {World.Name}");
        s_InstanceLookup.Add(World, this as T);
    }

    // 按当前 World 从表上解除登记。临时切出这一端后，其他类按 World 查找就会拿到空值。
    protected override void OnDestroy() {
        Debug.Assert(s_InstanceLookup.ContainsKey(World),
            "ClientServerSingletonSystem is being destroyed but does not contain a valid instance for its world");
        s_InstanceLookup.Remove(World);
    }
}