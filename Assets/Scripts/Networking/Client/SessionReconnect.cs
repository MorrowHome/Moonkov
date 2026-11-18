// 负责记住上次在线连接的地址、玩法和发起账号，掉线时向同一账号短时提供重连按钮。恢复旧角色要由服务器找到还在保留期内的出战记录。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    // A bounded UI offer, not permission to spawn a new raid. The server must find
    // the same authenticated participant and transfer the retained deployment.
    public static class SessionReconnect {
        private static string s_Address, s_Port, s_Code;
        private static RaidGameMode s_Mode;
        private static CreationType s_Route;
        private static bool s_CanResume;
        private static string s_AccountScope;
        private static DateTime s_Deadline;

        public static int SecondsRemaining =>
            Math.Max(0, (int)Math.Ceiling((s_Deadline - DateTime.UtcNow).TotalSeconds));

        // 记录存在、时间没过、仍是原账号并且属于在线模式时，菜单才可以使用这条重连信息。
        public static bool Available =>
            s_CanResume && SecondsRemaining > 0 && !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
            !string.IsNullOrEmpty(s_AccountScope) && s_AccountScope == PlayerProfileClient.LocalPreferenceScope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            s_CanResume = false;
            s_Deadline = default;
        }

        public static void Remember(CreationType route) {
            // 只记住加入远端的连接方式。单人和开本机房的生命周期由它们各自管理。
            s_CanResume = route == CreationType.ConnectAndJoin || route == CreationType.JoinRelay;
            s_Route = route;
            s_Address = ConnectionSettings.Instance.IPAddress;
            s_Port = ConnectionSettings.Instance.Port;
            s_Code = ConnectionSettings.Instance.SessionCode;
            s_Mode = GameSettings.Instance.RequestedMode;
            s_Deadline = default;
            s_AccountScope = PlayerProfileClient.LocalPreferenceScope;
        }

        // 断开后只打开一次短时窗口。重复收到断开信号不能重新计时，否则 UI 会比服务器保留角色更久。
        public static void Offer() {
            if (s_CanResume && s_Deadline == default) s_Deadline = DateTime.UtcNow.AddSeconds(90);
        }

        public static void Dismiss() {
            s_CanResume = false;
            s_Deadline = default;
        }

        public static CreationType Restore() {
            if (!Available) throw new InvalidOperationException("重连时限已过，请等待原战局结算后重新出击。");
            // 把地址、会话码和玩法放回正常加载要读的设置。返回的入口类型还会决定重新走直连还是中继。
            ConnectionSettings.Instance.IPAddress = s_Address;
            ConnectionSettings.Instance.Port = s_Port;
            ConnectionSettings.Instance.SessionCode = s_Code;
            GameSettings.Instance.RequestedMode = s_Mode;
            return s_Route;
        }
    }
}