// 单人模式的服务端 World 标记。ServerGameSystem 用它选择本机档案作为结算入口，防止服务端根据前台界面当前的账号状态来猜。
using Unity.Entities;

namespace Unity.MP_FPS {
    public struct OfflineRaidMode : IComponentData {
    }
}