// 代码里使用的 Unity 物理与渲染层编号，对应项目的层设置。要拼射线或摄像机的层掩码时，对编号左移一位。
public enum LayerIndex {
    Default = 0,
    TransparentFX = 1,
    IgnoreRaycast = 2,
    // 同一进程的客户端和服务端可以各有自己的角色碰撞体。命中射线用层掩码选择要处理的这一边。
    ServerPlayer = 3,
    Water = 4,
    UI = 5,
    ClientPlayer = 6,
    Ground = 7,
    // 第一人称的近景模型可以放在叠加层上，从普通世界相机的绘制中分出来。
    FirstPersonOverlay = 8
}