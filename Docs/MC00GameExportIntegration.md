# MC00 游戏导出版接入

来源：`E:/Download/MC00_Game_Export.blend`，2026-10-11。原文件保留，项目内归档在 `SourceData/Models/MC00GameExport/MC00_Game_Export.blend`，SHA-256 记录于同目录 `unity-manifest.json`。

2026-10-11 最初以 MC00 单体的独立候选接入，当时没有替换正式 Apollo12 地图。作者说明、来源映射、碰撞覆盖、交互约束与灯位清单保存在 `SourceData/Models/MC00GameExport/AuthorNotes/`；内嵌 Python 只作来源说明，当次没有执行。以下导入和 CPU 整理计数保留当时版本的结果，不等同于后续 Full v02 的计数。

## 正式地图现状（2026-10-13）

正式 `Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12GameScene.unity` 已改为引用 `Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Prefabs/Apollo12MC00Candidate.prefab`，由 Full v02 的模型、碰撞和已整理的渲染组替换原环境实例。源 FBX、作者 `.blend`、旧环境 Prefab 和各独立预览继续保留；正式主菜单、Host、Client 与独立服务器仍加载同一 Apollo12 游戏场景。

替换入口为 **Tools → Moon Environment → Apollo 12 → Use optimized MC00 in gameplay**。场景保留 8 个出生点、全部 12 个物资点、撤离点和原搜打撤逻辑；MC00 内物资数组索引 4–7 的原坐标通过落地高度与站立空间检查，未据此宣称逐房间碰撞或导航验收通过。

正式环境继续使用 `Apollo12LaptopPipeline`、350 m 室外阴影距离与服务端时钟连接；封闭近景通过运行时管线副本缩短阴影距离，视野开放恢复原值，详见视觉精修记录。Full v02 预览使用的固定 30 m 室内性能配置没有应用到整张正式地图。灯具、按钮和绑定随环境资产接入，但操作适配器仍只有本地预览版：**尚未接入正式联网开关交互**，不能把可见按钮视为所有玩家已能同步操作。

正式入口与检查边界见 [Moon 正式联机地图](MoonNetworkMap.md)，本轮画面和反馈调整见 [视觉质感精修](VisualPolish.md)。

## 导出与导入入口

```powershell
& D:/Blender51/blender.exe --background --disable-autoexec E:/Download/MC00_Game_Export.blend --python Tools/Models/export_mc00_game.py -- SourceData/Models/MC00GameExport
```

脚本只修改当前 Blender 后台进程的内存，不保存 `.blend`。分开导出 `MC00_Render.fbx` 与 `MC00_Collision.fbx`，保留相同根变换；排除 32 个休眠旧件，保留 118 个 Empty 灯锚点和运动父链。FONT/CURVE 经 FBX 导出器求值成几何，不能只选 Mesh 而漏掉文字或管线。

关键选项：Selected Objects、Scale 1、Apply Unit、FBX Units Scale、-Z Forward / Y Up；Apply Transform 关闭、Apply Modifiers 开启、Normals Only、Bake Animation 关闭。**Triangulate Faces 明确关闭**，避免导出器三角化破坏共享网格。最终三角形由 Unity 导入器生成，不能据此假设与 Blender 的每个三角形对角线都相同。

Tangent Space 和 Custom Properties 也关闭：切线由 Unity 根据最终 UV0 计算；需要的元数据通过 JSON 传递，不依赖 FBX 自定义属性，因此无需修改作者的 `contact_normal` 整数向量。作者随后提供的 Blender 4.3.2 完整 FBX 往返结论与本方案一致，但不代替本地 Blender 5.1 / Unity 检查。

Unity 菜单：**Tools → Moon Environment → MC00 Game Export**。

1. **Import exported assets**：导入贴图、创建显式 URP 材质、导入两份 FBX。仅处理本候选目录；重复运行会刷新本候选材质和导入设置。
2. **Create separate map candidate**：生成模型 Prefab、复制现有 Apollo12 环境并只替换其中 MC00，另存行走预览场景。已有候选时拒绝覆盖。
3. **Optimize candidate CPU submission**：在候选生成后执行一次，生成固定建筑局部渲染组，并将严格符合长方体的实体碰撞换为 BoxCollider。已有渲染组时拒绝叠加；换版先生成干净的新候选，不沿用旧版合并网格。
4. **Create separate light switch trial**：另存本地单灯/批量面板试点，不覆盖性能候选或正式地图。操作、v2 映射和验证边界见 [灯光按钮说明](MC00LightSwitches.md)。

最初候选场景：`Assets/MoonEnvironment/Variants/MC00GameExport/Scenes/MC00GameExportPreview.unity`，候选环境 Prefab 为同目录下 `Prefabs/Apollo12MC00Candidate.prefab`。2026-10-11 接入时正式主菜单仍使用原环境；当前正式引用以本文顶部的 Full v02 记录为准。

## 材质与 UV

- 解出全部 44 张打包图片；以文件内容哈希区分同名图片，不使用云端绝对路径。
- 材质按原节点的实际图片引用绑定到 URP Lit；ORM 转换为 URP 使用的 Metallic / Occlusion / Smoothness 通道，法线按 Normal Map 导入，数据贴图关闭 sRGB。
- 两块软垫 `MC105_REST_back_pad`、`MC105_REST_fitted_seat_cushion` **仅在导出内存副本**中把 `MC106_Portable_BakeUV` 调到 UV0，原 UV 保留为第二通道。材质统一使用 UV0，Unity 按 UV0 重新计算 MikkTSpace 切线。没有全模型 Swap UVs，也没有生成覆盖第二通道的 Lightmap UV。
- Blender 透射玻璃在 URP Lit 中采用明确的透明近似，并非完整还原 Blender 折射。材质使用共享 `.mat`，勾选 GPU Instancing 只是许可，不等同于运行时一定走实例化。
- 导出器可能对同一共享网格的重复材质引用给出索引警告；保留 `fbx-export.log`，以 Unity 实际每对象材质槽和三角形检查判断，不忽略真实不匹配。
- Unity 会重排子网格并省略未使用的材质槽，不能将 Blender 材质槽数组按原序覆盖回去。本地按每种材质实际承载的三角形数量校验，再保留 Unity 的子网格与材质对应关系。

## 碰撞与灯位

277 个建筑代理导入为静态、非 Convex 的 MeshCollider，Renderer 关闭且不投影。代理 Mesh 保持 CPU 可读，供运行时导航收集几何。关闭 FBX 自动生成碰撞。

按 `MC00_COLLISION_REPLACEMENT_COVERAGE.json` 排除已完整覆盖的可见网格碰撞；仅给未覆盖且作者标记 `collision_solid` 的几何保留 MeshCollider。发现部分覆盖时脚本拒绝继续，不能整对象删除碰撞。作者记录的 8 个身份未明确条目没有被假定为已覆盖，门、家具和工具等也不并入静态建筑代理。

118 个锚点按作者声明的前向、上向转换到 Unity。能按名称对应旧 MC00 的灯沿用本地校准参数；新增灯采用有限强度的候选配置，原始瓦数不直接当 Unity 强度。通过原有 `LocalLightBudget` 统一管理，不恢复按相机距离选灯。感应灯玩法、门抽屉驱动与网络交互不在这次资产接入中实现。

作者已确认 FBX 不保留半程滑轨驱动和滑门限位。抽屉父子关系仍在、工具随抽屉移动，并不意味着完整机构联动已经接入；不能把这一状态当作玩法验收通过。

## 首次导入的验证边界（2026-10-11）

本轮针对导入风险检查几何数、三角形数、共享网格、材质槽、特殊 UV/切线、坐标与碰撞排除关系。没有自动进入 Play、进行长时间性能采样或宣称达到 4060 Laptop 60 FPS。

本地旧 MC00 为 9,443 个 Renderer、863,210 个三角形；本次作者文件默认可见几何为 9,108 个、948,678 个三角形。两者内容版本不同，不能将帧率变化全部归因于组织优化。LOD 和细节烘焙尚未提供；交互行程、实际人物通过与 NavMesh 仍需运行验收。

Unity 原始导入：9,108 个 Renderer、7,604 个不同 Mesh、948,198 个三角形。相比 Blender 少的 480 个三角形分别对应 14 件仪表/手套网格的退化面数量；没有缺失对象。三角形数量不一致不能一概解释为漏面，也不能未经核对就忽略。

首次候选 Prefab 接入结果：精确网格复用后为 **3,843 个 Mesh**，5,133 个可见网格参与投影；277 个代理加 4,251 个未覆盖实体碰撞。118 盏灯中 116 盏沿用旧参数，基地受管灯总数变为 247。当时原正式地图仍为 245 盏，不受该候选影响。

最终静态检查通过：每种材质承载的三角形数量匹配（允许已核对的退化面剔除）；无缺失对象、材质或脚本；已覆盖区域没有重复可见网格碰撞，未覆盖标记实体没有漏配碰撞；代理 Renderer 均关闭、非 Convex。模型与代理根位置完全一致，9,108 个物件相对作者世界位置的最大误差约 **0.000122 m**。两块软垫均保留两套 UV 并具备切线。实际外观、法线细节和运行性能未据此宣称通过。

结果摘要保存在 `SourceData/Models/MC00GameExport/UnityIntegrationReport.json`，接入脚本为 `Assets/MoonEnvironment/Editor/MC00GameExportTools.cs`。

## CPU 提交整理（2026-10-11）

接入报告记录的是初始状态；后续整理结果另存为 `SourceData/Models/MC00GameExport/CpuOptimizationReport.json`。

| MC00 指标 | 整理前 | 整理后 |
| --- | ---: | ---: |
| 启用的 Renderer | 9,108 | 8,096 |
| 启用的投影 Renderer | 5,133 | 4,141 |
| 渲染三角形 | 948,198 | 948,198 |
| MeshCollider | 4,528 | 4,400 |
| BoxCollider | 0 | 128 |

1,355 个作者明确标记为固定建筑的 Renderer 被归入 343 个预生成渲染组。按作者空间分区、材质、阴影/探针设置及 4 m 网格分桶，每组包围盒最长边不超过 8 m、最多 64 件、16,000 顶点。不跨作者分区合并，不改灯光，不增加按距离开关；门、抽屉、工具和未分类物件保持独立。组变大可能降低细粒度剔除效率，因此这些数量不是 Draw Call 或性能提升的保证。

原物件、Mesh 引用和碰撞保留，仅关闭参与合并的原 Renderer。`BuildingRenderClusters` 无每帧扫描：组件关闭时恢复原 Renderer，开启时使用预生成网格。**拆卸、移动、隐藏、销毁或单独换材质前，交互系统必须先调用 `ReleaseFor(sourceRenderer)`**，它会永久释放当前会话中的所属小组并恢复原 Renderer；之后再执行单件操作。尚未自动接入拆卸玩法。禁用该组件可比较渲染组织，但不会恢复碰撞类型。

碰撞转换仅接受几何验证为闭合长方体、无剪切变换、无 Rigidbody 的非 Convex 实体网格；保留位置、尺寸、层和物理材质。277 个建筑代理保持不变。碰撞体总数仍为 4,528，收益来自其中 128 个使用基础形状查询；没有扩大碰撞包围盒来封住门洞。

正常照明模式的 `LocalLightBudget` 不再每帧遍历全部灯；只在初始化、通电状态改变或退出实验距离预算时应用状态。玩法继续使用 `SetPowered`，不可直接竞争写入 Light 的强度或 enabled。

优化前候选备份：`Library/BuildValidation/BeforeMC00Cpu-20261011-094523/MC00GameExport.prefab`。还原此文件到原 Prefab 路径即可恢复该早期候选的渲染和碰撞配置，保留原 `.meta`；Library 备份不代替版本控制。当次未覆盖原 FBX、作者 `.blend` 或正式地图；该备份不是当前 Full v02 正式环境的回滚来源。

验证范围为编译、保存数据及恢复/通电状态检查，不包含运行画面、碰撞手感或新的 FPS 采样。用户只需在当前候选场景沿原先慢点走一遍，重点看朝向 MC00、室内转头、门口和楼梯；目标仍为 4060 Laptop 的 1440p 稳定 60 FPS，尚未确认达成。
