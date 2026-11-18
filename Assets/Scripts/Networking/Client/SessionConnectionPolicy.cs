// 连接阶段共用的等待规则。GameManager 用服务端 Ghost 总数和已同步数量算进度，连接系统把 NetCode 断线原因换成界面容易理解的提示。
using Unity.NetCode;

namespace Unity.MP_FPS {
    public static class SessionConnectionPolicy {
        public const double TimeoutSeconds = 30;
        public const double EmptyWorldGraceSeconds = 1;

        // 有目标数量时用已生成的 Ghost 比例显示进度。服务端没有上报任何 Ghost 时给它一秒时间，再允许真正的空场景走完加载。
        public static float ReplicationProgress(int serverCount, int instantiatedCount, double elapsedSeconds) {
            return serverCount > 0 ? Mathematics.math.saturate(instantiatedCount / (float)serverCount) :
                elapsedSeconds >= EmptyWorldGraceSeconds ? 1f : 0f;
        }

        // 连接错误按能采取的操作归类。握手超时可以核对地址，协议不匹配要对齐版本，认证被拒则需重新登录。
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