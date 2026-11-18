// 战局加入的 RPC 字段定义。ClientGameSystem 写入角色选择、入场票与玩法参数，ServerGameSystem 从实际发送这条消息的连接开始验证。
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
