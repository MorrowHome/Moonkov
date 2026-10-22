# 赏金对决客户端与入口

本文件记录客户端实现及验收边界。2026-10-22 新增正式离线赏金速战；原 2026-10-21 验证记录保留为历史证据，不代表本次 AI/离线结算已经验收。实际权威规则见规划、Shared 协议与服务端；本文不把编译或 UI 样例当作多人闭环证据。用户已取消多客户端联机验收（Mac 内存限制）。

## 2026-10-22 单人赏金速战接入

- 在现有离线资料和模式卡中选择“单人赏金速战”，沿用 `ShowRaidPreparation → CreationType.Offline`、真实离线 profile/token 和独立离线仓库。1 名真人迎战三支双人 AI 小队；死亡掉装，撤离保留战利品，不是临时练习局。
- 只有本机 Offline 创建流程且所选玩法为 BountyClash 时，才在 Server/Client World 同步创建 `ClashSoloMode`，与原 `OfflineRaidMode` 和 `RaidModeState` 一起供可信本地运行判定。原远征、在线账号入口与模式批准握手不变。
- 单人 Lobby 读取服务端 `IsSolo` 隐藏建队、邀请码、容量与离队控件；自动单人队伍只需准备。`SoloNavigationReady` 为 false 时禁用准备，`SoloNavigationStatus` 显示玩家可读反馈；`IsBot` 名单行显示“AI 对手”，不把已脱敏的敌方行误报为断线。
- 真人部署结束后，下一局服从服务端 `CanStartNext`（离线必须真实保存成功），不要求剩余 AI 先结束全局比赛；保存中/重试和请求未确认时仍禁用。离线仓库已存档、战利品保留或损失装备移除的提示只在该批准后出现。下一局沿用仓库中实际配置，缺装时需返回仓库整备。
- 新增源码的 JSON/UXML 解析、唯一控件名、15 个新增中英键占位符一致及定向 `git diff --check` 已通过。服务端组统一首轮 Editor 编译通过；单人 UI 短窗口已执行 `ApplySoloLocalization.cs`，只精确保存 en/zh/SharedData 三张表并逐项核对 15 键。
- UI 验证使用一个无 Transport、无持久化的既有 fixture ClientWorld。模式卡真实 `NavigationSubmit` 回调选中通过；旧 800 模式卡截图可读性失败：预览相机 1920 像素被误作 UI 视口，导致 660 宽窗口缩成约 273 像素。源码已改读 `Screen.width/height`，保持原 USS 和样式，等待仅重拍 800/1280 两张模式卡确认。导航等待快照使准备禁用，在线组队区/离队均隐藏。中文 800 准备页保持可滚动名单和固定底部按钮；注入 `Phase=Active, Life=Extracted, CanStartNext=true` 后下一局可用，撤去批准后禁用且显示保存中。该结果证明状态呈现与 UI gating，不证明真实 AI 导航、实战、死亡/撤离结算；实地图由后续统一窗口验证。
- 截图位于 `Library/BuildValidation/BountyClash/UI/solo-mode-800.png`、`solo-mode-1280.png`、`solo-nav-1280.png`、`solo-ready-800.png`、`solo-finished-800.png`。英文保存中截图出现字体分段捕获伪影，未作为英文视觉通过证据。清理脚本首轮误用 GameView 自定义分辨率索引产生 3 条 `Invalid index` 工具错误，修正后只移除本次 3 个临时尺寸，Console 原记录保留。退出后 MainMenu 单场景 clean、非 Play、World/fixture/验证设备为 0，12 个偏好键差异为 0；两个字体文件 SHA 与用户开始时字节基线完全一致，不在本任务提交内。

## 入口和部署

- 原月面远征与在线赏金对决通过仓库页和连接页的模式按钮选择。连接方式（单机、Host、Direct、Relay）与玩法分开。
- 偏好 `Moonkov.RaidMode` 只记录所选模式，默认远征。原存档、原玩法偏好不迁移；离线资料现可通过上面的可信本机入口进入单人赏金速战。
- Server World / Client World 在加载同一正式 `TheophilusGameScene` 前建立 `RaidModeState`。专用服可用 `-raid-mode bounty-clash`，不读取客户端上次选择作为服务器默认值；UGS 入口使用同一初始化。
- Join RPC 携带模式与规则版本；客户端只有收到匹配的 Accepted 才完成赏金模式批准。地图表现读同一模式状态。Relay 赏金会话最多 12 人，远征保留原人数设置。
- 原版个人重部署在赏金模式中停用；下一局只能由服务端 `CanStartNext` 批准。部署失败时单独显示取消/等待返还/已返还，不把未开始战斗误写成装备损失。

## 真实输入和表现

| 输入 | 赏金模式行为 |
|---|---|
| Tab | 打开背包，沿用真实库存树 |
| 1–9 | 选择实际绑定物品；不直接消耗；绑定和拖入见 `Docs/QuickSlots.md` |
| 左键 | 使用所选道具；武器沿用原瞄准/射击规则 |
| F | 在目标附近请求放逐/拾赏金；靠近倒地队友时按住救援，0.25 秒续租，松开取消 |
| V | 按住进入暗视；非持有者看目标/赏金但无免费敌情 |
| V + 右键 | 当前赏金持有者有个人余量时请求增强暗视 |
| B / 鼠标中键 | 切换视角；远征与独立动作实验场保留原 V/中键 |
| E | 战术箱/陷阱/自救优先，消费本帧交互后不再打开旧容器；否则沿用搜刮 |
| G | 保留原能量雷，耗时动作与暗视期间互斥 |

暗视的 125 米增强橙云、75 米持有者 Instinct 与累计 5 秒预算参考 [Crytek 官方玩家指南](https://www.huntshowdown.com/player-guide/en_US) 的 Step 5（本轮协调核对），不是旧草案 150 米。预算与暂停撤离时间全部读服务器值；客户端不决定敌人位置、扣时、救起或结算。

增强云只消费 `ClashSenseContactRpc` 的短期授权模糊区域，使用实际 owned character camera 的专属 RenderGraph pass 投影；不扫描敌方 Ghost，不附带敌方名字/生命/命中框，环境冷暗与柔云只作用于该 owned camera，不影响菜单、其他相机或 UI。松键、失去赏金、阶段或 epoch 改变立即清除；每点最多 0.35 秒有效。界面只显示前方相机投影的模糊云，不显示旧版方向/距离扇区；预算耗尽只保留持有者 75 米无方向边缘存在感。

倒地保留同一网络实体与装备，用原角色 Prone 动画/胶囊和收枪呈现；复活从权威姿态恢复。缺失角色时使用中性观战镜头，不显示个人自动复活倒计时。队友标记与示意地图只读取服务器标记的 OwnTeam 行；敌方持有者地图区域由服务端量化。A/B 出口位置和掉落赏金位置均来自快照，未硬编码地图坐标。

## 2026-10-21 历史验证边界

- JSON / UXML 解析、唯一控件名及 `git diff --check` 已通过。
- `BountyClashClientChecks.Run()` 已落盘，覆盖真实客户端缓存的消息乱序、非持有者权限、云点过期/松键/epoch 清理、满名单换局、确认与失败反馈、原生 HUD 绑定/Dispose。已在现有 Editor 执行并通过 17 项；追加停止/迟到同序号、耗尽、死亡和 epoch 等 14 项后再次实际通过 31 项。
- `LocalData/BountyClashValidation/ApplyLocalization.cs` 只精确新增本轮 en/zh/SharedData 字符串；不运行全量 Builder 或重建字体。145 个条目及追加取消反馈共 168 条已定向写入并逐项核对；中英画面在单 World fixture 中验收。
- 本轮只使用现有单个 Editor，每次最多一个轻量 ClientWorld，使用内存 scope / inventory，不创建 Transport、Host 或额外游戏客户端。
- 实际 UITK 回调通过键绑定 4–8、拖入 9、右键清除和重拖，库存版本不变；模板改为复用 `InventoryItem.uxml` 后也通过对应回调与 Hit 检查。
- `QuickSlotCancelInputChecks.Run(9)` 实际通过 31 项：真实 Primary 发一次精确 ItemId 的 Use，未注入 Started 时 X 发唯一顺序 Cancel；同次按住不进入踮脚，释放后下次 X 正常；菜单、文本焦点、pending 文本 X 不误用/重绑/选择/取消。数量 2、库存版本 9 全程不变。回执是明确 fixture 注入，见 `Library/BuildValidation/BountyClash/UI/cancel-input-31.json`。
- 实际数字输入切到原装备 2/3 后，ghost 武器 ID 分别为 4/3；从道具切回 1 后持物针筒被清除。失焦和关闭 UI 第一帧拒绝输入属于原捕获保护；重新聚焦并独立按键后验证。
- 实际 V 心跳排入值 1、V+RMB 排入值 3，释放值 0。该 World 没有 driver，RPC 仅为本地发送意图。预算、敌情和倒地状态由 fixture 注入，不能视为服务端扣时/消费。
- 同一角色的注入倒地状态实际呈现 Prone/胶囊 0.58，恢复 Standing/1.7，最大健康比例 0.75，所持武器 ID 保留。B 实际切到 third-person 布尔状态；独立实验场的相机碰撞和未启动 NetCode bootstrap 的中性观战分支不作为完整生产相机验收。
- 正式 MC00 无 World 渲染验证见 `Library/BuildValidation/BountyClash/DarkSight-World-v1/report.txt`：遮挡射线命中正式墙；授权增强云为 1，背后/过期/零预算/松开/非 owner/清理的云为 0，ClearImmediate 释放 driver，场景恢复。这里是接收 API 构造的授权样本，不是真实网络敌人。
- 界面按用户反馈复用 Archive 窗口、RaidStatus 仪表、InventoryItem 与 mk 状态层；早期蓝金矩形版本及其视觉通过结论已撤销。最终实图与实际相机尺寸/面板边界记录位于 `Library/BuildValidation/BountyClash/UI/`。原 `PlayerHUD.uxml` 两角同屏使用同 owner 数值作布局呈现检查，没有改旧仪表样式。
- 输入和 UI 回调使用真实路径；服务器快照、倒地/恢复姿态、疲劳和回执如果由 fixture 注入，结果明确记为状态呈现。队列内出现 RPC 不代表服务端已消费或完成结算。真实多客户端闭环未验，按用户要求不阻断本次交付。

最终定向导入 UXML/USS 后，原生模式页居中遮罩、准备页固定 footer、结算页与 800/1280 中文/英文长反馈图已完成实际评审。模式页实际 NavigationSubmit 调用原 Button 回调，关闭后 display=None 且 panel.Pick 回到库存；Dispose 移除原生 CloneTree scope。最终键检查、模板引用和样式复用均保留原交互 name/委托。

清理核对：验证 Play 退出，原 WeaponGestureLab 单场景无 dirty，Client/Server World、验证设备、fixture 对象均为 0，内存 scope/store 释放；12 个备份偏好语义一致。原 Full HD 尺寸恢复，测试自建 7 个分辨率项移除。清理工具曾因 RemoveCustomSize 索引语义产生 7 条 Console 错误，已修正并确认自建项剩余 0；该工具错误记录保留，不作为玩法异常，也没有清空 Console。

最终记录：`Library/BuildValidation/BountyClash/UI/final-cleanup.json`、`final-console.json` 与 `final-compile.json`。最后实际请求脚本编译完成：failed=false、compilationFailed=false、errors=[]；Console cursor 79526 后新增错误 0。168 个本轮键的 JSON 占位符一致。字体动态缓存由协调聊天备份并恢复，随后只定向导入，没有再 SaveAssets。最终中英/尺寸图为 `final-inventory-800.png`、`final-inventory-1280.png`、`final-tool-fatigue-800.png`、`final-tool-fatigue-1280.png`、`final-tool-fatigue-en-1280.png`，模式页 `archive-mode-final-800.png`，准备/结算 `final-preparing-800.png`、`final-finished-800.png`。
