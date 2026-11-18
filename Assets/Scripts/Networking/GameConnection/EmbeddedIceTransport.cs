// 原生 ICE 库的 C# 包装。对内用 peer 编号识别对端，对 Unity Transport 提供对应的逻辑端点，并负责缓冲区、收发和原生句柄寿命。
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.MP_FPS.EmbeddedNetworking {
    public enum EmbeddedIceState {
        Disconnected,
        Gathering,
        Connecting,
        Connected,
        Completed,
        Failed
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct EmbeddedIceStatistics {
        public ulong ReceivedPackets, ReceivedBytes, SentPackets, SentBytes;
        public ulong DroppedPackets, SendErrors, QueuedPackets;
    }

    /// <summary>
    /// Windows x64 与 macOS 按战局选择 ICE 数据报。无需另装服务或网卡适配器。
    /// 一个上下文同时只让一个驱动接入。好友房在回合之间保存
    /// 战局上下文，在线会话则把归属转给驱动。
    /// 调用 AddPeer 或 SetRemoteDescription 前，信令层先认证对端。
    /// ICE 负责寻路。此接口的上层要始终使用已有的 DTLS 加密。
    /// </summary>
    public sealed class EmbeddedIceTransport : IDisposable {
        public const int MaxDatagram = 1472;
        public const int MaxPeers = 64;
        private const int MaxPacketsPerUpdate = 256;
        private readonly object m_Gate = new();
        private readonly NativeHandle m_Handle;
        private readonly Dictionary<int, NetworkEndpoint> m_Endpoints = new(MaxPeers);
        private readonly Dictionary<NetworkEndpoint, int> m_Peers = new(MaxPeers);
        private readonly byte[] m_ReceiveBuffer = new byte[MaxDatagram];
        private bool m_Attached, m_CloseAfterDriver;
        public NetworkEndpoint LocalEndpoint { get; } = MakeEndpoint(0);
        public ulong AdapterDrops { get; private set; }

        public bool IsDisposed {
            get {
                lock (m_Gate) { return m_Handle == null || m_Handle.IsClosed; }
            }
        }

        public EmbeddedIceTransport(int receiveCapacity = 256) {
            // 编辑器选择的构建目标不能用来判断实际运行系统。即使同一系统，
            // 专用服务器也有自己的 RuntimePlatform 值。
            // 看实际运行平台，运行在 macOS 的编辑器即使选了 Windows 构建目标也要找 macOS 插件。独立服还有自己的平台枚举。
            var platform = Application.platform;
            if (platform != RuntimePlatform.WindowsEditor && platform != RuntimePlatform.WindowsPlayer &&
                platform != RuntimePlatform.WindowsServer && platform != RuntimePlatform.OSXEditor &&
                platform != RuntimePlatform.OSXPlayer && platform != RuntimePlatform.OSXServer)
                throw new PlatformNotSupportedException("内置 P2P 当前支持 Windows x64 和 macOS。");
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Embedded ICE requires a 64-bit process.");
            if (receiveCapacity < 8 || receiveCapacity > 4096)
                throw new ArgumentOutOfRangeException(nameof(receiveCapacity));
            int abi;
            // 创建句柄前核对原生库的接口版本。机器缺库或带了旧库时尽早报错，不让后面的调用用错内存布局。
            try { abi = Native.mice_abi_version(); } catch (DllNotFoundException ex) {
                throw new InvalidOperationException("本地 P2P 组件缺失或无法加载，请完整更新游戏后重试。", ex);
            }
            if (abi != 1) throw new InvalidOperationException("Embedded ICE native ABI mismatch.");
            m_Handle = new NativeHandle(Native.mice_create(receiveCapacity));
            if (m_Handle.IsInvalid) {
                m_Handle.Dispose();
                throw new InvalidOperationException("Could not allocate ICE context.");
            }
        }

        public int AddPeer(string bindAddress = null, string stunHost = null, ushort stunPort = 3478) {
            // 显式绑定回环只用于编辑器隔离检查。
            // 正式游戏传实际选中的网卡和已经认证的对端。
            ValidateText(bindAddress, 255, nameof(bindAddress));
            ValidateText(stunHost, 255, nameof(stunHost));
            // 由信令层进行可取消的 DNS 解析。
            // 销毁 libjuice 时会等待原生层尚未返回的 DNS 查询。
            if (!string.IsNullOrEmpty(bindAddress) && !IPAddress.TryParse(bindAddress, out _))
                throw new ArgumentException("ICE bind address must be an IP literal.", nameof(bindAddress));
            // 地址解析放在外层的可超时流程。原生连接使用纯 IP，关闭句柄时不会卡在一个尚未返回的 DNS。
            if (!string.IsNullOrEmpty(stunHost) && !IPAddress.TryParse(stunHost, out _))
                throw new ArgumentException("Resolve the STUN hostname before creating an ICE peer.", nameof(stunHost));
            lock (m_Gate) {
                ThrowIfDisposed();
                // 原生层返回 peer ID，C# 为它生成 UTP 的逻辑端点并双向登记。收包用 ID 找端点，发包时反过来找。
                int id = Native.mice_add_peer(m_Handle, bindAddress, stunHost, stunPort);
                Check(id, "add peer");
                var endpoint = MakeEndpoint(id);
                m_Endpoints.Add(id, endpoint);
                m_Peers.Add(endpoint, id);
                return id;
            }
        }

        public void RemovePeer(int peer) {
            lock (m_Gate) {
                ThrowIfDisposed();
                var endpoint = GetEndpoint(peer);
                // 只在原生成功移除后清掉索引。之后再有该端点的包就不会转发给另一个 peer。
                Check(Native.mice_remove_peer(m_Handle, peer), "remove peer");
                m_Endpoints.Remove(peer);
                m_Peers.Remove(endpoint);
            }
        }

        public NetworkEndpoint GetEndpoint(int peer) {
            lock (m_Gate) {
                ThrowIfDisposed();
                return m_Endpoints[peer];
            }
        }

        internal bool HasPeerEndpoint(NetworkEndpoint endpoint) {
            lock (m_Gate) {
                ThrowIfDisposed();
                return m_Peers.ContainsKey(endpoint);
            }
        }

        public void Gather(int peer) {
            lock (m_Gate) {
                ThrowIfDisposed();
                Check(Native.mice_gather(m_Handle, peer), "gather");
            }
        }

        public bool IsGatheringComplete(int peer) {
            lock (m_Gate) {
                ThrowIfDisposed();
                int value = Native.mice_gathered(m_Handle, peer);
                Check(value, "gathering status");
                return value == 1;
            }
        }

        public EmbeddedIceState GetState(int peer) {
            lock (m_Gate) {
                ThrowIfDisposed();
                int value = Native.mice_state(m_Handle, peer);
                Check(value, "state");
                return (EmbeddedIceState)value;
            }
        }

        public string GetLocalDescription(int peer) {
            lock (m_Gate) {
                ThrowIfDisposed();
                // 一次交换完整 SDP。里面有会话凭据，不能写入日志。
                // 候选没有收齐前不要交换部分信息。这份连接描述还包含本次连接用的凭据，调用者只通过信令送给允许的对端。
                if (!IsGatheringComplete(peer))
                    throw new InvalidOperationException("Wait for ICE candidate gathering to finish.");
                var description = new StringBuilder(4096);
                Check(Native.mice_description(m_Handle, peer, description, description.Capacity), "local description");
                return description.ToString();
            }
        }

        public void SetRemoteDescription(int peer, string description) {
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("ICE description is empty.", nameof(description));
            // 对端的描述先按长度和格式限制，再交给原生解析。实际连通性由后续的 ICE 状态推进。
            ValidateText(description, 4095, nameof(description));
            lock (m_Gate) {
                ThrowIfDisposed();
                Check(Native.mice_remote_description(m_Handle, peer, description), "remote description");
            }
        }

        public EmbeddedIceStatistics GetStatistics() {
            lock (m_Gate) {
                ThrowIfDisposed();
                Check(Native.mice_get_stats(m_Handle, out var result), "statistics");
                return result;
            }
        }

        // Windows 的包装必须用托管 struct，相关 UTP 问题编号为 1412477。
        // 归属交给一个驱动，同一个上下文包装两次会让 UTP 的存储被释放两遍。
        public NetworkInterfaceUnmanagedWrapper<EmbeddedIceNetworkInterface> CreateNetworkInterface() {
            return AttachDriver(true);
        }

        // 好友房的战局连接只借用这层。回到房间时卸下本局 driver，房间仍能保留原来的 ICE 通道供下次开局。
        internal NetworkInterfaceUnmanagedWrapper<EmbeddedIceNetworkInterface> BorrowNetworkInterface() {
            return AttachDriver(false);
        }

        // 一个 ICE 上下文同时只挂一个 Unity Transport driver。这样谁该释放包装和原生句柄就一直有明确归属。
        private NetworkInterfaceUnmanagedWrapper<EmbeddedIceNetworkInterface> AttachDriver(bool ownsTransport) {
            lock (m_Gate) {
                ThrowIfDisposed();
                if (m_CloseAfterDriver) throw new ObjectDisposedException(nameof(EmbeddedIceTransport));
                if (m_Attached) throw new InvalidOperationException("An ICE context can belong to only one driver.");
                var wrapper = new EmbeddedIceNetworkInterface(this, ownsTransport).WrapToUnmanaged();
                // 驱动包装持有这份传输后记录所有权。不能用第二个包装同时读写同一份 UTP 暂存状态。
                m_Attached = true;
                return wrapper;
            }
        }

        internal void ReleaseDriver(bool ownsTransport) {
            lock (m_Gate) {
                m_Attached = false;
                // 官方战局驱动拥有其连接，退出直接销毁。好友房驱动只是借来开一局，除非关房已经请求等待释放。
                if (ownsTransport || m_CloseAfterDriver) Dispose();
            }
        }

        internal void CloseAfterDriver() {
            lock (m_Gate) {
                // 关房时战局可能还在销毁。先留个待关标记，由 driver 放下包装之后再关原生句柄。
                if (m_Attached)
                    m_CloseAfterDriver = true;
                else
                    Dispose();
            }
        }

        internal unsafe void Receive(ref ReceiveJobArguments arguments) {
            lock (m_Gate) {
                ThrowIfDisposed();
                fixed (byte* buffer = m_ReceiveBuffer) {
                    // 每次更新最多搬一批且受接收队列容量限制。未知 peer 或装不下的包计入丢弃，不能继续挤队列。
                    for (int i = 0;
                         i < MaxPacketsPerUpdate && arguments.ReceiveQueue.Count < arguments.ReceiveQueue.Capacity;
                         i++) {
                        int size = Native.mice_receive(m_Handle, out int peer, (IntPtr)buffer, MaxDatagram);
                        if (size == 0) break;
                        Check(size, "receive");
                        if (!m_Endpoints.TryGetValue(peer, out var endpoint)) {
                            AdapterDrops++;
                            continue;
                        }
                        if (!arguments.ReceiveQueue.EnqueuePacket(out var packet)) {
                            AdapterDrops++;
                            break;
                        }
                        if (size > packet.BytesAvailableAtEnd) {
                            packet.Drop();
                            AdapterDrops++;
                            continue;
                        }
                        // 上层连接只看到抽象端点。把该 peer 的数据和端点放在同一包里，UTP 才能交给正确连接。
                        packet.EndpointRef = endpoint;
                        packet.AppendToPayload(buffer, size);
                    }
                }
            }
        }

        internal unsafe void Send(ref SendJobArguments arguments) {
            lock (m_Gate) {
                ThrowIfDisposed();
                for (int i = 0; i < arguments.SendQueue.Count; i++) {
                    var packet = arguments.SendQueue[i];
                    if (packet.Length == 0) continue;
                    // 从端点反查原生 peer，再将包的有效负载送出。发失败交给 UDP 上层判断是否需要重传，这里统一取下已处理的包。
                    if (m_Peers.TryGetValue(packet.EndpointRef, out int peer)) {
                        int result = Native.mice_send(m_Handle, peer,
                            (IntPtr)((byte*)packet.GetUnsafePayloadPtr() + packet.Offset), packet.Length);
                        // UDP 发送失败按丢包处理。这里不会暗中重试或积累无上限的发送队列。
                        if (result < 0) AdapterDrops++;
                    } else { AdapterDrops++; }
                    packet.Drop();
                }
            }
        }

        public void Dispose() {
            lock (m_Gate) {
                // 在互斥锁内关闭原生句柄并清两张映射。收发与增删对端共用这把锁，避免访问已关闭的句柄。
                m_Handle?.Dispose();
                m_Endpoints.Clear();
                m_Peers.Clear();
            }
        }

        private void ThrowIfDisposed() {
            if (m_Handle == null || m_Handle.IsClosed) throw new ObjectDisposedException(nameof(EmbeddedIceTransport));
        }

        private static void Check(int result, string operation) {
            if (result < 0) throw new InvalidOperationException($"ICE {operation} failed ({result}).");
        }

        private static void ValidateText(string value, int maximum, string name) {
            if (value == null) return;
            if (value.Length > maximum) throw new ArgumentException("ICE text exceeds its limit.", name);
            foreach (char c in value)
                if (c == '\0' || c > 127)
                    throw new ArgumentException("ICE text must be ASCII without NUL.", name);
        }

        private static NetworkEndpoint MakeEndpoint(int peer) {
            var bytes = new NativeArray<byte>(8, Allocator.Temp);
            try {
                bytes[0] = (byte)'M';
                bytes[1] = (byte)'I';
                bytes[2] = (byte)'C';
                bytes[3] = 1;
                for (int i = 0; i < 4; i++) bytes[4 + i] = (byte)(peer >> (i * 8));
                var endpoint = default(NetworkEndpoint);
                endpoint.SetRawAddressBytes(bytes, NetworkFamily.Custom);
                return endpoint;
            } finally { bytes.Dispose(); }
        }

        private sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid {
            public NativeHandle(IntPtr value) : base(true) { SetHandle(value); }

            protected override bool ReleaseHandle() {
                Native.mice_destroy(handle);
                return true;
            }
        }

        private static class Native {
            private const string Library = "moon_ice";

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_abi_version();

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr mice_create(int capacity);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void mice_destroy(IntPtr context);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern int mice_add_peer(NativeHandle context, string bind, string stun, ushort port);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_remove_peer(NativeHandle context, int peer);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_gather(NativeHandle context, int peer);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_gathered(NativeHandle context, int peer);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_state(NativeHandle context, int peer);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern int mice_description(
                NativeHandle context,
                int peer,
                StringBuilder buffer,
                int capacity);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern int mice_remote_description(NativeHandle context, int peer, string description);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_send(NativeHandle context, int peer, IntPtr buffer, int length);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_receive(NativeHandle context, out int peer, IntPtr buffer, int capacity);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int mice_get_stats(NativeHandle context, out EmbeddedIceStatistics stats);
        }
    }
}