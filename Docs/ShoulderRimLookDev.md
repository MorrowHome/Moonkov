# 肩部边缘光与 Bloom 实装

2026-10-17，`character-lookdev`；本轮冻结基线为 `163981cd`。目标是用户描述的肩部晶亮边缘与外侧柔和光晕。已应用到正式共享材质与既有 Volume。这是局部光感升级，整体皮肤通透、眼睛和头发仍未获得用户的最终审美验收。

## 实际画面

![本轮修改前与最终加强版](Images/ShoulderRim/BeforeAfter.png)

同相机、同姿态、同光向、同曝光。左侧是本轮之前，右侧是加强版；左 Bloom intensity/scatter 为 0.10/0.55，右为 0.65/0.68，阈值都为 1.5。均使用修正后的太阳 shadow near plane 16m，避免把遮挡错误当成材质差异。图片是实际 Unity 截图排版与相同区域放大，无修色或生成图。

![相同最终材质，只切换 Bloom](Images/ShoulderRim/BloomComparison.png)

第一版过于保守，用户指出“不够亮、没有泛光”；其截图保留于 [PreviousSubtle](Images/ShoulderRim/PreviousSubtle/BeforeAfter.png)。最终增加局部 HDR 能量和 Bloom，未再次抬高全身漫反射。最终 Bloom 开关对照的肩部窗口平均 RGB 字节差 7.05，外侧窗口 4.42，面部抽样窗口 0.20；这是显示图像差异，不是辐射度或通透度指标。窗口坐标见 [BloomDelta.json](Images/ShoulderRim/BloomDelta.json)。

- [原生 GGX、Rim、组合及 Bloom 开关拆解](Images/ShoulderRim/Ablation.png)：只切换 Body/InnerDress 的相应功能，其他 11 份材质相同。“修改前”的 Bloom 开启行使用旧强度，其他列使用最终强度；关闭行不含该差异。
- [正常近景](Images/ShoulderRim/PortraitComparison.png)、[全身](Images/ShoulderRim/full-after.png)、[转动观察相机](Images/ShoulderRim/turn-after.png)、[背光](Images/ShoulderRim/back-after-bloom.png)、[正面强光](Images/ShoulderRim/front-after.png)。
- 正式 Theophilus 离线对局：[Before](Images/ShoulderRim/game-before.png) / [After](Images/ShoulderRim/game-after.png)。固定姿态展示副本置于真实角色附近，使用当前地图灯光和管线，无新增灯；两侧均使用最终 Bloom，只隔离两份材质差异。这不是完整玩家移动或多人验收。

## 材质与后处理

复用现有 MoonToon/lilToon 原生 Rim，不新增 Shader、Pass、透明壳或后处理功能。只改 `Body` 与 `InnerDress`，并同步已有 LookDev 副本；Face、Eyes、Hair 等其余 11 份共享材质未改。原主颜色贴图、UV、渲染队列、材质 GUID、模型和动画不变。

| 参数 | Body | InnerDress |
| --- | --- | --- |
| `_RimColor`，线性 HDR RGBA | (5.5, 5.1, 4.7, 0.75) | (5.1, 5.4, 5.6, 0.55) |
| `_RimFresnelPower` | 2.2 | 2.2 |
| `_RimBorder` / `_RimBlur` | 0.45 / 0.65 | 0.45 / 0.65 |
| `_RimDirStrength` / `_RimDirRange` | 1 / 0 | 1 / 0 |
| `_RimMainStrength` | 0.1 | 0.1 |
| `_RimEnableLighting` / `_RimShadowMask` | 1 / 0 | 1 / 0 |
| `_RimIndirColor.a` / `_RimBackfaceMask` | 0 / 1 | 0 / 1 |

两张 512² RGBA 遮罩仅在肩部皮肤、袖子区域有 alpha，RGB 白色。按当前固定姿态网格位置烘到原 UV，边缘补 3 像素，线性、Mip、Clamp、CompressedHQ、Read/Write 关闭。该模型 Renderer 为 100 倍缩放，需 `BakeMesh(mesh,true)`，否则肩部空间定位错误。运行时只采贴图，没有逐帧区域计算。

`_RimShadowMask=0` 移除 lilToon 的 toon N·L 门控，否则会抹掉逆光轮廓；真实主光/局部灯遮挡仍经现有 MoonToon 的 `fd.lightColor`，由 `_RimEnableLighting=1` 保留。未打开 Emission、环境反射或提高 Smoothness，未降低皮肤透明度。这是艺术化边缘光，不等于皮肤 SSS 或完整物理透射。

正式 `MoonAnimePostProcessing` 与 LookDev grade：Bloom intensity **0.65**、threshold **1.5**、scatter **0.68**。原 Neutral、+0.4 EV、其他调色保持不变。Bloom 是全局效果，也会增强其他超过阈值的饰件、地图亮点，不能声称只影响肩部。

## HDR 截图修正

旧捕获器显式指定 8-bit 相机目标，可能在 Bloom 前截断 HDR，不能用于评判真实泛光。现改为线性 FP16 场景目标，完成 URP 后处理后才 Blit 到 sRGB 8-bit PNG。常量线性 0.18 校准输出 **118/255**，与预期一致，见 [ColorCalibration.json](Images/ShoulderRim/ColorCalibration.json)。旧历史图片未覆盖。

关闭后处理的 FP16 探针：全图最大通道 **11.789 → 27.547**，全图平均最大通道 **0.4656 → 0.4837**，超过 Bloom 阈值的像素 **568 → 3556**。亮点能量提升而平均值只小幅提高；这不代表没有局部白色饱和。肩部 ROI 也包含金色饰件，不能把其峰值归因于纯皮肤。数据见 [Summary.json](Images/ShoulderRim/Summary.json)。

## 战术阴影修复与验证

发现之前的高顶棚漏光并非 Rim 单独造成：在同一现场，标准 URP Lit 白球和人物都无法接收顶棚阴影。只改变方向光 `shadowNearPlane`，0.2m 时 4m 顶棚下人物亮度与无遮挡相同；16m 或 128m 时人物和 Lit 白球都进入阴影，8m 顶棚在 16m 下也通过。原因是当前小近级联的阴影视锥裁掉上方投影体。证据与六阶段照片见 [ShadowNearProbe.json](Images/ShoulderRim/ShadowNearProbe.json)、`shadow-near-0..5.png`。

在既有 `MoonSceneLighting.OnEnable` 中将方向光 shadow near plane 至少设为 **16m**，保存旧值，`OnDisable` 恢复。无序列化字段/场景资产变化；无新增逐帧操作。正式游戏生命周期验证为 **16 → 0.2 → 16**，停用时管线恢复 PC_RPAsset，重启组件恢复室内运行时管线，见 [GameValidation.json](Images/ShoulderRim/GameValidation.json)。

正式离线对局的当前运行时管线中，另建隔离测试区：实际新材质、64m 宽/8m 高顶棚、标准 Lit 对照球，依次日光、顶棚、顶棚下点光、无光。结果见 [LiveShadowResult.txt](Images/ShoulderRim/LiveShadowResult.txt) 与 `live-tactical-*.png`；独立 LookDev 同时通过 4m/8m 顶棚与点光测试。测试结束恢复灯光、管线和玩家名，临时区销毁，未写入真实玩家存档。

## 性能与验证范围

Unity 6000.5.10f1，Apple M5 / Metal，Editor Play，960×1080，固定姿态。A/B 交错四轮，各轮预热 60、记录 120 帧，每侧 **480 帧**。截图读取不计入样本。来自最终加强版这一轮的中位数：

| 指标 | 修改前 | 修改后 |
| --- | ---: | ---: |
| Main Thread | 4.996ms | 4.907ms |
| 相机 begin/end 提交墙钟 | 1.051ms | 1.032ms |
| Editor 帧间隔 | 4.965ms | 4.870ms |
| Draw / SetPass | 75 / 47 | 75 / 47 |
| 三角形 | 477345 | 477345 |

GPU recorder 不可用（记录 0，不能解释为零耗时）；Editor 数值波动不能证明性能提升。此基准同时包含太阳 near plane 0.2→16 和材质/Bloom 变化，未单独测量整张地图增加的阴影投影体成本。新增两张贴图 Editor 驻留估算共 **1,400,224 bytes（约 1.34 MiB）**，不是独立 GPU 显存测量。未完成打包版本、其他 GPU/平台、全地图 GPU 性能验收。

脚本重编译 0 错误/0 警告；材质应用→回退→重应用 CRC 检查、其余 11 份材质不变、Shader 编译错误 0，见 [AssetAudit.json](Images/ShoulderRim/AssetAudit.json)。原始逐帧数据见 [Performance.json](Images/ShoulderRim/Performance.json)。

## 复现、回退与文件

- `Tools/Moonkov/lilToon/Shoulder Rim/Build Copies`：从冻结 Before 重建遮罩与候选，不覆盖正式效果。
- `Apply To Game`：只应用上述两份材质和现有 grade；当前已应用。
- `Restore Previous Look`：恢复本轮前两份材质与 grade；太阳阴影和截图修复保留，完整回退使用 Git。
- 打开 `Assets/DollSinger/LilToonLookDev/LilToonLookDev.unity`，进入 Play，运行 `Capture Full In Play`。退出 Play 后再编辑；捕获期间不要重编译。该测试会临时更改场景灯光，勿在 Play 中保存场景。
- `python3 Tools/LookDev/MakeShoulderComparison.py`：Pillow/NumPy 排版和汇总，字体可用 `LOOKDEV_FONT` 指定。
- `Tools/LookDev/ValidateShoulderAssets.cs`、`ValidateShoulderSunLifecycle.cs`、`ValidateShoulderShadow.cs`、`CaptureShoulderInGame.cs` 可通过 Unity CLI `run_script` 执行；游戏脚本只用于开发离线对局，入口分别为 `ApplyShoulderAudit.Main`、`ValidateShoulderSunLifecycle.Main`、`ShoulderLiveShadowProbe.Start`、`ShoulderWorldCapture.Start`。生命周期测试会切换组件，勿用于联机实战。

旧 `Build LookDev Copies` / 旧 MMD 应用菜单可能重建覆盖本轮效果，应使用 Shoulder Rim 菜单。新增资产位于 `Assets/DollSinger/LilToonLookDev/ShoulderRim`；编辑器实现为 `MoonkovShoulderRimLookDev.cs` / `MoonkovShoulderRimCapture.cs`。没有更改 Prefab、FBX、动画、原颜色贴图或渲染管线资产。
