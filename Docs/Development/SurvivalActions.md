# 搜刮、吃喝与治疗动作

当前先在 Blender 检查完整动作。正式源文件是 `ArtSource/DollSinger/SurvivalActions.blend`。它使用洛天依正式网格、原骨轴和库存物品。原瞄准源 `WeaponGestures.blend` 不变。新源动作尚未回灌 Unity，也未完成三套角色的运行时验收。

源文件按 60 帧/秒制作。每段 3 秒。NLA 轨道可独立编辑。

| 动作 | 时间线 | 物品与接触 |
| --- | --- | --- |
| 搜刮 | 1–181 | 沿取物动作伸进开口。箱框只是参照，不导出 |
| 进食 | 211–391 | 右手持软袋，左手撕下并持有封口条 |
| 喝水 | 421–601 | 左手旋开并持盖，右手抬瓶，随后合盖 |
| 注射 | 631–811 | 拔针帽，露出左前臂，拇指压按钮，针尖接触裸露皮肤 |

搜刮参考 Quaternius 的 `PickUp_Table`。吃喝参考 `Consume`。注射参考 BURNER 的持针动作。接触、握法和动作路径已按实际模型重做。作者、授权与下载记录见 [注射资源](../../SourceData/Animations/Burner/README.md)和 [随包署名](../../Assets/StreamingAssets/MoonkovSurvivalActionCredits.txt)。原始注射模型只作研究，不作为游戏角色网格。

食物包装改为薄壁软袋。开口有真实内壁与薄边。库存原有字样烘焙到 `RationPrint.png`，避免凸字和封口筋悬空。瓶身和针筒的标记贴合曲面。针帽、瓶盖、封口条和活塞是独立物体。这些是交互变体；库存原预制体与图标尚未替换。其他食品仍使用现有美术。

## 制作与检查

先编辑保存的 Blender 动作。不要用旧的 `fit_survival_actions.py` 覆盖它。该工具只保留作原型记录。`survival_actions_authoring.py --rebuild` 也会替换本任务的源文件，只能在明确要重建起点时使用。

`SourceData/Animations/Survival/AuthoringSeed.json` 保留初始姿态，避免制作脚本依赖忽略目录。`ReviewProps.json` 保留实际库存几何。`Calibration.json` 保留三套正式角色的嘴部和前臂测量。

`revise_survival_drink.py`、`revise_survival_eat.py`、`revise_survival_injection.py` 和 `revise_survival_search.py` 记录本次源动作的制作方法。它们会改写对应 Blender 动作。手工修改源文件后，不要无意重跑。`bake_ration_print.py` 与 `refine_survival_props.py` 制作包装交互变体。

整条手臂复用瞄准手势的骨轴与扭转分配。右掌法线是 `-Cross(指根横向, 掌心前向)`，左掌符号相反。握圆筒和捏薄片需要不同手势，不能只换物品偏移。接触按变形后的皮肤检查，不能只看关节中心。

`finalize_survival_authoring.py` 整理动作、修复本源文件的贴图路径，并处理四元数正负号连续性。相邻四元数必须在同一半球，否则 Blender 的分量插值会在半帧翻转。无权重的指尖终点继承末节旋转，不再从旧姿态独立取旋转。

`validate_survival_authoring.py` 检查实际保存的动作。它核对每段 181 帧的骨架往返误差，并抽查实际蒙皮的接触与非相邻部位穿插。几何检查使用 Blender 资产 QA。细小零件的体积检查先平移到原点附近，避免单精度在世界坐标下丢失有效位。物件形状、缩放和相对位置保持一致。包装的 `PrintUV` 用于印字，`ExportUV` 用于完整网格的展开检查。

`render_survival_review.py` 输出完整双视角预览。第一人称使用正式眼骨、65 度视角、相同手臂裁切规则和固定相机偏移；搜刮预览向下看 55 度。它用于源动作检查，不能替代 Unity 的头部俯仰、材质与运行时验收。报告和预览写入忽略目录 `LocalData/Actions/Blender`。 已保存的检查摘要见 `SourceData/Animations/Survival/BlenderValidation.json`，包含源文件 SHA-256。当前 6 组交互物件几何检查通过。四段动作共检查 724 帧骨架，并抽查 52 帧蒙皮表面；抽查未发现非相邻部位穿插。指腹接触压缩最多约 2.2 毫米。手持物件离地与薄膜内外壁相向属于预期结构。完整动作仍须结合预览判断。

## Unity 原型与待办

本次只交付 Blender 源。工作区中的 Unity 原型仍需接入新源，下述旧入口与旧检查不代表该接入已经完成。

现有原型由 `RaidHandActionSystem` 读取真实库存、战术道具和人形敌人计时。动画不扣物品，也不决定完成。动作序号、物品代码与剩余时间控制展示。新增 Ghost 字段要求客户端和服务器一起重新编译。

嘴部测量使用 `あ` 形变区域。`SkinnedMeshRenderer.BakeMesh` 已包含模型缩放；转换到世界坐标时只再施加位置与旋转。再次 `TransformPoint` 会重复缩放。

原型已移除独立腕部 IK。接触应离线处理整条骨链，运行时只采样。嘴部小幅变化只能在肩根修正整条手臂。原型的通用物品朝向、左右手瓶盖、针帽、包装、活塞行程和三套角色握点仍需按新 Blender 源更新。

开包装、开瓶、咬食、吞咽和针剂操作已选用录制音效。裁剪脚本是 `Tools/Audio/build_survival_action_audio.py`，需要 ffmpeg 和 numpy。搜刮继续复用 `LootSearchWorld`。未包装食材不能播放拆包装声，氧气罐不能归类为喝水。

旧原型的 156 项定向检查只证明旧数据的服务端状态、采样与清理规则。它们不证明新动作观感通过。`SurvivalActionRuntimeChecks.cs` 仍需修正采样时机，再按新源动作完成单 Editor 验收。正式战局、联机和 Player 构建未验收。
