using Unity.Collections;
using Unity.NetCode;

namespace Unity.MP_FPS
{

    /// <summary>
    /// Client to server to request joinning current session. 
    /// </summary>
    // V2 adds the expedition companion count and invalidates the old Burst serializer.
    public struct ClientJoinRequestV2Rpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
        public FixedString128Bytes LoginToken;
        public int CharacterIndex;
        public int CarryCells;
        public RaidGameMode Mode;
        public int RulesVersion;
        public bool ResumeOnly;
        public byte ExpeditionCompanions;
    }

    /// <summary>
    /// Client request a death + respawn from the server. For testing purposes.
    /// </summary>
    public struct ClientRequestRespawnRpc : IRpcCommand
    {
    }
}
