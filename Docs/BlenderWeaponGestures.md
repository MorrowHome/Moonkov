# Blender 手势动作

四种武器均在 `ArtSource/DollSinger/WeaponGestures.blend` 中制作并保存为 Blender Actions。现有 `LuoTianYi_DollSinger.fbx` 的蒙皮、手指和扭转辅助骨骼完整，本任务不需要原始 MMD 模型。

- Rifle_ThunderSeal：双指手势。
- Revolver_SingleActionFan：右手指枪、竖起拇指；开放左掌下压拨击“击锤”，随后抬手回位。每次正式射击 tick 播放一次，重复的预测／确认 tick 不会重复启动。
- Shotgun_Rupture：相向的弧形掌势，双臂共同回弹。
- Sniper_ClosedViewfinder：相对的两组拇指／食指闭合直角框，食指各节伸直。本轮按用户最新确认的 Blender 长方框原样保留，长宽由模型指长确定。

每种武器都有 `_pose`、`_fire`、`_reload` 三个 Action。源文件默认打开狙击定姿，包含 Owner / Front / Side / Back 相机。源文件位于 Assets 外，Unity 不会自行调用 Blender 对源文件进行模型导入。

## 接入

`Assets/DollSinger/Animation/Blender/WeaponGestures.json` 为 60 fps 的完整双臂旋转烘焙，覆盖 101 个节点，包括真正有蒙皮权重的 `腕捩`、`手捩` 以及手指。使用源 FBX 的真实绑定轴，将 Blender 右手坐标系转换到 Unity；不会重新进行 Humanoid 肌肉映射。

正式 `DollSingerNetworkPlayer.prefab` 和本地 `DollSingerPlayer.prefab` 的 HaloAim 均引用此数据。`DollSingerBakedGestures` 在 Animator 后播放局部旋转；瞄准只对双臂整体施加共同方向修正。旧 Halo Aim 手势层在使用此烘焙时权重为零，旧的按屏幕锚点摆手腕、二节臂重解算已移除。每帧动画求值前恢复辅助骨骼，避免退出瞄准、暂停或再次启用时残留姿态。衣裙避让继续使用原有手势优先级。

手部相机调整投影取景，不改变手指、腕部或小臂姿态。世界光环、瞄准射线和权威伤害路径继续由原系统负责。三个第三人称相机观察同一正式实例的完整模型。

## 修改和导出

在 Blender 中修改相应 Action，并保存源文件后，使用只导出模式：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python Tools/Blender/weapon_gestures.py -- --export-only
```

此模式读取保存的 Blender 动作，不调用姿态生成器。退出并重新进入 Unity Play，加载更新后的数据。即使关闭 Domain Reload，资源缓存也会检测内容变化。

不带 `--export-only` 会从生产 FBX **重新生成**整个作者文件和动作，覆盖手工编辑，只用于重建初始资源。`Tools/Blender/DollSingerBind.json` 为原 FBX 的绑定轴参考；更换模型骨架时需重新校准。

## 验证

在 `WeaponGestureLab.unity` 中 Play，再运行 `Tools / Moonkov / Validate Blender Gestures In Playing Lab`。会检查四种武器的定姿、完整射击和装填时间段，所有手指可见性，第一／第三人称模式，俯仰 -65/0/+65，退出／进入瞄准和暂停恢复。测试不会启动服务器或伤害判定。

本轮通过记录见 [PlayChecks.txt](AI/BlenderGestureValidation/PlayChecks.txt)。实际观测的左轮左掌行程约 123 mm；源烘焙与运行时局部旋转差小于 0.001°。狙击食指最大折角约 0.028°，拇指与食指直角误差约 0.001°，两个接点间隙最大约 0.155 mm。

以下两图使用相同实际第一人称手部相机。Blender 保留源 Action 的双臂局部姿态，只匹配正式玩家的身体位置及双臂共同朝向。独立对比所有手势关节的世界坐标，最大差约 0.0007 mm；图中手形与关节轮廓一致，材质／灯光分别来自 Blender Workbench 和 Unity 正式渲染。

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
