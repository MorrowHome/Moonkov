# 全身分区边缘光与材质光泽

2026-10-17，`character-lookdev`。冻结基线为 `9507d1c0` 的肩部版本。本轮重点按用户后续澄清收敛为：**全身受光边缘光、局部泛光、皮肤略带红润，保留现有配色，避免整个人惨白或塑料感**。

## 相同条件截图

![全身前后对比](Images/FullBody/BeforeAfter.png)

左右只切换材质：相同模型、固定姿态、相机、灯光、曝光和 Bloom。左侧是上一轮肩部版本，右侧是全身版本。原始 960×1080、线性 FP16 相机目标经过 URP 后处理后才读为 sRGB PNG；仅排版缩放，无修色或生成角色图像。

- [近景对比](Images/FullBody/PortraitComparison.png)、[眼睛与头发](Images/FullBody/EyeHairComparison.png)、[腿部与袜料](Images/FullBody/LegComparison.png)、[背部](Images/FullBody/BackComparison.png)。
- [强度 4 的白色主光](Images/FullBody/StrongLightComparison.png)、[强光／顶棚／点光／全暗矩阵](Images/FullBody/ShadowComparison.png)。
- [正式地图前后对比](Images/FullBody/GameComparison.png)：在正式离线 Theophilus 对局中放置固定姿态展示副本，无新增展示灯，使用地图实际灯光、运行时管线和 Volume；不是联机或完整玩家移动验收。

## 实际处理

应用到 11 份共享材质，并同步既有原生 LookDev 副本；眉毛与睫毛两份完全不变。没有编辑 Prefab、FBX、骨骼、动画、原颜色贴图、主 UV 或渲染队列。没有新 Shader、Pass、角色灯或全屏效果；现有 MoonToon hook 另有一项直接光／环境光分离修正。继续使用 lilToon 原生 Rim、Reflection、Anisotropy、MatCap、Backlight 与现有 MoonToon 遮挡兼容层。

| 区域 | 边缘光处理 | 辅助表面表现 |
| --- | --- | --- |
| 脸部 | 小范围暖玫瑰／蜜桃色，面颊区渐变，抑制嘴周 | 分区柔润镜面；不画唇色、不改嘴形 |
| 身体皮肤 | 全部皮肤区域有基础 Rim，肩部仍有局部强调；RGB 遮罩提供轻微红润 | 高光强度与光滑度分别编码，保留暖 Backlight |
| 蓝白袜料 | 从 Body 混合图集中分离，蓝色边缘保蓝，浅色袜料用低强度冷白 | 比皮肤更粗糙，降低反射强度；图集中金饰另做金属遮罩 |
| 头发 | 偏冷、保留发色的细长边缘亮带 | 两层各向异性 GGX；弱受光丝绸 MatCap 光带，避免极细亮线 |
| 蓝白衣服 | Rim 乘主色，按图集蓝布、白布、金饰分区；布料边缘克制 | 蓝布稍有缎光，白布较柔，金饰承担较集中反射 |
| 眼睛 | 保留原虹膜、瞳孔与绘制高光，避免全眼发光轮廓 | 原生视差、局部凸角膜法线、受光 MatCap 与镜面；眼白仅弱反光 |
| 金属／宝石 | 窄而集中的亮点，局部轮廓增强 | 各自的 Smoothness、Metallic、Reflection 与 MatCap 强度 |

关键发现：`body.png` 同时画了皮肤、蓝色袜料、浅色袜料和饰件，因此仅按材质名将 Body 当成皮肤会使袜料也染红、泛白。本轮在原 UV 上增加颜色区域分类与柔和遮罩，不改原贴图。分类基于当前作者交付的色板，是近似分区，并非人工重画的精细材质 ID。

## 参数与亮度控制

所有新增 Rim 依旧 `_RimEnableLighting=1`、`_RimDirStrength=1`、`_RimDirRange=0`、背面遮罩开启、间接 Rim alpha=0。`_RimShadowMask=0` 去掉 toon N·L 门控，真实遮挡仍由现有 MoonToon 的受光颜色控制；没有移除主光或局部灯阴影。

| 材质 | `_RimColor` RGBA，Unity 材质参数值 | Fresnel / Border / Blur | MainStrength |
| --- | --- | --- | --- |
| Face | (2.5, 2, 1.95, 0.30)，另乘区域遮罩 | 3.5 / 0.50 / 0.75 | 0.20 |
| Body | (4.4, 4.4, 4.4, 0.65)，另乘分区 RGB/alpha | 2.8 / 0.50 / 0.75 | 0.20 |
| Hair | (2.1, 2.4, 2.8, 0.34) | 3.3 / 0.55 / 0.65 | 0.75 |
| 布料 | (2.2, 2.6, 3, 0.40)，鞋 alpha 0.22，另乘区域遮罩 | 3.5 / 0.50 / 0.75 | 0.80 |
| Jewel / Glasses | (2, 2.2, 2.5, 0.25) | 4 / 0.60 / 0.50 | 0.70 |

Body 的皮肤 Rim RGB 为 (1, 0.78, 0.76)，在白光下形成轻微暖粉边缘；蓝袜区域趋向 (0.2, 0.4, 1)，浅色袜料趋向 (0.85, 0.9, 1)。基础 alpha 为皮肤 0.36／袜料 0.10，肩部另外强调。Body 的 Metallic=0.5 乘专用金饰遮罩，皮肤区域为零。

皮肤 Smoothness 标量仍由纹理控制：Face 约 0.55–0.74、身体皮肤约 0.60–0.67、袜料约 0.42、图集金饰约 0.85；纹理 alpha 单独控制反射能量。头发 Smoothness=0.68，各向异性 scale=0.5，两层强度 0.48/0.20、宽度 1.5×0.7 / 2×0.9。眼睛 Smoothness=0.96、Reflectance=0.065、Parallax=0.026，角膜法线只覆盖原虹膜 UV 岛；它是法线近似，不是新角膜网格或真实折射。嘴周继续抑制额外反光，避免之前嘴部异常。

**本轮不改变全局 Volume**：Bloom 仍为 intensity 0.65 / threshold 1.5 / scatter 0.68，曝光 +0.4 EV、Neutral 及原调色不变。原主色参数、HSVG、阴影色与明暗 ramp 都保持基线；`_LightMaxLimit=1`、`_LightMinLimit=0`、`_AsUnlit=0`。不启用 Emission 或环境反射。新增亮度集中于角度相关的高光和轮廓。

强光整图平均显示 RGB 0.26990→0.27067；RGB 三通道都 >0.98 的像素比例约 **0.01283%→0.00829%**。这组特定镜头没有增加大面积纯白，但不能代替所有角度的审美判断。HDR 全图最大通道 18.672→47.469、平均最大通道 0.11515→0.11703，超过 Bloom 阈值像素 181→296，表明主要增加局部亮点。原始报告中的 `shoulderPeak` 沿用旧捕获器屏幕窗口，**不是本轮单独皮肤测量**，不用于结论。

## 阴影与游戏验证

独立 LookDev 完成侧光、背光、正面强光、室内点光、4m/8m 顶棚、遮挡内点光、全暗，以及正面全身、脸部、腿部和背面相机对照。8m 顶棚与全暗的全身前后图像差为零。运行时继续沿用上一轮已验证的太阳 shadow near plane 16m 修复。

首次将检查从脸部扩大到全帧时，发现阴影／无太阳条件下仍有少数金属亮点：脸部约 0.005，但峰值达到 0.2745/0.2706。证据保留在 `Images/FullBody/AmbientHighlightDiagnostic`。这不是 Emission，而是原生 `lilReflection` 将包含 Probe/环境填充的 `fd.lightColor` 当成方向光，GGX 将微弱填充放大；HDR Rim 也有类似轮廓放大风险。

因此在既有 `MoonToonHooks.lilblock` 中，仅调用原生 Reflection / Rim 时，临时使用“实际主光颜色 × 真实遮挡可见度 + 已接收阴影的局部灯颜色”，随后恢复原来的 `fd.lightColor`。Rim 保留原受光上限，GGX 保留真实 HDR 直接光；漫反射和环境 Probe 填充不变。未改写 GGX 或 Rim 形状计算，也未用降低曝光掩盖问题。A/B 两侧共享当前这个 hook，因此图片/表格隔离的是材质改动，未单独测得此 Shader 修正的 GPU 差值。

正式对局复核脚本 `ValidateFullBodyShadow.cs` 保留实际运行时管线，在隔离测试区使用当前正式共享材质、标准 URP Lit 对照球和 64m 宽／8m 高顶棚。测试开始临时将环境光和 Probe 设零，游戏时钟仍可能刷新环境填充（各阶段实际 ambient/SH 值随结果记录）；分别拍日光、顶棚遮挡、遮挡内点光、全暗；检查全帧峰值而非仅检查脸部，结果见 [LiveShadowResult.txt](Images/FullBody/LiveShadowResult.txt)，原图为 `live-tactical-*.png`。最终太阳／顶棚／点光／无太阳测试通过：顶棚及无太阳全帧峰值均为 **0.0078（约 2/255）**，而修复前约 0.2745/0.2706；脸部依旧约 0.005。各阶段 ambient/SH0 实际记录为零，残留仅约 2/255 的显示基底，未再出现集中亮点。测试结束恢复原灯光、环境、管线、玩家名并销毁临时区，使用一次性离线档案。角色能量线等既有战斗 VFX 在表面测试中关闭，本轮未修改其玩法表现。

材质应用→恢复→重应用 CRC 检查通过；原主贴图、UV、Shader、队列、漫反射主色与阴影 ramp 保持，Volume 内容不变，Shader 错误 0，见 [AssetAudit.json](Images/FullBody/AssetAudit.json)。最后一次脚本重编译 0 错误/0 警告，见 [Compile.json](Images/FullBody/Compile.json)。启动期间出现过新脚本 Texture2D 构造函数错误，已修正并重新编译，不计为现存错误。对局期间另有 NetCode tick batching 和 Unity Account API 超时警告；未改动相关系统，也不据此宣称对局帧率达标。

## 成本与范围

Unity 6000.5.10f1 / Apple M5 / Metal，Editor Play 960×1080，A/B 交错 4 轮，每轮预热 60、记录 120 帧，每侧 480 帧；同管线、同 Bloom、同阴影参数。PNG 读取不计入性能样本。

| 中位数 | 修改前 | 修改后 |
| --- | ---: | ---: |
| Main Thread | 4.048ms | 3.063ms |
| 相机 begin/end CPU 墙钟 | 0.956ms | 0.861ms |
| Editor 帧间隔 | 4.049ms | 2.931ms |
| Draw / SetPass / 三角形 | 75 / 48 / 477345 | 75 / 48 / 477345 |

Editor 差值受后台负载和调度影响，不是性能提升结论。GPU recorder 不可用（原始 0 不是零成本），未取得可靠 GPU 对照，不能据此宣称 FPS 改善或成本不变。13 张新增数据／法线贴图的 Editor 驻留合计估算 **9,101,456 bytes，约 8.68 MiB**，不是打包版 GPU 显存差值。头发增加一次弱 MatCap，眼睛增加法线采样，服装新增分区 Rim；无额外 Pass，但片元工作仍有成本。未完成其他 GPU、打包版本、多人或全地图性能验收。

模型原有硬边、部分发束法线/UV 接缝、图集分辨率与已绘制的反光仍限制近景；当前发束方向场也是几何近似。本轮保留源模型，不用重后处理掩盖这些限制。原始性能与测量在 [Performance.json](Images/FullBody/Performance.json)、[Summary.json](Images/FullBody/Summary.json)。

## 复现与回退

- `Tools/Moonkov/lilToon/Full Body/Build Copies`：冻结 Before 只创建一次，重建候选与数据贴图；不会直接应用正式效果。
- `Apply To Game` / `Restore Previous Look`：在编辑模式下应用全身版／恢复 `9507d1c0` 肩部版材质，同步既有原生 LookDev 副本；不改 Volume。
- 打开现有 `LilToonLookDev.unity`，进入 Play，执行 `Full Body/Capture Full In Play`；截图期间不重编译，不在 Play 中保存场景，完成后退出 Play。
- `python3 Tools/LookDev/MakeFullBodyComparison.py` 排版和汇总，依赖 Pillow/NumPy，字体可用 `LOOKDEV_FONT` 指定。
- `ValidateFullBodyAssets.cs`、`CaptureFullBodyInGame.cs`、`ValidateFullBodyShadow.cs` 通过 Unity CLI `run_script` 执行，入口为同名验证类 `.Main`、`FullBodyWorldCapture.Start`、`FullBodyLiveShadowProbe.Start`。游戏测试只用于开发离线对局。

候选、Before 与贴图位于 `Assets/DollSinger/LilToonLookDev/FullBody`；编辑器实现为 `MoonkovFullBodyLookDev.cs` / `MoonkovFullBodyCapture.cs`。材质回退不撤销本轮 hook 的直接光修复；完整回退使用 Git。旧 Shoulder Rim / Native / MMD 应用菜单会覆盖这轮正式材质，应使用 Full Body 菜单。完整文件回退可用 Git。
