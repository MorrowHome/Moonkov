# Bounty Clash 据点地图接入

2026-10-21。地图阶段实现与勘察记录，含单 Editor 接地近景复核。用户后续取消多开联机验收；本文不以局部地图证据宣称完整对局已经验证。

Bounty Clash 与原搜打撤共用正式 `TheophilusGameScene`，不复制或保存另一份正式场景。`MoonRaidMap` 在运行时给该场景的地图对象装配 `BountyClashArenaSettings`；原序列化物资、敌人、600 秒局时和单撤离配置保留。共享服务端读取 `Definition` 的独立复制，地图不持有队伍、赏金归属、计时或奖励裁决。

## 实测选点

据点是 MC00 一层及周围已有掩体与外楼，不是只有视觉块的示意场。既有正式角色从入口到 L22 和外围外楼的通行记录可见 `Docs/Planning/Missions/Role-Validation-20261019.md`、`Docs/AI/Theophilus_Outer_Buildings_Actual_Player_Validation.json`。本轮临时 Additive 打开正式地图，复用现有服务端导航构建器，采集 1668 个支撑、胶囊净空和完整路线均成立的候选点，再筛出以下最终点。没有修改 Terrain、Collider、建筑、正式场景、Prefs 或账户。

- XZ 圆边界中心 `(1620, 211.6, 2168)`，半径 `130 m`。
- 死亡目标地面 `(1613.5, 211.6362, 2157)`；交互锚点在地面上 `0.45 m`，即 `(1613.5, 212.0862, 2157)`。该点确认半径 1.6 m、高 3.2 m 的主体净空。没有沿用狭窄的 L22 任务设备站位。
- 南撤离地面 `(1599.954, 204.4442, 2067.8)`，北撤离配置 `(1625, 216.3493, 2280)`。撤离半径 `8 m`，阻挡半径 `15 m`，均为主组共享规则配置。南点到目标的完整导航路线约 `159.9 m`。北点从首轮贴墙的 `(1640,215.3514,2280)` 西移到既有连通候选，原全图采样确认它与 MC00 导航连通；最终 8 m 区域的 24 个周边站位净空均通过，未再次烘焙整图或重走角色路线。

出生数组按 `team * 3 + slot` 排列。下表是实际地面坐标，运行配置统一加 `0.12 m` 脚下余量：

| 队 | 槽0 | 槽1 | 槽2 |
|---|---|---|---|
| 0 | 1585,210.9641,2125 | 1584.25,210.8649,2126.299 | 1583.5,210.8649,2125 |
| 1 | 1670,206.0314,2115 | 1671.5,205.734,2115 | 1670.75,206.0611,2116.299 |
| 2 | 1660,214.0129,2240 | 1661.5,213.9633,2240 | 1659.25,214.1962,2241.299 |
| 3 | 1600.049,214.0075,2235.695 | 1599.974,214.1094,2237.524 | 1599.893,214.2337,2239.477 |

12 点均有实际地面、1.8 m 胶囊净空和完整目标路线，距离约 90–157 m。同队点距至少 1.5 m，局部导航路程不超过 7 m且处在同一邻近高度；跨队所有初始站位的眼高视线均被现有物件阻挡。最初隔墙但直线相近的同队候选、无近掩体或跨队直视的候选已剔除。此为几何与路线证据，不代表真人攻防平衡已验收。

本地详细输出在 `Library/BuildValidation/BountyClash/survey.csv` 与 `anchors-final.csv`（忽略目录）。首次开正式图超过 Pipeline 默认 5 秒而记录工具超时，勘察仍实际完成；首次复用导航系统直接 Dispose 又因运行时 `Destroy` 在 EditMode 不适用记录清理错误。后续改为先移除临时导航实例、清空系统引用，再 `DestroyImmediate`，最终临时 NavMeshData 为 0。没有清 Console 掩盖工具错误。原 WeaponGestureLab 恢复单场景、dirty=false、Play=false；Unity 自身 Editor/Streaming World 保留，不粗暴销毁内部世界。

## 模式表现与补给

- 世界表现只在收到模式状态的 Client/Server World 中启用：死亡触手目标、驱散/赏金信号、A/B 两撤离短地面刻线和低矮信标，以及沿地形的边界警示桩。目标网格复用现有 `BranchCreatureRig`，静态倒伏、没有活 Boss ghost 或攻击脚本；不是把普通球当 Boss。首轮高抛光径向形状未通过审查；V2 改为每实例粗糙组织纹理、低光泽、不对称肉质轮廓和弯曲倒伏触须。V4 修正本尸体私有触须网格的三角绕向与法线并开启尸体投射/接收阴影，近景已由地图组与协调方逐图复核，功能可读性检查通过；模型图不代表完整对局。
- `PresentObjective(progress, bountyAvailable, approvedBountyPosition)` 只消费 HUD 已批准的快照值，保留原两参重载。驱散改变信号色，`Ground` 在服务端实际位置显示单枚装饰核心，倒地掉落到别处时移走信号且不再标旧 Boss 站点；携带/撤离后隐藏地面核心与目标信号。不扫描 Ghost、不生成权威物品。
- 原单撤离的两个模型归入独立显示根，在速战中关闭；原模式恢复。圆外旧物资箱的世界标记隐藏，箱子 ID 和服务端库存通路不重编号。
- 圆内有原物资点索引 1–6 共六箱，覆盖 Supplies、Industrial、Equipment、Medical，沿既有搜刮和表生成；新增医疗/军用道具由道具组维护表，本组不改共享掉落表。六箱均已在正式碰撞场景找到实际站位，通过真实服务端首次搜索与取物处理器；测试为隔离 World，不是玩家从出生点连续步行六箱或真实网络 UI 点击。
- 原 PMC 人口系统与异形遭遇系统收到速战模式即退场并停止生成，普通 Boss 也不再出现。本轮选择无额外 PvE，不伪称已调好低密度干扰；切回原模式保留原作者配置与正常生成路径。

## 本轮专项结果

- 2026-10-21 18:05（上海）全项目重编译 `failed=false / compilationFailed=false / errors=[]`；随后实际 `EditorUtility.scriptCompilationFailed=false`。已加载 `Assembly-CSharp.dll` UTC 写入时刻 `2026-10-21T10:05:14.785287Z`，没有用旧程序集冒验。
- `BountyClashMapChecks.Run()`：25 项通过，包含不可变配置复制、12 出生/双撤离边界、实际旧异形与 PMC 及输入实体退场、原模式恢复、掉落赏金位置/携带后信号隐藏、资源清理。
- 正式图隔离服务端实测：索引 1 ration、2 dust、3 cells、4 medkit、5 cells、6 ration 的首次搜索和实际库存转移都通过；源箱子与玩家不重复持有。圆外索引 0 在 Clash 被拒绝，切回 Expedition 搜索成功。此项也覆盖主组新增权威圆界门禁，而非只隐藏世界模型。
- 北出口西移后周边 24/24 站位支持与净空通过，最终图中不再贴墙。12 出生仍沿已勘察坐标，没有重复 1668 候选采样。
- 实际 URP 1280×720 图在 `Library/BuildValidation/BountyClash/Presentation-v2/`：`fallen-target.png`、`ground-bounty.png`、`exit-south-A.png`、`exit-north-B.png`、`compound-boundary.png`；箱子结果为同目录 `cache-access.txt`。首轮 `Presentation/` 保留失败证据，不冒充最终图。
- 全蓝原因是正式场景 `MoonSilverPrint` 的既定钴蓝点色，不是材质、LineRenderer 默认顶点色或捕获通道错误。V2 使用现有 `MoonSilverPrintSubject.Protect` 保留目标/信标的玩法语义色，没有修改全局艺术风格；粗整环和长直光柱已移除。BranchRig 的皮肤本来就是每实例材质，组织纹理/网格也私有并在 Dispose 释放，不修改活 Boss 的资源。
- 最终恢复 WeaponGestureLab 单场景、dirty=false、Play=false、无活动 Arena/临时 NavMesh 或游戏 World；保留 Unity 自身 Editor/Streaming World。未触碰账户/Prefs/正式场景保存。最后可读 Console groundTruth 为 0 errors，18 条现存编译 warnings；正式图镜头记录了原有大量灯影图集降分辨率日志，未为该任务修改光照配置，也未清 Console 隐藏历史错误。

## 单 Editor 接地近景复核

UI 组在其独占窗口代跑 `/tmp/bounty_corpse_contact_review.cs`，随后按地图组交付的最小补丁修复绕向，代跑 `/tmp/bounty_corpse_contact_review_v4.cs`。仅使用现有 Unity Editor，临时 Additive 打开正式图、构造实际表现和使用正式 URP 后处理捕获，不进 Play、不创建网络 World、不烘焙导航、不保存场景。画面尺寸为 1280×720，眼高分别为真实地面以上 1.6 m 与 1.8 m。

- UI 组统一重编译最终返回 completed、failed=false、errors=0、warnings=0；CLI 回执中的 compilationFailed 曾短暂保留 true。随后原生 `console_status` 在 2026-10-21 20:15:52（上海，UTC 12:15:52）确认 groundTruth compilationFailed=false、compiling=false、consoleErrors=0，新 `BountyClashUIFixture` 类型也已实际加载。UI 组确认 V4 图与指标来自此新程序集；未以旧程序集结果替代，也未为补记录重复编译或跑图。
- V3 失败证据保留在 `Library/BuildValidation/BountyClash/Contact-v3/`：触须 2304 个三角面全部与外向法线相反，图中呈内表面薄带。修复只交换本静态尸体私有网格每个三角面的第二、第三索引，并提供扁椭圆截面的外向法线；没有改活 Boss 的 Rig、共享材质或全局光照。
- V4 报告为 `Library/BuildValidation/BountyClash/Contact-v4/geometry-contact.txt`。触须 2304 outward / 0 inward，中央主体 768 outward / 0 inward；触须最低离地 0 m、507 个顶点在地面 3.5 cm 内，中央主体最低离地 0.0132 m、22 个顶点在地面 3.5 cm 内。采样到的全部尸体网格均无超过 3.5 cm 的穿地顶点；上部组织瓣与伤口按其位置处于主体上方，不要求每个部件独立落地。渲染器均为 cast=On / receive=True，北出口周边仍为 24/24 净空通过。
- 地图组已实际查看同目录 `fallen-target.png`、`ground-bounty.png`、`fallen-target-eye-180.png`。触须恢复外侧实体圆管；驱散状态与地面赏金状态的核心、短细标及灯色可辨。协调方随后亲读三图与指标，确认功能可读性通过，停止本轮美术扩展。此项为局部视觉和接地复核，不宣称真人攻防或最终美术品质验收。
- UI 组报告 V4 结束恢复 WeaponGestureLab 单场景、dirty=false、Play=false。须以本次 Editor 的实际前后基线处理自动加载的原生 Editor/Streaming World；重启前 V2 的既有 World 状态不能代替重启后实测，也不应随意销毁 Unity 内部 World。

## 验证范围

用户因 Mac 内存限制明确取消多开联机验收，因此不再以独立客户端地图呈现作为本轮交付门禁，也不创建额外 Editor 或网络 World。地图阶段已有编译、正式碰撞/路线、目标与双出口模型图、隔离箱子搜刮与模式门禁、接地及局部清理证据。独立客户端地图状态、玩家从出生连续步行搜刮、真实 HUD 点击、原单撤离标记随真实 Play 模式隐藏在本组仍未验证；完整争夺、救援、阻挡撤离和持久化的跨组结论应引用各自证据，不能由地图几何检查替代。
