// 角色和设备的网络脚本继承这个基类后，可以通过已绑定的 Ghost 读写 ECS 数据、收发 RPC，并在物体绑定和销毁前收到通知。
using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public abstract class GhostMonoBehaviour : MonoBehaviour {
    private GhostGameObject m_GhostGameObject;
    public GhostGameObject GhostGameObject => m_GhostGameObject;
    // 每份场景脚本能读到桥接对象分配的端和 World。共用一个预制件时，脚本可以据此区分服务端、本机和远端职责。
    public MultiplayerRole Role => m_GhostGameObject.Role;
    public World World => GhostGameObject.World;
    public void SetGhostGameObject(GhostGameObject ghostGameObject) { m_GhostGameObject = ghostGameObject; }

    // 访问 Ghost 数据的内部辅助方法。
    protected bool TryReadGhostComponentData<T>(out T data) where T : unmanaged, IComponentData {
        // 可选的 ECS 数据先检查是否存在再读。不同 Ghost 模板的组件集合可能不同，调用方可以用返回值决定是否继续。
        if (GhostHasComponent<T>()) {
            data = ReadGhostComponentData<T>();
            return true;
        }
        data = default;
        return false;
    }

    protected T ReadGhostComponentData<T>() where T : unmanaged, IComponentData {
        return m_GhostGameObject.ReadGhostComponentData<T>();
    }

    protected void ReadGhostComponentData<T>(out T data) where T : unmanaged, IComponentData {
        m_GhostGameObject.ReadGhostComponentData(out data);
    }

    protected void WriteGhostComponentData<T>(in T data) where T : unmanaged, IComponentData {
        m_GhostGameObject.WriteGhostComponentData<T>(data);
    }

    protected bool GhostHasComponent<T>() where T : unmanaged, IComponentData {
        return m_GhostGameObject.GhostHasComponent<T>();
    }

    public DynamicBuffer<T> GetGhostDynamicBuffer<T>() where T : unmanaged, IBufferElementData {
        return m_GhostGameObject.GetGhostDynamicBuffer<T>();
    }

    public virtual bool AllowAutoRegisterForUpdates() { return true; }
    // 等桥接已建立，子类才有可用的实体和 World。要登记或读取同步状态可以放这里，退出时到 OnGhostPreDestroy 收尾。
    public virtual void OnGhostLinked() { }
    public virtual void OnGhostPreDestroy() { }

    protected void BroadcastRPC<T>(T rpcCommand) where T : unmanaged, IRpcCommand {
        GhostGameObject.BroadcastRPC(rpcCommand);
    }

    protected bool ConsumeRPC<T>(out T rpc) where T : unmanaged, IRpcCommand {
        return GhostGameObject.ConsumeRPC(out rpc);
    }

    protected void IterateRPC<T>(Action<T> callback) where T : unmanaged, IRpcCommand {
        GhostGameObject.IterateRPC(callback);
    }

    // 场景脚本从自己这个实例发定向消息，底层会把这个 Ghost 的标识一起写进去。
    protected void SendDirectedRPC<T>(T rpcCommand) where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC {
        GhostGameObject.SendDirectedRPC(rpcCommand);
    }

    protected bool ConsumeDirectedRPC<T>(out T rpc) where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC {
        return GhostGameObject.ConsumeDirectedRPC(out rpc);
    }

    protected T GetRequiredComponent<T>() where T : Component {
        if (!TryGetComponent<T>(out var component))
            Debug.LogError($"{name} does not contain required component <{typeof(T).Name}>");
        return component;
    }

    protected void GetRequiredComponent<T>(out T component) where T : Component {
        if (!TryGetComponent<T>(out component))
            Debug.LogError($"{name} does not contain required component <{typeof(T).Name}>");
    }

    protected T GetRequiredComponentInChildren<T>() where T : Component {
        var component = GetComponentInChildren<T>();
        Debug.Assert(component != null, $"{name} does not contain required component in children <{typeof(T).Name}>");
        return component;
    }

    protected void GetRequiredComponentInChildren<T>(out T component) where T : Component {
        component = GetComponentInChildren<T>();
        Debug.Assert(component != null, $"{name} does not contain required component in children <{typeof(T).Name}>");
    }
}