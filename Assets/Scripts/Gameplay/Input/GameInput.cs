// 场景里的组件用这份共享的 InputSystem 动作表订阅操作。每次运行初始化会重建动作表，Player 载入场景后再把它启用。
using Unity.MP_FPS;
using UnityEngine;

/// <summary>
/// 保存场景组件共同使用的输入动作。
/// </summary>
public static class GameInput {
    // 每次会话重新创建输入动作，场景组件可在 OnEnable 里订阅。
    // 场景脚本进入 OnEnable 时就可能订阅操作，所以先在场景启动前构造 Actions 实例。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RuntimeInitializeOnLoad() { Actions = new InputSystem_Actions(); }

    // Player 构建中的 Input System 会在 BeforeSceneLoad 重置动作状态。
    // Player 读第一个场景前 Input System 会重新设置动作启用状态，因此在场景读完后统一启用这份共享映射。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnableActions() { Actions.Enable(); }

    public static InputSystem_Actions Actions { get; private set; } = null!;
}