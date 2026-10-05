# 霰弹枪光环来源

用户制作的 GameMaker 项目：`E:\Code\Halos\Game Comp Test Space`，2026-10-05 导入。

| Unity 文件 | 原始 Sprite 帧 |
|---|---|
| ShotShell0.png（4 发） | a9b218eb-cfea-413b-8213-bff6efc82515 |
| ShotShell1.png（3 发） | 18ce062b-1b74-46a0-8943-7881b466a17f |
| ShotShell2.png（2 发） | 9abfaf98-0556-4eaa-8fb1-8a578fc3b98c |
| ShotShell3.png（1 发） | 50ef505e-2a84-4a83-8c36-3a91c7ec60cd |
| ShotShell4.png（0 发） | 8892ac16-ace7-4b7b-b258-be2a298ae1bf |
| ShotDot.png | 33dd3842-3239-4b90-891e-3898679900d0 |

所有 PNG 原样复制，无裁切或重绘。动效由 `ShotControl/Step_0.gml` 与 `Shotgun/Step_0.gml` 转换为 `ShotgunHaloVisual`。原代码的固定帧率插值换为按时间插值，输入、弹量、装填及伤害接入现有服务器系统。
