// 场景里的组件用这份共享的 InputSystem 动作表订阅操作。每次运行初始化会重建动作表，Player 载入场景后再把它启用。
using Unity.MP_FPS;
using UnityEngine;

/// <summary>
/// Owns the shared input actions used by scene components.
/// </summary>
public static class GameInput {
    // Recreate the actions each session so scene components can subscribe in OnEnable.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RuntimeInitializeOnLoad() { Actions = new InputSystem_Actions(); }

    // Input System resets action state at BeforeSceneLoad in Player builds.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnableActions() { Actions.Enable(); }

    public static InputSystem_Actions Actions { get; private set; } = null!;
}