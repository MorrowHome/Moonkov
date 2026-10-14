# Theophilus 坡肩外楼首批接入

2026-10-14，已将现有 EB08 EVA 服务、EB03 接收隔离、EB06 冷样本档案三栋建筑实际落位，保存到 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。场景已重开，编辑态干净、未处于 Play、未在编译。

后续第二组 EB01、EB02、EB04 已实际落位，六栋当前总清单与第二组检查见 `Docs/AI/Theophilus_Outer_Buildings_Batch02.md`。以下首批数据保留为其原始验证范围。

## 本地来源与范围

实际来源为 `Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Prefabs/Apollo12MC00Candidate.prefab` 中 `Base 3 / modular FBX` 的独立建筑根，底层模型为项目已有 `Assets/FBX/Map/Moonkov_Expanded_Base_Latest (3).fbx`。本地主要完整外楼为 EB01–EB08、PH01、RB00，共十栋；另有 U06–U08 小型设备模块。首批不依赖云端补导出。

三栋复制保留原建筑根下的完整层级，含屋顶、围护、楼面、室内房间、门框、原退回门叶、`Entry_Route` 锚及 `EntryRamp`。原始 mesh/collider 分别为 EB08 292/292、EB03 344/344、EB06 629/629；没有再向原网格叠加碰撞体。每栋五盏源灯保留，十五盏灯保持独立，没有注册进 MC00 的 118 灯控制系统。该源建筑未带 MC00 同类灯控脚本，静态退回门叶也不是新接入的动态门；本轮不宣称它们拥有独立可操作门控。

## 实际位置与地面连接

下表为标准 Unity Y-up 包装根世界坐标，缩放 1。根下保留源 FBX 轴向和导入缩放；表中的 yaw 是包装根绕 Y 旋转，192.852° 等价于 -167.148°。

| 建筑 | 根 XYZ / m | yaw / ° | 室内楼面 Y / m | 最长地形支柱 / m | 新连接坡长 / m | 最大纵坡 / ° |
| --- | --- | --- | --- | --- | --- | --- |
| EB08 EVA 服务 | 1615, 215.2827, 2240 | 192.8522 | 215.8827 | 1.8081 | 8.1526 | 13.5549 |
| EB03 接收隔离 | 1655, 216.5058, 2270 | -146.4091 | 217.1058 | 1.7803 | 8.4994 | 15.2938 |
| EB06 冷样本档案 | 1665, 214.1177, 2220 | -126.9271 | 214.7177 | 3.3153 | 10.6842 | 19.0706 |

按实际原生 Terrain 高度选择 MC00 后侧相对平缓坡肩，再根据各楼完整包络的高度采样设置楼根高度。每栋增加八根独立可移除支柱，支柱下端埋入原地形 0.10 m，上端接到楼板底。没有平整或盖章修改 TerrainData，没有复制 PH01 的旧庭院铺地，也没有重造建筑。

每栋新增一条约 3.1 m 宽的连接坡，从已有入口坡实际网格外侧坡脚延伸到原生地面。连接坡按中心和两边地形高度分段适配，末端贴地留约 0.025 m 间隙；不将地形改成平面。既有入口坡、门与入口锚保持在原建筑内。

专属新增资产位于 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/BuildingAdditions/`。场景根为 `THEOPHILUS_OUTER_BUILDINGS_BATCH01`，其下三个已连接 Prefab 实例。移除该根即可回退本批落位；本批前场景备份保存在 `LocalData/AI/AssetHandoffs/TheophilusShoulder/BeforeBuildings.unity`，不要在存在后续编辑时覆盖回退。

## 最小必要检查

三处新入口各做一次编辑态原生 `CharacterController.Move` 碰撞探针检查，胶囊从正式 `DollSingerNetworkPlayer.prefab` 复制：高度 1.7 m、半径 0.28 m、Step Offset 0.25 m、Slope Limit 45°。检查路径仅包含原生地面、新连接坡、已有入口坡、门框和门内三米，三处均到达室内楼面，实际水平移动约 17.10 / 17.45 / 19.60 m。探针对象检查后移除，重开无缺失脚本；这是入口碰撞证据，不是正式玩家手动 Play 体验验收。

新增建筑与连接坡包络之间，以及与原 MC00 / 已走通气闸结构之间未相交。对已记录 A 路线 341 点检查新增非触发碰撞体的水平 AABB 距离，最小净距 66.9254 m。原地形高度文件未改，MC00 原 135 个灯控按钮、单个气闸链保持不变。本轮没有重复旧整段回程，也没有截图。

保存后重开确认三栋源网格/碰撞计数、各五盏灯、零缺失脚本，场景 `dirty=false`、`playing=false`、`compiling=false`。656 个原有保护文件哈希无变化，包含正式场景、旧副本、正式角色、源建筑以及本次原有 Theophilus 专属资产。Console 小范围读取仍记录一条此前回程报告中已出现的 Package Manager 窗口错误：`The "path" argument must be of type string. Received undefined`；不写零错误结论。

辅助脚本及原始数据位于持久目录 `LocalData/AI/AssetHandoffs/TheophilusShoulder/`，汇总验证见 `Docs/AI/Theophilus_Outer_Buildings_Batch01_Validation.json`。没有新增 Assets C# 运行时代码、包安装、正式场景修改、提交或推送。

## 尚未完成

- EB01 加工、EB02 仓储、EB04 货运已在第二组完成落位及入口检查，详见第二组报告；不重复首批检查。
- EB05 控制、EB07 水电、PH01 工艺厅、RB00 科研楼及 U06–U08 尚未接入本场景；不是缺少本地建筑源。PH01 后续接入需排除旧庭院铺地，不能把整根庭院当坡肩地基。
- 新楼的动态门、灯控交互、多人逻辑、性能、主观画面与声音没有扩大验收。用户明确不需要截图验证，本批未采集图像。
