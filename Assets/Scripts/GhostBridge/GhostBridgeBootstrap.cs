// NetCode 的 World 启动入口。普通游戏按需启动客户端或房主的两个 World，专用服启动时就建服务端 World。这里也负责清理与它们对应的 GameObject 树。
#if UNITY_EDITOR
using Unity.Scenes.Editor;
#endif
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

[ResetOnPlayMode("ResetStaticState")]
[UnityEngine.Scripting.Preserve]
public class GhostBridgeBootstrap : ClientServerBootstrap {
#pragma warning disable UDR0001
    public static GhostBridgeBootstrap Instance; // 由 ResetOnPlayMode 特性触发重置。
#pragma warning restore UDR0001
    public new World ServerWorld { get; private set; }
    public new World ClientWorld { get; private set; }

    public static bool IsServerOnly {
        get {
#if UNITY_EDITOR
            return RequestedPlayType == PlayType.Server;
#elif UNITY_SERVER
            return true;
#else
            return false;
#endif
        }
    }

    // 同一个进程里的表现对象按 World 分成两棵树。客户端 World 和服务端 World 都能有某个网络对象，但各自更新各自那棵树。
    private GameObject m_ClientGameObjectHierarchy;

    public GameObject ClientGameObjectHierarchy {
        get {
            if (m_ClientGameObjectHierarchy != null) return m_ClientGameObjectHierarchy;
            // 创建场景层级的根物体。
            m_ClientGameObjectHierarchy = new GameObject {
                name = "Client"
            };
            return m_ClientGameObjectHierarchy;
        }
    }

    private GameObject m_ServerGameObjectHierarchy;

    public GameObject ServerGameObjectHierarchy {
        get {
            if (m_ServerGameObjectHierarchy != null) return m_ServerGameObjectHierarchy;
            // 创建场景层级的根物体。
            m_ServerGameObjectHierarchy = new GameObject {
                name = "Server"
            };
            return m_ServerGameObjectHierarchy;
        }
    }

    private Hash128 m_ClientGuid;
    public Hash128 ClientGuid => m_ClientGuid;

    public static bool TryGetInstance(out GhostBridgeBootstrap instance) {
        instance = Instance;
        return instance != null;
    }

    protected static void ResetStaticState() {
        // 退出时清空静态驱动构造器。
        // 否则可能沿用中继驱动，
        // 导致 IP 直连也用了上一局的驱动。
        // 驱动构造器是静态的，前一局使用的模式可能被带到下一局。重置时把覆盖配置清空，重连才能按新的传输选择建驱动。
        NetworkStreamReceiveSystem.DriverConstructor = null;
        if (Instance == null) return;
        Instance.DestroyMultiplayerWorlds();
        Instance = null;
        // GhostGameObject.UpdateEntityCommandBuffer = default;
    }

    public override bool Initialize(string defaultWorldName) {
        Instance = this;
#if UNITY_SERVER
        // 普通日志省略调用栈，减少控制台里的噪音。
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
#endif
#if UNITY_EDITOR
        LiveConversionEditorSettings.LiveConversionEnabled = false;
#endif
        // Unity 第一次启动时给普通场景留一个默认 World。专用服还会马上建 ServerWorld，因为 ServerScene 启动时就要从里面取监听驱动。
        var world = new World(defaultWorldName, WorldFlags.Game);
        World.DefaultGameObjectInjectionWorld = world;
        if (!IsServerOnly) return true;
        NetworkStreamReceiveSystem.DriverConstructor =
            new Unity.MP_FPS.EntityDriverConstructor(Unity.Services.Multiplayer.NetworkRole.Server);
        CreateServerWorld();
        return true;
    }

    // 进入战局后实体要归入真正的客户端或服务端 World。先释放启动时的普通 World，再给新 World 登记 Ghost 的更新系统。
    public void StartMultiplayerWorlds(bool asHost) {
        Debug.Log($"StartMultiplayerWorlds asHost {asHost}");
        GhostGameObjectUpdateSystem.Reset();

        // 当前已经有联机用的 World，
        // 此处释放启动阶段的默认 World，也能精简编辑器的 World 列表。
        // 这样实体只会读入目标 World，
        // 避免落入默认 World 后偶发无法恢复的实体场景加载错误。
        if (World.DefaultGameObjectInjectionWorld != null) {
            World.DefaultGameObjectInjectionWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;
        }
        if (asHost) {
            var serverWorld = CreateServerWorld();
            GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
        }
        var clientWorld = CreateClientWorld();
        GhostGameObjectUpdateSystem.GatherWorldSystems(clientWorld);
    }

    // 这是先停更、后清理的路径。把 World 移出每帧更新列表并等它的作业完成，后续释放数据时不会还有作业拿着指针。
    private void SafelyQuitWorld(World world) {
        if (world != null) {
            world.QuitUpdate = true;
            world.EntityManager.CompleteAllTrackedJobs();
            ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
        }
    }

    // 退场要同时收 ECS World 和场景树。最后清理桥接的静态回调，这样下一局可以登记新的一批管理器。
    public void DestroyMultiplayerWorlds() {
        if (ClientWorld != null) DestroyWorld(ClientWorld);
        if (ServerWorld != null) DestroyWorld(ServerWorld);
        if (m_ClientGameObjectHierarchy != null) {
            Object.Destroy(m_ClientGameObjectHierarchy);
            m_ClientGameObjectHierarchy = null;
        }
        if (m_ServerGameObjectHierarchy != null) {
            Object.Destroy(m_ServerGameObjectHierarchy);
            m_ServerGameObjectHierarchy = null;
        }
        GhostSingleton.InvokeAndClearStaticCallbacks();
    }

    public void QuitMultiplayerWorlds() {
        if (ClientWorld != null) SafelyQuitWorld(ClientWorld);
        if (ServerWorld != null) SafelyQuitWorld(ServerWorld);
    }

    private void DestroyWorld(World world) {
        if (world != null) {
            world.QuitUpdate = true;
            world.EntityManager.CompleteAllTrackedJobs();
            if (ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(world))
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
            world.Dispose();
        }
    }

    // 客户端 World 自己有一份新生成的 GUID。它用于标识这一份客户端实例，随 World 的创建一起更新。
    private World CreateClientWorld() {
        ClientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");

        // 给这次创建的客户端生成唯一 GUID。
        var hash128 = new UnityEngine.Hash128();
        hash128.Append(System.Guid.NewGuid().ToString());
        m_ClientGuid = hash128;
        return ClientWorld;
    }

    private World CreateServerWorld() {
        ServerWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
        return ServerWorld;
    }
}