// 账号注册会附带一个本机访客凭据。正式凭据存入 PlayerPrefs 供以后复用，临时调用则每次生成新值。
using System;
using System.Security.Cryptography;
using UnityEngine;

namespace Unity.MP_FPS {
    public static class RaidGuestIdentity {
        // 原型访客认证。持有这个凭据的一方拥有对应档案。
        // PlayerName 仅用于显示。薄客户端应与主玩家仓库隔离。
        public static string GetToken(bool temporary) {
            const string key = "MoonRaid.GuestCredential.v1";
            // 正式凭据优先复用本机存过的值。临时身份跳过这一步，避免用同一个访客标识访问原玩家的档案。
            string token = temporary ? null : PlayerPrefs.GetString(key, "");
            if (token != null && token.Length == 64) return token;
            // 凭据从安全随机数生成并编码成十六进制。昵称可以重名、也可以改，所以不拿昵称充当身份。
            byte[] bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) { random.GetBytes(bytes); }
            token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            // 只有正式身份写盘。临时身份获得同样格式的值，但下次运行不会从 PlayerPrefs 取回它。
            if (!temporary) {
                PlayerPrefs.SetString(key, token);
                PlayerPrefs.Save();
            }
            return token;
        }
    }
}