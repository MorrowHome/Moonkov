# Blender 手势动作

四种武器均在 `ArtSource/DollSinger/WeaponGestures.blend` 中制作并保存为 Blender Actions。现有 `LuoTianYi_DollSinger.fbx` 的蒙皮、手指和扭转辅助骨骼完整，本任务不需要原始 MMD 模型。

- Rifle_ThunderSeal「雷轨·交叉剑诀」：双手剑指交叉，射击时右手向前突刺并弹动中指；装填时左手旋印。两手前后分层，避免指节互穿。
- Revolver_SingleActionFan「审判·剑指拨锤」：右手食指沿瞄准方向前伸、拇指作击锤；左手分开的剑指掠过击锤，随后抬手回位。每次正式射击 tick 播放一次，重复的预测／确认 tick 不会重复启动。
- Shotgun_Rupture「魔王掌炮」：右手恶魔角起势，左手托住右腕；射击时五指张开、双臂回弹，装填时托手做短行程抽动。拇指绕过指节展开。
- Sniper_ClosedViewfinder：相对的两组拇指／食指闭合直角框，食指各节伸直。本轮按用户最新确认的 Blender 长方框原样保留，长宽由模型指长确定。

每种武器都有 `_pose`、`_fire`、`_reload` 三个 Action。普通武器另有 `_Pitch<角度>_...` 的完整双臂俯仰姿态，覆盖 -80° 到 +80°；手形仍使用同一组源手指姿态，重新安排肘部以绕开躯干、头部与裙装。采样器在相邻作者姿态之间插值，不在 Unity 里重新解算手腕。源文件默认打开步枪交叉指诀，包含 Owner / Front / Side / Back 相机。源文件位于 Assets 外，Unity 不会自行调用 Blender 对源文件进行模型导入。

## 接入

`Assets/DollSinger/Animation/Blender/WeaponGestures.json` 为 60 fps 的完整双臂旋转烘焙，覆盖 101 个节点，包括真正有蒙皮权重的 `腕捩`、`手捩` 以及手指。使用源 FBX 的真实绑定轴，将 Blender 右手坐标系转换到 Unity；不会重新进行 Humanoid 肌肉映射。

正式 `DollSingerNetworkPlayer.prefab` 和本地 `DollSingerPlayer.prefab` 的 HaloAim 均引用此数据。`DollSingerBakedGestures` 在 Animator 后播放局部旋转；瞄准只对双臂整体施加共同方向修正。旧 Halo Aim 手势层在使用此烘焙时权重为零，旧的按屏幕锚点摆手腕、二节臂重解算已移除。每帧动画求值前恢复辅助骨骼，避免退出瞄准、暂停或再次启用时残留姿态。衣裙避让继续使用原有手势优先级。

普通武器的手部相机使用居中的等比投影和位置取景，将手指放在画面下方，保留中央瞄准空区。居中的投影使沿瞄准方向前伸的食指延长线指向准星；不改变手指、腕部或小臂姿态。抬头／转头时同步更新构图，避免沿用旧角度的取景而漂移、跳变。双臂共同朝向使用玩家的稳定上方向，再叠加主动侧倾；不再使用可能与仰视方向接近平行的动画胸口上方向，避免极端抬头时整组手臂滚转。第一人称头／颈部在 Humanoid LookAt 后移除额外侧转，眼位不再随纯俯仰横向漂移。普通武器瞄准时仅拥有者相机隐藏身体表面，避免裙装进入中央；完整阴影和第三人称角色保留。狙击保持已认可的框形与取景。世界光环、瞄准射线和权威伤害路径继续由原系统负责。三个第三人称相机观察同一正式实例的完整模型。

## 修改和导出

在 Blender 中修改相应 Action，并保存源文件后，使用只导出模式：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python Tools/Blender/weapon_gestures.py -- --export-only
```

此模式读取保存的 Blender 动作，不调用姿态生成器。退出 Play，在 Unity 对 JSON 执行 Reimport，再重新进入 Play，加载更新后的数据。关闭自动刷新时，单纯重新进入 Play 不会导入磁盘的新版本；验证器会核对导入资源与磁盘 revision，一致后才开始。即使关闭 Domain Reload，资源缓存也会检测内容变化。

`--revise-other-weapons` 仅重做前三种武器，保留并逐项断言已保存的狙击动作完全相同；用于本轮脚本化重制。

不带以上参数会从生产 FBX **重新生成**整个作者文件和动作，覆盖手工编辑，只用于重建初始资源。`Tools/Blender/DollSingerBind.json` 为原 FBX 的绑定轴参考；更换模型骨架时需重新校准。

## 验证

在 `WeaponGestureLab.unity` 中 Play，再运行 `Tools / Moonkov / Validate Blender Gestures In Playing Lab`。会检查四种武器的定姿、完整射击和装填时间段，所有手指可见性，第一／第三人称模式，俯仰 -65/0/+65，退出／进入瞄准和暂停恢复。测试不会启动服务器或伤害判定。

最终烘焙版本为 `989de9868b58`。Unity 6000.5.10f1 脚本编译完成，当前 Console 为 0 条错误，保留测试前已有的 7 条警告。两份正式玩家 Prefab 均无缺失脚本，且引用同一份最新烘焙。退出 Play 后时间倍率恢复为 1，临时手部相机和演示 RenderTexture 均为 0。

本轮通过记录见 [PlayChecks.txt](AI/BlenderGestureValidation/PlayChecks.txt)。检查包含普通武器中央 12% 方形区域的实际手臂网格遮挡、手指位于屏幕底部、左轮指向准星，以及 16:9／4:3、俯仰 -80° 到 +80° 和连续抬头过程。源烘焙与运行时旋转一致性检查通过（容差 0.12°）；检查还比较肩根的共同世界朝向，防止局部旋转正确却整组转歪。狙击食指最大折角约 0.028°，拇指与食指直角误差约 0.001°，两个接点间隙最大约 0.155 mm。

源蒙皮检查工具 `Tools/Blender/validate_gesture_surfaces.py` 逐帧检查前三种武器的手指屈曲方向、腕部角度，以及手指、双手、前臂、躯干表面的交叉。相邻的同手掌／前臂接缝、肩部连接和未在源 Action 中驱动的头部不纳入源表面交叉判断；头部、动态衣物和中间俯仰姿态通过 Unity 实际变形网格另行检查，仍须结合四机位画面。结果见同目录 `SourceSurfaces.json`。实际 Unity 蒙皮的 45 个角度／射击／装填关键姿态结果见 `RuntimeSurfaces.json`。这些是有限采样，不能替代联网、其他移动姿态或所有衣物动态的验收。

以下为此前已认可的狙击对照，两图使用相同实际第一人称手部相机。Blender 保留源 Action 的双臂局部姿态，只匹配正式玩家的身体位置及双臂共同朝向。独立对比所有手势关节的世界坐标，最大差约 0.0007 mm；图中手形与关节轮廓一致，材质／灯光分别来自 Blender Workbench 和 Unity 正式渲染。

![Blender 同机位源动作](AI/BlenderGestureValidation/Blender-Sniper-Owner.png)

![Unity 实际玩家相机](AI/BlenderGestureValidation/Unity-Sniper-Owner.png)

![正式玩家四机位](AI/BlenderGestureValidation/Unity-FourCameras-Sniper.png)

对照数据与实际相机快照保存在同目录，可重现 Blender 渲染：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python Tools/Blender/render_gesture_comparison.py -- \
  Docs/AI/BlenderGestureValidation/UnitySnapshot.json /tmp/gesture-comparison
```

已检查四机位画面，未出现此前的反手腕或小臂翻转。当前验收针对演示场景中的正式玩家表现；尚未启动联网对局或构建独立客户端。

实际 Unity 蒙皮快照检查（在演示场景 Play 时依次运行，等待 `Temp/GestureRuntimeSurfaces/complete.txt` 出现后再检查）：

```sh
unity command eval_file --project-path . --caller plugin --skill unity-feature-implementation \
  --file Tools/Blender/export_runtime_gesture_surfaces.cs
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python Tools/Blender/validate_runtime_gesture_surfaces.py
```

主动侧倾另外检查四武器 × 三个俯仰角 × 左右侧倾，共 24 组；头部方向、手指可见性、左轮指向和狙击框形通过，见 `LeanChecks.txt`。

实际录像采用同一帧的四个相机输出，左上为第一人称，右上为正面，左下为右侧，右下为背面；录制时将动作时间放慢以便观察。

- [步枪四机位](AI/BlenderGestureValidation/Unity-Rifle-FourViews.mp4)
- [左轮四机位](AI/BlenderGestureValidation/Unity-Revolver-FourViews.mp4)
- [霰弹枪四机位](AI/BlenderGestureValidation/Unity-Shotgun-FourViews.mp4)

- [连续抬头与低头四机位](AI/BlenderGestureValidation/Unity-Pitch-FourViews.mp4)
