# Theophilus 黑白银白与钴蓝风格

正式地图使用黑白块面、少量钴蓝和有光泽的白色表面。大白墙保留已有贴图、法线、AO 与烘焙光照，并通过现有 `Industrial Dust` Shader 的真实清漆层产生高光；积尘着色关闭，不生成内部云纹，也不增加几何。

## 调整入口

在正式 `TheophilusGameScene` 中选择 `Theophilus Silver Print`，或使用菜单 `Tools → Moon Environment → Theophilus → Select style controls`。

- **黑白钴蓝（正式）**：写入正式风格预设。必须保存场景，Player 才使用这些设置。
- **更强块面**：进一步收紧黑白分界，保留白面细节和 HDR 亮部。
- **原始照明（对照）**：跳过整个风格化处理链。它不撤销白墙材质的清漆层。
- **Ctrl+L**：仅 Editor／Development Play 使用，临时反转当前场景的风格设置；再次按下恢复。每次进入 Play 从场景保存值开始，不再强制重置为保真。普通 Player 初始效果与场景保存值一致。

## 白色表面与影调

新增的 `VisualPass01/Materials/WhiteFinish_*` 是正式地图专用材质副本，覆盖既有 Hull Ceramic Paint 与 Painted Metal Grey 白墙及相应 GreyWhiteShell／Dust 变体。源材质、模型、Transform、碰撞体、灯光及 lightmap 数据不变。

白墙使用 `_CoatStrength`、`_CoatSmoothness` 调整釉面高光；保留原来的 BaseMap、法线、金属／光滑度和 AO 通道。`_SurfaceWear` 复用已有表面纹理的 B 通道细粒，连续改变白底亮度与粗糙度，让细颗粒参与实际受光；默认0保持其他旧材质不变，白墙设为0.35、纹理重复率2。投射沿表面主轴展开，避免旧地面投射在竖墙上拉成斜条纹。它没有新增采样或 Shader 关键字，不使用 R 通道的大片云斑。没有原贴图的白材仍保留原有基础输入，不伪造大理石内部纹理。`_DustAmount=0` 禁止积尘着色。

后处理在场景线性 HDR 中塑造黑层、暗层和白面，保留原材质的局部细节及亮部残差，之后交给现有 Bloom／tonemapping。它不把 HDR 高光裁到 1，也不叠加屏幕颗粒、世界云斑、局部自动提亮或第二层晕光。原来的亮度提取与两次模糊已移除。

钴蓝只从原本蓝色占主导且饱和度足够的表面选取。冷白、青色、紫色和黄褐安全漆不再统统转蓝；没有蓝色来源的表面不会凭空得到蓝色图案。

## 范围与性能边界

效果仍在透明物之前运行，只有声明 `MoonEnvironmentScope` 的不透明环境材质参与。角色、武器、拾取物、透明效果、HUD 和独立肖像预览不因白墙接入而扩大作用范围。遮罩按可见深度匹配，不覆盖前景人物轮廓。

运行时只保留环境遮罩与一次 HDR 合成，原始照明模式直接跳过两者。所有临时纹理由 Render Graph 管理，没有常驻 RT 或逐帧材质创建。减少 Pass 不等于已测得 GPU 帧耗时提升，正式游戏中移动视角和目标硬件表现仍须按实际验证记录判断。

此功能控制已有地图的材质和影调。参考图的建筑构图、轮廓与空间布局不会通过滤镜自动生成。

旧实现与历史截图保留在 [2026-10-22 环境作用域记录](ArtDirection/20261022-EnvironmentOptics/README.md) 和 [2026-10-19 连续银盐记录](ArtDirection/20261019-ContinuousSilver/README.md)，其中旧默认值和旧执行链不再代表当前版本。
