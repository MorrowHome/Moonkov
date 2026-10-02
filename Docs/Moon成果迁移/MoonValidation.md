# 月球迁移验证记录

日期：2026-10-02；Unity 6000.5.10f1；当前 Windows Editor／URP。

遵循最小充分原则，仅检查迁移带来的编译、材质引用、静态资源、演示状态与恢复风险。

| 检查 | 结果 |
| --- | --- |
| Runtime／Demo／Editor 程序集 | Unity 编译完成，failed=false |
| 月壤、两份岩石材质绑定 | 正确指向 MoonEnvironment/Regolith 与 MoonEnvironment/LunarRock；所有使用的贴图均存在 |
| Shader Pass | 三个材质的 Forward／ShadowCaster／DepthOnly／DepthNormals 编译后无 Shader 消息 |
| 实际 Game 视图 | 已在 Play Mode 看见灰色月壤、岩石与陨坑，紫色错误材质已消失 |
| 环境引用 | 2 个 Terrain、6000 个 MoonRockGrounding；Terrain 为 TwoSided |
| DEM 对比 | 关闭近场后远景中心孔洞填回，重新启用细节成功 |
| 反射对比／太阳切换 | PBR／月球反射、0.5°太阳角度开关成功，已恢复 25°与详细模式 |
| 玩家 | 已接地、相机引用存在、独立 Gravity=-1.62 |
| 运行时资产安全 | 运行时远景 TerrainData 为非持久副本；退出后原始 TerrainData／材质引用恢复 |
| Pipeline 恢复 | Play 中为 MoonPipeline，退出后恢复 PC_RPAsset |
| 资源溯源 | 两份 TerrainData 与源项目 SHA-256 一致；高度数据符合 dem.json 中的 SHA-256 |
| 元数据和引用 | 月球文件 .meta 完整，无重复 GUID；月球预制体／材质／场景引用解析完整，包括现有包中的 PlayerInput／URP 相机和灯光组件 |
| 修改范围 | 仅新增月球模块和文档；模板原有场景、ProjectSettings、Packages 无文件差异 |

迁移过程中发现并修复：材质引用旧 Shader GUID 导致 InternalErrorShader，以及岩石着色器残留 Built-in 实例化宏。编译成功必须同时核对实际材质绑定，不能仅检查新 Shader 文件。

后续修复了编辑模式全黑：PC_Renderer 使用 Forward+，月球 Shader 最初缺少 `_CLUSTER_LIGHT_LOOP` 变体，错误进入普通 Forward 的主光衰减分支。两套 Shader 已补齐 clustered 光照变体和附加方向光循环，并使用 URP 光照循环要求的 `inputData` 名称。在非 Play、PC_RPAsset 仍活动的情况下，实际镜头图像已经显示由 Lunar Sun 照亮的月壤与岩石，两套 Shader 无错误消息；没有修改全局管线或太阳配置。

另有一次验证入口切换失败后误进入原有模板主菜单，Unity 在 `GhostBridgeManager.IsServerListening`／`ManagerGhostsSpawner.LateUpdate` 调用中崩溃；Editor 重新连接后使用独立 MoonDemo 完成了上述验证。没有在此迁移中修改模板网络代码。

本记录不代表完整联机地图测试、目标平台构建验证、输入手感验收、所有低太阳角度阴影的视觉一致性或性能认证。最终游玩与画面调校由使用者判断。
