# Hatsune Miku 追加角色

Hatsune Miku 作为第二个可选角色接入。`GameSettings` 保留 DollSinger 的角色编号 `2`，Miku 使用编号 `3`；出击准备页选择角色，选择保存至 `Moonkov.PlayerCharacter`。人物菜单展示、玩家生成及尸体外观使用同一编号。

仓库和背包中的 Miku 使用独立的动态人物预览；背包优先根据实际拥有者模型识别角色，仓库根据已保存的选择显示。现有 DollSinger 静态人物／尸体肖像及其装备锚点保留，只在 Miku 路径替换人物预览，避免把新角色套在原角色肖像上。关闭面板或切换角色时释放旧预览。

## 模型来源与转换

输入素材为用户提供的 `Hatsune Miku_by_Boku_Wa_Luka/Hatsune Miku.pmx`。源 PMX 注释列出 `Aster/JosueMMD13/Sega/Craft Egg` 与 `CRYPTON FUTURE MEDIA, INC`；文件夹没有另附模型使用许可。附带 Shadekai shader 的 README 禁止该 shader 商用；本次 Unity 材质使用项目现有 shader，没有移植 Shadekai。

转换使用上游 [MMD Tools](https://github.com/MMD-Blender/blender_mmd_tools/tree/e321bd02dd0288a3dc9f1974c06597803c060e11) `4.5.14`，固定 revision `e321bd02dd0288a3dc9f1974c06597803c060e11`，Blender `5.2.2 LTS`。addon 及其随附 OpenCC wheel 放在临时目录，只在后台 Blender 进程内注册，不安装到用户配置。

获取上述 revision 的仓库 ZIP 并解压到临时目录后，在项目根目录执行：

```sh
/Applications/Blender.app/Contents/MacOS/Blender \
  --background --factory-startup --python-exit-code 1 \
  --python Tools/Models/import_hatsune_miku.py -- \
  --mmd-tools /tmp/moonkov-miku-import/blender_mmd_tools-e321bd02dd0288a3dc9f1974c06597803c060e11 \
  --source "/Users/morrow/Downloads/Hatsune Miku_by_Boku_Wa_Luka/Hatsune Miku.pmx"
```

运行前确认没有其他 Blender 重型任务。项目任意时刻最多一个 Unity Editor；不要同时导入、构建或运行多个项目。

- `ArtSource/HatsuneMiku/HatsuneMiku.blend`：完整 MMD 导入源，保留原骨名、形态键、材质信息、附加旋转、IK、112 个刚体、151 个关节，基础贴图已打包。用于源模型编辑；不放入 `Assets`，Unity 不会自动调用 Blender 导入它。
- `Assets/HatsuneMiku/Art/Model/HatsuneMiku.fbx`：用于 Unity 的静态绑定骨架、蒙皮和 26 个顶点表情形态键。238 个原骨骼加 10 个真实指尖辅助骨；不导出 PMX grant、IK、物理约束或驱动器。
- `Assets/HatsuneMiku/Art/Model/HatsuneMikuImport.json`：原始文件哈希、转换工具 revision、骨名映射、原始物理参数、米制碰撞尺寸、物理链根、材质贴图对应与 FBX 回读检查结果。
- `Assets/HatsuneMiku/Art/Textures/tex_bdy_MIK_00_C.png`、`tex_chr_MIK_00_C.png`：源基础颜色贴图。网格保留 `body`、`skirt`、`head` 三个材质槽，前两者使用 body 贴图。

缩放系数为 `0.08040068047305701`，网格总高度约 `1.65 m`。坐标采用 `Blender=(PMX.x, PMX.z, PMX.y)`，对应 Unity 的 `(-PMX.x, PMX.y, -PMX.z)`。左右身体骨转为 `.L/.R` 后缀，保留手指全角数字；头发、领带和裙子骨名保留源命名。Humanoid 下肢映射至 `足`、`ひざ`、`足首`、`足先EX`；源 `足D/ひざD` 没有蒙皮权重。

## Unity 接入与运行文件

编辑器入口由 `Assets/Editor/HatsuneMikuSetup.cs` 提供，顺序为：

1. **Tools → Hatsune Miku → Import Model And Export Bind**：配置 Miku Humanoid，并导出实际 Unity 绑定至 `Tools/Blender/HatsuneMikuBind.json`。
2. `python3 Tools/Blender/retarget_hatsune_miku.py`：根据两个角色的实际绑定、手形与臂长生成 Miku 专属武器／近战旋转库，不覆盖原动作库。
3. **Tools → Hatsune Miku → Build Additional Operator**：生成角色预制体、菜单设置、材质及独立演示场景，并注册玩家和尸体资源。
4. **Tools → Hatsune Miku → Update Character Translations**：由 `HatsuneMikuSetup.UpdateTranslations()` 将 `HATSUNE MIKU`、`SELECT OPERATOR` 写入 `MoonkovUI` 英文／简体中文 String Table；中文值读取 `Assets/Localization/Translations.json`。此步骤不重建字体资产。

构建器的输出路径为：

- `Assets/HatsuneMiku/Prefabs/HatsuneMikuPlayer.prefab`
- `Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab`
- `Assets/HatsuneMiku/Prefabs/HatsuneMikuMenuVisual.prefab`
- `Assets/HatsuneMiku/Prefabs/HatsuneMikuCorpseVisual.prefab`
- `Assets/HatsuneMiku/Scenes/HatsuneMikuDemo.unity`
- `Assets/HatsuneMiku/Animation/WeaponGestures.json`
- `Assets/HatsuneMiku/Animation/MeleeGestures.json`
- `Assets/Resources/Moonkov/HatsuneMikuMenuCharacterSettings.asset`

输入、移动、姿态、视角、光环、武器玩法、网络表现和尸体播放继续使用现有系统。Miku 使用自己的 Avatar、网格、材质、角色配置与完整旋转烘焙。头发、领带和裙子通过 `SecondaryBoneSpring` 的角色专属链引用及碰撞代理运行；手部避让继续使用 `SkirtHandAvoidance`。

Miku 的 MMD 链交替包含静态 `*_offset` 与实际变形的 `EX_*` 骨。若将每个 offset 也当作弹簧关节，极短段会额外叠加弯曲，导致落地静止、气流为零时双马尾仍卷到腰部。Miku 通过 `SecondaryBoneSpring.passiveChainBones` 标记这些静态 offset：遍历继续到子节点，但 offset 本身不创建模拟关节，保持绑定姿态；头发／领带实际模拟 18 个 EX 骨。DollSinger 的该配置为空，继续使用原有链绑定行为。

不能直接使用 DollSinger 的骨骼旋转 JSON：两个模型的绑定轴、手长和臂长不同。专属烘焙显式处理加权扭转辅助骨，避免把 PMX grant 与已烘焙旋转叠加。

Miku 另挂可选组件 `HumanoidTwistFollowers`，从 Humanoid 求值后的上臂／前臂关节提取轴向扭转，为各段的三个旁支辅助骨分配 `0.25 / 0.5 / 0.75` 旋转。该组件只写辅助骨，在武器／近战专属烘焙之前执行，因此普通移动与护裙动作也能驱动原 PMX grant 对应的蒙皮节点。菜单采用手动动画图求值，在每次求值前恢复辅助骨，求值后更新扭转，再播放专属手势；原 DollSinger 没有挂此组件。

`head` 材质同时包含脸与头发，第一人称不能整槽隐藏。前发的实际加权骨前缀为 `EX_Center_hair`、`EX_Left_CS_hair`、`EX_Right_CS_hair`；双马尾使用 `EX_Left_BS_hair`、`EX_Right_BS_hair`，需要保留。原模型没有眼球骨，眼位用角色配置标定；视线与眨眼等 26 个形态键保留在网格中。

## 已完成验证

转换脚本使用同一个 Blender 进程重新导入交付 FBX，得到 7,162 顶点、9,071 三角形、248 骨、26 个表情形态键、3 个材质槽；材质槽三角形数量依次为 `4295 / 850 / 3926`。所有原 PMX BDEF 蒙皮权重逐项一致，最大权重误差 `0`；绑定网格和形态键坐标最大误差约 `2.76e-7 m`，骨骼绑定位置最大误差约 `5.60e-7 m`，回读高度 `1.6500000115 m`。详细值位于导入 manifest 的 `fbxRoundTrip`。

Unity `6000.5.10f1` 的编辑器检查通过，报告保存在 `Temp/HatsuneMikuValidation/checks.json`。导入后的 Unity 网格有 7,672 顶点，材质／法线边界拆分使顶点数增加；三角形数量仍为 9,071，26 个表情形态键和三个材质槽保留。按完整 Renderer 变换后的世界坐标测得高度 `1.64999986 m`。第一人称遮挡网格保留 5,971 个三角形，第三人称原网格保持完整。

本次验收快照提交于 `Docs/AI/HatsuneMikuValidation.json`，菜单实机预览为 `Docs/AI/HatsuneMikuPreview.png`。网络 Prefab 的 Ghost ID 已显式保存为自身资产 GUID。

- 12 个扭转辅助骨完成 24 组实际骨架姿态检查，覆盖左右上下臂、整体旋转、纯弯曲、轴向扭转和混合姿态；角度分配及重复调用误差均为 `0°`，主关节不变，辅助骨确实影响蒙皮，恢复后不残留上一姿态。
- 273 组枪械动作／俯仰／射击装填采样、216 组近战采样和 36 个 Humanoid 动画片段通过，未产生非有限顶点或失控网格。狙击闭框实际最大指尖间隙为 `0.19087781 mm`（约 `0.191 mm`），食指最大折角约 `0.0198°`，直角误差约 `0.00026°`。
- 连续 240 帧头发／裙子模拟及随后传送、长帧重置检查通过，运动与下落会驱动物理链，骨骼位置保持有限。
- 新增 600 帧静止收敛回归，确认只模拟 18 个头发／领带 EX 骨，offset 不参与模拟；收敛后双马尾末端最大高度为 `0.387992859 m`，不再保持此前卷至腰部的状态。孤立 Play 场景的静止复查也已观察到两侧末端约为 `0.39 / 0.36 m`；完整 Play 流程结果另列下节。

已生成并复核真实 Unity 渲染的材质与待机、步枪、狙击、匕首、能量剑画面：`idle.png`、`rifle.png`、`sniper.png`、`melee-Dagger.png`、`melee-EnergySword.png`，以及两种近战的 `grip-*.png`，位于同一临时验证目录。以上为编辑器有限姿态采样，不能保证任意动画与动态衣物组合都完全没有穿模；没有启动联机验收。

## Play Mode 单 Editor 验收

在唯一 Unity Editor 中运行 `HatsuneMikuDemo`，确认 `Time.frameCount` 持续推进，世界列表只有 `Default World`，没有创建 Client/Server world。`Tools/Validation/HatsuneMikuPlayChecks.cs` 通过 Pipeline `run_script` 执行，完整结果位于 `Temp/HatsuneMikuValidation/play-checks.json`。

- 连续运行 669 帧；真实本地移动约 `2.548 m`，蹲伏、趴下、踮脚、站立四次姿态切换通过。
- 第一人称手臂投影激活，步枪瞄准／开火表现正常；前发和头部没有遮住视野。
- 5 m 下落激活气流与护裙（采样权重约 `0.442 / 0.475`），随后正常落地。静止与落地收敛后的最大发尾高度分别约 `0.3865 / 0.3869 m`。
- 角色预览 `2 → 3 → 2 → 3` 四次切换通过；Miku 仓库预览随角色切换出现／释放，关闭仓库后清理。验证恢复原角色偏好。
- `play-third-person.png`、`play-first-person.png`、`play-first-person-rifle.png`、`play-fall-cover.png` 和 `menu-miku.png` 保存实际运行画面。未进行网络连接、多人同步或独立客户端验收。

复跑时先在唯一 Editor 中进入 Miku Demo Play Mode，然后执行：

```sh
unity command run_script --project-path "$PWD" --caller plugin --skill unity-cli --format json -- \
  --file Tools/Validation/HatsuneMikuPlayChecks.cs --entry HatsuneMikuPlayChecks.Run --timeout_ms 55000
```

结束后退出 Play Mode。脚本仅在指定演示场景运行，拒绝存在 Client/Server world 的环境。
