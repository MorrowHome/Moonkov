// 把一次跨网好友邀请中需要交换的房间身份、两条 ICE 路线和临时证书编成可复制文本。房主用临时证书启动加密连接，来宾用邀请里的证书验证对方。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;

namespace Unity.MP_FPS {
    internal sealed class LanPeerIdentity {
        private const string HostName = "moonkov-friend-room";
        public string Certificate { get; }
        private readonly string m_PrivateKey;

        public LanPeerIdentity(string certificate, string privateKey = null) {
            Certificate = certificate;
            m_PrivateKey = privateKey;
        }

        public void Configure(ref NetworkSettings settings, bool server) {
            // 房主用证书配私钥，来宾把邀请里的公开证书当作这个房间的信任根。两端同用一个事先约好的名称。
            if (server)
                settings.WithSecureServerParameters(Pem("CERTIFICATE", Certificate), m_PrivateKey);
            else
                settings.WithSecureClientParameters(Pem("CERTIFICATE", Certificate), HostName);
        }

        private static string Pem(string type, string data) {
            return $"-----BEGIN {type}-----\n{data}\n-----END {type}-----\n";
        }

        // Unity's Mono profile does not implement CertificateRequest. Encode a
        // short-lived self-signed X.509 certificate and sign it with the runtime's RSA.
        // Only the public certificate leaves this process, in the private invitation.
        public static LanPeerIdentity Create() {
            // 临时证书在本机创建，私钥不落系统密钥存储。Unity 的 Mono 缺少常用证书创建 API，因此代码自己组装所需字段。
            using var rsa = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            var key = rsa.ExportParameters(true);
            try {
                byte[] name =
                    Sequence(Der(0x31, Sequence(Oid(0x55, 4, 3), Der(0x0c, Encoding.UTF8.GetBytes(HostName)))));
                byte[] signatureAlgorithm = Sequence(Oid(0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 1, 1, 11), Der(5));
                byte[] publicKey = Sequence(Sequence(Oid(0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 1, 1, 1), Der(5)),
                    Der(3, new byte[] { 0 }, Sequence(Integer(key.Modulus), Integer(key.Exponent))));
                // The self-signed certificate is also the explicit trust anchor.
                byte[] constraints = Sequence(Oid(0x55, 0x1d, 0x13), Der(1, new byte[] { 0xff }),
                    Der(4, Sequence(Der(1, new byte[] { 0xff }), Integer(new byte[] { 0 }))));
                var now = DateTime.UtcNow;
                byte[] body = Sequence(Der(0xa0, Integer(new byte[] { 2 })), Integer(Guid.NewGuid().ToByteArray()),
                    signatureAlgorithm, name, Sequence(Date(now.AddDays(-2)), Date(now.AddDays(7))), name, publicKey,
                    Der(0xa3, Sequence(constraints)));
                // 房主用刚生成的私钥签自己这张证书，发送出去的是客人能用来验证的公开证书。
                byte[] signature = rsa.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                byte[] certificate = Sequence(body, signatureAlgorithm, Der(3, new byte[] { 0 }, signature));
                byte[] privateKey = Sequence(Integer(new byte[] { 0 }), Integer(key.Modulus), Integer(key.Exponent),
                    Integer(key.D), Integer(key.P), Integer(key.Q), Integer(key.DP), Integer(key.DQ),
                    Integer(key.InverseQ));
                try {
                    return new LanPeerIdentity(Convert.ToBase64String(certificate),
                        Pem("RSA PRIVATE KEY", Convert.ToBase64String(privateKey)));
                // 转换成驱动所需的证书文本后，清掉用过的临时私钥字节缓冲。
                } finally { Array.Clear(privateKey, 0, privateKey.Length); }
            } finally {
                foreach (byte[] value in new[] { key.D, key.P, key.Q, key.DP, key.DQ, key.InverseQ })
                    Array.Clear(value, 0, value.Length);
            }
        }

        private static byte[] Date(DateTime date) {
            bool utcTime = date.Year < 2050;
            string value = date.ToString(utcTime ? "yyMMddHHmmss'Z'" : "yyyyMMddHHmmss'Z'",
                CultureInfo.InvariantCulture);
            return Der(utcTime ? (byte)0x17 : (byte)0x18, Encoding.ASCII.GetBytes(value));
        }

        private static byte[] Oid(params byte[] value) { return Der(6, value); }
        private static byte[] Sequence(params byte[][] items) { return Der(0x30, items); }

        private static byte[] Integer(byte[] value) {
            int start = 0;
            while (start < value.Length - 1 && value[start] == 0) start++;
            byte[] significant = value.AsSpan(start).ToArray();
            return (significant[0] & 0x80) != 0 ? Der(2, new byte[] { 0 }, significant) : Der(2, significant);
        }

        private static byte[] Der(byte tag, params byte[][] items) {
            int length = 0;
            foreach (byte[] item in items) length += item.Length;
            var header = new List<byte>(4) { tag };
            if (length < 128) { header.Add((byte)length); } else if (length < 256) {
                header.Add(0x81);
                header.Add((byte)length);
            } else {
                header.Add(0x82);
                header.Add((byte)(length >> 8));
                header.Add((byte)length);
            }
            byte[] bytes = new byte[header.Count + length];
            header.CopyTo(bytes);
            int offset = header.Count;
            foreach (byte[] item in items) {
                item.CopyTo(bytes, offset);
                offset += item.Length;
            }
            return bytes;
        }
    }

    internal sealed class LanPeerInvitation {
        private static readonly string OfferPrefix = $"MK{LanProtocol.Version}I",
            ReplyPrefix = $"MK{LanProtocol.Version}R";

        public string Id, RoomId, HostId, Control, Raid, Certificate;

        // 邀请和回执有不同前缀，且都带房间协议版本。接收到文本时可先判断玩家是不是把回执当邀请粘贴。
        public string Encode(bool reply) {
            string descriptions = $"{Id}.{EncodeDescription(Control)}.{EncodeDescription(Raid)}";
            return reply
                ? $"{ReplyPrefix}.{descriptions}"
                : $"{OfferPrefix}.{descriptions}.{RoomId}.{HostId}.{Certificate}";
        }

        // 解码先检查字符、段数和大小。只有邀请携带房主身份和证书，回执仅用同一个邀请号及来宾的两个连接描述。
        public static LanPeerInvitation Decode(string text, bool reply) {
            string error = reply ? "剪贴板里没有有效的跨网回执。" : "剪贴板里没有有效的跨网邀请。";
            if (string.IsNullOrWhiteSpace(text) || text.Length > 14000) throw new InvalidDataException(error);
            string[] parts = text.Trim().Split('.');
            if (parts.Length != (reply ? 4 : 7) || parts[0] != (reply ? ReplyPrefix : OfferPrefix) ||
                !Guid.TryParseExact(parts[1], "N", out _))
                throw new InvalidDataException(error + "请确认双方使用相同游戏版本。");
            try {
                var invite = new LanPeerInvitation
                    { Id = parts[1], Control = DecodeDescription(parts[2]), Raid = DecodeDescription(parts[3]) };
                if (!reply) {
                    if (!Guid.TryParseExact(parts[4], "N", out _) || !Guid.TryParseExact(parts[5], "N", out _) ||
                        parts[6].Length > 2048 || Convert.FromBase64String(parts[6]).Length < 256)
                        throw new FormatException();
                    invite.RoomId = parts[4];
                    invite.HostId = parts[5];
                    invite.Certificate = parts[6];
                }
                return invite;
            } catch (FormatException) { throw new InvalidDataException(error); }
        }

        private static string EncodeDescription(string description) {
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(description));
        }

        private static string DecodeDescription(string value) {
            byte[] bytes = Convert.FromBase64String(value);
            if (bytes.Length == 0 || bytes.Length > 4095 || Array.Exists(bytes, b => b == 0 || b > 127))
                throw new FormatException();
            return Encoding.ASCII.GetString(bytes);
        }
    }
}