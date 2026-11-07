using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Unity.Collections;
using Unity.Networking.Transport;

namespace Unity.MP_FPS.EmbeddedNetworking
{
    public enum EmbeddedIceState { Disconnected, Gathering, Connecting, Connected, Completed, Failed }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct EmbeddedIceStatistics
    {
        public ulong ReceivedPackets, ReceivedBytes, SentPackets, SentBytes;
        public ulong DroppedPackets, SendErrors, QueuedPackets;
    }

    /// <summary>
    /// Windows x64 ICE datagrams selected per raid. No service, adapter or account is installed.
    /// A driver owns exactly one context. Dispose its completed driver before abandoning a session.
    /// Signaling must authenticate peers before calling AddPeer / SetRemoteDescription.
    /// ICE is not encryption: always put the existing DTLS layer above this interface.
    /// </summary>
    public sealed class EmbeddedIceTransport : IDisposable
    {
        public const int MaxDatagram = 1472;
        public const int MaxPeers = 64;
        const int MaxPacketsPerUpdate = 256;
        readonly object m_Gate = new object();
        readonly NativeHandle m_Handle;
        readonly Dictionary<int, NetworkEndpoint> m_Endpoints = new Dictionary<int, NetworkEndpoint>(MaxPeers);
        readonly Dictionary<NetworkEndpoint, int> m_Peers = new Dictionary<NetworkEndpoint, int>(MaxPeers);
        readonly byte[] m_ReceiveBuffer = new byte[MaxDatagram];
        bool m_Attached;
        public NetworkEndpoint LocalEndpoint { get; } = MakeEndpoint(0);
        public ulong AdapterDrops { get; private set; }
        public bool IsDisposed { get { lock (m_Gate) return m_Handle == null || m_Handle.IsClosed; } }

        public EmbeddedIceTransport(int receiveCapacity = 256)
        {
#if !(UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
            throw new PlatformNotSupportedException("内置 P2P 当前支持 Windows x64 客户端和独立服务器。");
#else
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Embedded ICE requires x64.");
            if (receiveCapacity < 8 || receiveCapacity > 4096) throw new ArgumentOutOfRangeException(nameof(receiveCapacity));
            if (Native.mice_abi_version() != 1) throw new InvalidOperationException("Embedded ICE native ABI mismatch.");
            m_Handle = new NativeHandle(Native.mice_create(receiveCapacity));
            if (m_Handle.IsInvalid) { m_Handle.Dispose(); throw new InvalidOperationException("Could not allocate ICE context."); }
#endif
        }

        public int AddPeer(string bindAddress = null, string stunHost = null, ushort stunPort = 3478)
        {
            // Explicit loopback binding is reserved for the isolated editor check.
            // The game path supplies its selected physical interface and authenticated peer.
            ValidateText(bindAddress, 255, nameof(bindAddress));
            ValidateText(stunHost, 255, nameof(stunHost));
            // Resolve DNS in the signaling layer, with its own cancellation.
            // libjuice destruction can wait for an outstanding native DNS resolver.
            if (!string.IsNullOrEmpty(bindAddress) && !IPAddress.TryParse(bindAddress, out _))
                throw new ArgumentException("ICE bind address must be an IP literal.", nameof(bindAddress));
            if (!string.IsNullOrEmpty(stunHost) && !IPAddress.TryParse(stunHost, out _))
                throw new ArgumentException("Resolve the STUN hostname before creating an ICE peer.", nameof(stunHost));
            lock (m_Gate)
            {
                ThrowIfDisposed();
                int id = Native.mice_add_peer(m_Handle, bindAddress, stunHost, stunPort);
                Check(id, "add peer");
                var endpoint = MakeEndpoint(id);
                m_Endpoints.Add(id, endpoint);
                m_Peers.Add(endpoint, id);
                return id;
            }
        }

        public void RemovePeer(int peer)
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                var endpoint = GetEndpoint(peer);
                Check(Native.mice_remove_peer(m_Handle, peer), "remove peer");
                m_Endpoints.Remove(peer);
                m_Peers.Remove(endpoint);
            }
        }

        public NetworkEndpoint GetEndpoint(int peer)
        {
            lock (m_Gate) { ThrowIfDisposed(); return m_Endpoints[peer]; }
        }

        public void Gather(int peer)
        {
            lock (m_Gate) { ThrowIfDisposed(); Check(Native.mice_gather(m_Handle, peer), "gather"); }
        }

        public bool IsGatheringComplete(int peer)
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                int value = Native.mice_gathered(m_Handle, peer);
                Check(value, "gathering status");
                return value == 1;
            }
        }

        public EmbeddedIceState GetState(int peer)
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                int value = Native.mice_state(m_Handle, peer);
                Check(value, "state");
                return (EmbeddedIceState)value;
            }
        }

        public string GetLocalDescription(int peer)
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                // One complete SDP exchange. Do not log it: it contains session credentials.
                if (!IsGatheringComplete(peer)) throw new InvalidOperationException("Wait for ICE candidate gathering to finish.");
                var description = new StringBuilder(4096);
                Check(Native.mice_description(m_Handle, peer, description, description.Capacity), "local description");
                return description.ToString();
            }
        }

        public void SetRemoteDescription(int peer, string description)
        {
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("ICE description is empty.", nameof(description));
            ValidateText(description, 4095, nameof(description));
            lock (m_Gate) { ThrowIfDisposed(); Check(Native.mice_remote_description(m_Handle, peer, description), "remote description"); }
        }

        public EmbeddedIceStatistics GetStatistics()
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                Check(Native.mice_get_stats(m_Handle, out var result), "statistics");
                return result;
            }
        }

        // This wrapper must be a managed STRUCT on Windows (UTP issue 1412477).
        // Ownership passes to one driver; wrapping twice would double-release UTP storage.
        public NetworkInterfaceUnmanagedWrapper<EmbeddedIceNetworkInterface> CreateNetworkInterface()
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                if (m_Attached) throw new InvalidOperationException("An ICE context can belong to only one driver.");
                var wrapper = new EmbeddedIceNetworkInterface(this).WrapToUnmanaged();
                m_Attached = true;
                return wrapper;
            }
        }

        internal unsafe void Receive(ref ReceiveJobArguments arguments)
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                fixed (byte* buffer = m_ReceiveBuffer)
                {
                    for (int i = 0; i < MaxPacketsPerUpdate && arguments.ReceiveQueue.Count < arguments.ReceiveQueue.Capacity; i++)
                    {
                        int size = Native.mice_receive(m_Handle, out int peer, (IntPtr)buffer, MaxDatagram);
                        if (size == 0) break;
                        Check(size, "receive");
                        if (!m_Endpoints.TryGetValue(peer, out var endpoint)) { AdapterDrops++; continue; }
                        if (!arguments.ReceiveQueue.EnqueuePacket(out var packet)) { AdapterDrops++; break; }
                        if (size > packet.BytesAvailableAtEnd) { packet.Drop(); AdapterDrops++; continue; }
                        packet.EndpointRef = endpoint;
                        packet.AppendToPayload(buffer, size);
                    }
                }
            }
        }

        internal unsafe void Send(ref SendJobArguments arguments)
        {
            lock (m_Gate)
            {
                ThrowIfDisposed();
                for (int i = 0; i < arguments.SendQueue.Count; i++)
                {
                    var packet = arguments.SendQueue[i];
                    if (packet.Length == 0) continue;
                    if (m_Peers.TryGetValue(packet.EndpointRef, out int peer))
                    {
                        int result = Native.mice_send(m_Handle, peer,
                            (IntPtr)((byte*)packet.GetUnsafePayloadPtr() + packet.Offset), packet.Length);
                        // UDP send failure is loss, not a hidden retry/unbounded queue.
                        if (result < 0) AdapterDrops++;
                    }
                    else AdapterDrops++;
                    packet.Drop();
                }
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                m_Handle?.Dispose();
                m_Endpoints.Clear();
                m_Peers.Clear();
            }
        }

        void ThrowIfDisposed()
        {
            if (m_Handle == null || m_Handle.IsClosed) throw new ObjectDisposedException(nameof(EmbeddedIceTransport));
        }
        static void Check(int result, string operation)
        {
            if (result < 0) throw new InvalidOperationException($"ICE {operation} failed ({result}).");
        }
        static void ValidateText(string value, int maximum, string name)
        {
            if (value == null) return;
            if (value.Length > maximum) throw new ArgumentException("ICE text exceeds its limit.", name);
            foreach (char c in value) if (c == '\0' || c > 127) throw new ArgumentException("ICE text must be ASCII without NUL.", name);
        }
        static NetworkEndpoint MakeEndpoint(int peer)
        {
            var bytes = new NativeArray<byte>(8, Allocator.Temp);
            try
            {
                bytes[0] = (byte)'M'; bytes[1] = (byte)'I'; bytes[2] = (byte)'C'; bytes[3] = 1;
                for (int i = 0; i < 4; i++) bytes[4 + i] = (byte)(peer >> (i * 8));
                var endpoint = default(NetworkEndpoint);
                endpoint.SetRawAddressBytes(bytes, NetworkFamily.Custom);
                return endpoint;
            }
            finally { bytes.Dispose(); }
        }

        sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public NativeHandle(IntPtr value) : base(true) { SetHandle(value); }
            protected override bool ReleaseHandle() { Native.mice_destroy(handle); return true; }
        }
        static class Native
        {
            const string Library = "moon_ice";
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_abi_version();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr mice_create(int capacity);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void mice_destroy(IntPtr context);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int mice_add_peer(NativeHandle context, string bind, string stun, ushort port);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_remove_peer(NativeHandle context, int peer);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_gather(NativeHandle context, int peer);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_gathered(NativeHandle context, int peer);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_state(NativeHandle context, int peer);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int mice_description(NativeHandle context, int peer, StringBuilder buffer, int capacity);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int mice_remote_description(NativeHandle context, int peer, string description);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_send(NativeHandle context, int peer, IntPtr buffer, int length);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_receive(NativeHandle context, out int peer, IntPtr buffer, int capacity);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int mice_get_stats(NativeHandle context, out EmbeddedIceStatistics stats);
        }
    }
}
