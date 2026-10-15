# Theophilus 正式游戏地图

2026-10-15，正式游戏加载入口 `GameManager.GameSceneName` 改为 `TheophilusGameScene`。从原 `MainMenu` 开始游戏，离线、Host、Client 和独立 Server 沿用既有共用场景加载流程。

正式场景：`Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`；出生子场景：同目录 `TheophilusSpawnPointsSubScene.unity`。两者均登记并启用在 Build Settings。原 Apollo12 地图和旧研究场景仅作为历史来源保留，不再维护研究副本。

正式地图包含 Theophilus 地形、基地、八楼、气闸、巨构、R7 接地和 R8 表面/渲染配置。R9/R10 承托塔和中央下降塔的历史资产保留，中央旧显示按 R21 最终候选清单隐藏。气闸的 `AirlockPrototypeDemo` 与本地诊断读数移除，角色由正式 Ghost/玩家流程生成；不再生成本地验证角色。原 F/滚轮交互、气闸服务端规则及声音走现有正式系统。

R21 直接接入此正式场景：保留既有内容，增加 13 件 R20 机械构件与 6 件 R21 井体显示构件，禁用 10 个旧封盖 Renderer。正式近景、远景的显示与碰撞各使用一份独立 TerrainData（共四份），按交付 JSON 的 112 m、64 边形洞口同步裁切 holes；只有四角均在边界内的格子才开洞。近景格子 0.25 m、远景格子 2 m，边缘保留保守的栅格轮廓。原共享地形、DEM 与历史场景不改写。

构筑物现已补 1349 个静态非凸 MeshCollider，覆盖巨构基础、三座桥及接头、R6/R7/R9 和全部 38 件 R18–R21 构件；R3 原有 53 个简化 BoxCollider 保留，不叠加 MeshCollider；关闭的旧封盖不补碰撞。井底源面存在向下绕序，另用独立双面碰撞网格，显示网格不改。三处桥面、井底向下和井壁由内向外的定点射线通过，保存重开引用完整；未完成真实角色走图／AI 导航验收，未新增钻井玩法或灯光。地形 holes、原三桥、R7 位置和物资／撤离配置保留。记录见 `Docs/AI/Theophilus_Gameplay_Collision.json`。此前显示接入阶段的证据和限制仍保留于 `Docs/AI/Theophilus_R21_Delivery.md`。

最终候选的 38 件显示构件现已覆盖：9 件 R18 保留件、10 件 R19 保留件、13 件 R20 和 6 件 R21，均使用对应 R23 材质槽映射；三座原桥另行保留。R18/R19 补齐严格来自固定 R21 源中仍可渲染且未列入隐藏回退的 19 件，原清单对应的 40 个旧 Renderer 关闭，旧对象、碰撞与交互组件保留。此前 19 件 R20/R21 不重复导入。详见 `Docs/AI/Theophilus_R21_Retained_Delivery.md`。

R23 针对明显纹理重复使用 4 个独立 URP Lit 材质和 6 张本批贴图，38 件中 36 件包含 UV 更新。三种金属去除云斑 BaseColor／Normal，使用恒定底色与细微粗糙度；混凝土在线性空间混合 12% 原图和 88% 常量底色，法线强度 0.12、2 m 尺度。UV 按源循环索引和原三角化展开顺序映射成米制坐标。源资产、地形、洞口及既有对象 Transform 保留。保存重开与 Shader／引用检查通过，未截图或作最终画面验收。初始材质转换记录见 `Docs/AI/Theophilus_R23_Delivery.md`。

针对 Play 帧率问题，正式场景的 6000 块石头新增 `MoonRockLod`：18 m 内保留原网格，18–80 m 使用共享中精度网格，80–600 m 使用共享远精度网格，更远处关闭显示；边界包含石头包围球半径。每 0.25 s 按真实主相机位置更新，仅在层级改变时切换网格，不增加每石头脚本或 LOD 子物体。原碰撞、材质、变换和最高精度源网格不变，组件关闭时恢复原显示。两种原石头分别有 9958／5598 三角形，中精度为 1193／671，远精度为 199／111。固定 Scene 视角的石头几何量由 46654920 降到 527800（约 98.9%）；这是几何量对照，不是 Play FPS。验证记录见 `Docs/AI/Theophilus_RockLOD_Validation.json`。

地图包含一套现有 `GhostSpawnerManager`、`LeaderboardSpawner`，自动加载共享 `GameResourcesSubScene` 和新的 Theophilus 出生子场景。8 个出生点位于 A 起点南侧附近，使用实际地图地面定位并检查胶囊空间。原场景中 Apollo12 的出生坐标不会混入。

`MoonRaidMap` 复用原搜打撤规则，配置 12 个地面物资点、A 起点东南侧撤离区，保留 8 秒撤离和原局时/生存设置。原 DollSinger 敌人数量 3 保留；8 个异形遭遇位置迁移至 Theophilus 物资点附近，Profile/注册 Ghost 仍为现有正式资源。未添加新的网络协议、角色控制器或离线专用玩法。

2026-10-15 用户明确要求不再使用研究副本。后续地图、显示和渲染增量直接修改此正式场景，按完成的独立步骤做本地 commit 存档；不再先改研究场景或维持两套同步流程。旧资源目录中的 Mesh/材质仍可复用，无需因历史目录名称搬动或重复导入资产。

本次必要检查覆盖保存重开、正式加载常量、两个自动加载子场景、Build Settings、出生点空间以及正式资源/搜打撤入口。没有截图、重复走图或完整联机/性能测试；Host + 第二客户端、独立构建与实际画面尚未验收。客户端和服务器需使用包含此地图入口的同版工程或构建。

接入记录在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/FormalMap/`。正式地图制作前研究场景回退为 `BeforeFormalIntegration.unity`，原研究资产未重写。
