#nullable disable
// Only Unity presentation/preferences are stubbed. AccountClient, its JSON
// parser and the shared inventory/catalogue rules are the production sources.
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
    public sealed class GameSettings
    { public static readonly GameSettings Instance = new(); public string PlayerName; }
    public static class RaidGuestIdentity { public static string GetToken(bool create) => ""; }
    public static class AccountServiceSettings
    { public static void ValidateAddress(string baseUrl, bool allowLanHttp) { } }
}
