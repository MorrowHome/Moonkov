# 建筑群换版与性能优化流程

适用项目：FPS_Template。当前候选地图：`Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/MoonBaseApollo12.unity`。

正式对局现使用同目录 `Apollo12GameScene.unity`，从 `MainMenu` 进入；两者引用同一个 `Apollo12Base.prefab`。Prefab 中的建筑优化会同步生效，预览场景实例上单独做的覆盖不会自动迁移。正式出生点、物资点、撤离点为场景专属配置，换版后若布局变动需跟着调整，见 `Docs/MoonNetworkMap.md`。

目标：**RTX 4060 Laptop、2560×1440 输出、稳定 60 FPS**。当前 Laptop 管线使用 75% 渲染比例和 STP，即 1920×1080 内部渲染。尚未完成目标笔记本的独立构建验收。

2026-10-11 收到的 `MC00_Game_Export.blend` 已另存为独立候选，未替换正式入口。其外观/碰撞分离导出、材质与 UV 适配、碰撞覆盖和灯位接入流程见 [MC00 游戏导出版接入](MC00GameExportIntegration.md)。不要直接将这份带独立碰撞代理的导出当作旧版 FBX 全自动添加碰撞。

## 已有脚本与菜单

菜单位于 **Tools → Moon Environment → Buildings**。退出 Play，在 Hierarchy 或 Prefab Mode 中选中一个建筑根节点；不能直接选 Project 面板里的 FBX 文件。

| 菜单 | 用途 |
| --- | --- |
| Prepare Selected Building | 新建筑常用入口：禁用导入模型自带的相机和方向光、转换并校准导入灯、刷新灯列表、复用重复网格、简化装饰零件投影 |
| Reuse Identical Meshes in Selection | 只对所选节点及子节点做精确网格复用，不要求叫 MC00_ROOT |
| Optimize Decorative Shadows in Selection | 仅关闭导入建筑小装饰件与明确命名的细窄装饰条投影，保留可见模型和碰撞；支持 Undo |
| Restore Imported Mesh References in Selection | 将 MeshFilter 恢复为当前 FBX 中对应原始节点的网格，供原地更新模型后重新优化；也会重置所选范围内手工换网格的覆盖 |

操作支持 **Undo**，脚本不主动保存；Prefab Mode 开启 Auto Save 时由 Unity 自动保存。需要先审阅时关闭 Auto Save。若处理抛出异常，菜单会撤销这一组修改。

重复执行不会把已经转换的 Spot 再乘一次亮度系数。校准以原 FBX 的强度 × 0.02、限制到 2～24 为上限，只压低超限灯，保留手工调暗结果；同时统一为 Low 软阴影。向下照射的转换面积灯会根据地板高度收紧固定范围，内锥角上限为 80°。手工设置的范围、强度和阴影可能被这一步调整，需按美术意图审阅。灯光预算的灯数、距离、渐变时间和相机设置保留；有父级预算时刷新该拥有者的完整列表，禁止父、子预算重叠管理同一组灯。

模型自带的相机和方向光按展示用途处理；没有 FBX 来源的手动添加相机和方向光不受影响。若模型内的摄像机准备用于玩法，需要处理后明确接入。不要选玩家根节点或整个场景来执行建筑准备。

## 推荐：新版 FBX 单独导入

1. 保存当前工作，保留上一版候选场景/环境 Prefab。模型与地图分别留版本，例如 `(3)` 与 `(4)`，避免直接覆盖旧版。
2. 导入新 FBX，检查比例、原点和材质；Model Importer 中关闭 **Import Cameras**，防止展示相机拖慢帧率。
3. 在新候选环境 Prefab 中替换建筑实例，保留地形、放置位置、独立太阳与玩家。使用新导入的模型实例，不沿用上一版建筑的网格覆盖。比较时只启用一套建筑，避免几何、灯光和碰撞重叠。
4. 若旧 LocalLightBudget 位于保留的父节点，保留它；若随旧建筑移除，工具会在新建筑上创建默认预算。不要重复挂载。
5. 选中新建筑根节点，执行 **Prepare Selected Building**。只需处理某个模块时，可选择该模块执行单独的网格复用菜单。
6. 检查展示地板等非游戏内容，按需在候选实例禁用；工具不自动删除任意展示几何。
7. 保存场景/Prefab。确认 `MoonSceneLighting.pipeline` 仍引用 `Apollo12LaptopPipeline.asset`，再按下文验收。

菜单不重建 DEM、不修改原 FBX 几何、不自动搬移建筑，也不迁移玩法、交互和网络组件。新版的门、交互点、碰撞通路等仍需按实际版本接入。

## 必须原地覆盖 FBX 时

**不能仅在覆盖后重跑去重就认为完成更新。** 网格复用留下的 MeshFilter 覆盖可能仍指向旧的规范网格；新版若让两个原本相同的零件变得不同，旧引用会掩盖这一变化。

1. 更新前保存模型、场景和 Prefab 版本，保留恢复依据。
2. Reimport 完成后，在候选环境 Prefab Mode 中选中建筑根节点。
3. 执行 **Restore Imported Mesh References in Selection**，检查恢复后的新版几何。
4. 重新执行 **Prepare Selected Building**，再保存和验证。

恢复命令读取当前 FBX 的原始对应节点，不是恢复旧文件快照。它会重置所选范围内手工 MeshFilter 换网格的覆盖；自定义部分应排除在选择范围外，或恢复后重新应用。它不重置 Transform、材质、灯光或碰撞覆盖。

新版若改名、删除节点，导致 FBX 对象对应关系丢失，恢复命令无法可靠修复。此时采用“新版 FBX + 新实例”流程，并检查碰撞与其它覆盖，不要继续叠加修补。

## 网格复用的边界

以下描述针对通用“网格复用”菜单。MC00 游戏导出版另有固定建筑局部合并流程，见 [MC00 接入文档](MC00GameExportIntegration.md#cpu-提交整理2026-10-11)：先执行候选创建，再执行 **3 - Optimize candidate CPU submission**。它使用作者分类和空间分区，不能直接套用到没有这些元数据的任意 FBX。新版本应重建候选并重新生成渲染组。

该候选启用的 Renderer 从 9,108 降为 8,096，投影 Renderer 从 5,133 降为 4,141，三角形不变；128 个严格长方体 MeshCollider 换成 BoxCollider。合并组内原物件仍在，但单件拆卸或改变可见状态前必须调用 `BuildingRenderClusters.ReleaseFor`。这些属于提交组织和碰撞类型变化，不代表已经测得帧率收益。

工具逐字节比较顶点、索引，并比较顶点布局、拓扑、子网格和包围盒。只有完全一致的静态网格才复用；UV、法线、切线等不同的网格不会被视为相同。带 BlendShape 或 bindposes 的网格跳过。

- GameObject、Transform、Renderer、材质和碰撞引用均保留，不合并层级或删除零件。
- 同一版本重复执行，没有新的重复引用时，修改数应为 0。
- 零件仍可独立启用、隐藏、移动、拆卸。运行时修改某个零件的顶点前，需要为它创建独占 Mesh；直接修改共享 `sharedMesh` 会影响其它复用者。
- 工具不做减面、LOD、碰撞简化，也不保证自动达到 60 FPS。重复网格少的版本可能获益较小。

## 需要保留的运行配置

| 项目 | 当前策略与注意事项 |
| --- | --- |
| LocalLightBudget | 默认关闭 `Limit Lights By Distance`：通电灯保持原亮度和阴影，由 URP 处理光照影响范围；增删灯后刷新列表。旧的距离限灯仅留作手动 A/B，不用于正常游玩 |
| 玩法开关 | 调用 `SetPowered(light, on)` 或 `SetPoweredBatch`；预算管理 `Light.enabled/intensity`，不要竞争写入。[独立按钮试点](MC00LightSwitches.md)已接本地单灯/批量控制，尚未接正式地图或网络同步 |
| Laptop 管线 | STP、75% Render Scale、4096 主阴影、350 m 阴影距离；室内 Low 软阴影档 256，附加灯阴影图集 4096；建筑菜单不会重新设定管线画质 |
| GPU 绘制 | 2026-10-23 当前所有 URP 资产的 GPU Resident Drawer 均已关闭；若重新启用，需要重新检查自定义 shader、MaterialPropertyBlock 与 BRG 变体设置 |
| BRG 变体 | Graphics Settings 使用 Strip if Entities Graphics Package is not installed；当前没有 Entities Graphics，裁掉未使用的 DOTS 实例化变体。重新启用 GPU Resident Drawer 或自定义 BRG 时，须恢复 Keep All |

## 灯光优化的取舍

已撤回“按玩家距离选最近 20 盏，再做渐变”的默认策略。它不知道门窗、房间和可见墙面，可能让前方可见区域等玩家走近才亮；延长渐变只会把突然开灯变成缓慢开灯。正常模式不限制为 24 盏，不自动改变照明半径或删除阴影。

当前建筑投影物由 18,189 减少到 10,811；本轮在前一轮基础上又关闭 3,502 个非结构零件的投影，所有零件仍可见、接收阴影并可独立拆卸。撤回 128 硬阴影方案，改为 256 Low 软阴影、4096 附加灯阴影图集。取舍是小装饰件不再产生细节投影，软阴影及更大图集本身也有成本；尚无本轮优化后 FPS 数据。

本轮校准 245 盏灯，其中 29 盏强度被压低，最大强度为 24，修正了 6 盏原生 Spot 仍保留 550/1100 导入强度的问题；57 盏灯按实际地板高度收紧固定范围，最小为 8 m。这是编辑阶段的固定照射范围，不会随玩家距离开关。关闭仅根据太阳可见性判断的自动曝光，避免已亮起的室内仍获得最高 +4 EV（16 倍）增益；采用固定曝光和少量环境补光 `(0.025, 0.03, 0.04)`。补光是美术近似，不是烘焙或实时 GI，关掉灯后仍有这一基础亮度。

`BuildingShadowTools` 仅处理 FBX 来源的 MeshRenderer：世界包围盒最长边不超过 8 m、最长边 × 次长边不超过 2 m²，且名称不含墙、门、地板等结构标识的装饰件；或明确命名为面板接缝、踏步边条等装饰、最长不超过 8 m 且另外两个尺寸不超过 0.15 m 的细条。ShadowOnly 辅助遮挡物不处理。这是命名/尺寸规则，不是自动识别房间；新版若有特殊命名或微型结构，应按美术意图保留投影。带结构名称的墙板、门板、地板等受到保护。

2026-10-11 本轮修正前的候选 Prefab 和管线备份位于 `Library/BuildValidation/BeforeLightingCorrection-20261011-061210/`；更早的投影简化前备份为 `Library/BuildValidation/BeforeShadowSimplification-20261011-054543/`。原 FBX 和旧地图未修改。Library 备份不替代版本控制。

后续优化按证据选择：

- 不需要开关、不会拆改的环境照明可烘焙。需要彻底关灯的灯不能直接烘焙其贡献；Mixed 的烘焙间接光也不会随实时关灯消失。
- 可交互灯保留实时直接光。阴影分辨率、投影物数量、灯光覆盖重叠应单独测量。不能为省阴影直接让光穿墙；无阴影补光需限制在本房间或使用经过验证的遮挡方案。
- 室内分区应依据房间、门窗连通和可见照明表面；灯具本体不在相机视野中也可能照亮可见地面，不能只判断灯具中心是否可见。
- 建筑侧继续检查 MC00 的绘制提交、遮挡剔除、LOD 和投影几何。当前 GPU Resident Drawer/网格复用仍保留。

要判断旧限灯是否真的有收益，在同一位置、朝向、分辨率下，手动切换组件的 `Limit Lights By Distance`；切换后等过渡结束再采样，并分别保存报告。此 A/B 同时改变照明与阴影数量，只能衡量旧策略总成本，不能单独归因于阴影。当前正常模式恢复完整照明后的性能尚未验收。

已知回退：用户在恢复完整照明后报告常态 40 多 FPS、低时 20 多 FPS；这说明旧预算具有明显收益，但现存旧报告不能拆分光照与阴影成本。不要直接把所有室内灯永久改为无阴影。

以下对比工具仅供按需定位，不是换版或本轮优化的必做步骤。

定位该回退时，在 Play 中站在 MC00 慢点，执行 **Tools → Moon Environment → Apollo 12 → Compare Indoor Lighting Cost (15 seconds in Play)**，回到 Game 保持位置和朝向，不操作灯开关。工具顺序测完整照明、仅关闭受管室内灯阴影、关闭受管室内灯；每段等待 2 秒、采样 3 秒。结束、退出 Play、暂停或重编译时恢复灯和管理组件，临时穿墙/变暗只是诊断效果。工具不自动进入 Play，不保存场景，不改太阳和手电。

报告为 `Library/BuildValidation/Apollo12LightingCost-时间戳.json`，保留历史报告。先看 `comparablePose`：移动超过 0.25 m 或转动超过 2 度的比较不可直接归因。完整照明与无室内阴影的差值包含阴影生成和采样；无阴影与无室内灯之间的差值是剩余局部光照成本的线索。各阶段均慢则继续定位建筑/其它系统。录制期间不要同时运行其它采样工具；Editor 帧率上限、等待和场景动态仍可能干扰对比。

参考：[Unity 灯光模式](https://docs.unity3d.com/6000.0/Documentation/Manual/LightModes-introduction.html)、[URP 灯光限制与渲染路径](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/lighting/light-limits-in-urp.html)。

## 每次换版的最小验证

1. **外观与功能**：检查门口、楼梯和室内转头；确认无缺件、闪烁、碰撞变化、明显灯光跳变。测试关灯后离开再返回。
2. **性能**：固定 2560×1440、75% STP，同一地点、朝向与路线：室外朝向 MC00 → 进入建筑 → 室内转头。先遍历一次，避免把首次 shader 编译当作稳定性能。
3. Play 中运行 `Tools → Moon Environment → Apollo 12 → Capture Frame Times (10 seconds in Play)`，回到 Game 行走 10 秒。同一结论不反复采样；有回退或新问题才扩大范围。
4. 报告为 `Library/BuildValidation/Apollo12Performance.json`，下次会覆盖；对比前先复制为版本明确的文件。关注 P95/P99、超过 16.67 ms 的帧比例、GPU P95/P99、慢帧位置和绘制次数，以及 `gpuOcclusionActive`。中位 FPS 超过 60 不等于稳定 60。
5. 最终在实际 4060 Laptop 上，记录供电/性能模式和分辨率，验证独立构建。4070 SUPER 的 Editor 数据只作开发参考。

Main Thread 包含等待；GPU recorder 可能与墙钟帧时间错开；Editor 绘制计数可能包含其它视图。它们是定位线索，不能单独代替完整 profiler 分析。

## 维护入口与已知基线

| 文件 | 职责 |
| --- | --- |
| `Assets/MoonEnvironment/Editor/BuildingOptimizationMenus.cs` | 所选建筑准备、复用、恢复菜单及 Undo |
| `Assets/MoonEnvironment/Editor/MC00MeshReuseTools.cs` | 通用 `OptimizeSubtree`；旧 `OptimizeHierarchy` 包装仍要求一个 MC00_ROOT |
| `Assets/MoonEnvironment/Editor/MC00CpuOptimizationTools.cs` | 游戏导出版候选的有界建筑渲染合并与严格长方体碰撞替换 |
| `Assets/MoonEnvironment/Runtime/BuildingRenderClusters.cs` | 无每帧扫描的合并表示管理；拆卸前释放所属渲染组 |
| `Assets/MoonEnvironment/Editor/BuildingShadowTools.cs` | 可复用的装饰零件投影简化，不改变模型、碰撞和拆卸结构 |
| `Assets/MoonEnvironment/Editor/BuildingLightingTools.cs` | 校准导入灯强度上限、软阴影及按安装高度计算的固定照射范围 |
| `Assets/MoonEnvironment/Editor/MoonBaseApollo12Tools.cs` | 独立地图创建、灯光和 Laptop 管线配置 |
| `Assets/MoonEnvironment/Runtime/LocalLightBudget.cs` | 开关状态；默认完整照明，距离预算仅供对比 |
| `Assets/MoonEnvironment/Editor/Apollo12PerformanceCapture.cs` | 手动 10 秒采样，不自动启动 Play |

旧的 **Create Separate Base Preview** 仍绑定 `(3).fbx` 和 Apollo12 数据，且拒绝覆盖已有地图；它不是升级按钮。后续模型换版优先使用本文的选择菜单。

2026-10-11，模型 `(3)` 的 MC00：9,443 个 Renderer、863,210 个三角形；渲染使用的独立网格从 9,443 降至 3,360，零件和碰撞数量不变。这不是下一版必须达到的数量阈值。

GPU 绘制优化前，4070 SUPER 的一次 1440p / 75% STP 采样：中位 12.938 ms，P95 21.821 ms，P99 24.295 ms，20.479% 的帧超过 16.67 ms，GPU 中位 4.506 ms。此基线不代表优化后成绩或 4060 Laptop 验收结果。
