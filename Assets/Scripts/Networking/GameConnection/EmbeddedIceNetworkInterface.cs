// 把原生 ICE 的收发接到 Unity Transport 接口。Unity 调用 Bind 时会校验逻辑端点，底层 UDP 套接字由 ICE 负责绑定。
using System;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;

namespace Unity.MP_FPS.EmbeddedNetworking {
    /// <summary>UTP 在 Windows 上封装非托管网络接口时要求使用的托管 struct。</summary>
    public struct EmbeddedIceNetworkInterface : INetworkInterface {
        private readonly EmbeddedIceTransport m_Transport;
        private readonly bool m_OwnsTransport;

        internal EmbeddedIceNetworkInterface(EmbeddedIceTransport transport, bool ownsTransport) {
            m_Transport = transport;
            m_OwnsTransport = ownsTransport;
        }

        public NetworkEndpoint LocalEndpoint => m_Transport.LocalEndpoint;

        public int Initialize(ref NetworkSettings settings, ref int packetPadding) {
            // 除专门运行的原生数据报检查外，连接必须配置加密。
            // ICE 只负责找到网络路线。这个 UTP 适配器要求上层已经配置 DTLS，再确认 UDP 包不会超过原生缓冲区大小。
            settings.GetSecureParameters();
            if (settings.GetNetworkConfigParameters().maxMessageSize > EmbeddedIceTransport.MaxDatagram)
                throw new ArgumentException("UTP datagram exceeds the ICE bridge capacity.");
            return 0;
        }

        // NetCode 的 Listen 绑定 AnyIpv4。UTP 的 Connect 则根据远端地址
        // 自动绑定对应的 Custom 端点，和 LocalEndpoint 可能不同。这些都是逻辑绑定，
        // 实际 ICE socket 已绑定到选好的物理地址。
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
            // 每批收发只同步一次，原生回调不负责等待 Job。
            // 改成通过 Job 收发需要性能测量，还要重新设计缓冲区归属。
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