// 代码里使用的 Unity 物理与渲染层编号，对应项目的层设置。要拼射线或摄像机的层掩码时，对编号左移一位。
public enum LayerIndex
{
    Default = 0,
    TransparentFX = 1,
    IgnoreRaycast = 2,
    ServerPlayer = 3,
    Water = 4,
    UI = 5,
    ClientPlayer = 6,
    Ground = 7,
    FirstPersonOverlay = 8,
}
