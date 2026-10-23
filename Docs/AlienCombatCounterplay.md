# AI / Boss 追击与攻击辨识修复（2026-10-21）

## 调查证据

- Unity 6000.5.10f1；`develop` 初始干净，调查基线包含协调计划提交 `aa88fe08`。不切换分支/工作树，不修改地图/场景/Profile。
- 两只正式 Boss 的视距均为 65m，`MucusRange=14` 经旧体型倍率后为 25.2m；下一攻击序号若是短程横扫/黏液，超射程就不递增序号，因此可一直停留在无法开始的动作上。
- 巨型 Boss `Scale=4`，身体中心距脚底为 2.24m；旧 NavMesh 起点从身体中心采样半径仅 2m。失败后直接返回目标向量，换位也只检验终点。
- Roots 被普通移动分支排除，只每 6 秒尝试约 4.18m 的短距换位。巨型实体仍由碰撞阻挡，遇狭窄门道无法到达玩家。
- 丢失目标后普通异形只走向固定最后目击点，到达后停住直至记忆过期。
- `AlienAttackPresentation` 明确不画预警范围，只在 Strike 创建落点；服务端脉冲偏移 0.08s，客户端特效/声音从 0s 计算。狂暴第二扫使用反方向，客户端仍画前向横扫且只显示至多 9.6m，而正式伤害半径为 13.2m。
- PMC 路径拐点与停止条件只用水平距离，可能跳过上层拐点或过早停止；进展检测没有区分搜索停顿/治疗/实际移动意图。重寻路时间还随累计 Seed 无界增长。

这些是代码/配置证据；正式玩家报告的完整地形复现与两端联机视觉/听感需要单独验收。

## 实现

- 地面导航从脚底采样并保留楼层高度。无 NavMesh 时只允许经连续地面、坡度和全身胶囊净空验证的直线路径，最多 96 个分段；失败停在当前位置。Boss 换位复用此路径。路径每 0.75s 重算，移动仍逐步经过原服务端碰撞。
- 根系 Boss 可以正常追击；丢失目标后仅搜索最后已知线索附近，目标不可见时不读取其实时位置，记忆到期回巡逻。侧绕受阻会短暂直接尝试并重新规划。
- 两只 Boss 在下一近程攻击无法开始时选择黏液，射程覆盖现有视距；每次重新检查遮挡，攻击在前摇开始时锁定。弹道时间按速度计算，最长 4s，攻击活动时间覆盖飞行；横移可躲避，沿途 SphereCast 结算，仍由服务端权威处理。
- 巨型实体攻击保留至少 1.15s 前摇，远程黏液至少 0.85s；狂暴每个后续脉冲间隔至少 1.25s。每次横扫都沿已预告方向，不再出现背向第二扫。
- 客户端依据同一攻击快照绘制锁定扇区、三块根刺区域、重砸落点或黏液弹道；各段持续预告并变化亮度。线条有正常深度遮挡；地面投影仅每次攻击创建时计算，回收/死亡隐藏，无伤害碰撞。
- 伤害、粒子和攻击声音共同使用 `AlienCombatPattern.PulseTime/PulseAt`，第一段为 Strike+0.08s，后续为固定间隔。横扫可见触肢达到实际射程。远程区域伤害同时验证 Boss 原点至受害者视线；横扫增加垂直范围约束，楼层/墙体能阻挡。
- PMC 保留跨层拐点，只有到达对应高度才前进；停止距离同时检查高度；主动等待不作为卡住依据；重寻路错帧限定为七档。

## 可重复检查

Unity Edit Mode，允许临时打开并关闭空的附加检查场景；不会保存生产场景或改动地图。

```sh
unity recompile --project-path /Users/morrow/UnityProjects/Moonkov --format json
unity command eval 'AlienEnemyChecks.RunCounterplayContract();' --caller plugin --skill unity-build-validation --result-only
unity command eval 'AlienEnemyChecks.RunHorrorContract();' --caller plugin --skill unity-build-validation --result-only
unity command eval 'AlienEnemyChecks.RunPressureContract();' --caller plugin --skill unity-build-validation --result-only
unity command eval 'DollSingerHumanlikeChecks.Run();' --caller plugin --skill unity-build-validation --result-only
```

新专项 `RunCounterplayContract` 覆盖：两只正式 Profile 的 40m 静止命中、横移躲避、出手后高墙阻挡、8m 高台命中、视线遮挡不出手；狂暴全部脉冲与每次伤害时刻、前向锁定、多层楼板零伤害；客户端前摇几何/回收隐藏/零碰撞；异形线索搜索与记忆过期；PMC 跨层拐点。

## 实际验证结果

2026-10-21，在本机 Unity Editor 6000.5.10f1 执行：

- 最后修正狂暴第二段流束后，由库存组在接续独占时段重新编译（completed、failed=false、errors=[]），并于北京时间15:13:48重新执行 `RunCounterplayContract` 成功，包含第二段命中前0.04s必须存在可见流束的断言。代码自该版本起冻结。
- Unity 编译通过，0 C# 错误。最初受其他组 `MenuCharacterView` 的 PointerType 歧义阻塞，修复后复编译与新检查类型加载均正常。
- `RunCounterplayContract` 18 项通过：两种 Boss 40m 静止/8m高台各命中一次；横移、出手后墙体、预先遮挡均零命中；远程实测前摇 0.87s；狂暴横扫两段、根刺三段全部按共同脉冲时刻命中，段间至少 1.2s（30Hz采样），隔层零伤害；搜索/PMC楼层/客户端几何与清理通过。
- 原 `RunHorrorContract` 7 项通过：两只巨型 Boss 移动、无伤威吓/唯一狂暴、净空/服务端零渲染，凝视暂停上限、侧绕、退让、10度坡移动。
- 原 `RunPressureContract` 10 项通过：两种 Boss 换位/墙阻挡、根系攻击后换位、黏液命中/闪避/墙体/身体命中与状态清理。
- `DollSingerHumanlikeChecks.Run` 通过既有 PMC 掩体、治疗与决策生命周期回归。
- `CaptureCounterplayWarnings` 保存并人工查看两张 960×720 GPU 渲染图，橙色扇区与三个独立根刺区域可见：`warning-Tentacle.png`、`warning-Roots.png`。这不是正式场景玩家视角验收。
- `RunFormalHorrorPlacement` 的正式 Theophilus 23槽位中四个 Boss 候选均找到合法巨型落点，单 Boss/同种一次生成预算仍成立。该 eval 因加载真实地图超过 Pipeline 内部5s分派等待返回超时错误；实际方法继续完成，报告于本轮15:11生成且随后确认无附加场景残留。不能将此工具超时描述成游戏逻辑异常，也未将落点检查描述成完整路径通过。
- 验证后初始 `WeaponGestureLab.unity`、dirty=false、场景数1、Play=stopped。未保存或改动生产场景。Console 无当前编译错误，有一次上述工具超时；14条既有弃用/序列化与账户连接警告不在本任务范围。

报告目录：`Library/BuildValidation/AlienIntegration/`，包含 `counterplay-contract.txt`、`horror-contract.txt`、`pressure-contract.txt`、`horror-formal-spawns.txt` 与两张预警图。

## 剩余验收

- 正式地图 Host + 第二客户端中分别在 40m、65m、高台、门内、楼梯上下进行移动交战，核对两端预警、遮挡、命中时序。
- 视觉截图确认地表不同明暗下的线条辨识与遮挡，实际音频听感及完整路线。
- 不因狭窄地点限制任意挪动巨型 Boss；MC00 封闭楼层是备选设计，需要独立地图/体型验收。此修复不迁移、不封锁生产地图。
- 本轮不宣称完成目标设备构建、性能负载或双端网络传输验收。

## 2026-10-21 第二轮：攻击可行性、导航净空与实际撞击

基线 `b5aed916`，延续原分支与工作区。仅敌人脚本、专用检查与本文变更，无场景/Profile/地图迁移，无跨组 Ghost/RPC 通用文件修改。

- `AlienAttackEvents` 分别按攻击序号、接触序号去重。发射声在第一次观察到本次 Strike 时独立触发，不等待 0.08s 伤害脉冲；落地/后续脉冲仍按原共享时钟。首次快照建立基线，晚加入不补播旧攻击/撞击，乱序旧序号不回放。
- 横扫起手与结算共用 `AlienCombatPattern.SweepContains`，包含同一水平范围/角度和脚底相对高度限制。近距离高台超出横扫高度时选择有预警的黏液，不修改伤害去扩大穿层范围。
- 地面导航硬性每 0.75s 最多规划一次，目标漂移与受阻都不绕过该间隔。人形 NavMesh 仅提供候选路线；每次最多验证12m前缀、144次物理查询，按 Boss 实际胶囊验证地面、坡度、整段净空。三秒阻塞目标记忆避免重复尝试同一目标通道，并在预算内检查四个附近可到达的侧方停靠点。没有合法位置就停住，仍由原 motor 逐步碰撞运动。未建立独立大型导航框架，也不宣称能解开任意复杂迷宫。
- 发射前用最多两组各25次查询（起点占用+24段保守球扫）验证实际弹道：先标准弧，再低弧；两者不合法则不发射，失败最多每0.5s重试。低弧标志进入同一攻击快照，服务端飞行、客户端预告与弹体共用 `AlienMucusTrajectory`，不改变伤害权威或穿越遮挡。
- 服务端实际 SphereCast 接触同步 `ImpactSequence/Point/Normal/Kind`（墙体/身体/面部），与 `MucusInFlight` 无关。客户端按接触快照在真实点位沿法线播放飞溅及撞击声，重复快照不重复播放。新增 Ghost 状态字段要求联网双方使用相同版本。

本轮验证（本机 Unity 6000.5.10f1，2026-10-21 北京时间15:54前完成）：

```sh
unity recompile --project-path /Users/morrow/UnityProjects/Moonkov --format json
unity command eval 'AlienEnemyChecks.RunRefinementContract();' --caller plugin --skill unity-build-validation --result-only
unity command eval 'AlienEnemyChecks.RunCounterplayContract(); AlienEnemyChecks.RunPressureContract();' --caller plugin --skill unity-build-validation --result-only
```

- 最终编译 `failed=false`、`compilationFailed=false`、`errors=[]`。
- 新专项19条通过：连续快照0/.033/.067/.1s的单次launch、乱序/丢前摇/首包抑制；5/8/12m×高度0/2/3/8m的实际起手选择；40m目标在远端5m棚顶下选择低弧并命中面部/身体；出手后新增墙体得到真实墙接触且零伤害；直线可见但两种弧都碰顶的走廊拒绝发射；临时构建的人形 NavMesh 能通过2m门洞而巨型路线拒绝，并选择合法侧方停靠；高频大幅漂移目标无法绕过导航间隔/预算；客户端接触粒子在权威点位沿法线生成，重复快照不增发，首包不回放。
- 必要回归：上一轮 `RunCounterplayContract` 18条、`RunPressureContract` 10条通过。不重跑地图落点与无关完整旧矩阵。
- 首两次专项在窄门停靠断言失败，定位为视线终点恰在地面导致误撞地板；终点抬高0.5m后复编译并完整复跑通过。没有删除断言或放宽胶囊净空来获得通过。
- 结束时确认 `WeaponGestureLab.unity`、dirty=false、Play=false、sceneCount=1，临时 NavMesh/附加场景均已清理。Console当前0错误、14条既有警告。检查报告：`Library/BuildValidation/AlienIntegration/refinement-contract.txt`。

边界：序号gate与粒子行为检查证明触发/去重逻辑，未完成实机声音听感；未跑Host+第二客户端传输、正式地图完整路线与性能负载。144次物理查询/规划及硬限频已通过行为断言，但不据此声称帧率提升或目标设备达标。Boss仅从现有可达位置改变开火角度，不会瞬移或穿过窄门。

## 2026-10-24 Boss 换位的缓存路径修正

新增保持交战距离行为时，原“攻击后换位”检查抓到葬根计入一次换位但实际移动距离为零：`UprightWaypoint` 的 0.75 秒缓存仍指向前一目标，旧逻辑拿到当前位置后立即退出换位，冷却却已经消耗。现在换位等待自身目标的已验证路径刷新，等待最多额外 0.8 秒；实际移动开始后才计算完整移动时段，且只有真正发生位移才增加换位计数。侧移和后退仍面向已观察目标。保持原导航限频、144 次查询预算、地面与完整胶囊净空检查，不重置寻路计时器，不瞬移。

原 `RunPressureContract` 的移动距离、墙体阻挡、攻击后换位及黏液命中／躲避／墙阻挡断言保留。新行为调试中最初在“Roots remained stationary”失败，确认实际 travel=0 后修正缓存等待逻辑；没有通过降低移动阈值来消除失败。
