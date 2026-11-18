// 连接阶段共用的等待规则。GameManager 用服务端 Ghost 总数和已同步数量算进度，连接系统把 NetCode 断线原因换成界面容易理解的提示。
using Unity.NetCode;

namespace Unity.MP_FPS {
    public static class SessionConnectionPolicy {
        public const double TimeoutSeconds = 30;
        public const double EmptyWorldGraceSeconds = 1;

        public static float ReplicationProgress(int serverCount, int instantiatedCount, double elapsedSeconds) {
            return serverCount > 0 ? Mathematics.math.saturate(instantiatedCount / (float)serverCount) :
                elapsedSeconds >= EmptyWorldGraceSeconds ? 1f : 0f;
        }

        public static string DisconnectMessage(NetworkStreamDisconnectReason reason) {
            return reason switch {
                NetworkStreamDisconnectReason.Timeout or NetworkStreamDisconnectReason.MaxConnectionAttempts
                    or NetworkStreamDisconnectReason.HandshakeTimeout
                    or NetworkStreamDisconnectReason.ApprovalTimeout =>
                    "Server connection timed out. Check the address and try again.",
                NetworkStreamDisconnectReason.BadProtocolVersion or NetworkStreamDisconnectReason.InvalidRpc
                    or NetworkStreamDisconnectReason.ProtocolError => "Client and server versions do not match.",
                NetworkStreamDisconnectReason.AuthenticationFailure or NetworkStreamDisconnectReason.ApprovalFailure =>
                    "Server rejected this session. Sign in again before reconnecting.",
                _ => "Connection lost. Return to the ship and reconnect."
            };
        }
    }
}