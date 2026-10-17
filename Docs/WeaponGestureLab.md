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

当前四种武器使用 `ArtSource/DollSinger/WeaponGestures.blend` 的完整双臂动作，包含原 MMD 骨架的加权扭转辅助骨骼。Unity 直接采样其局部旋转，原先按屏幕锚点重摆手腕的解算已移除。左轮使用左手剑指拨锤、抬手回位的单动速射循环；狙击沿用本轮确认的 Blender 闭合直角框，食指保持伸直。原模型指长决定框的长宽。源文件、导出方法和对照图见 [Blender 手势](BlenderWeaponGestures.md)。

Play 后可运行 `Tools / Moonkov / Validate Blender Gestures In Playing Lab`，直接检查本场景的正式玩家，输出到 `Temp/GestureValidation/Blender`。这项检查包括源动作一致性、食指伸直、拇指直角、合框、手指可见性、射击、装填及瞄准切换；仍须同时检查输出画面。

## 已验证

Unity 脚本编译通过；实际 Play 验证了四机位实时显示、正式网络 Prefab 引用、第一／第三人称切换、切枪、UI 单发开火、换弹和单机位放大。暂停后游戏时间与手腕位置保持不变，观察机位仍可调整。退出 Play 后时间倍率恢复为 1，演示 RenderTexture 与手部相机均无残留。未启动联网对局或打包独立程序。

2026-10-17：四武器实际 Play 验证通过，第一人称及三个第三人称观察机位均已查看。狙击恢复直角框，食指关节角误差最大约 0.028°、闭合间隙约 0.155 mm；与 Blender 同机位对照图及独立关节坐标对比保存在 `Docs/AI/BlenderGestureValidation`。未进行联网对局或独立程序构建。

本轮普通武器：步枪「雷轨·交叉剑诀」、左轮「审判·剑指拨锤」、霰弹「魔王掌炮」。第一人称手部位于画面下方，左轮食指沿准星方向前伸。实际运行验证增加中央空区的蒙皮三角形检查、指向检查、16:9／4:3 与大角度俯仰检查；源蒙皮交叉及屈指方向另由 `Tools/Blender/validate_gesture_surfaces.py` 检查。狙击动作数组与此前确认版保持完全相同。

抬头回归检查覆盖连续 0° → -80° → 0° → +80° → 0°。除手指可见与准星空区外，还检查头骨的 forward/up 和双臂共同世界朝向，捕获 Humanoid 头部侧转及胸口参考轴退化造成的整组滚转。实际蒙皮的角度关键姿态另检查手、前臂与动态头部／衣物交叉。
