// ECS 中用来缓存网络状态文字的单一组件。NetworkStatusSystem 写入，SessionInfo 读取后展示。
using Unity.Collections;
using Unity.Entities;

public struct NetworkStatusSingleton : IComponentData {
    // 字段是固定容量的文本，能直接存进 ECS 组件。系统先格式化一次，分帧显示的控件只需读取它。
    public FixedString512Bytes Status;
}