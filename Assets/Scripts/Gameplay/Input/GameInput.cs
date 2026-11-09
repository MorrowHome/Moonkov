using Unity.MP_FPS;
using UnityEngine;

/// <summary>
/// Owns the shared input actions used by scene components.
/// </summary>
public static class GameInput
{
    // Recreate the actions each session so scene components can subscribe in OnEnable.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RuntimeInitializeOnLoad() => Actions = new InputSystem_Actions();

    // Input System resets action state at BeforeSceneLoad in Player builds.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnableActions() => Actions.Enable();

    public static InputSystem_Actions Actions { get; private set; } = null!;
}
