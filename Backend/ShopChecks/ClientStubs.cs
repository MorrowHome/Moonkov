#nullable disable
// Unity presentation/preferences and the unrelated lobby state are stubbed.
// AccountClient, its JSON parser and shared shop rules are production sources.
namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static int Min(int a, int b) => Math.Min(a, b);
    }
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Values = new();
        public static string GetString(string key, string fallback) => Values.GetValueOrDefault(key, fallback);
        public static void SetString(string key, string value) => Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() { }
    }
}
namespace Unity.MP_FPS
{
    public static class RaidRules { public const int BagCapacity = 12; }
    // Shop catalogue checks never join an online instance or request a game ticket.
    public static class OnlineLobbyClient
    { public static bool InInstance => false; public static string InstanceId => null; }
    public sealed class GameSettings
    { public static readonly GameSettings Instance = new(); public string PlayerName; }
    public static class RaidGuestIdentity { public static string GetToken(bool create) => ""; }
    public static class AccountServiceSettings
    { public static void ValidateAddress(string baseUrl, bool allowLanHttp) { } }
}
