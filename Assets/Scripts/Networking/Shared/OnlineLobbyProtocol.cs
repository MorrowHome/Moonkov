// 账号后端、游戏客户端和独立服共用的大堂、申请、实例票与 ICE 信令字段。ConnectionPath 由后端分配，客户端据此选网络传输。
#nullable disable
using System;

namespace Unity.MP_FPS.Online {
    public static class OnlineNetworkProtocol {
        // 账号后台与独立服要先确认应用协议号，不支持的连接方式不能偷偷走成另一种线路。
        public const int Version = 3;
        public const string P2P = "p2p", Vps = "vps";
        public static bool ValidPath(string path) { return path == P2P || path == Vps; }
    }

    // 各端共用的消息格式。队伍和实例的成员资格由账号服务分配。
    [Serializable]
    public sealed class LobbyMember {
        // 大厅成员状态供准备、席位与重连判断。实际身份由后端认证的 PlayerId 识别，昵称用于显示。
        public string PlayerId, Name;
        public bool Ready, Returned;
        public bool Connected = true;
        public int ReconnectSeconds;
        public int Team;
        public LobbyAppearance Appearance = new();
    }

    [Serializable]
    public sealed class LobbyAppearance {
        public int CharacterIndex = 2, PoseIndex = -1;
        public string Weapon = "", Headgear = "", ChestBadge = "", Backpack = "", Melee = "";
        // 公开外观只带穿戴代码和模块组合，不带背包内容、真实物品 ID 或查到的电量。
        public ushort Modules;
        public bool Shield;

        public bool SameAs(LobbyAppearance other) {
            return other != null && CharacterIndex == other.CharacterIndex && PoseIndex == other.PoseIndex &&
                   Weapon == other.Weapon && Headgear == other.Headgear && ChestBadge == other.ChestBadge &&
                   Backpack == other.Backpack && Melee == other.Melee && Modules == other.Modules &&
                   Shield == other.Shield;
        }

        public LobbyAppearance Copy() { return (LobbyAppearance)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class LobbySnapshot {
        public string Code, LeaderId, SelfId, Mode, Phase, InstanceId, Address, ServerName, Message;
        // 已分配实例同时带公网入口和 ICE 所需信息。ConnectionPath 决定客户端选哪条，ServerName 用于独立的证书验证。
        public string ConnectionPath, IceHostId, IceHostAddress, IceStunHost;
        public int NetworkProtocol, IceStunPort;
        public bool IceAvailable;
        // 失败进场后一人能否重试或离队由后端快照告知，界面不根据按钮显示状态猜席位。
        public bool CanRetry, CanLeave;
        public int Port;
        public bool PublicQueue;
        public LobbyMember[] Members = Array.Empty<LobbyMember>();
        public bool RoomDirectoryAvailable, CanRequestJoin;
        public LobbyRoomInfo[] Rooms;
        public LobbyJoinRequest JoinRequest;
        public LobbyJoinRequest[] JoinRequests = Array.Empty<LobbyJoinRequest>();
    }

    [Serializable]
    public sealed class LobbyRoomInfo {
        // 公开目录使用独立房间 ID。它能用于申请，但好友直接加入用的队伍码不会出现在这一行。
        public string RoomId, LeaderName, Mode, ConnectionPath, Status;
        public int MemberCount, Capacity;

        public bool SameAs(LobbyRoomInfo other) {
            return RoomId == other.RoomId && LeaderName == other.LeaderName && Mode == other.Mode &&
                   ConnectionPath == other.ConnectionPath && Status == other.Status &&
                   MemberCount == other.MemberCount && Capacity == other.Capacity;
        }
    }

    [Serializable]
    public sealed class LobbyJoinRequest {
        public string RequestId, RoomId, LeaderName, ApplicantName, Status;
    }

    [Serializable]
    public sealed class LobbyAction {
        // 同一类消息覆盖排队、选择队伍和审核加入。后端按 Action 校验参与者权限和所需字段。
        public string Action, Code, Mode, TargetPlayerId;
        public string RoomId, RequestId;
        public string ConnectionPath;
        public bool Ready, PublicQueue;
        public int Team;
    }

    [Serializable]
    public sealed class InstanceCredential {
        // 战局票与真实实例编号配对。收到后还需独立服消费核对，不能用在另一场。
        public string Token, InstanceId;
        public int Protocol;
    }

    [Serializable]
    public sealed class InstanceEvent {
        public string InstanceId, Kind;
        public int Protocol;
        public bool IceAvailable;
        public string IceHostId, IceHostAddress;
    }

    [Serializable]
    public sealed class IceClientMessage {
        // 信令中的尝试号用于归档单次连接。客户端可发本机候选、轮询服务器候选或取消这次尝试。
        public string InstanceId, AttemptId, Action, Description;
        public int Protocol;
        public bool PublicOnly;
    }

    [Serializable]
    public sealed class IceReply {
        public string Status, Description, Error;
    }

    [Serializable]
    public sealed class IceOffer {
        public string PlayerId, AttemptId, Description;
        public bool Cancelled, PublicOnly;
    }

    [Serializable]
    public sealed class IceAnswer {
        public string PlayerId, AttemptId, Description, Error;
    }

    [Serializable]
    public sealed class IceServerPoll {
        public string InstanceId;
        // 独立服向后端一批次送应答，再接收一批候选。每份都保留之前发来的玩家号和尝试号。
        public IceAnswer[] Answers = Array.Empty<IceAnswer>();
    }

    [Serializable]
    public sealed class IceServerInbox {
        public IceOffer[] Offers = Array.Empty<IceOffer>();
    }
}