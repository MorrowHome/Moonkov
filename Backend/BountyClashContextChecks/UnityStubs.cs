using Unity.MP_FPS.Inventory;
namespace Unity.Collections
{
    public struct FixedString64Bytes {string value; public override string ToString()=>value??"";public static implicit operator FixedString64Bytes(string v)=>new(){value=v};}
}
namespace Unity.Entities
{
    public interface IComponentData { }
    public struct Entity { public int Index; }
}
namespace UnityEngine
{
    public static class Application { public static string dataPath;public static string persistentDataPath=>Path.GetDirectoryName(dataPath); }
    public enum RuntimeInitializeLoadType{SubsystemRegistration}
    [AttributeUsage(AttributeTargets.Method)]public sealed class RuntimeInitializeOnLoadMethodAttribute:Attribute{public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type){}}
    public static class Mathf{public static int Clamp(int v,int min,int max)=>Math.Clamp(v,min,max);public static int Min(int a,int b)=>Math.Min(a,b);}
    public static class PlayerPrefs
    {static readonly Dictionary<string,string> data=new();public static string GetString(string key,string fallback)=>data.GetValueOrDefault(key,fallback);public static void SetString(string key,string v)=>data[key]=v;public static void DeleteKey(string key)=>data.Remove(key);public static void Save(){}}
    public static class Debug { public static void Log(object s)=>Console.WriteLine(s);public static void LogWarning(object s)=>Console.WriteLine(s); }
}
namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]public class MenuItem:Attribute{public MenuItem(string text){}}
    public static class EditorApplication{public static bool isPlayingOrWillChangePlaymode;}
}
namespace Unity.MP_FPS
{
    public enum RaidPhase { Active,Extracted,Dead,TimedOut }
    public enum RaidLoadoutError{None,InvalidCount,InsufficientCells,Rejected}
    public struct RaidSession{public int RaidId,Dust,Alloy,Cells;public RaidPhase Phase;public bool PersistentDeployment;public Unity.Collections.FixedString64Bytes SettlementId;}
    public static class RaidRules{public const int BagCapacity=12;public static bool ValidLoadout(int i)=>i>=0&&i<=12;}
    public sealed class RaidInventoryState{public InventoryGraph Graph;public int RaidId;}
    public static class RaidInventoryTransport{public static InventoryGraph Decode(string s)=>Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryGraph>(s);public static string Encode(InventoryGraph g)=>Newtonsoft.Json.JsonConvert.SerializeObject(g);}
    public sealed class GameSettings{public static readonly GameSettings Instance=new();public string PlayerName;}
    public static class MoonkovLocalization{public static string Text(string text)=>text;}
    public static class RaidGuestIdentity{public static string GetToken(bool create)=>"";}
    public static class AccountServiceSettings{public static void ValidateAddress(string baseUrl,bool allowLan){}}
}
