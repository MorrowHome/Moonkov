# Theophilus 写实环境基线

正式地图使用原有材质、照明和烘焙资源呈现写实环境。黑白块面、钴蓝点色和白墙表面颗粒属于已停用的风格实验，不再作为正式默认效果。

## 正式配置

- `TheophilusGameScene` 恢复风格实验前的10个原材质绑定，移除 `VisualPass01/Materials/WhiteFinish_*` 材质副本及 `IndustrialDust` 的 `_SurfaceWear` 实验。原有材质的贴图和受光输入继续生效。
- 场景中的 `MoonSilverPrint` 组件禁用，保存 `faithfulLighting=true`、`intensity=0`、`previewInSceneView=false`。保留滤镜代码和编辑入口供实验使用；它们不代表正式写实基线。
- `TheophilusLook` 保留 ACES 和 `postExposure=0.55`，设置 `contrast=0`、Bloom `intensity=0.15`。
- 正式场景的默认环境反射强度为 `1`；房间局部反射探针仍由 `BuildingBakedLighting` 按供电状态切换强度，通电为 `1`、断电为 `0`。

本次不修改模型、贴图、烘焙资源、自动曝光，以及已有打包和联机修复。恢复材质与关闭滤镜不等于重新烘焙光照，也不自动消除已有烘焙基底中的环境填充。

## 开发对照

Editor Play／Development Build 保留 Ctrl+L 输入绑定。没有已启用、强度大于0且材质有效的 `MoonSilverPrint` 时，组合键被消费但不切换画面、不打印旧风格切换提示，也不触发头灯手势。

只有显式启用实验组件并提供有效材质和强度后，Ctrl+L 才临时反转其保真／风格化选择；不会把实验设置保存回场景。详见 [开发用环境风格对比](Development/EnvironmentStyleToggle.md)。

## 验证边界

2026-10-23：Unity 脚本编译通过；正式图不再引用 WhiteFinish，Industrial Dust 与 URP Lit 在当前编辑器中受支持且没有 Shader 错误；禁用效果后的比较方法不会切换状态。场景与实验前版本的差异仅为滤镜配置及默认反射强度，模型、碰撞、摆放与其他材质引用一致。

已检查正式地图室内外各一处 Edit Mode Game 相机预览，截图和验证记录位于本地 `Library/BuildValidation/Realism20261023/`。这仅确认原始材质和受光恢复：外墙细节仍偏平，不代表最终写实画质验收。本轮未进入 Play、重新打包或进行联机验收，方法检查也不能替代实际键盘交互。

历史效果可查阅 [2026-10-22 环境作用域记录](ArtDirection/20261022-EnvironmentOptics/README.md) 和 [2026-10-19 连续银盐记录](ArtDirection/20261019-ContinuousSilver/README.md)。其中截图、默认值和执行链仅代表当时的实验状态。
