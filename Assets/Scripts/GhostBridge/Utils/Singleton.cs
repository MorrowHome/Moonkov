// 给 InputSystemManager 等场景组件提供一个共享实例。Awake 时登记，可选跨场景保留，也支持调用方等实例初始化。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[ResetOnPlayMode("ResetStaticState")]
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour {
    public delegate void OnInitialiseCallback(T singleton);

#pragma warning disable UDR0001
    // ResetStaticState 负责清空这张表。
    private static T s_Instance;
    private static List<OnInitialiseCallback> s_InitialisationCallbacks = new();
#pragma warning restore UDR0001

    public static bool TryGetInstance(out T instance) {
        instance = s_Instance;
        return instance != null;
    }

    protected virtual bool Persistent => false;

    protected static void ResetStaticState() {
        s_Instance = null;
        s_InitialisationCallbacks?.Clear();
    }

    public static T Instance => s_Instance;

    public virtual void Awake() {
        if (s_Instance != null) Debug.LogError($"[Singleton::Awake] {typeof(T).ToString()} instance already exists");
        // 组件创建时先设置实例，再通知等候者。回调里的代码就能直接访问这个刚登记好的对象。
        s_Instance = this as T;
        foreach (var callback in s_InitialisationCallbacks) callback(s_Instance);
        s_InitialisationCallbacks.Clear();
        // 需要跨场景共用的组件可以声明常驻，例如输入设备管理器。普通单例随它所在的场景结束。
        if (Persistent) DontDestroyOnLoad(gameObject);
    }

    public virtual void OnDestroy() {
        // 销毁的组件只有在仍是登记对象时才清掉指针。如果已有另一份实例接班，它保留自己的登记。
        if (s_Instance == this) {
            s_Instance = null;
            s_InitialisationCallbacks.Clear();
        }
    }

    // 依赖者可以提前登记，等 Awake 把实例准备好。已有实例时直接回调，调用方用同一种写法兼容两种顺序。
    public static void OnInitialise(OnInitialiseCallback callback) {
        if (s_Instance != null)
            callback(s_Instance);
        else
            s_InitialisationCallbacks.Add(callback);
    }
}

public abstract class AutoSingleton<T> : Singleton<T> where T : MonoBehaviour {
    protected override bool Persistent => true;

    public new static T Instance {
        get {
            if (!TryGetInstance(out var instance)
#if UNITY_EDITOR
                && EditorApplication.isPlayingOrWillChangePlaymode //只在编辑器进入运行流程后允许自动创建实例。
#endif
               ) {
                Debug.Log($"Creating new instance of type {typeof(T).Name}");
                // AutoSingleton 首次被访问时可以建场景物体，并让 Unity 添加对应组件。普通编辑态会只返回已存在的实例。
                var singleton = new GameObject();
                var newInstance = singleton.AddComponent<T>();
                singleton.name = typeof(T).ToString();
                return newInstance;
            } else { return instance; }
        }
    }
}