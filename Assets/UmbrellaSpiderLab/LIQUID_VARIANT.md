# Umbra 08 液态变体

打开 `Scenes/MC00LiquidSpiderLab.unity`，或菜单 **Tools → Umbrella Spider → 4 - Open independent Umbra liquid variant**。此场景默认 **Fluid 粒子流体**、手动移动；原来的 `MC00SpiderLab.unity` 保持甲壳版。

- 点击画面取得控制，鼠标环绕、滚轮缩放，WASD 移动，Shift 冲刺。
- **F2 切换 HEAVY / ORIGINAL 流动风格**。HEAVY 使用接近临界阻尼的骨架吸引、更高黏度、更弱表面聚合与压力喷溅，减少弹性回摆和圆珠感；按运动方向沉积短黏滞条带，HEAVY 的有效积液量、截面与表面阈值均可调整。材质降低均匀塑料高光，增加不均匀湿膜粗糙度。切回 ORIGINAL 恢复提交版本的流动参数和材质。
- F5 循环 **Legacy → Carapace → Liquid → Fluid**；保留原型、甲壳版及前一版连续软皮，切换不重置身体和足点。
- **F3 切换骨架对液体的吸引**：关闭后粒子继续受重力、压力和黏度作用，恢复吸引后聚回骨架。停住身体再切换更容易观察聚散。
- F4 显示足点，F8 拆解骨架，F6 自动往返，F7 用原角色近距离观察。
- `=` 收腿、脱离吸附并重新落地；现有自动卡死恢复继续使用原实现。

## Fluid：骨架吸引的粒子流体

`SkeletonFluidPresentation` 以交付模型的实体体积为基础重新分配 5,731 个粒子：躯干保留约 45% 的原粒子并压薄，小腿和足端补足为 2,416 个粒子，占总量约 42%。下段沿原骨架中线生成连续液体截面，半径由膝部 10.8 cm 渐减到足端 7.8 cm，避免原雕刻的极细末端在流体密度场中消失。远端提高吸引和跟随强度，原来的软皮和骨架不受这种分配影响。

GPU 使用简化 SPH 解算：邻域密度压力、短程聚合、XSPH 速度交换实现黏性，叠加世界重力和骨架吸引。粒子保留自己的位置和速度；骨架是移动的引导场，不直接覆盖粒子的位置。引导点附近允许自由流动，远处吸引增强。粒子在转向和加速时可以积聚、拖开和重新聚回。

HEAVY 另有稀疏的黏滞连接网络：`FluidFilaments` 从初始粒子建立最多 900 条候选连接，以沿肢体的连接为主，跨腿连接仅限腿根。默认显示比例 .32，避免铺成均匀蛛网；默认基础半径 4–22 mm，粗细混杂、偏心收颈、根部积液、重力下垂和速度拖曳均可调。弱、带阻尼的伸展约束参与粒子解算，GPU 丝线网格读取同一批粒子的当前位置，拉长后变细并按伸展量断开。它补充 96³ 密度网格无法解析的细丝，不改变骨架或足点。身体、腿根、膝部与足端各有截面比例，八条腿有独立厚度差异。主体与拉丝使用 `Umbra08LiquidStyle.hlsl` 的同一湿膜材质计算和同一组参数，并声明相同的 Color 底色属性以统一线性色彩转换；反射探针、雾、根部表面法线和深度法线通道也保持一致。丝线已提高到 12 边、17 截面，法线包含收颈斜率，避免原先偏浅和粗糙的线网。ORIGINAL 恢复旧解算、截面、积液量和材质，隐藏连接丝并停用其约束。

## Inspector 调节

选择场景中的 `UmbrellaSpiderExperiment`，展开 **Fluid**；所有字段每帧传入解算和渲染，Play 中可实时调节，无需重建粒子或读取 GPU。

- **Filament Density**：拉丝量，0 关闭，默认 .32；**Minimum/Maximum Radius** 控制粗细范围，**Radius Variation / Necking / Root Pooling** 控制不均匀程度、收颈和厚根。
- **Mantle/Root/Knee/Toe Thickness** 分别控制身体、腿根、膝和足端；**Leg Asymmetry / Local Thickness Variation** 控制左右腿差异和沿肢体厚薄变化。
- **Liquid Mass / Surface Threshold** 控制积液饱满度；**Body/Foot Attraction、Damping、Viscosity、Pressure、Cohesion、Gravity** 控制聚回、阻尼、黏性和下落。
- **Sag / Velocity Lag / Minimum Break Length / Break Stretch** 控制下垂、运动拖尾和断裂；**Colour / Smoothness / Wet Film Variation / Clear Coat** 同时影响主体与丝线。
- **Gait** 展开后可调前倾、侧倾、身体压低、起伏、步幅周期、摆腿时长和抬脚高度；组件原有速度、冲刺、摄像机参数继续保留，新增转身速度与脱困阈值。

Play 中的 Inspector 修改遵循 Unity 的临时值规则，退出后会还原；要长期保存请在 Edit Mode 修改并保存场景。镜头初始角度/距离在进入 Play 时读取。骨段长度和碰撞体尺寸仍按现有绑定固定，避免随意改变后破坏模型、预测与 IK 的一致性。


每帧使用空间网格寻找粒子邻居，以最多 4 ms 的子步积分。附近 BoxCollider 转换为 GPU 障碍，MC00 的独立 MeshCollider 碰撞代理和细小导入障碍采用保守 AABB；零厚度碰撞平面生成薄接触层，不修改原场景碰撞器、身体移动器或足点调度。

可见外形每帧由粒子重新生成：粒子密度沉积进 96³ 三维体纹理，经过连续平滑，再由等密度面射线步进呈现。光照、表面法线、深度和阴影来自重建的流体表面；不是给原蒙皮增加波纹，也不是渲染一堆独立球体。需要支持计算着色器和三维渲染纹理的 GPU。当前是离线实验中的有限体积流体，不是完整水体或网络流体系统。

菜单 **6 - Probe skeleton-attracted particle fluid** 运行一次 8 秒检查：静止吸引、关闭吸引、重新聚回，最后沿 MC00 跑动。仅在四个阶段读取一次粒子状态，记录数值稳定性及偏离引导场的程度；同时验证足点和旧外观回退。输出 `Library/BuildValidation/UmbrellaSpider/fluid-report.json`、聚散阶段截图和短视频原始帧。

## Liquid：上一版连续软皮，保留回退

运行模型来自交付的连通表皮 `V4_LiquidSkin_ConstrictedMantle`，保留凹陷躯壳、八条弯曲的软腿和腿根褶皱。原始 576,748 三角面的网格生成 43,256 三角面、21,620 顶点的连续运行表皮。89 个组织采样骨骼沿原作者的八条 Bezier 中线绑定。

`LiquidSpiderPresentation` 使用原有 IK 的髋、膝、足端。中段组织的阻尼弹簧产生加速拖曳、回弹和缓慢行进波，最大位移限制为 9 cm；骨骼端点不迟滞。附近碰撞面压缩局部软组织截面，不参与身体路线和足点调度。材质采用 URP 不透明湿润黑色、清漆和连续流动的细微起伏；表面振幅默认 4 mm，足端接触圈抑制位移，阴影和深度使用相同形变。

原始 Blender 文件保存在 `SourceData/Models/Umbra08Liquid/`，避免 Unity 自动启动 Blender 导入高模；实际运行资源是 `Resources/Umbra08Liquid.bytes` 和 `Umbra08Viscous.shader`。导出方法和来源校验见该目录的 `SOURCE.md`。当前减面是自动生成的实验运行版本，未进行手工拓扑和贴图烘焙。

菜单 **5 - Validate Umbra liquid in MC00** 进行一次 8 秒 Play 检查，记录运动、足点、组织迟滞、蒙皮表面抽样，以及三个外观的同姿态截图。输出在 `Library/BuildValidation/UmbrellaSpider/liquid-report.json`；它不会保存自动移动设置，也不会替换旧版验证记录。抽样表皮检查不等价于完整网格碰撞保证。

共享的加权骨架液体还用于独立的蛇与毛毛虫液态场景，见 `PROCEDURAL_CREATURES.md`。蜘蛛的默认骨骼数量、体积尺寸与原始粒子资源保持原配置。
