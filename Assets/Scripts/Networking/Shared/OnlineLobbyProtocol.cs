#nullable disable
using System;

namespace Unity.MP_FPS.Online
{
    public static class OnlineNetworkProtocol
    {
        public const int Version = 2;
        public const string P2P = "p2p", Vps = "vps";
        public static bool ValidPath(string path) => path == P2P || path == Vps;
    }
    // Shared wire contract. Team/instance membership is assigned by the account service.
    [Serializable] public sealed class LobbyMember
    {
        public string PlayerId, Name;
        public bool Ready, Returned;
        public bool Connected = true;
        public int ReconnectSeconds;
        public int Team;
        public LobbyAppearance Appearance = new LobbyAppearance();
    }
    [Serializable] public sealed class LobbyAppearance
    {
        public int CharacterIndex = 2, PoseIndex = -1;
        public string Weapon = "", Headgear = "", ChestBadge = "", Backpack = "", Melee = "";
        public ushort Modules;
        public bool Shield;
        public bool SameAs(LobbyAppearance other) => other != null && CharacterIndex == other.CharacterIndex &&
            PoseIndex == other.PoseIndex && Weapon == other.Weapon && Headgear == other.Headgear &&
            ChestBadge == other.ChestBadge && Backpack == other.Backpack && Melee == other.Melee && Modules == other.Modules && Shield == other.Shield;
        public LobbyAppearance Copy() => (LobbyAppearance)MemberwiseClone();
    }
    [Serializable] public sealed class LobbySnapshot
    {
        public string Code, LeaderId, SelfId, Mode, Phase, InstanceId, Address, ServerName, Message;
        public string ConnectionPath, IceHostId, IceHostAddress, IceStunHost;
        public int NetworkProtocol, IceStunPort;
        public bool IceAvailable;
        public bool CanRetry, CanLeave;
        public int Port;
        public bool PublicQueue;
        public LobbyMember[] Members = Array.Empty<LobbyMember>();
        public bool RoomDirectoryAvailable, CanRequestJoin;
        public LobbyRoomInfo[] Rooms;
        public LobbyJoinRequest JoinRequest;
        public LobbyJoinRequest[] JoinRequests = Array.Empty<LobbyJoinRequest>();
    }
    [Serializable] public sealed class LobbyRoomInfo
    {
        public string RoomId, LeaderName, Mode, ConnectionPath, Status;
        public int MemberCount, Capacity;
        public bool SameAs(LobbyRoomInfo other) => RoomId == other.RoomId && LeaderName == other.LeaderName &&
            Mode == other.Mode && ConnectionPath == other.ConnectionPath && Status == other.Status &&
            MemberCount == other.MemberCount && Capacity == other.Capacity;
    }
    [Serializable] public sealed class LobbyJoinRequest
    {
        public string RequestId, RoomId, LeaderName, ApplicantName, Status;
    }
    [Serializable] public sealed class LobbyAction
    {
        public string Action, Code, Mode, TargetPlayerId;
        public string RoomId, RequestId;
        public string ConnectionPath;
        public bool Ready, PublicQueue;
        public int Team;
    }
    [Serializable] public sealed class InstanceCredential
    {
        public string Token, InstanceId;
        public int Protocol;
    }
    [Serializable] public sealed class InstanceEvent
    {
        public string InstanceId, Kind;
        public int Protocol;
        public bool IceAvailable;
        public string IceHostId, IceHostAddress;
    }
    [Serializable] public sealed class IceClientMessage
    {
        public string InstanceId, AttemptId, Action, Description;
        public int Protocol;
        public bool PublicOnly;
    }
    [Serializable] public sealed class IceReply
    {
        public string Status, Description, Error;
    }
    [Serializable] public sealed class IceOffer
    {
        public string PlayerId, AttemptId, Description;
        public bool Cancelled, PublicOnly;
    }
    [Serializable] public sealed class IceAnswer
    {
        public string PlayerId, AttemptId, Description, Error;
    }
    [Serializable] public sealed class IceServerPoll
    {
        public string InstanceId;
        public IceAnswer[] Answers = Array.Empty<IceAnswer>();
    }
    [Serializable] public sealed class IceServerInbox
    {
        public IceOffer[] Offers = Array.Empty<IceOffer>();
    }
}
