# Windows 续接：Blender 整图外观还原

更新：2026-10-29。接力设备：用户的 RTX 4070 Super 台式机。

## 先读结论

任务没有完成。正式游戏场景尚未安装新世界。当前截图不能作为最终画面。

全图材质、模型和固定光照已生成并导入。完整反射还没算完。平均画面仍有明显脏斑和模糊块。换显卡能缩短预计算时间，不能自动修复错误的数据或着色流程。

用户最新要求是转到 Windows 开发。不要继续在 Mac 后台启动整图烘焙。交接时没有本任务的 Blender 渲染或临时防休眠进程。

## Windows 接续状态（2026-10-28）

- 项目为 `E:/UnityProjects/FPS_Template`，继续使用 `develop`。恢复前工作区干净。传输快照已单独提交为 `050a6e4a`。
- 传输包内清单的 7680 个文件均通过大小和 SHA-256 校验。恢复 3741 个项目文件。生成文本仅清理行尾空白；二进制数据不变。
- 忽略的中间数据已移到项目 `LocalData/SourceFidelity20261028`。恢复阶段 4、索引 103 的暂停状态，没有继续导入。
- 原始候选 blend 留在 `E:/Download/Moonkov_SourceFidelity_Windows_20261028.zip/Extracted/ExternalSource`。不要复制进 Assets。
- 本机显卡为 RTX 4070 SUPER，显存 12 GB，驱动 610.74。同版 Unity 位于 `D:/Unity/Editor/6000.5.10f1/Editor/Unity.exe`。本次未启动 Editor。
- 旧 Blender `D:/Blender51/blender.exe` 为 5.1.1。读取准备文件会提示版本过旧，禁止用它重算源数据。
- 已从 Blender 官方下载并校验 5.2.2 便携版，路径为 `D:/_DevTools/Blender/blender-5.2.2-windows-x64/blender.exe`。默认场景的 OPTIX 渲染小样通过：64×64、8 samples，渲染约 2.85 秒。该计时不代表整图速度。
- 同版 Blender 能读取 `PreparedWorldResolved.blend`。已显式选择 `SourceWorldStatic`。176 个图像数据块没有缺失外部文件。检查没有保存源文件。
- 整图烘焙与反射脚本改用 `cycles_device.py`。Windows 自动选择 OPTIX，未发现设备时尝试 CUDA。没有 GPU 就报错；只有显式设置 `SOURCE_CYCLES_BACKEND=CPU` 才允许 CPU。
- `inspect_gpu.py` 提供独立小样本检查。使用 `--background --factory-startup --python`，追加 `-- --render-probe` 可渲染 64×64、8 samples 的默认场景。输出在 `LocalData/SourceFidelity20261028/WindowsGpu`。它不加载源世界。
- 本次接续不代表脏斑、完整反射、Windows Shader 编译或正式安装通过。继续前仍须按下文定位脏斑，并验证导入器补丁。

## Blender 工具（2026-10-29）

- 已按用户要求安装 ahujasid `mcp-for-blender` 2.2.6。Codex 全局服务名为 `blender`，使用现有 uvx，连接本机 `127.0.0.1:9876`。Blender 5.2 的 `blender_mcp` 插件已启用。实际 MCP 握手和场景读取通过。当前桥接实例为空场景，不是正式源世界。
- 已安装 RobLe3 的 `blender-rendering`、`blender-uv-texturing`，以及它们依赖的 `text-to-blender` 和 `reference-look-calibration`。位置为用户 `.codex/skills`。Skill 下一轮可用；Codex 原生 MCP 工具需重启应用加载。不要自动重启 Codex。
- 后续复用已有 Blender 桥接实例。长时间烘焙可继续使用命令行断点脚本，避免超过 MCP 的单次调用超时。不要把已启动的长任务重复提交。
- 已用 4070 SUPER 的 OPTIX 完成同镜头 7 个通道的真实场景诊断。纯颜色和 AO 未出现面板大块脏斑；直接显示旧 irradiance 已复现。图在 `LocalData/SourceFidelity20261028/World/WindowsChannels`。
- 前景面板约 1.83 平方米，旧 4096 图集只给它约 11×27 像素。原始 irradiance 噪声被放成大块，降噪后仍有模糊斑。
- 2026-10-29 局部对照：同一实例、同一光照、128 samples，256 小图比 32 小图平整。低密度增加到 8192 samples 仍有斑块。高密度先降噪再缩图仍保留改善。结果在 `World/DensityPilot`。
- 整张图集离线 8192、128 samples、缩回 4096，用时约 132 秒，仍有可见色斑，不能作为修复交付。结果在 `World/SupersamplePilot`。
- 保留原 UV，在图集 26 的 `[3440,976,80,80]` 像素区域按 1024 离线烘焙，再缩回 80，面板表面色斑明显减轻。结果在 `World/RoiPilot`。这只确认局部方法；不是整图验收。
- 新增 `bake_world_tiled.py`。在 `PreparedWorldResolved.blend` 中运行，参数 `-- --atlas=26`。默认 64 块、8 倍离线密度、128 samples、运行时图仍为 4096。每块保留 8 个运行时像素的重叠区，降噪后裁边。保留原 AO UV；降噪不使用伪对象 ID 作为 Albedo。
- 分块结果独立写入 `World/TiledQuality_s128_x8`。每块完成后原子写入配置标记，包含源、准备文件和 helper 哈希。中断后同命令续跑。全部完成后仍须检查接缝、亮度及统计数据，再决定替换旧 Bakes。不要直接安装进正式场景。
- 分块预览必须先保存为线性 EXR，再重新加载用于渲染。临时预览直接修改内存贴图时显示过假方块，不能据此作废正确断点。第 14 块与同范围单独重烘的缩图结果相符，平均绝对误差约 `2.6e-6`。已落盘重载的部分预览在 `World/TiledPartialPreview`；其中仍有未重烘区域，不代表最终结果。
- 用户已明确允许 GPU 持续跑，画质优先。当前图集任务保持运行。名为“继续 Blender 源外观烘焙与 Unity 接入”的本聊天后续唤醒已启用，每 10 分钟检查并推进下一阶段；正常计算时保持安静。收口后停用。不要重复创建同一后续任务或重复启动烘焙。
- `9caa6029` 已将分块脚本和诊断结论提交并推送到 `origin/develop`。恢复快照 `050a6e4a` 也已在远端。此前的推送拒绝已不再是当前阻塞。

## 用户已确定的目标

- 完整接入正式游戏。不能停在局部演示。
- 优先还原候选 Blender 的材质、面积灯、反弹光和反射。
- 所有固定照明都可预计算。游戏仍需实时运行。
- 原 MC00 自主灯控可废弃。
- 噪点应细且稳定。停下来不闪，转动视角时自然变化。
- 画面优先。不能用一层屏幕颗粒掩盖错误照明。
- Windows 不要使用鼠标操控 Unity。任何时候只运行一个 Unity Editor。

## 仓库和文件转移

当前工作分支为 `develop`。交接检查时 HEAD 为 `8ca5826d`，这是其他事项的提交。外观还原已有提交包括 `a5fe636e`、`ceae0d49`。Mac 交接时新增的大量外观代码、生成资产和实验尚未提交。Windows 已用 `050a6e4a` 保存传输快照。LocalData 仍被忽略，不能只拉取 Git 就认为断点数据齐了。

Mac 交接时工作区还有其他聊天的动作、音频、字体等修改。Windows 恢复后也可能有其他聊天并行改动。不要全量暂存，不要回滚它们，不要覆盖 Windows 的未提交工作。不要自动切分支。main 只由用户管理。

配套传输包：Mac 的 `Downloads/Moonkov_SourceFidelity_Windows_20261028.zip`。包内是本任务文件快照，不是完整 Unity 项目。包含原始候选 blend（`ExternalSource`）、相关代码、生成资产、诊断图和被 Git 忽略的断点数据。先解压到独立目录，比较差异后再接入 Windows 的 Moonkov 仓库。不要直接覆盖已有工作区。

同目录提供 `.zip.sha256` 和 `.manifest.json`。包内也有 `WINDOWS_TRANSFER_MANIFEST.json`，逐文件记录大小和 SHA-256。Windows 可先用 `Get-FileHash -Algorithm SHA256` 校验 ZIP，再按清单核查。`ExternalSource` 中的 blend 是外部参考源，不要直接复制进 Assets。

关键目录：

| 路径 | 用途 |
| --- | --- |
| `Assets/MoonEnvironment/Variants/SourceFidelity/World` | 已生成的整图运行资源，约 3.3 GiB |
| `Assets/MoonEnvironment/Variants/SourceFidelity/Generated` | 477 份材质 Shader 与 HLSL |
| `SourceData/Models/SourceFidelity20261028` | 源审计、转换、烘焙和验证代码 |
| `LocalData/SourceFidelity20261028/World` | 必须另外转移的中间数据和断点，Git 忽略此目录 |
| `LocalData/SourceFidelity20261028/World/PreparedWorldResolved.blend` | 后续射线计算的整图输入，约 467 MiB |
| `LocalData/SourceFidelity20261028/World/Geometry` | 3318 个导出二进制，供 Editor 导入 |

不要转移或复用 Mac 的 `Library`、`Temp`、`Obj`。Windows 自行导入。不要把 Git LFS 指针当成模型；缺少本地对象时先按仓库规则恢复 LFS。

## 权威源

源文件：`Moonkov_WH01_HU01_LR06_SR01_L23_Removed_Components_CANDIDATE.blend`。

SHA-256：`2576f4ac6ba4b370843688fc28ace29e1169d594633102d13e0e59c64a6522cf`。

Mac 原路径在 Downloads。原文件没有保存修改。转换和法线修正在生成副本上进行。

Blender 版本为 5.2.2 LTS。源为 Cycles 64 samples、不降噪、总反弹上限 3。显示变换为 AgX / Medium High Contrast，曝光 0，Gamma 1。

固定光源有 403 盏正功率面积灯/聚光灯，另有主太阳。源太阳能量 2.2，线性颜色约 `(0.8, 0.88, 1)`。源背景线性辐射为 `(0.0024, 0.003, 0.0048)`。

`PreparedWorldResolved.blend` 是 library 文件。打开后可能提示空场景。脚本必须显式选择名称以 `SourceWorldStatic` 开头的 Scene。

## 当前产物

- 去重后约 30395 个源显示实例，排除 633 个精确重复实例。
- 最终运行几何为 3318 个块、4451542 个三角形、7296246 个顶点。
- 运行二进制检查没有无效索引、非有限数、错误光照 UV 或空法线。
- 转译了 477 种材质图。实际面使用 375 种。导入图像变体 112 个。
- 源固定照明分为 27 张图，含面积灯、太阳和反弹。主版本为 16 spp，加降噪。
- 反射计划为 461 个位置。必须保留修正后的 `WorldReflectionPlan.json`，其中 `positionVersion=3`、`boundsVersion=2`。
- 交接扫描确认 128 个反射文件与现计划位置和源哈希匹配，9 个旧位置缓存失效。文件存在不代表有效。完整 ID 清单在 `WindowsHandoffCaptureStatus.json`。
- `SourceWorldLighting.asset` 已有静态光照和 AO/方差数组。它仍是未完成的数据资产，没有完成角色探针及正式安装验收。

## 脏斑：已经确认什么

用户指出的图为 `World/full_static_unity_pilot_mean_0.png`。它是诊断截图，不是最终结果。

已做三组对照：

1. 关闭运行时漫反射噪点后，脏斑仍存在。
2. 移除 OIDN 中错误的对象编号颜色引导后，有改善，但没有根治。
3. 图集 26 提高到 128 spp 后，脏斑仍存在。再关闭镜面反射，脏斑仍存在。

因此，不能再把问题简单归因于显卡慢、屏幕颗粒或镜面反射。还没有确认最终根因。应继续分离源材质颜色、烘焙光照、AO、光照 UV、法线和纹理过滤。

建议下一步按同一镜头依次输出：纯 Base Color、纯 irradiance、纯 AO、几何法线、实际着色法线、光照 UV。分别核对 Blender 和 Unity。再临时关闭压缩与 mipmap，排除采样和串色。不要一次改多项，也不要未经定位就整图增加采样。

图集用 `smart_project(island_margin=4/side)`，烘焙 margin 为 12。需要核查 UV 图块边界、降噪跨图块混色和 mipmap 串色。这是待验证假设，不是已确认根因。

## 当前存在混合实验数据

必须注意：Unity 运行资产中的图集 26 和 AO/方差数组第 26 层，已被替换成 `DenoisePilot` 的 128 spp、无假反射率引导版本。

但 `World/Bakes` 中的整图主数据仍是原 16 spp 版本。`DenoisePilot` 中才有图集 26 的 128 spp 原始图和处理图。两者不能混称为统一完成的整图烘焙。

`LocalData/SourceFidelity20261028/ImportDenoisePilot.cs` 的 `Run()` 导入实验，`Restore()` 恢复主目录版本。该脚本可作为操作参考。不要在 Play 中运行。

## 已通过的验证

- AgX 八组色块相对 Blender 最大通道差为 1 个 8 位阶。
- 477 / 477 源着色器检查无错误。几何、阴影和深度 Pass 的代表检查也通过。
- Cubemap 轴向校准通过，Mac Metal 的面 Y 符号为 -1。Windows 必须重新校准，不能硬编码这个值。
- 均匀入射辐射的 SH 能量校准通过。
- 136 字节顶点载荷原生读回通过。
- 第一帧相机变换、每帧禁止旧调色重启、最后一个 3D Overlay 上执行一次 AgX、卸载恢复相机和全局光照，均通过检查。
- 已载入完整 3318 块、375 材质和 27 图集，渲染了 5 个检查视角。像素没有 NaN/Inf。活动场景保持不变。

以上不等于画面验收、正式战局验收、性能验收或 Player 构建通过。同步渲染计时包含首次编译和读回，不能当作游戏 FPS。

## 重要代码

运行时：

- `SourceWorldLighting.cs`：固定 lightmap、统计数组、探针和相机显示变换的作用域。
- `SourceWorldLightingData.cs`、`SourceWorldReflectionData.cs`、`SourceWorldMeshBundle.cs`：二进制资源容器。
- `SourceAgXCamera.cs`：源显示变换。支持最后一个 3D Overlay；UI 不参与。
- `MoonSceneLighting.cs`：源固定外观分支，冻结视觉昼夜。使用 `customReflectionTexture`，兼容非 Cubemap 类的反射纹理。
- `MoonEnvironment.cs`：源外观安装后跳过旧程序地表初始化。
- `SourceMaterialNodes.hlsl`、`SourceWorldSurface.hlsl`、`SourceWorldDepth.hlsl`：源节点和表面求值。

Editor：

- `SourceWorldAssetImport.cs`：断点导入。当前阶段为 4，索引 103，已停止。
- `SourceWorldCubeBuilder.cs`、`SourceCubeSampling.compute`：轴向校准、GGX mip 和 SH。
- `SourceWorldShaderValidation.cs`：477 个 Shader 检查。
- `SourceWorldSceneInstall.cs`：正式场景安装入口，尚未执行。

## 已知续跑错误

导入器在索引 103 停止，报 `captured.position` 空引用。已确认该处读取的是渲染器写出的 JSON 完成标记；并发读写存在窗口。

本地代码已补上空内容/不完整内容检查，并将反射完成标记改为写临时文件后原子替换。补丁尚未重新编译和回归。先验证这个修复，再恢复导入。不要仅把状态改为 running=true 后放任运行。

断点：`World/AssetImport.state.json`。

阶段：0 贴图；1 材质；2 光照图；3 网格和静态核心数据；4 反射；5 角色探针和最终数据；6 完成。

恢复用菜单：`Tools/Moon Environment/Source World/Import complete source world assets`。直接调用菜单比临时脚本里先 Refresh 再安排 delayCall 更可靠。域重载会丢失临时回调。

## 必须保留的技术修正

- 未连接的图像坐标使用原活动 UV；噪声使用 Generated。切到光照 UV 前必须冻结。
- AO 分全局和 only_local。合并对象后不能把局部 AO 改成整图 AO。修正缓存为版本 2。
- 反射位置不能都放在高处灯旁。已将 189 个位置移到实际地板上方约 1.5 米。旧缓存必须校验位置。
- 反射盒要覆盖实际地板。`resolve_reflection_bounds.py` 用导出几何求交，修正实际包围盒。
- Unity `AddDirectionalLight` 不是原始物理积分权重。当前以其常数项校准到 Lambert 的 1/4，并有均匀场测试。
- 材质共享 Repeat / Clamp sampler，避免 222–224 号材质超过 sampler 限制。
- Perlin 格点循环不要完全展开，避免原生 Shader 编译内存暴涨。
- Source Shader 固定为 Forward+。正式 renderer 要启用反射图集和盒投影。
- 复制 renderer 时要连子资产一起复制。不能浅复制后修改原 SSAO feature。源方案关闭旧屏幕 AO，避免叠加。
- 导入大 HDR 时预置正确 meta，避免默认压缩后再压缩一次。
- 角色探针模板直接读取 Baseline/Probes.asset。不要加载全部旧 BuildingBakedLightingData，否则会连带加载很多旧 lightmap 状态。

## Windows GPU 运行前

Unity 项目版本为 `6000.5.10f1`（`3bd4f66ad299`）。优先安装同一版本，避免接手时顺带升级项目。

1. 确认台式机项目路径、分支和未提交状态。
2. 检查 Blender 版本、NVIDIA 驱动和 OptiX 设备。
3. `bake_world.py` 和 `capture_world_reflections.py` 已共用跨平台设备选择。Windows 优先 OPTIX，其次 CUDA。启动时打印真实 GPU 名称；没有 GPU 就停止。不要直接照抄 LocalData 中的 Mac 临时命令。
4. 原始脚本多用项目相对路径。LocalData 的历史临时脚本有 Mac 绝对路径和旧 PID，必须改写，不可直接执行。
5. 先做短样本，确认 GPU 确实工作、输出哈希/位置正确，再跑整图。

GPU 检查示例：

```powershell
& "实际的Blender路径\blender.exe" -b --factory-startup --python "inspect_gpu.py"
```

Windows 检查脚本为 `SourceData/Models/SourceFidelity20261028/inspect_gpu.py`。便携版与 OPTIX 小样已通过。不需要远程服务或防火墙改动。

## 最终正式接入顺序

1. 定位并修好脏斑，统一整图光照与统计数据。
2. 完成全部有效反射，并核查源哈希、位置和包围盒。
3. 完成动态角色受光和探针验证。当前从反射场估计 SH，再重采样到 7227 点；尚未实际验收，不能宣称与 Cycles 等价。
4. 做导出几何 QA，尤其是同向共面重叠。当前只有结构检查通过，完整 z-fighting / 多角度几何 QA 未通过交付门槛。
5. 运行 `Install complete world in formal game`。
6. 正式场景为 `Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`。
7. 安装器保留碰撞和玩法，保护气闸门可动部件，解除旧显示和灯光资产引用。必须实际复核出生点、地板、门和通路，不能仅凭代码认为功能没坏。
8. 进入正式离线游戏，检查室外、MC00、NR01、玻璃、金属、角色和第一人称手臂。不要做多端联机验收。
9. 测实际帧时间与内存，检查 Console 和构建。玻璃当前为屏幕颜色折射近似，不能宣称完整光线追踪玻璃。
10. 更新文档，明确文件清单提交，再推送当前工作分支。不要混入动作、字体和音频改动。

## 下一位 Codex 的第一条任务

先读本文件、HANDOFF.md 和 AGENTS.md。检查 Windows 项目、Blender 与 OptiX。确认传输包完整，再用同镜头的纯颜色、纯 irradiance、AO、法线和 UV 输出定位脏斑。不要把“GPU 更快”当作修复，不要直接开启整图长任务，不要把未完成资产安装进正式游戏。
