# 正式玩家手势演示场景

打开 `Assets/DollSinger/Scenes/WeaponGestureLab.unity` 后按 Play。也可使用 `Tools / Moonkov / Open Weapon Gesture Lab`。

场景直接实例化正式 `DollSingerNetworkPlayer.prefab`，沿用其 DollSinger 模型、Animator Controller、手势动画、Movement 表现、SkirtHandAvoidance、SecondaryBoneSpring、HaloAim 和 DollSingerView。四个视角实时观察**同一个实例、同一帧的骨骼**，不另建一套演示手臂，也不替换手势解算。

左上是正式玩家相机，包含正常的第一人称头部过滤和手部 Overlay；其余为正面、右侧、背面第三人称相机。第三人称仍看到完整头部及身体，可以对照腕部、袖口、肘部与裙子避让的实际关系。点击任一机位标题放大，点击右上“四机位总览”恢复。

| 控制 | 用途 |
|---|---|
| 1–4 / 武器按钮 | 步枪、左轮、霰弹、狙击，读取正式四份 WeaponData |
| 瞄准 | 检查进入、退出瞄准的过渡 |
| Space / 开火 | 使用正式网络射击表现入口和射击 tick 去重 |
| 循环射击 | 按正式武器射击间隔重复，空弹后自动换弹 |
| R / 换弹 | 展示充能／换弹过程，时长来自正式 WeaponData |
| P / 暂停 | 冻结当前动作；暂停时仍能调整观察机位和切换显示布局 |
| 播放速度 | 0.1–1 倍慢放 |
| 玩家第一人称 | 切换正式 DollSingerView 的第一／第三人称姿态驱动 |
| 俯仰 | 检查抬头、低头后的手势 |
| 观察机位环绕 / 距离 | 同时环绕三个观察相机；近距离看双臂，远距离看全身 |

演示驱动只在本场景生效：使用与 `DollSingerNetworkPresentation` 相同的公开表现接口注入静止、视角、武器、弹药、射击及装填状态。正式网络 Prefab、武器配置和游戏入口不修改。为便于直接 Play，停用此实例的 Ghost、伤害和位移驱动，不创建 Host／服务器、不模拟命中伤害或库存同步。因此本场景是**正式玩家表现的演示与排查环境**，不是联机验收。

`WeaponGestureLab.cs` 驱动控制和四份运行时 RenderTexture；`WeaponGestureLab.uxml/.uss` 为 UI Toolkit 界面；`WeaponGestureLabSetup.cs` 创建／打开独立场景。场景仅首次不存在时生成，不会自动覆盖手工修改。退出时释放 RenderTexture 和玩家实例，恢复原先的时间倍率与鼠标状态。

本次已撤回未提交的狙击姿态试验，保留上一次提交的手势作为观察基线。场景搭建不代表手腕反转、狙击合框或左轮单动速射动作已经修好；后续动画可在此场景中用正式玩家验收。

## 已验证

Unity 脚本编译通过；实际 Play 验证了四机位实时显示、正式网络 Prefab 引用、第一／第三人称切换、切枪、UI 单发开火、换弹和单机位放大。暂停后游戏时间与手腕位置保持不变，观察机位仍可调整。退出 Play 后时间倍率恢复为 1，演示 RenderTexture 与手部相机均无残留。未启动联网对局或打包独立程序。
