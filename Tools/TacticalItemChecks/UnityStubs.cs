// Minimal engine types needed to exercise the production weight and sprint rules.
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Clamp01(float value) => System.Math.Clamp(value, 0, 1);
    }
}

public struct PredictedPlayerGhost
{
    public bool ClashEnabled;
    public float CarriedWeight;
}

public class FirstPersonController
{
    public struct ControllerConsts
    {
        public struct StateConsts { public float Speed; }
        public StateConsts Walk, Sprint;
        public float JumpHeight;
    }
}
