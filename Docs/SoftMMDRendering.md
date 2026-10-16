# 柔和 MMD / VRChat 风格预览

2026-10-16，分支 `anime-toon-lighting`。用户明确选择奶白肤色、浅彩阴影、水润眼睛；上一版硬阴影方案已撤回。**当前正式角色材质和 Theophilus 场景保持原样，本分支先提供可审阅预览，等待画面方向确认。**

![左：原效果；右：柔和 MMD 预览](Images/SoftMMDPortraitComparison.png)

## 已完成

- `MoonkovAnimeLightingSetup.ConfigureSoftMaterial` 在预览材质副本上应用浅粉肤色阴影、浅紫衣物阴影、较柔和的明暗过渡和低强度高光。描边宽度、透明模式、贴图和原有近/远遮光距离不改；没有增加自发光或最低照明。
- 独立 `MoonAnimePostProcessing` 使用 Neutral、+0.4 EV、对比度 -4、饱和度 -2、轻微冷暖分离、Bloom 0.18 / 阈值 1.5、暗角 0.04。复制自正式地图的 `TheophilusLook`，沿用其 Bloom 降采样等成本配置。
- 独立 `MoonSoftAnimeRenderer` 复制自 `MoonRenderer`，只把 SSAO 的 `AfterOpaque` 从 true 改为 false。前者在不透明表面着色后全屏乘 AO，近景脸颊、眼下和肩膀出现明显灰斑；改为 URP 光照阶段使用 AO 后，本次 lilToon 角色近景不再叠这层灰斑。URP Lit 环境仍可使用 AO；没有新增 AO 遮罩或渲染 Pass。
- 独立 `MoonSoftAnimePipeline` 复制正式场景使用的 `MC00BakedPipeline`，仅换为上述 Renderer。原共享管线、Renderer、正式 VolumeProfile 不改写。
- 原 13 份角色材质已逐项恢复，Git 检查没有角色材质差异。字体的用户未提交修改未纳入本次任务。

## 复现预览

Edit Mode 菜单：`Tools → Moonkov → Rendering → Compare Current With Soft MMD`。

该工具生成/更新独立预览 Profile、Renderer、Pipeline，在临时 PreviewScene 中复制玩家和材质，生成 `Library/BuildValidation/AnimeLighting/before-*` 与 `soft-*`。`before` 指运行该工具时的当前源材质，不是永久保存的历史基线。

两侧使用同一角色、姿势、相机和太阳光（2.2 强度，色温为显式暖白色）。输出顺光、侧光、背光及顺光近景；这些是受控 Editor 渲染，不是正式地图游玩截图。太阳、环境设置、Quality 管线覆盖和临时资源均在 finally 中恢复/释放。没有启动 Host、账号登录或修改玩家存档。

## 画面确认后的应用入口

1. `Apply Soft MMD Character Look`：更新共享角色材质，本地玩家、网络玩家、复用这些材质的敌人和菜单模型都会受影响。
2. `Use Soft MMD Lighting In Open Theophilus Scene`：正式地图已打开时，只替换现有 Volume 的 Profile 和现有 MoonSceneLighting 的 Pipeline 引用，不添加灯，不调整太阳、天文时钟或环境光。保存场景需显式执行。

本轮未执行这两个应用入口。当前正式游戏画面保持原效果。

## 验证与限制

- Unity 6000.5.10f1 / URP 17.5，本机 Metal；Editor 脚本编译通过。
- 两种配置各四个受控渲染，近景已人工检查；确认原效果与预览的 AO 灰斑差异。
- 验证器确认预览前后 16 份角色目录下的材质文件字节一致；活动场景 dirty 状态、管线、太阳和环境光一致，PreviewScene 数量恢复，没有临时场景泄漏。
- 当前目录材质的 Shader 诊断无 Error；没有修改 lilToon 包、Shader 源码或项目全局质量设置。
- Theophilus 在打开/脚本重载后仍出现此前文档记录的 `SubScene.AddSceneEntities / OnEnable` 断言；本轮另外产生过一次 Pipeline eval 的 5 秒调度超时。没有把 Console 称为零错误。后续采用足够时限的 run_script，最终验证通过。
- 尚未应用到正式地图，未作真实游玩、多人、目标平台独立构建、GPU 帧时或低照度实机验收，也不据此宣称达到某款商业游戏的最终美术品质。
