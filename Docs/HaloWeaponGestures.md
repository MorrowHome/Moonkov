# 光环武器手势与第一人称展示

当前正式与本地玩家使用 Blender 完整双臂动作，详见 [源文件、导出与实测对照](BlenderWeaponGestures.md)。四种武器均包含定姿、射击、装填三段动作；切枪由采样器在 0.16 秒内过渡。瞄准仍由玩家相机及正式网络系统决定。

| 武器 | 手势 | 射击表现 |
|---|---|---|
| 步枪 | 双手食指／中指指诀 | 短促回弹 |
| 左轮 | 右手指枪、拇指击锤；左手开放手掌 | 左掌下压拨击并抬手回位 |
| 霰弹 | 相向弧形双掌 | 双臂共同后撤 |
| 狙击 | 双手闭合直角取景框，食指各节伸直 | 保持框形，双臂共同回弹 |

`DollSingerHaloAim` 播放 Blender 烘焙的肩、臂、加权扭转辅助骨骼和手指旋转；瞄准调整双臂的共同方向。旧的屏幕锚点、手腕朝向计算和双节臂重摆已经移除。

`DollSingerView` 保留第一人称头部过滤和完整影子。`DollSingerFirstPersonHands` 从同一蒙皮网格拆出双臂，复用实时骨骼、材质及表情权重，通过拥有者相机的 URP Overlay 展示。拥有者身体网格暂时排除双臂；其他相机继续看到完整角色。

世界相机继续使用原有瞄准倍率，手部相机默认 65°。投影相机按手指范围取景，切换狙击或重新进入瞄准时重新构图；此步骤只移动手部相机。裙子避让在手势期间清除手部 IK／肘部提示，服装弹簧和手部布料碰撞继续运行。

```mermaid
flowchart LR
    Blender[Blender 完整双臂 Actions] --> Bake[原骨架 60fps 烘焙]
    Weapon[武器 / 射击 tick / 装填进度] --> Sample[动作采样与过渡]
    Bake --> Sample
    Sample --> Bones[同一正式玩家骨骼]
    Aim[正式瞄准方向] --> Pair[双臂共同朝向]
    Pair --> Bones
    Bones --> Owner[第一人称手部投影]
    Bones --> Observers[第三人称完整角色]
    Cloth[裙子避让] --> Priority[手势期间让出双手控制]
```

打开 `Assets/DollSinger/Scenes/WeaponGestureLab.unity` 并 Play，可以同时查看第一人称、正面、右侧、背面。`Tools / Moonkov / Check Weapon Gestures` 或 `Validate Blender Gestures In Playing Lab` 均运行新的实际玩家验证，包括源动画一致性、可见性、直角和闭合检查。结果保存在 `Temp/GestureValidation/Blender`，验收须结合画面。

`Capture Playing First Person` 截取 Play 中的实际拥有者相机。旧四个 Humanoid Gesture 动画及其生成工具保留为历史资源；正式玩家引用 Blender 烘焙，编辑旧动画不会改变本轮动作。
