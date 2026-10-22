# 近战动作与武器表现

2026-10-22。状态：源码／独立资源已制作，等待总协调安排 Unity 编译和实际画面验收。本文件不将 Blender 预览或本地状态注入称为正式地图、权威命中或联机验收。

## 资源与动作

`ArtSource/DollSinger/MeleeGestures.blend` 为独立作者文件，沿用生产 FBX 的原骨架及 101 个双臂节点，包括有蒙皮权重的腕捩、手捩和手指。没有重生成、替换旧枪械 .blend 或 WeaponGestures.json。当前近战烘焙 revision 为 `ca75eab36800`。

拳头、匕首、能量剑各含 idle、equip、light0、light1、light2、heavy、recover、holster 八个源 Action，分别有 0／±40／±70 度俯仰采样，60 fps。攻击中的恢复由完整攻击 Action 的最后一段播放；取消从当前完整双臂目标混合回警戒，避免忽然跳到另一攻击的收势。独立 recover／holster 源 Action 也保留供后续作者编辑。

- 拳头：左刺拳、右直拳、短勾拳，重击收臂后冲拳。手指握合，拇指横扣在屈指外侧，不以翻腕或镜头抖动代替出拳。
- 匕首：外划、回划、突刺，重击收臂重刺。刀身有实体厚度、中脊与尖端，握柄、护手和尾帽分开；刃长约 0.23 m。
- 能量剑：左右斜斩及直刺，重击有明显蓄势下劈。约 0.72 m 薄亮刃、较细白色核心及实体发射器／握柄；短拖尾只在规则有效窗显示，不依赖 Bloom 才能辨认。

动作与刀剑均独立制作。外部资源查询与归属记录见 `ArtSource/DollSinger/MeleeGestures-Sources.md`；本轮没有导入外部动作、模型或贴图，也没有采购资源。

## 正式接入

`DollSingerNetworkPresentation.OnGhostLinked` 在客户端给实际角色配置 `DollSingerMeleePresentation`。其 Update 读取实际 Ghost 的 MeleeWeapon／AttackWeapon、AttackSequence／CancelSequence／HitSequence、Phase、Combo、Elapsed 和 HitPoint 等字段，构造 MeleeViewState 并实际调用 PresentMelee。拳头使用类型，不创建库存物品。权威选择、按键、耐力、伤害和库存由近战核心及装备组负责。

MeleeRules 是唯一运行时时间定义。采样器将源 Action 的前摇、有效窗、恢复分别重映射到 `definition.Timing(attack, combo)`，不从动画事件触发伤害。序列号变化启动新攻击，取消／换装／倒地停止拖尾；Epoch 变化及 Ghost 重用会清除序列缓存，初始同步不补播旧命中。命中序列只生成已裁决的短火花并提供 AuthoritativeImpact 事件。

HaloAim 的 ExternalHandPoseActive 将完整双臂的控制权交给近战，已有 SkirtHandAvoidance 读取 OwnsHandPose 后清除裙装 IK／手腕修正，裙装脚本另将近战期间的护裙层权重归零，避免装备混合阶段重新写双臂。动画每帧求值前恢复辅助骨骼，求值后只采样原局部旋转，朝向校正作用于完整双臂。武器握点和尺寸补偿原 MMD 手骨的厘米缩放，避免挂到手骨后模型放大 100 倍。

拥有者和第三人称使用同一角色、骨骼和时间线。拥有者手臂使用独立固定取景；挥砍过程中不追踪手腕包围盒，不改变关节角度，不接管世界相机的放大。剑／匕首各有世界和拥有者渲染副本，绑定同一手骨；拥有者副本只由手部相机绘制，观察者看世界副本，避免两套投影让武器离开手掌。

独立资源放在各自的 Resources 子目录，仅在角色初始化时加载；烘焙解析结果共用缓存。刀剑网格与材质初始化后复用，拖尾及命中效果有限生命周期，不进行每帧 Instantiate 或重新解析 JSON。

## 作者重制与预览入口

```sh
blender --background --python Tools/Blender/melee_gestures.py
blender --background --python Tools/Blender/melee_gestures.py -- --export-only
blender --background --python Tools/Blender/render_melee_authoring.py
```

默认模式重做本文件的近战动作／刀剑，不写枪械资源。`--export-only` 从保存的近战 Actions 导出。Blender 预览输出到 `Library/MeleeAuthoring`，使用源模型眼位、固定取景和四个观察方向，属于制作预览。

待总协调取得唯一 Editor 令牌并导入完整源码后，可在已有 WeaponGestureLab Play 中使用以下入口，复用其同一个正式玩家及四个实际 RenderTexture，不新建玩家或改场景／旧 Prefab：

```csharp
MeleePresentationPreview.Begin();
MeleePresentationPreview.PlayAttack(MeleeWeaponKind.EnergySword, MeleeAttackKind.Light, 0);
MeleePresentationPreview.Show(MeleeWeaponKind.Dagger, MeleeAttackKind.Heavy, 0, .40f);
MeleePresentationPreview.Capture("dagger-heavy");
MeleePresentationPreview.End();
```

Capture 写入 `Library/MeleePreview`；Show 用固定时刻，PlayAttack 连续播放。实验室原有的俯仰／观察角度与第一／第三人称切换可继续操作。该入口只注入表现状态，不验证网络、攻击合法性或伤害。

本组尚未调用 Unity 命令、编译或 Play；必要的下一步是三类轻／重完整轨迹、待机中央留白、握持／手指／小臂、转头与取消的实际四机位检查，并由核心组验证真实命中时机、遮挡及正式局链路。不要据当前源预览宣称这些已通过。

## 攻击几何待核心对齐

本组已在 DTO／网络桥加入 Epoch，换局与 Ghost 重用会 ResetPresentation 清除攻击、取消和命中缓存。

当前作者轨迹：拳头 light0 左刺拳、light1 右直拳、light2 横勾拳、heavy 前冲直拳；匕首 light0／1 为相反方向划击，light2／heavy 前刺；能量剑 light0／1 为相反方向斜斩，light2 前刺，heavy 纵向下劈。核心不能将直刺和纵劈统一处理为水平扇扫。源角色单臂长度约 0.338 m，刀刃有效长度约 0.23／0.72 m；权威接触距离须结合攻击起点与目标碰撞半径校准，不能仅按常见成人角色的臂长套用。该项目前仍待核心几何与实际命中验收。
