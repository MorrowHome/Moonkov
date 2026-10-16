# lilToon 角色材质与光影升级

后续实装：[肩部边缘光与 Bloom](ShoulderRimLookDev.md)。本页旧相机截图使用 ARGB32 目标，不能作为 HDR Bloom 的当前验收；新捕获器已改为 FP16 并做 sRGB 校准。此前高顶棚漏光也已定位到方向光 shadow near plane，并完成局部及正式离线对局复核，证据见新文档。以下保留历史记录。

2026-10-17，分支 `character-lookdev`。基线为 `4fc02012` 中的正式材质，与 `491d07e3` 相同。本版已应用到 13 份共享 DollSinger 材质及现有正式地图 Volume Profile；动画、Prefab、FBX、原颜色贴图、主贴图 UV 和渲染队列保持原状。

## 画面与实现

此前浅色阴影、较强的全表面加算 MatCap 和中灰色调叠在一起，使颜色层次不足，衣料与皮肤的反射差别也不清楚。这版以保留原人物设计、恢复有颜色的明暗和区分表面材质为主。

- 皮肤：暖桃色阴影、较宽的过渡、轻量 RimShade 和接收阴影的暖背光。脸部使用局部 Smoothness / Reflection / MatCap 遮罩，嘴周抑制额外高光；未加入唇色、改写嘴形或降低透明度。原先较强的全脸 MatCap 减弱。
- 眼睛：保留原虹膜、瞳孔和已绘制高光，加入 lilToon 原生单步 Parallax。深度遮罩按现有虹膜 UV 岛制作；与独立的视角反射形成小幅相对变化。未使用 POM、Emission、额外透明角膜或新增 Pass。
- 头发：补上此前缺失的各向异性方向贴图，按模型几何及其切线空间投影头顶向外的流向，使用双层原生镜面高光；加深有颜色的阴影与边缘暗部，关闭头发 MatCap，避免白色加算覆盖原画。该方向场是近似发束流向，并非离线毛发 groom。
- 服装：用原颜色贴图中的金色区域制作金属 / 光泽遮罩，让金色装饰承担较强反光，白色与蓝色布料保持较柔和的反射。关闭大部分服装全表面 MatCap；眉毛、睫毛和眼白不叠亮漆。
- 反射资源：眼睛、饰件复用已安装 lilToon 包中的 `Texture/matcap_metal_realistic.png`。未下载或复制其他头像的材质和贴图。
- 受光：保留 `_LightMinLimit=0`、`_AsUnlit=0`，不启用角色自发光；中途放宽至 1.8 的版本使强光下底色发白，已撤销。最终上限为 1，以保留浅色表面层次；镜面由后续原生 GGX 计算产生。环境反射仍关闭，避免室外 Probe 倒影在遮挡内浮亮。
- 调色：保留正式地图原有 +0.4 EV 与 Neutral，调整为对比度 6、饱和度 4，去掉原冷暖分离偏色；Bloom 0.1 / 阈值 1.5。没有增加后处理或修改月球太阳、环境光。

使用的是 lilToon 已有材质功能；在现有 MoonToon 兼容层中有两项窄范围修正。首先是描边遮光：默认模板会裁掉描边的主光阴影变体，且描边不能直接沿用主体的太阳遮挡计算。本版保留相应变体，在原描边 Pass 中采样真实主光阴影并使用既有近远遮光距离，避免红蓝描边在黑暗中暴露角色。没有新增渲染 Pass 或更换管线。

第二项是强光镜面分离：lilToon 的受光上限原本同时限制漫反射与 GGX。调用原生 `lilReflection` 时，暂时把超过 `_LightMaxLimit` 的主光能量加回，乘真实主光遮挡可见度；随后立即恢复原漫反射光照。底色保持上限 1，局部镜面可随强光增强。没有改写 GGX、添加无光高光或解除遮挡。此项主要针对方向主光；新增的点光/聚光没有另一套 HDR 镜面路径。相同光向下强度由 1.6 升至 4：金色肩饰区域的通道亮度增量 P99 为 19/255，蓝色衣料区域 P99 为 0，表明变化集中在亮点而非整个底色。ROI 与原始数据在 `SpecularResponse.json`；截图字节不是 HDR 辐射度。

## 相同条件对照

![前后对比拼图](Images/LilToonNative/BeforeAfter.png)

![任务前、上一版与最终修正](Images/LilToonNative/ReviewComparison.png)

第一行柔和主光强度 1，第二行强光强度 4。上一版保留用户指出惨白时的候选材质，三列都使用当前 Shader 兼容层；保存在 `Previous`；任务前为 `Before`，当前为 `Materials`。三列同一行只切换材质，背景、曝光与灯光相同。图为原始截图排版，无修色或生成角色画面。强光下更能保住发色、肤色与明暗，局部镜面强度由遮罩控制；这仍不是完成高级水润质感的审美验收。

以下单张原图使用同一 960×1080 Game Camera、固定姿态、灯光、曝光与调色，仅切换材质。Before 是本次冻结的上版材质，并非最初导入模型的版本。两边均使用修正后的 MoonToon 阴影与镜面代码，因此不是完整 Shader 修改成本的对照。

| Before | After |
| --- | --- |
| ![改动前](Images/LilToonNative/studio-before.png) | ![改动后](Images/LilToonNative/studio-after.png) |

截图目录还包括阳光、室内点光、背光、顶棚遮挡、遮挡下局部灯、无主光，以及眼部近景和斜侧视角的成对图片。每阶段等待 20 个实际渲染帧后，在相机提交完成后读取渲染目标；截图不是生成图。

| 阳光 | 顶棚阴影 | 阴影中的局部灯 |
| --- | --- | --- |
| ![阳光](Images/LilToonNative/sun-after.png) | ![阴影](Images/LilToonNative/shade-after.png) | ![局部灯](Images/LilToonNative/lamp-after.png) |

同一脸部 ROI 的相对截图亮度为：阳光 0.8055，遮挡 0.0055，局部灯 0.7109，无主光 0.0047；遮挡约为阳光的 0.69%。这是截图像素比较，不是照度，也不能代表全角色所有位置的可见性。受控顶棚的阴影边界仍照到头顶饰件，图中上部有亮区；真实遮挡范围之外的部位仍会受光。

## 正式地图运行验证

通过主菜单启动正式 Theophilus 离线对局，使用独立临时档案；运行状态为 InGame，实际管线为 `MoonSoftAnimePipeline (interior runtime)`，已生成 DollSinger 角色。在真实地图位置使用固定姿态视觉副本、同一相机和现有 Volume 拍摄 A/B，没有增加摄影补光。

| 地图 Before | 地图 After |
| --- | --- |
| ![地图改动前](Images/LilToonNative/game-before.png) | ![地图改动后](Images/LilToonNative/game-after.png) |

这是地图光照下的材质对照，拍摄对象是固定姿态副本；不将它描述为玩家操作、多人对战或全图验收。

同一正式对局内另建临时远处舞台，用真实顶棚投影，测阳光、遮挡、遮挡中的点光和无光；相机连续运行，每阶段等待 20 帧并在提交后读取。最初 4m 高、16m 宽顶棚一轮测得阳光 0.7966、遮挡 0.0057、局部灯 0.7486、无光 0.0050；后续升高顶棚测试失败，角色与标准 URP Lit 参照球均仍受光。继续改变顶棚和相机远裁剪后仍未定位原因，最后一轮为诊断暂停后退出 Play。失败截图与记录位于 `Images/LilToonNative/Diagnostics/`，不作为通过证据。该测试会暂时隔离其他太阳，退出后恢复原状态。**正式地图所有遮挡条件的验收仍未通过，需要进一步区分阴影级联、验证相机与运行时管线问题。**

## 关键参数

| 部位 | 参数 |
| --- | --- |
| 皮肤 | ShadowStrength 0.8，Border 0.72，Blur 0.6；ShadowColor RGB 0.94 / 0.73 / 0.70；Smoothness 1 乘局部数据遮罩（基础 0.52，局部最高约 0.9），MatCap alpha 0.26 再乘局部遮罩 |
| 头发 | Anisotropy 0.6；两层高光强度 0.32 / 0.08，第二层 Shift 0.12；Smoothness 0.72，Shadow Border 0.78 / Blur 0.5；关闭原头发 MatCap |
| 眼睛 | Smoothness 0.9；Parallax 0.02 / Offset 0.5，实际位移还乘深度图与视角；POM 关闭，反射 MatCap alpha 0.24 |
| 服装 | Smoothness 0.85 乘遮罩，布料遮罩 0.38、金色区域约 0.87；Metallic 0.5 乘金色遮罩 |
| 共通 | 最低亮度 0，Unlit 0，上限 1；MatCap / Rim 随光照变化，Backlight 接收阴影，保持 MoonShadowNear 0.12 / Far 0.8 |

这些数值用于当前模型，不是跨模型通用预设；颜色空间、UV 和原画改变后需要重新检查。

## 性能与资产验证

Editor Play / Apple M5 / Metal，固定姿态、960×1080，相同灯光和修正后的 Shader，4 轮交错 A/B，每块 60 帧预热、120 帧采样，共 960 个样本。截图读取与 PNG 编码不计入采样。

| 中位数 | Before | After |
| --- | ---: | ---: |
| Camera CPU 提交耗时 | 1.8387 ms | 1.4005 ms |
| Editor Main Thread | 14.9534 ms | 10.6991 ms |
| Draw Calls | 90 | 90 |
| SetPass | 50 | 50 |
| 三角形 | 597557 | 597557 |

该结果包含 Editor 活动，不能据此宣称游戏 FPS 提升。GPU ProfilerRecorder / FrameTiming 未返回有效 GPU 数据，未取得独立构建 GPU 成本；兼容层描边/镜面修复的额外 GPU 成本没有单独隔离。原始数据在 `Images/LilToonNative/Performance.json`。

11 张共享数据贴图采用 mipmap、线性采样、CompressedHQ、关闭 Read/Write。在当前 Editor 的 `Profiler.GetRuntimeMemorySizeLong` 中，总驻留估计由 27,798,323 降至 7,045,872 bytes（约 6.72 MiB）。这不是独立 Player 的纯显存测量；贴图由所有角色共享，不按角色复制。

13 份正式材质经过 Apply → Restore → Apply 校验，Restore 后 CRC 与冻结的 Before 一致；主颜色贴图、主 UV、Shader、队列检查通过。共享材质 GUID 保留，源 FBX 和贴图没有修改。两个公开 Shader 与隐藏 Pass 的诊断无 Error。C# 编译检查通过；此前 Console 的 SubScene 断言及其他系统告警不在本任务的修复范围。

## 使用与回退

独立场景：`Assets/DollSinger/LilToonLookDev/LilToonLookDev.unity`。它不在 Build Settings 中。

- `Tools → Moonkov → lilToon → Build LookDev Copies`：从冻结的 Before 重建候选材质和数据图，不覆盖正式材质。
- `Apply Native Look To Game`：应用本版共享材质与调色。
- `Restore Before Look`：恢复本轮之前的 13 份材质与调色；Shader 遮光与强光镜面兼容修复仍保留。若需完整回到任务前状态，用 Git 回退本次提交。
- `Capture Comparison In Play`：在独立场景 Play 中生成对照与交错性能记录。

旧的 `Rendering/Apply Soft MMD Character Look` 菜单属于前版公式，会覆盖本版材质；本版使用上述 lilToon 菜单。

## 质量边界

当前模型的眼睛高光、部分头发高光和大块服装颜色已画在原颜色图里；嘴部设计也很简化。Shader 能增加光照与材质层次，不能把原画里的高光变成完整、独立的角膜反射，也不能凭参数产生参考头像的五官与发束造型。本次保留这些设计，没有用程序虹膜或后处理替换它们。

发束方向图是几何近似，尚未逐束人工绘制；金属遮罩按原画颜色筛选，可能需要后续逐岛精修。未实现物理 SSS、真实角膜折射、实时反射或离线 Ray-MMD 效果，不将本版宣称为已经达到商业作品或精品头像的最终美术品质。

截图左侧的棕色扇形几何在冻结的 Before 中也存在，未作为本次 Shader 新问题去改源网格或骨骼。最终审美以实际截图和游戏内观察为准。

对比图排版脚本：`Tools/LookDev/MakeComparison.py`（Pillow、NumPy；默认使用 macOS Hiragino Sans GB，可通过 LOOKDEV_FONT 指定中文字体）。性能汇总脚本：`Tools/LookDev/SummarizePerformance.py`。
