# 领域手印动作

六种光环改用《咒术回战》的领域手印取材。参考是动画截图和漫画分镜。只参考手型，不导入原作图片、特效或模型。

| 光环 | 手印参考 | 手型要点 |
| --- | --- | --- |
| 三律 | 五条悟，无量空处 | 食指伸直，中指交扣。其余手指收起。 |
| 天枢 | 宿傩，伏魔御厨子 | 双手中指和无名指立起。两侧指尖相接。食指和小指收起。 |
| 星棘 | 秤金次，坐杀博徒 | 右手拇指与食指成环。其余三指立起。左手平放。 |
| 坠日 | 羂索，胎藏遍野 | 双手反掌相合。手背朝内。 |
| 岚轮 | 伏黑惠，嵌合暗翳庭 | 双手食指和中指交扣。其余手指收起。 |
| 夜曲 | 虎杖悠仁，领域手印 | 只立起双手中指。其余手指扣合。 |

参考图可在[手印分镜索引](https://newsinfomation.net/archives/52644)逐项查看。五条与伏黑分别对应动画第 7 集和第 23 集。秤、羂索、虎杖分别参考漫画第 182、205、264 话。图中遮住的部分按真实手掌结构补足。动作是适配射击视角的取材，不是逐帧复刻。

步枪、左轮、霰弹枪、狙击，以及蝉鸣、望舒、回环保持原动作。三套动作库的这七组数据在导出前后必须完全一致。不得用四武器导出器覆盖十三武器库。

旧的 `halo_expansion_gestures.py` 全量导出会恢复旧版九种扩展动作。需要修改蝉鸣、望舒或回环时，只对对应动作使用 `--only`。六种领域手印统一使用本页的新源与导出器。

## 动作源与导出

新源为 `ArtSource/DollSinger/DomainSeals.blend`。旧的 `WeaponGestures.blend` 和 `HaloExpansion.blend` 不变。六种手印共 180 个 Action。包含准备、射击、充能、俯仰和三律连发。

手工修改后保存源文件，再运行本机 Blender：

```sh
blender --background --python Tools/Blender/export_domain_seals.py
blender --background --python Tools/Blender/review_domain_seals.py
```

导出器只读保存的 Action。默认写入忽略目录 `LocalData/JujutsuGestureReview/DomainSeals/Staged`。检查通过后加 `-- --install` 安装到三套正式动作库。导出器会核对七组保留动作、原射击时长、采样帧数、连发数量和俯仰档位。

`domain_seals.py` 是重建工具。默认只生成忽略目录里的中立对照稿。只有显式加 `-- --author-actions` 才重建正式源。不要用它导出手工修改。

三套骨架离线适配。双手接触按实际手掌尺寸求解。星棘的拇指按目标手掌重新拟合。运行时沿用缓存的骨旋转插值。本次动作改动没有增加相机、IK 层或网络字段。夜曲不再使用食指指向修正，因为它立起的是中指。

六种动作按真实骨长抬手并前伸。指节角度保持不变。检查第一人称时，交指、捏指和扣指的关键位置必须能看见，不能只露出指尖。可见性与准星遮挡使用实际渲染手部的投影检查。摄像机实现单独维护。

## 检查入口

- `SourceData/Animations/DomainSeals/Export.json` 记录源文件、版本和保留动作。
- `SourceData/Animations/DomainSeals/BlenderReview.json` 记录源蒙皮接触、完整动作手型和旧源一致性。
- `SourceData/Animations/DomainSeals/HeightReview.json` 对照抬手前后的三套指节角度。
- `SourceData/Animations/DomainSeals/SamplerReview.json` 记录正式采样器的 1800 组检查。覆盖三套骨架、六种手印、第一／第三人称采样、俯仰、开火、充能和连发。检查手型、骨长和缩放，不代替摄像机最终构图验收。
- `DomainSealChecks.Run()` 在一个 Editor 中顺序检查三套实际蒙皮，并生成正面与左右侧面截图。每次只实例化一个展示角色。
- `unity command eval_file Tools/Validation/DomainSealSampling.cs` 使用不激活的独立预览角色调用正式采样器。不会启动角色回调、联机或额外客户端，也不会切换当前场景。
- `BlenderGestureValidation.RunWeapon()` 在原离线手势展示场检查实际准备、射击、充能、俯仰、第一人称和开镜过渡。六种手印依次使用索引 `5、6、8、9、11、12`。不启动联机。

接触检查允许交指和拇指按压有最多 2 毫米的软组织压缩。掌根与腕部相连的边界不作为独立指节表面。参考网格保留原有共面、退化和重复顶点问题。本次不改模型拓扑。
