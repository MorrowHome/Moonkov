#nullable disable
using System;

namespace Unity.MP_FPS.Online
{
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
        public int Port;
        public bool PublicQueue;
        public LobbyMember[] Members = Array.Empty<LobbyMember>();
    }
    [Serializable] public sealed class LobbyAction
    {
        public string Action, Code, Mode, TargetPlayerId;
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
