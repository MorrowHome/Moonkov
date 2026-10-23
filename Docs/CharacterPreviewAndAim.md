# 角色预览拖动与视角瞄准

2026-10-21，当前 develop。

## 证据与修正

- 首页、出战角色确认、仓库装备页和局内装备页复用 `MenuCharacterView`。原实现将图片设为 `PickingMode.Ignore`，且每次动画推进重写模型 yaw，没有指针检视入口。现在只允许在人物图片上开始鼠标左键拖动，横拖一个图片宽度旋转 180°，指针捕获可延伸到图片外；松手、取消、丢失捕获后停止。关闭或隐藏时释放预览舞台并复位。开始检视后固定用户选择的角度，不再叠加展示动画的自动转身角度；人物动作继续播放。
- 装备按钮、物品拖动继续拥有自己的指针目标与捕获。库存组配套将 `ContainerInventory.uxml` 中空白 `slotLayer` 设为 Ignore，使空白区域能拾取下方人物；子装备按钮仍可拾取。
- `DollSingerPlayer.prefab` 的原第三人称镜头为 45°，`DollSingerView.firstPersonFieldOfView` 为 70°，原代码随视角混合这两个值。两种视角现在共用现有 70° 世界相机标定，保留序列化字段名和可调值；第三人称轨道位置、碰撞回缩、第一人称眼部位置仍独立。
- 原 `TargetAimMagnification` 只在第一人称使用武器倍率，第三人称退回 1.75 倍。现在两种视角使用相同武器倍率与同一抬手后变焦时间线；镜头 FOV 仍按投影正切计算。70° 下，1.75 倍约 43.61°，8 倍约 10.00°。普通手势的独立手部相机 FOV、狙击手部镜头跟随世界变焦、光环和手臂平滑过渡不改。
- 原第三人称中心射线带有镜头初始 12° 局部俯角，第一人称命令射线直接采用 look rotation。现在两种视角世界相机的局部旋转均为零，由同一输入视向驱动；第三人称仍通过轨道与肩部偏移改变相机位置。命令取点与屏幕中心方向一致，保留枪口到目标的原有视差和遮挡判定。
- 输入路径未发现双重倍率缩放；鼠标原始 delta 直接乘基础灵敏度，`DollSingerLookController` 再使用 `AimLookSensitivity` 一次。原比例根据 aim blend 推算，可能早于实际画面的变焦；现在从当前已呈现的相机 FOV 投影计算比例，8 倍为 1/8，保留原瞄准灵敏度配置。自由观察沿用原速度。
- MonoBehaviour 的网络表现 Update 早于 ECS 的 GhostInputSystemGroup，本地光环 LateUpdate 可能沿用上次表现 Update 提交的旧目标。`CaptureAimPoint` 现在立即将最新本地目标提交到光环，再由原 LateUpdate 和既有平滑链路表现。未修改武器伤害、射速、散射或服务端弹道。

```mermaid
flowchart LR
    Mouse[原始鼠标或手柄输入] --> Scale[按已呈现世界镜头投影缩放一次]
    Scale --> Look[本帧视向]
    Look --> Camera[两种视角共用方向与FOV标定]
    Look --> Ray[相机取点]
    Ray --> Halo[即时提交本地光环目标]
    Ray --> Command[原输入命令与枪口遮挡判定]
```

## 回归入口

先用本项目 Unity 6000.5.10f1 编译，然后在停止 Play 的状态运行：

```csharp
MoonkovViewInteractionChecks.CheckCamera();
MoonkovViewInteractionChecks.CheckPortrait();
// UI 检查在独立临时 EditorWindow 排版后异步执行。
// 查询 MoonkovViewInteractionChecks.LastPreviewResult，最终应为 passed。
MoonkovShootingFeelChecks.Run();
MoonkovThirdPersonCameraChecks.Run();
```

专用检查使用临时 fixture，不保存或切换用户场景。相机检查实际执行 `DollSingerView.LateUpdate` 并测量 Camera 投影，验证两视角 1/1.75/6/8 倍、中心方向、屏幕目标尺寸、缩放一次、微小输入与当前画面变焦一致。人物检查使用真实预览模型和 UI Toolkit 指针事件，验证拖动、释放、取消、叠放按钮、移除清理与重开复位。

当前实际 `SniperHalo.prefab.scopeMagnification` 为 6，Player/NetworkPlayer 没有覆盖；旧射击文档中的“默认 8 倍”不代表当前资产。保留该配置，8 倍作为可配置倍率回归场景单独验证。

人工验收：在首页、出战角色页、仓库及局内装备区左右拖动人物；松手停止、离开重开复位，按钮与物品拖动不旋转人物。在真实战局使用同一武器依次切第一/第三人称，检查普通镜和 8 倍镜目标比例、中心取点、近墙枪口遮挡、QE 侧身、换镜、抬手/放手、自由观察及 UI 开关。相机位置引起的近距离视差保留。主观手感、多人网络及可执行构建不能由专项数值检查替代。

## 验证记录

- 静态 `git diff --check` 通过。
- Unity 6000.5.10f1 全工程编译 completed，0 错误，无新增相关警告。首次编译的 `PointerType` 同名歧义已显式限定后复验；既有序列化分析器警告未扩大处理。最终 Console groundTruth 为 0 错误、1 条 Unity AI Account API 30 秒连接超时警告；历史工具缓冲中的旧编译/超时记录不作为当前错误。
- `CheckCamera`、`CheckPortrait` 均通过，UI 异步结果明确读取为 `passed`；既有 `MoonkovShootingFeelChecks.Run()`、`MoonkovThirdPersonCameraChecks.Run()`、`MoonkovShotChecks.Run()` 通过，包括精细输入、轨道碰撞回缩、头部/自身/枪口遮挡和密集取点回归。
- 在现有 `WeaponGestureLab` 临时 Play，真实正式角色 Prefab 的普通镜两视角 FOV 均约 43.61454°，正式 6 倍均约 13.31276°，临时运行实例的 8 倍均约 10.00424°；灵敏度分别约 0.5714286 / 0.1666667 / 0.125。普通手部镜头保留 65°，狙击手部镜头跟随世界镜头；第三人称关闭手部镜头。放下瞄准后 AimBlend=0、世界镜头回到 70°、手部显示关闭。运行帧计数实际持续推进。
- 初始数值夹具在 10 km 坐标、3 cm 近裁面重建 `ViewportPointToRay` 出现浮点抵消，已将常规投影夹具移到原点附近，并额外在正式出生点量级复验，未用改阈值隐藏结果。正式 Spawn 1 无父变换，位置 `(1796,180.90761,2164)`；将真实角色运行实例临时移至该位置，以 pitch=-17°、yaw=-100° 检查两视角：命令射线方向与实际 camera.forward 向量差均为 0，100 m 中心目标的投影误差约 4.89e-6 个视口单位。该坐标的 `ViewportPointToRay` 方向向量差约 0.00075（第三人称）/ 0.00135（第一人称），说明直接用相机视向建命令射线可避开近裁面重建误差；未引入浮动原点。
- 最终恢复原 `WeaponGestureLab` 单场景、dirty=false、Play=false；正式狙击资产仍为 6 倍。未加载战局、改账户/玩家名称或保存测试场景。原始量化记录保留在本地忽略目录 `LocalData/ViewValidation/runtime-projection.json`。
- 本轮没有完整实战或双客户端/延迟丢包/可执行构建验收，未宣称主观瞄准手感和最终画面通过。手臂与光环沿用原平滑代码，运行数值和显隐状态回归不能替代实际游玩评价。

## 2026-10-23 人物检视拖动修复

- 复现：初音预览在同一次 UI 更新内收到 40 次拖动事件，就立即提交 40 次相机渲染；转回原角度后镜头 x 从约 0.028 漂到 -0.071，距离 z 从约 4.508 增到 4.637。原因是指针事件直接渲染，取景又持续累积旋转后的世界轴对齐包围盒。
- 拖动事件现在只累计 yaw，下一次预览更新统一推进姿态、光环和附属表现后渲染。按住检视时更新间隔为 16 ms，释放、取消或失去捕获后恢复 33 ms；静态编辑器预览同样消费待更新角度。横拖一个图片宽度仍旋转 180°。
- 取景将蒙皮局部包围盒通过 rootBone 转回人物坐标，再围绕人物轴构造旋转包络。仅转动人物不再扩张或偏移镜头，动作需要更多空间时仍允许扩张。初音的蒙皮包围盒与 Renderer Transform 坐标不同，已用真实正面、侧面渲染检查站立比例。
- 同一初音探针修复后 40 次输入处理约 3.1 ms（修复前约 17.4 ms），事件内没有相机提交；相同往返流程的显式检查渲染共 2 次（原为 82 次）。镜头 x 保持 0，往返前后 z 约 4.87312，差约 1.4e-6。这是编辑器定向探针数据，不代表实机 FPS。
- 回归入口：`MoonkovViewInteractionChecks.CheckPortrait()` 检查 DollSinger；`CheckPortraitForCharacter(GameSettings.HatsuneMikuCharacterIndex)` 检查初音。均需读取异步 `LastPreviewResult`。覆盖站立显示比例、完整转圈取景、密集输入合并、静态预览刷新、释放、取消、失去捕获、漏收松键、按钮隔离、移除清理和重开复位。
- 全程复用现有 Editor 和 MainMenu，未启动 Play 或联机客户端；不将这些检查称为完整游戏内主观手感验收。
- 最终 Unity 6000.5.10f1 编译 completed、failed=false；DollSinger、初音专项检查分别读取为 passed，Console 当前 groundTruth 为 0 错误，既有警告保留。结束时 MainMenu 为唯一普通场景、dirty=false、Play=false；`git diff --check` 通过，原有字体资产修改未纳入修复。

### 继续修复：动画导致的逐渐缩小

- 用户反馈仍会缩小。此前完整转圈检查使用 `Advance(0)`，未覆盖动作推进；将测试改为真实时间推进并逐个切换展示动作后，旧代码在 DollSinger 第 1 个动作即触发镜头变化断言。
- 删除每次动画推进后的取景包围盒扩张，只在创建舞台时计算初始取景。旋转、动画、头发和光环运动都不再改动镜头基准；页面重开或换角色重新初始化。较大动作可超出初始站立取景，后续由用户主动缩放查看。
- 回归改为使用正常展示模式，并逐一进入全部配置动作，每个推进 1.2 秒后检查镜头位置。拖动转圈同样以非零时间步推进，避免再次只测静态姿态。

### 滚轮缩放

- 在可交互人物预览图片上向上滚动放大、向下缩小，复用月球预览的指数滚轮映射，范围 0.65～3 倍。左键拖动只改 yaw；镜头位置和初始取景基准固定，按投影正切设置相机 FOV，避免缩放倍率被自动取景抵消。大动作或放大后的局部允许超出画面，可向下滚动查看。
- 滚轮沿用预览更新调度，静态编辑器预览也会刷新。人物图上的滚轮停止向父级滚动容器传播，叠放装备按钮保留自己的事件。关闭、隐藏、移除或切换人物后缩放回到 1 倍，事件在 Dispose 时注销；人物图片提供操作提示。
- 验证：Unity 6000.5.10f1 编译 completed、failed=false，DollSinger 与初音的完整专项结果分别为 passed；增加相机实际投影倍率、正反滚轮恢复比例、上下限、缩放后拖动及动画保持倍率、装备及父级滚轮隔离、移除重开复位检查。逐一展示动作回归也通过，并检查初音默认及约 2 倍的实际渲染图。未启动 Play 或联机实例，主观手感仍需用户在实际界面体验。
