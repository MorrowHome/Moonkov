using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    // A bounded UI offer, not permission to spawn a new raid. The server must find
    // the same authenticated participant and transfer the retained deployment.
    public static class SessionReconnect
    {
        static string s_Address, s_Port, s_Code;
        static RaidGameMode s_Mode;
        static CreationType s_Route;
        static bool s_CanResume;
        static string s_AccountScope;
        static DateTime s_Deadline;
        public static int SecondsRemaining => Math.Max(0, (int)Math.Ceiling((s_Deadline - DateTime.UtcNow).TotalSeconds));
        public static bool Available => s_CanResume && SecondsRemaining > 0 && !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
            !string.IsNullOrEmpty(s_AccountScope) && s_AccountScope == PlayerProfileClient.LocalPreferenceScope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_CanResume = false; s_Deadline = default; }

        public static void Remember(CreationType route)
        {
            s_CanResume = route == CreationType.ConnectAndJoin || route == CreationType.JoinRelay;
            s_Route = route; s_Address = ConnectionSettings.Instance.IPAddress;
            s_Port = ConnectionSettings.Instance.Port; s_Code = ConnectionSettings.Instance.SessionCode;
            s_Mode = GameSettings.Instance.RequestedMode; s_Deadline = default;
            s_AccountScope = PlayerProfileClient.LocalPreferenceScope;
        }
        public static void Offer()
        { if (s_CanResume && s_Deadline == default) s_Deadline = DateTime.UtcNow.AddSeconds(90); }
        public static void Dismiss() { s_CanResume = false; s_Deadline = default; }
        public static CreationType Restore()
        {
            if (!Available) throw new InvalidOperationException("重连时限已过，请等待原战局结算后重新出击。");
            ConnectionSettings.Instance.IPAddress = s_Address; ConnectionSettings.Instance.Port = s_Port;
            ConnectionSettings.Instance.SessionCode = s_Code; GameSettings.Instance.RequestedMode = s_Mode;
            return s_Route;
        }
    }
}
