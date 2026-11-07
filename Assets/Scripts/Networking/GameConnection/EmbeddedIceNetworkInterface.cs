using System;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;

namespace Unity.MP_FPS.EmbeddedNetworking
{
    /// <summary>Managed struct required by UTP's Windows unmanaged wrapper.</summary>
    public struct EmbeddedIceNetworkInterface : INetworkInterface
    {
        readonly EmbeddedIceTransport m_Transport;
        internal EmbeddedIceNetworkInterface(EmbeddedIceTransport transport) { m_Transport = transport; }
        public NetworkEndpoint LocalEndpoint => m_Transport.LocalEndpoint;
        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            // Refuse accidental plaintext use outside the explicit native datagram check.
            settings.GetSecureParameters();
            if (settings.GetNetworkConfigParameters().maxMessageSize > EmbeddedIceTransport.MaxDatagram)
                throw new ArgumentException("UTP datagram exceeds the ICE bridge capacity.");
            return 0;
        }
        // NetCode Listen binds AnyIpv4. UTP Connect auto-binds a Custom endpoint
        // using the remote peer address, not LocalEndpoint. Both are logical binds:
        // ICE sockets are already bound to the selected physical address.
        public int Bind(NetworkEndpoint endpoint) => endpoint == LocalEndpoint ||
            m_Transport.HasPeerEndpoint(endpoint) ||
            endpoint.Family == NetworkFamily.Ipv4 && endpoint.WithPort(0) == NetworkEndpoint.AnyIpv4 ? 0 : -1;
        public int Listen() => 0;
        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dependency)
        {
            // Synchronize once per batch, never on native callbacks.
            // A jobified bridge requires profiling and a separate ownership design.
            dependency.Complete();
            m_Transport.Receive(ref arguments);
            return default;
        }
        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dependency)
        {
            dependency.Complete();
            m_Transport.Send(ref arguments);
            return default;
        }
        public void Dispose() => m_Transport.Dispose();
    }
}
