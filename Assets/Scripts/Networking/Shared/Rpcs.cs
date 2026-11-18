// 战局加入的 RPC 字段定义。ClientGameSystem 写入角色选择、入场票与玩法参数，ServerGameSystem 从实际发送这条消息的连接开始验证。
using Unity.Collections;
using Unity.NetCode;

namespace Unity.MP_FPS {
    /// <summary>
    /// 客户端请求加入当前会话。
    /// </summary>
    // V2 加入远征同伴人数，也通过新类型让旧的 Burst 序列化器失效。
    public struct ClientJoinRequestV2Rpc : IRpcCommand {
        public FixedString64Bytes PlayerName;
        // 这里的 Token 字段装本次玩法需要的进场凭证。账号在线走游戏票，好友房走房主票，单人使用占位。
        public FixedString128Bytes LoginToken;
        public int CharacterIndex;
        public int CarryCells;
        public RaidGameMode Mode;
        // 服务端先看模式与规则版本能否配对。ResumeOnly 告诉它只能接回原角色，不能因此划一份新装备。
        public int RulesVersion;
        public bool ResumeOnly;
        // 单人和好友远征可选择同伴人数。服务端再确认玩法和上限，拒绝在不支持的模式下生成。
        public byte ExpeditionCompanions;
    }

    /// <summary>
    /// 测试用，客户端请服务器让角色死亡后重生。
    /// </summary>
    public struct ClientRequestRespawnRpc : IRpcCommand {
    }
}