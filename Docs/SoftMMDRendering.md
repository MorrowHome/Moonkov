# 柔亮 MMD / VRChat 风格与战术阴影

2026-10-16，分支 `anime-toon-lighting`。已按用户选择，将柔和、奶白肤色、浅彩阴影及进一步要求的分材质润泽与通透感应用到共享角色材质和正式 Theophilus 地图。保留真实遮挡造成的变暗，未增加最低照明或角色自发光。

![当前分材质润泽预览（受控 Editor 机位）](Images/SoftMMDPearl.png)

![相同受控机位与灯光的改动前后对比，左为上一轮，右为当前版本](Images/SoftMMDWetComparison.png)

## 角色与材质

- 13 份共享 DollSinger 材质：肤色使用浅粉阴影，衣料使用浅紫阴影，过渡较柔和。眉毛、睫毛及眼白保持干净；描边宽度、透明模式、源贴图、近/远遮光距离不改。
- 四张共享 256×256 渐变 MatCap：`DewySkinMatCap` 用暖色薄层与冷色掠射边缘；`SilkHairMatCap` 用弯曲宽光带与较弱的第二层反光；`SatinClothMatCap` 用沿衣褶法线变化的长渐变；重绘的 `SoftPearlMatCap` 用于眼睛和饰件的窗形反射。替换上一轮集中圆斑的形状，不在皮肤和衣料上叠相同白斑。
- 头发启用原 lilToon 的双层各向异性镜面高光；皮肤降低镜面集中程度并保留暖背光；服装、鞋子、饰件、虹膜、眼白分别调整粗糙度与反射强度，眼白只叠很弱的湿润层。眉毛和睫毛不叠亮漆。保留源颜色贴图和已有绘制高光，未重绘模型细节。
- 四种 MatCap 是随视角/法线变化的风格化反光，不是场景的真实环境倒影或物理折射；这次未增加透明材质、实时反射探针或新渲染 Pass。发丝各向异性会增加着色计算，未测 GPU 成本。
- MatCap 开启照明与阴影遮罩，背光接收阴影；镜面高光使用真实灯光。环境反射项关闭，避免室外反射探针让角色在遮挡里仍亮着。`_LightMinLimit=0`、`_AsUnlit=0` 和原 `_MoonShadowNear/Far` 保留。
- 这些共享材质影响本地玩家、网络玩家、菜单和复用同材质的敌人。没有增加运行时材质实例、补光灯或网络字段。

## 正式地图接入

- 独立 `MoonAnimePostProcessing`：Neutral、+0.4 EV、对比度 -4、饱和度 -2、轻微冷暖分离、Bloom 0.18 / 阈值 1.5、暗角 0.04。复制正式 `TheophilusLook`，沿用原 Bloom 降采样配置。
- 独立 `MoonSoftAnimeRenderer`：保留现有 SSAO 功能，把 `AfterOpaque` 改为 false，由 URP 光照阶段消费 AO。原来的不透明表面着色后全屏相乘会在 lilToon 脸颊、眼下和肩膀形成灰斑；新配置避免这层灰斑。该顺序可能需要深度法线前置 Pass，没有测量其 GPU 成本。
- 独立 `MoonSoftAnimePipeline`：复制正式场景使用的 `MC00BakedPipeline`，只引用新 Renderer。原共享管线、Renderer、旧 Profile 不改写。
- 正式 `TheophilusGameScene` 仅替换现有 Volume 的 Profile 和 MoonSceneLighting 的 Pipeline 两个引用；太阳、环境光、天文时钟、地形、碰撞及玩法保持原配置。保存时产生的文档重排及空默认字段未纳入最终场景差异。
- 新开正式单机对局确认实际管线为 `MoonSoftAnimePipeline (interior runtime)`，地图 Volume 使用 `MoonAnimePostProcessing`，本地网络角色正常生成。

## Play 模式战术阴影验证

![同一机位：阳光、顶棚遮挡阴影、阴影中的局部灯](Images/SoftMMDTacticalShadows.png)

在正式 Theophilus 单机对局运行时，使用相同角色视觉模型与最终共享材质配置建立临时受控舞台。普通 Game Camera 连续渲染，每阶段等待八个实际渲染帧；用实体顶棚投射阴影，另测顶棚下点光源及无太阳状态。额外白色 URP Lit 球作为遮光参照。没有用 Editor PreviewScene 的同步渲染结果证明游戏阴影。

同一面部 100×100 像素区域的归一化采样亮度：

| 状态 | 采样值 |
| --- | ---: |
| 阳光下 | 0.5827 |
| 顶棚遮挡 | 0.0043 |
| 遮挡下开启局部灯 | 0.5498 |
| 无太阳、无局部灯 | 0.0035 |

遮挡后约为阳光下的 0.74%，新增反光未让脸部在暗处保持明亮。该值是同一渲染目标的相对采样，不是照度或显示器校准值，也不是全图所有位置的可见性保证。身体接收真实遮光后，高光随灯光一起衰减；发光武器、光环照明和射击效果仍有原本的暴露位置作用。

正式网络玩家的 `DollSinger_Body` 实测 `receiveShadows=true`、`shadowCastingMode=On`，使用现有 Moonkov lilToon Shader；第一人称完整影子副本继续为 ShadowsOnly。没有修改原遮光 Shader。

测试使用 `Library/BuildValidation/AnimeLighting/Profile-*` 临时档案，不使用玩家原存档。退出后清理临时舞台、相机、材质与光源，恢复管线、太阳及玩家名称并退出 Play。

## 工具与验证边界

- `Tools → Moonkov → Rendering → Compare Current With Soft MMD` 按当前生成公式更新四张共享 MatCap 及独立配置，在临时 PreviewScene 中复制模型和材质，输出顺光、侧光、背光及近景。`before-*` 为当前源材质配原地图 Pipeline/Profile，`soft-*` 为柔亮材质副本配新 Pipeline/Profile；两者不是单变量 MatCap 对照，亦不是永久历史基线。预览不覆盖正式源材质；更新生成的共享 MatCap 和独立配置会写入对应资产，相同像素内容不会重复导入。
- `Apply Soft MMD Character Look` 可重新应用材质；`Use Soft MMD Lighting In Open Theophilus Scene` 只改两个场景引用，不自动保存其他场景编辑。
- Unity 6000.5.10f1 / URP 17.5 / Metal：Editor 编译通过（0 Error / 0 Warning）；13 份材质的纹理和受光遮罩有效，Shader 诊断无 Error。应用后再次运行预览验证，确认源材质字节不变、场景状态与渲染设置恢复、无临时场景泄漏。
- 原始记录：`Library/BuildValidation/AnimeLighting/LiveShadowResult.txt`、`live-tactical-*.png`。早期 `tactical-*.png` 为未通过的同步诊断，不是最终证据。
- Theophilus 原有 `SubScene.AddSceneEntities / OnEnable` 断言仍可在打开/重载时出现；另有验证工具编译/调度错误，未称 Console 零错误。最终 Play 验证及脚本编译通过。
- 未作多人双方目视对战、整图走位、独立构建或 GPU/FPS 预算验收。观感需继续结合实际游戏场景评估，不宣称达到某款商业游戏的最终美术品质。
