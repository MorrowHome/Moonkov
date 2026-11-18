// 账号注册会附带一个本机访客凭据。正式凭据存入 PlayerPrefs 供以后复用，临时调用则每次生成新值。
using System;
using System.Security.Cryptography;
using UnityEngine;

namespace Unity.MP_FPS {
    public static class RaidGuestIdentity {
        // Prototype guest authentication: possession of this credential owns the profile.
        // PlayerName is display-only. Thin clients must not share the main player's stash.
        public static string GetToken(bool temporary) {
            const string key = "MoonRaid.GuestCredential.v1";
            string token = temporary ? null : PlayerPrefs.GetString(key, "");
            if (token != null && token.Length == 64) return token;
            byte[] bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) { random.GetBytes(bytes); }
            token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            if (!temporary) {
                PlayerPrefs.SetString(key, token);
                PlayerPrefs.Save();
            }
            return token;
        }
    }
}