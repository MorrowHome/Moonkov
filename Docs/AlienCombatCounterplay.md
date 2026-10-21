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
