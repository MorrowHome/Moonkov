# Theophilus 正式游戏地图

2026-10-15，正式游戏加载入口 `GameManager.GameSceneName` 改为 `TheophilusGameScene`。从原 `MainMenu` 开始游戏，离线、Host、Client 和独立 Server 沿用既有共用场景加载流程。

正式场景：`Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`；出生子场景：同目录 `TheophilusSpawnPointsSubScene.unity`。两者均登记并启用在 Build Settings。原 Apollo12 地图和旧研究场景仅作为历史来源保留，不再维护研究副本。

2026-10-18 精细月面从 512×512 m 扩到 1536×1536 m（面积 9 倍）：原中心保持不动，周围新增八块 512 m、2049 高度采样的 Terrain，范围 X=1024–2560、Z=1408–2944。外围继续使用原 4096 m DEM。25 cm 是生成几何的网格间距，原始观测仍为 3 m；新增细小起伏和月坑为确定性合成，不是新增观测数据。新块使用现有 R8 月壤材质／TerrainLayer，在中心边界 32 m 范围衔接原地形，外边界平滑接回 DEM；原中心地形、基地接地和深井洞口资产不改写。

新增 6144 块岩石复用原 `MoonRockLod` 的两套原始／中距／远距共享网格与 18／80／600 m 配置，每块地形一个控制器；不添加每石头 Update。直径至少 0.65 m 的 3326 块岩石使用既有远距简化网格作静态碰撞，小碎石仅显示。新 Terrain 共用显示／碰撞数据；远景显示及碰撞分别复制到 `Terrain/Expanded` 后扩切洞口，原四份 R21 TerrainData 保留。Prefab 实例显式记录 TerrainData 覆盖，避免重开后恢复旧显示地形而发生重叠碎斑。

本次 Unity 编译及保存重开通过：12 条内部接缝逐高度采样最大差 0；外边界抽样差 0；八块新增地形各一条地面射线通过；八个 LOD 控制器的网格引用及距离与原配置一致。远景显示／碰撞各 589824 个格子的扩区切洞检查通过，扩区外 holes 不变；一次修正后的区域渲染确认无此前重叠碎斑。证据在 `Library/BuildValidation/LunarExpansion/`。Console 仍有既往已记录的 `SubScene.AddSceneEntities/OnEnable` 断言；未测 Play FPS、角色走图、Host／Client 或构建。编辑器入口为 `Tools > Moon Environment > Theophilus Expansion`，逐块生成后 Connect and Save；已完成的八块不会重复生成。

2026-10-16 恢复正式天空：此前场景 RenderSettings 与 `MoonSceneLighting.spaceSkybox` 都引用 `R8_StudySpaceSky`，其星图强度／白昼可见度为 0，太阳与地球显示亮度也为 0。现改为正式专属 `Variants/TheophilusBase/Materials/TheophilusSky.mat`，从此前调好的 `Apollo12Sky` 复制参数（星图 0.25、白昼可见度 0.5、亮星增强 3、弥散星场 0.3，并保留原太阳／地球配置）。原研究材质与 Apollo12 材质不改写，昼夜／天文方向继续由原运行时代码驱动。已保存两处场景引用，隔离天空渲染预览确认恒星与银河可见、Shader 无错误；未重进正式 Play。预览为 `Library/BuildValidation/TheophilusSkyRestore/SkyRestored.png`。

正式地图包含 Theophilus 地形、基地、八楼、气闸、巨构、R7 接地和 R8 表面/渲染配置。R9/R10 承托塔和中央下降塔的历史资产保留，中央旧显示按 R21 最终候选清单隐藏。气闸的 `AirlockPrototypeDemo` 与本地诊断读数移除，角色由正式 Ghost/玩家流程生成；不再生成本地验证角色。原 F/滚轮交互、气闸服务端规则及声音走现有正式系统。

R21 直接接入此正式场景：保留既有内容，增加 13 件 R20 机械构件与 6 件 R21 井体显示构件，禁用 10 个旧封盖 Renderer。正式近景、远景的显示与碰撞各使用一份独立 TerrainData（共四份），按交付 JSON 的 112 m、64 边形洞口同步裁切 holes；只有四角均在边界内的格子才开洞。近景格子 0.25 m、远景格子 2 m，边缘保留保守的栅格轮廓。原共享地形、DEM 与历史场景不改写。

构筑物现已补 1349 个静态非凸 MeshCollider，覆盖巨构基础、三座桥及接头、R6/R7/R9 和全部 38 件 R18–R21 构件；R3 原有 53 个简化 BoxCollider 保留，不叠加 MeshCollider；关闭的旧封盖不补碰撞。井底源面存在向下绕序，另用独立双面碰撞网格，显示网格不改。三处桥面、井底向下和井壁由内向外的定点射线通过，保存重开引用完整；未完成真实角色走图／AI 导航验收，未新增钻井玩法或灯光。地形 holes、原三桥、R7 位置和物资／撤离配置保留。记录见 `Docs/AI/Theophilus_Gameplay_Collision.json`。此前显示接入阶段的证据和限制仍保留于 `Docs/AI/Theophilus_R21_Delivery.md`。

最终候选的 38 件显示构件现已覆盖：9 件 R18 保留件、10 件 R19 保留件、13 件 R20 和 6 件 R21，均使用对应 R23 材质槽映射；三座原桥另行保留。R18/R19 补齐严格来自固定 R21 源中仍可渲染且未列入隐藏回退的 19 件，原清单对应的 40 个旧 Renderer 关闭，旧对象、碰撞与交互组件保留。此前 19 件 R20/R21 不重复导入。详见 `Docs/AI/Theophilus_R21_Retained_Delivery.md`。

R23 针对明显纹理重复使用 4 个独立 URP Lit 材质和 6 张本批贴图，38 件中 36 件包含 UV 更新。三种金属去除云斑 BaseColor／Normal，使用恒定底色与细微粗糙度；混凝土在线性空间混合 12% 原图和 88% 常量底色，法线强度 0.12、2 m 尺度。UV 按源循环索引和原三角化展开顺序映射成米制坐标。源资产、地形、洞口及既有对象 Transform 保留。保存重开与 Shader／引用检查通过，未截图或作最终画面验收。初始材质转换记录见 `Docs/AI/Theophilus_R23_Delivery.md`。

针对 Play 帧率问题，正式场景的 6000 块石头新增 `MoonRockLod`：18 m 内保留原网格，18–80 m 使用共享中精度网格，80–600 m 使用共享远精度网格，更远处关闭显示；边界包含石头包围球半径。每 0.25 s 按真实主相机位置更新，仅在层级改变时切换网格，不增加每石头脚本或 LOD 子物体。原碰撞、材质、变换和最高精度源网格不变，组件关闭时恢复原显示。两种原石头分别有 9958／5598 三角形，中精度为 1193／671，远精度为 199／111。固定 Scene 视角的石头几何量由 46654920 降到 527800（约 98.9%）；这是几何量对照，不是 Play FPS。验证记录见 `Docs/AI/Theophilus_RockLOD_Validation.json`。

MC00 新增本地 `InteriorVisibility`：室外关闭 4236 个当前启用的室内细节 Renderer 和 109 盏室内灯，靠近三个入口或进入室内区域时恢复；外壳、碰撞、气闸和联网状态保留。组件每 0.1 s 检查相机，仅区域切换时修改显示，具有退出缓冲。未拆分异步子场景、未卸载资产内存，也未做逐房间裁剪。21 个定点断言及同视角 Scene CPU 提交对照通过；Play 整帧 CPU／FPS 仍待实测。详见 `Docs/AI/Theophilus_Interior_Visibility.md`。

R24 中央关节精修已作为 14 件独立显示增量接入，按映射绑定 11 个材质（8 新增、3 复用），3 张内嵌贴图归档。仅关闭旧 `R20_02_Grouped_Actuator_Muscles_And_Heavy_Nodes` 的 Renderer；旧对象、碰撞及其余 R21／R23 内容保留。新显示没有 Collider，不依赖 R22，不改 Terrain 或玩法。保存重开和必要引用／Shader 检查通过；未作截图、Play 或性能验收。详见 `Docs/AI/Theophilus_R24_Delivery.md`。

R29 最终状态现已实际接入：34 件新增显示、19 件材质／UV 补丁、10 个材质、43 张贴图和 2 个专用显示 Shader。先通过修复入口的零应用 UV／精确 R13 别名内容预检，再导出和绑定；仅额外关闭旧 R21_04 Renderer，保留全部旧对象及碰撞。现有 UV0、光图 UV1、Terrain 主材质／贴图和提交 5b44f34f 的 MC00 烘焙、回路、探针与反射保持；R28 可选遮罩关闭。正式场景已保存，引用及 Shader 编译检查通过，未做 Play／截图／性能验收。用户已有场景和灰漆材质修改不混入 R29 提交。详见 `Docs/AI/Theophilus_R29_Delivery.md`。

地图包含一套现有 `GhostSpawnerManager`、`LeaderboardSpawner`，自动加载共享 `GameResourcesSubScene` 和新的 Theophilus 出生子场景。8 个出生点位于 A 起点南侧附近，使用实际地图地面定位并检查胶囊空间。原场景中 Apollo12 的出生坐标不会混入。

`MoonRaidMap` 复用原搜打撤规则，配置 12 个地面物资点、A 起点东南侧撤离区，保留 8 秒撤离和原局时/生存设置。原 DollSinger 敌人数量 3 保留；8 个异形遭遇位置迁移至 Theophilus 物资点附近，Profile/注册 Ghost 仍为现有正式资源。未添加新的网络协议、角色控制器或离线专用玩法。

2026-10-15 用户明确要求不再使用研究副本。后续地图、显示和渲染增量直接修改此正式场景，按完成的独立步骤做本地 commit 存档；不再先改研究场景或维持两套同步流程。旧资源目录中的 Mesh/材质仍可复用，无需因历史目录名称搬动或重复导入资产。

本次必要检查覆盖保存重开、正式加载常量、两个自动加载子场景、Build Settings、出生点空间以及正式资源/搜打撤入口。没有截图、重复走图或完整联机/性能测试；Host + 第二客户端、独立构建与实际画面尚未验收。客户端和服务器需使用包含此地图入口的同版工程或构建。

接入记录在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/FormalMap/`。正式地图制作前研究场景回退为 `BeforeFormalIntegration.unity`，原研究资产未重写。
