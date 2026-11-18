// ECS 中用来缓存网络状态文字的单一组件。NetworkStatusSystem 写入，SessionInfo 读取后展示。
using Unity.Collections;
using Unity.Entities;

public struct NetworkStatusSingleton : IComponentData
{
    public FixedString512Bytes Status;
}
