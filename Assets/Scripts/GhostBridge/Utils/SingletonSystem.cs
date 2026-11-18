// 给同类 ECS 系统维护一个静态实例指针，创建时登记、销毁时清理。需要按 World 分开的系统使用 ClientServerSingletonSystem。
using Unity.Entities;
using UnityEngine;

[ResetOnPlayMode("ResetStaticState")]
public abstract partial class SingletonSystem<T> : SystemBase where T : SystemBase {
    public static T Instance { get; private set; }
    protected static void ResetStaticState() { Instance = null; }

    // 调用者可能在系统创建之前或 World 已退出时查询。先用返回值确认有没有可操作的实例。
    public static bool TryGetInstance(out T instance) {
        instance = Instance;
        return Instance != null;
    }

    protected override void OnCreate() {
        Debug.Assert(Instance == null, $"[SingletonSystem::OnCreate] {typeof(T)} instance already exists");
        Instance = this as T;
    }

    protected override void OnDestroy() {
        // 只清这次正在销毁的实例。某个 System 连同 World 退场时，不能把后来接管的实例一起清掉。
        if (Instance == this) Instance = null;
    }
}