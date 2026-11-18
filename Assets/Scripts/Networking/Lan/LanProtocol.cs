// 房间请求和回包的数据格式。局域网用 TCP 发送 JSON，先写长度让接收方知道要读多少。跨网控制通道复用相同的对象和编码。
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Online;

namespace Unity.MP_FPS {
    // 准备室、加载中、战局进行中和回房中是房主发布的阶段。客户端收到阶段变化才启动或停止战局 World。
    public enum LanRoomPhase {
        Lobby,
        Preparing,
        Playing,
        Returning
    }

    [Serializable]
    public sealed class LanMember {
        public string Id, Name;
        public bool Host, Ready, InGame;
        public LobbyAppearance Appearance = new();
    }

    [Serializable]
    public sealed class LanRoomSnapshot {
        public int Protocol = LanProtocol.Version, GamePort, Capacity, Round;
        public string Id, Name, Address, HostId;
        public RaidGameMode Mode;
        public LanRoomPhase Phase;
        public LanMember[] Members = Array.Empty<LanMember>();
    }

    public sealed class LanProfileSnapshot {
        public RaidPersistenceContext.Profile Profile;
        public RecoveryInbox Recovery;
        public bool RaidActive;
    }

    // 房间请求带动作和房间身份。仓库改动的详细命令放 Payload，房主看动作类型再反序列化。
    public sealed class LanRequest {
        public int Protocol = LanProtocol.Version;
        public string Action, Credential, Name, RoomId;
        public bool Ready;
        public int CharacterIndex = 2, PoseIndex = -1;
        public JObject Payload;
    }

    public sealed class LanReply {
        public string Error, MemberId, Ticket;
        public LanRoomSnapshot Room;
        public LanProfileSnapshot Profile;
    }

    // UTF-8 JSON 带长度和大小限制，反序列化使用固定消息类型。
    public static class LanProtocol {
        public const int Version = 3, ControlPort = 7978, GamePort = 7979, DiscoveryPort = 7980;
        public const string DiscoveryQuery = "Moonkov.LAN/2?";
        public const int MaxFrame = 2 * 1024 * 1024;

        public static readonly JsonSerializerSettings Json = new()
            { MaxDepth = 48, TypeNameHandling = TypeNameHandling.None };

        internal static byte[] Encode<T>(T value) {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Json));
            if (bytes.Length > MaxFrame) throw new InvalidDataException("房间消息过大。");
            return bytes;
        }

        internal static T Decode<T>(byte[] bytes) {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), Json) ??
                   throw new InvalidDataException("房间消息为空。");
        }

        public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct) {
            byte[] bytes = Encode(value);
            // TCP 只给连续字节，写入一次不等于读端一次收到完整包。用固定网络字节序的长度把两条 JSON 分开。
            byte[] size = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
            await stream.WriteAsync(size, 0, size.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }

        public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct) {
            byte[] size = new byte[4];
            await ReadExactly(stream, size, ct).ConfigureAwait(false);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(size, 0));
            // 长度超出协议限制直接拒绝。先核对大小再分配读取缓冲，免得异常包申请过量内存。
            if (length < 2 || length > MaxFrame) throw new InvalidDataException("无效房间消息长度。");
            byte[] bytes = new byte[length];
            await ReadExactly(stream, bytes, ct).ConfigureAwait(false);
            return Decode<T>(bytes);
        }

        private static async Task ReadExactly(Stream stream, byte[] buffer, CancellationToken ct) {
            int offset = 0;
            while (offset < buffer.Length) {
                // 一次 ReadAsync 可能只读到开头。逐次累加到声明长度，返回零表示对面中途断了。
                int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("房主已断开连接。");
                offset += count;
            }
        }

        public static async Task<LanReply> SendAsync(
            string address,
            int port,
            LanRequest request,
            CancellationToken ct) {
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                port < 1 || port > 65535)
                throw new InvalidOperationException("请输入有效的 IPv4 地址和房间端口。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // 局域网一次请求开一个 TCP 连接，写一条读一条。超时或取消会关闭 socket，从而结束还卡住的读写。
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var socket = new TcpClient(AddressFamily.InterNetwork);
            using var registration = timeout.Token.Register(socket.Close);
            try {
                await socket.ConnectAsync(ip, port).ConfigureAwait(false);
                using var stream = socket.GetStream();
                await WriteAsync(stream, request, timeout.Token).ConfigureAwait(false);
                var reply = await ReadAsync<LanReply>(stream, timeout.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(reply.Error)) throw new InvalidOperationException(reply.Error);
                return reply;
            } catch (Exception) when (timeout.IsCancellationRequested) {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("房间连接超时，请检查房主地址、网络和防火墙。");
            }
        }
    }
}