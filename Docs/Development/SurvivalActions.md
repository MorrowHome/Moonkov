# 搜刮、吃喝与治疗动作

正式源文件是 `ArtSource/DollSinger/SurvivalActions.blend`。它使用洛天依正式网格、原骨轴和库存物品。原瞄准源 `WeaponGestures.blend` 不变。四段动作已导入 Unity，按洛天依、初音未来和诗岸的正式骨架播放。

源文件按 60 帧/秒制作。每段 3 秒。NLA 轨道可独立编辑。

| 动作 | 时间线 | 物品与接触 |
| --- | --- | --- |
| 搜刮 | 1–181 | 沿取物动作伸进开口。箱框只是参照，不导出 |
| 进食 | 211–391 | 右手持软袋，左手撕下并持有封口条 |
| 喝水 | 421–601 | 左手旋开并持盖，右手抬瓶，随后合盖 |
| 注射 | 631–811 | 拔针帽，露出左前臂，拇指压按钮，针尖接触裸露皮肤 |

搜刮参考 Quaternius 的 `PickUp_Table`。吃喝参考 `Consume`。注射参考 BURNER 的持针动作。接触、握法和动作路径已按实际模型重做。作者、授权与下载记录见 [注射资源](../../SourceData/Animations/Burner/README.md)和 [随包署名](../../Assets/StreamingAssets/MoonkovSurvivalActionCredits.txt)。原始注射模型只作研究，不作为游戏角色网格。

食物包装改为薄壁软袋。开口有真实内壁与薄边。库存原有字样烘焙到 `RationPrint.png`，避免凸字和封口筋悬空。瓶身和针筒的标记贴合曲面。针帽、瓶盖、封口条和活塞是独立物体。这些是交互变体。库存原预制体与图标保持原样。其他食品仍使用现有美术，按各自尺寸适配握点。

## 制作与检查

先编辑保存的 Blender 动作。不要用旧的 `fit_survival_actions.py` 覆盖它。该工具只保留作原型记录。`survival_actions_authoring.py --rebuild` 也会替换本任务的源文件，只能在明确要重建起点时使用。

`SourceData/Animations/Survival/AuthoringSeed.json` 保留初始姿态，避免制作脚本依赖忽略目录。`ReviewProps.json` 保留实际库存几何。`Calibration.json` 保留三套正式角色的嘴部和前臂测量。

`revise_survival_drink.py`、`revise_survival_eat.py`、`revise_survival_injection.py` 和 `revise_survival_search.py` 记录本次源动作的制作方法。它们会改写对应 Blender 动作。手工修改源文件后，不要无意重跑。`bake_ration_print.py` 与 `refine_survival_props.py` 制作包装交互变体。

整条手臂复用瞄准手势的骨轴与扭转分配。右掌法线是 `-Cross(指根横向, 掌心前向)`，左掌符号相反。握圆筒和捏薄片需要不同手势，不能只换物品偏移。接触按变形后的皮肤检查，不能只看关节中心。

`finalize_survival_authoring.py` 整理动作、修复本源文件的贴图路径，并处理四元数正负号连续性。相邻四元数必须在同一半球，否则 Blender 的分量插值会在半帧翻转。无权重的指尖终点继承末节旋转，不再从旧姿态独立取旋转。

`validate_survival_authoring.py` 检查实际保存的动作。它核对每段 181 帧的骨架往返误差，并抽查实际蒙皮的接触与非相邻部位穿插。几何检查使用 Blender 资产 QA。细小零件的体积检查先平移到原点附近，避免单精度在世界坐标下丢失有效位。物件形状、缩放和相对位置保持一致。包装的 `PrintUV` 用于印字，`ExportUV` 用于完整网格的展开检查。

`render_survival_review.py` 输出完整双视角预览。第一人称使用正式眼骨、65 度视角、相同手臂裁切规则和固定相机偏移；搜刮预览向下看 55 度。它用于源动作检查，不能替代 Unity 的头部俯仰、材质与运行时验收。报告和预览写入忽略目录 `LocalData/Actions/Blender`。 已保存的检查摘要见 `SourceData/Animations/Survival/BlenderValidation.json`，包含源文件 SHA-256。当前 6 组交互物件几何检查通过。四段动作共检查 724 帧骨架，并抽查 52 帧蒙皮表面；抽查未发现非相邻部位穿插。指腹接触压缩最多约 2.2 毫米。手持物件离地与薄膜内外壁相向属于预期结构。完整动作仍须结合预览判断。

## 导出与接入

1. 保存并检查 `SurvivalActions.blend`。
2. 用 Blender 运行 `Tools/Blender/export_survival_runtime.py`。它只读取源文件，输出 `BlenderExport.json`，不重新生成姿态。导出时临时取消隐藏物体的视口动画，保证未出现的封口条也会更新位置；保留渲染显隐。
3. 在 Unity 编辑模式运行 `Tools/Validation/ExportSurvivalRigGeometry.cs`，入口为 `ExportSurvivalRigGeometry.Run`。`RigGeometry.json` 记录正式手臂蒙皮、手掌厚度和实际库存物品尺寸。
4. 用 Python 运行 `Tools/Blender/build_survival_runtime.py`。它输出三套 `Actions/*.json` 与 `RuntimeBake.json`。洛天依的搜刮、配给袋、水瓶和针剂直接保留已检查的源动作。其他骨架与物品离线适配完整手臂和手指。针尖接触点与变形后的正式手臂相交，不再只按骨骼半径估算。初音的前臂全被袖子覆盖，改用裸露的左上臂。右肘限制在肩下，避免抬到脸前。
5. 在 Unity 运行 `Tools/Moonkov/Actions/Build opened inventory prop variants`。它建立其他包装的分件，再导入源文件的袋、瓶、针剂和独立小件。录音安装入口为 `Install recorded action foley`。

运行时只采样关节旋转与道具轨迹。位置轨迹按米保存。使用 `bone.position + bone.rotation * offset`，不能对它再次 `TransformPoint`，否则 MMD 的缩放会重复生效。多个人物共享解析后的同一动作库，不逐帧加载 JSON，也不在运行时求解手腕 IK。

`RaidHandActionSystem` 读取真实库存、战术道具和人形敌人的计时。动画不扣物品，也不决定完成。动作序号、物品代码与剩余时间控制展示。搜索揭示下一件物品时不重启手势。新增 Ghost 字段要求客户端和服务器一起重新编译。

嘴部测量使用 `あ` 形变区域。`SkinnedMeshRenderer.BakeMesh` 已包含模型缩放，转世界坐标时只再施加位置与旋转。嘴部小幅变化在肩根修正整条手臂。搜刮沿用原第一人称身体视角。吃喝使用固定 65 度手臂相机。注射复用去头部遮挡的完整第一人称身体网格，从眼部向手臂俯看；不再后退相机露出肩部截面。三个角色使用各自固定的俯角，过程不追随手部边界缩放。其他观察者仍看完整人物。

开包装、开瓶、咬食、吞咽和针剂操作使用录制音效。裁剪脚本为 `Tools/Audio/build_survival_action_audio.py`，需要 ffmpeg 和 numpy。音效按源动作标记触发，同一快照不会重放。取消和死亡会停止当前声音。搜刮继续复用 `LootSearchWorld`。未包装食材不播放拆包装声，氧气罐不归类为喝水。

旧 `RaidActionAnimationSetup.BakeAll()` 只生成原型种子。发现第二版动作库时会拒绝覆盖。其他治疗仍使用原治疗参考动作，不属于这四段 Blender 动作。

## 验证入口

`SurvivalBlenderIntegrationChecks.Run` 检查三套骨架、全部食水与医疗物品的对应数据、保存的骨旋转、实际手臂蒙皮距离、分件、录音和取消清理。`SurvivalActionChecks.Run` 检查真实服务端库存计时、重复快照、取消、死亡和移动骨架隔离。

`SurvivalActionRuntimeChecks.Run` 在现有单 Editor 的 MainMenu 播放模式运行。它只创建临时角色，不建立战局或修改玩家档案。采样必须在 `RenderPipelineManager.endCameraRendering` 回调内完成，并限定为夹具的游戏相机。SceneView 的重绘也会发出渲染回调，不能混用。普通 `await` 恢复时可能处于 Update 与 LateUpdate 之间，此时读取到的是本帧恢复后的骨架，不能用来判断动作被覆盖。声音按实际 AudioSource 的播放记录检查，避免短录音结束后才取样。截图先记录该帧姿态，再在渲染回调外恢复并重绘；不能在 SRP 渲染回调里再次提交渲染请求。

原生截图和详细结果保存在忽略目录 `LocalData/Actions`。正式战局、联机和 Player 构建未验收。

本轮 2495 项编辑检查、174 项真实服务端检查和 175 项实际播放检查通过。播放时启用正式 Animator、扭转骨跟随和瞄准组件。三套角色共检查 12 组动作，保留第一人称和观察者截图。针尖与实际变形手臂表面的距离约为洛天依 2.46 毫米、初音 1.00 毫米、诗岸 0.99 毫米。源文件 724 帧骨架和 2172 次道具轨迹往返检查通过。完整摘要见 `SourceData/Animations/Survival/RuntimeValidation.json`。
