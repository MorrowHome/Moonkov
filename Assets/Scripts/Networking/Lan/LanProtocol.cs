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

namespace Unity.MP_FPS
{
    public enum LanRoomPhase { Lobby, Preparing, Playing, Returning }
    [Serializable] public sealed class LanMember
    {
        public string Id, Name;
        public bool Host, Ready, InGame;
    }
    [Serializable] public sealed class LanRoomSnapshot
    {
        public int Protocol = LanProtocol.Version, GamePort, Capacity, Round;
        public string Id, Name, Address, HostId;
        public RaidGameMode Mode;
        public LanRoomPhase Phase;
        public LanMember[] Members = Array.Empty<LanMember>();
    }
    public sealed class LanProfileSnapshot
    {
        public RaidPersistenceContext.Profile Profile;
        public RecoveryInbox Recovery;
        public bool RaidActive;
    }
    public sealed class LanRequest
    {
        public int Protocol = LanProtocol.Version;
        public string Action, Credential, Name, RoomId;
        public bool Ready;
        public JObject Payload;
    }
    public sealed class LanReply
    {
        public string Error, MemberId, Ticket;
        public LanRoomSnapshot Room;
        public LanProfileSnapshot Profile;
    }
    // Length-prefixed, bounded UTF-8 JSON; no polymorphic deserialization.
    public static class LanProtocol
    {
        public const int Version = 1, ControlPort = 7978, GamePort = 7979, DiscoveryPort = 7980;
        public const string DiscoveryQuery = "Moonkov.LAN/1?";
        public const int MaxFrame = 2 * 1024 * 1024;
        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings { MaxDepth = 48, TypeNameHandling = TypeNameHandling.None };
        public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Json));
            if (bytes.Length > MaxFrame) throw new InvalidDataException("房间消息过大。");
            byte[] size = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
            await stream.WriteAsync(size, 0, size.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }
        public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
        {
            var size = new byte[4]; await ReadExactly(stream, size, ct).ConfigureAwait(false);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(size, 0));
            if (length < 2 || length > MaxFrame) throw new InvalidDataException("无效房间消息长度。");
            var bytes = new byte[length]; await ReadExactly(stream, bytes, ct).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), Json) ?? throw new InvalidDataException("房间消息为空。");
        }
        private static async Task ReadExactly(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("房主已断开连接。");
                offset += count;
            }
        }
        public static async Task<LanReply> SendAsync(string address, int port, LanRequest request, CancellationToken ct)
        {
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || port < 1 || port > 65535)
                throw new InvalidOperationException("请输入有效的 IPv4 地址和房间端口。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var socket = new TcpClient(AddressFamily.InterNetwork);
            using var registration = timeout.Token.Register(socket.Close);
            try
            {
                await socket.ConnectAsync(ip, port).ConfigureAwait(false);
                using var stream = socket.GetStream();
                await WriteAsync(stream, request, timeout.Token).ConfigureAwait(false);
                var reply = await ReadAsync<LanReply>(stream, timeout.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(reply.Error)) throw new InvalidOperationException(reply.Error);
                return reply;
            }
            catch (Exception) when (timeout.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("房间连接超时，请检查房主地址、网络和防火墙。");
            }
        }
    }
}
