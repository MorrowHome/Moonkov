// 把原生 ICE 的收发接到 Unity Transport 接口。Unity 调用 Bind 时会校验逻辑端点，底层 UDP 套接字由 ICE 负责绑定。
using System;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;

namespace Unity.MP_FPS.EmbeddedNetworking {
    /// <summary>Managed struct required by UTP's Windows unmanaged wrapper.</summary>
    public struct EmbeddedIceNetworkInterface : INetworkInterface {
        private readonly EmbeddedIceTransport m_Transport;
        private readonly bool m_OwnsTransport;

        internal EmbeddedIceNetworkInterface(EmbeddedIceTransport transport, bool ownsTransport) {
            m_Transport = transport;
            m_OwnsTransport = ownsTransport;
        }

        public NetworkEndpoint LocalEndpoint => m_Transport.LocalEndpoint;

        public int Initialize(ref NetworkSettings settings, ref int packetPadding) {
            // Refuse accidental plaintext use outside the explicit native datagram check.
            // ICE 只负责找到网络路线。这个 UTP 适配器要求上层已经配置 DTLS，再确认 UDP 包不会超过原生缓冲区大小。
            settings.GetSecureParameters();
            if (settings.GetNetworkConfigParameters().maxMessageSize > EmbeddedIceTransport.MaxDatagram)
                throw new ArgumentException("UTP datagram exceeds the ICE bridge capacity.");
            return 0;
        }

        // NetCode Listen binds AnyIpv4. UTP Connect auto-binds a Custom endpoint
        // using the remote peer address, not LocalEndpoint. Both are logical binds:
        // ICE sockets are already bound to the selected physical address.
        // UTP 会传本地端点、对端逻辑端点或通配监听端点。这里认可已分配的这几种，实际 UDP 已经由 ICE 打开。
        public int Bind(NetworkEndpoint endpoint) {
            return endpoint == LocalEndpoint || m_Transport.HasPeerEndpoint(endpoint) ||
                   (endpoint.Family == NetworkFamily.Ipv4 && endpoint.WithPort(0) == NetworkEndpoint.AnyIpv4)
                ? 0
                : -1;
        }

        public int Listen() { return 0; }

        // 等传输当前的数据任务结束，统一把原生已排队的数据搬到 UTP 接收队列。本地任务句柄不会交给原生回调操作。
        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dependency) {
            // Synchronize once per batch, never on native callbacks.
            // A jobified bridge requires profiling and a separate ownership design.
            dependency.Complete();
            m_Transport.Receive(ref arguments);
            return default;
        }

        // 同样等上一批任务访问完成，然后把 UTP 准备好的待发包交给 ICE。不需要额外排一个 Job。
        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dependency) {
            dependency.Complete();
            m_Transport.Send(ref arguments);
            return default;
        }

        // 关闭 UTP 在这一局所用的包装。是否一并关实际 ICE 连接由创建时的所有权参数决定。
        public void Dispose() { m_Transport.ReleaseDriver(m_OwnsTransport); }
    }
}