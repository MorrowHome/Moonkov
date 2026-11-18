// 场景里的组件用这份共享的 InputSystem 动作表订阅操作。每次运行初始化会重建动作表，Player 载入场景后再把它启用。
using Unity.MP_FPS;
using UnityEngine;

/// <summary>
/// Owns the shared input actions used by scene components.
/// </summary>
public static class GameInput {
    // Recreate the actions each session so scene components can subscribe in OnEnable.
    // 场景脚本进入 OnEnable 时就可能订阅操作，所以先在场景启动前构造 Actions 实例。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RuntimeInitializeOnLoad() { Actions = new InputSystem_Actions(); }

    // Input System resets action state at BeforeSceneLoad in Player builds.
    // Player 读第一个场景前 Input System 会重新设置动作启用状态，因此在场景读完后统一启用这份共享映射。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnableActions() { Actions.Enable(); }

    public static InputSystem_Actions Actions { get; private set; } = null!;
}