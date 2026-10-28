# 第一人称身体与镜头

2026-10-28。保留中央准星和动画带动的运动感。此次不调整 FOV、倍率、后坐力、移动速度或弹道。

## 低头构图

- 本人第一人称网格将颈部收平，封住领口。使用绑定姿态定位切面。原网格和完整阴影不变。
- 收口顶点跟随胸骨。相关脸部形变不再拉开封口。保留所有形变名称和索引。
- 低头时保留自然向前的眼位。只在眼位退入胸部时补足前向间隙，不再把眼位往脖子上方拉回。
- 安全求解只检查眼位和近裁面附近的可见上身。包含肩部和当前可见的手臂。已切到独立手势相机的手臂不再顶高世界镜头。
- 三个角色的默认待机采样中，60°和 80°低头都不再需要原先的额外抬升。低头可以看到自然衔接的胸部和衣服。

`DollSingerFirstPersonBody` 在绑定角色时处理本人副本。`DollSingerTorsoClearance` 缓存表面和权重。逐帧不创建自碰撞体，不 BakeMesh，不重建网格。没有新增运行时相机或包。

## 验证

Unity 编译通过。三个角色通过 1,962 组动作与投影姿态、17,658 条真实近裁面射线检查。低头射线命中胸部 25 次。原网格和形变索引保留检查通过。中央投影与开火手部投影回归通过。

入口为 `MoonkovFirstPersonCameraChecks.RunReport()`、`MoonkovViewInteractionChecks.CheckCamera()` 和 `WeaponFramingChecks.Run()`。

MMD 导入模型可能带 100 倍 Renderer 缩放。独立几何参照使用 `BakeMesh(mesh, true)`。默认烘焙再乘 Renderer Transform 会得到错误的 100 倍查询几何。

本地图片和验收输出在 `Library/BuildValidation/CameraFix20261028/`，不入 Git。没有进行正式战局试玩、联机验收或平台构建。
