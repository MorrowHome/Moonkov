#nullable disable
using System;

namespace Unity.MP_FPS.Online
{
    // Shared wire contract. Team/instance membership is assigned by the account service.
    [Serializable] public sealed class LobbyMember
    {
        public string PlayerId, Name;
        public bool Ready, Returned;
        public int Team;
    }
    [Serializable] public sealed class LobbySnapshot
    {
        public string Code, LeaderId, SelfId, Mode, Phase, InstanceId, Address, ServerName, Message;
        public int Port;
        public bool PublicQueue;
        public LobbyMember[] Members = Array.Empty<LobbyMember>();
    }
    [Serializable] public sealed class LobbyAction
    {
        public string Action, Code, Mode;
        public bool Ready, PublicQueue;
        public int Team;
    }
    [Serializable] public sealed class InstanceCredential
    {
        public string Token, InstanceId;
    }
    [Serializable] public sealed class InstanceEvent
    {
        public string InstanceId, Kind;
        public int Protocol;
    }
}
