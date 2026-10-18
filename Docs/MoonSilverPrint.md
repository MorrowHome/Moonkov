# Theophilus 黑白银盐与钴蓝风格

2026-10-18，直接接入正式地图 `Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`。第二轮补齐光影与印刷质感，第三轮加强清晰线条、黑白交界并收束晕光。

参考用户提供的视频截图，以及已认可的 `Assets/Resources/Moonkov/UI/ApprovedHome/HomeLightBackdrop.png`、`HomeDarkBackdrop.png`、`CardIndustrial.png` 和 `Docs/UI/Approved-CD-20261018` 图片。配色按最新要求限定为黑、白、纯钴蓝，不使用紫色。

## 调整与关闭

在正式场景 Hierarchy 中选择 `Theophilus Silver Print`，调整 `MoonSilverPrint` 组件：

- `Intensity`：0 跳过银盐处理，1 为完整银盐效果。也可禁用组件。场景的太阳、补光和 Volume Bloom 是独立美术设置，不随这个滑块复原。
- `Exposure / Black Point / White Point / Contrast`：曝光、黑白端点、反差。黑位过高会使地面、敌人和掩体消失在暗部。
- `Silver Relief / Grain`：细节增强和细颗粒；颗粒固定，避免瞄准时闪烁。
- `Tonal Separation`：增强黑白块面的分离；0 保留连续影调，1 将中间调收紧到更窄的过渡区。
- `Contour Strength / Width`：利用已有深度纹理补细轮廓，默认强度 0.65、宽度 1 像素；仅在前景侧深度交界着墨，天空不描边，晕光在轮廓处抑制。不会识别同深度的平面纹理或所有几何折角；已有亮度细节增强负责这些表面细节。
- `Accent Color`：默认 `(0, 0.04, 1)`，纯钴蓝。`Accent Strength / Threshold` 控制点色强度和选择范围。
- `Local Adaptation`：空间局部提亮，上限 0.75 EV，保留纯黑。没有 GPU 回读或跨帧曝光变化；沿用场景原有的全局曝光适应。
- `Halation / Threshold / Radius`：调色后银白晕光的强度、亮度门槛、扩散尺度。与既有 Volume 中的 HDR Bloom 叠加，目的是让光晕保留在黑白影调中。
- `Surface Patina`：根据深度重建世界坐标，生成银盐斑驳；远处亚像素纹理按屏幕导数淡出，天空跳过。纹理固定在世界空间，会随移动物体穿过世界坐标而变化。
- `Preview In Scene View`：是否在 Scene 视图预览。Game 相机自动生效。

正式场景当前曝光 0.3、黑位 0.035、白位 0.86、反差 1.25、黑白分离 0.5、细轮廓强度 0.65、宽度 1 像素、纹理增强 0.32、颗粒 0.012、点色强度 0.92、点色阈值 0.22。局部影调 0.3、晕光 0.1、门槛 0.82、半径 1.5、表面斑驳 0.07。第三轮降低大范围晕光、暗部提亮与斑驳强度，避免灰雾和花纹软化黑白块面。

## 光照与 Bloom

- 正式场景 `Lunar Sun` 保留原方向、强度与阴影距离，改用 Soft 阴影，灯光附加数据使用 Medium 过滤。
- `Theophilus Silver Print/Poster bounce fill` 增加一盏强度 0.09 的中性白、无阴影方向补光，给背光侧最低限度的体积感。它是持续的美术补光，不受室内电路开关控制，也不参与动态阴影。
- 正式专用 `TheophilusLook.asset`：HDR Bloom 门槛 1.4、强度 0.4、散射 0.55、钳制 12、中性白、启用高质量上采样。基础曝光为 0.55，为亮墙和灯具留出层次。第三轮由第二轮的 1.2 / 0.55 / 0.64 收束，让灯具发光但轮廓清楚。
- Split Toning 的冷暖偏色均归中，防止原有蓝灰底色被点色选择误认成装饰色。黑白与钴蓝由银盐 Shader 统一处理。
- 不修改灯具电路、开关权威、灯具发光材质或烘焙光照贴图。没有扩大阴影贴图、级联或距离预算；柔和阴影、Bloom 和新增处理仍有 GPU 成本，尚未实测。

## 实现与范围

场景组件在 `beginCameraRendering` 注入 URP Render Graph pass，复用工程已有的相机注入方式。三个四分之一宽高的 pass 提取亮区并作横向、纵向高斯模糊；RGB 保存局部平均场景色，alpha 独立保存高亮场。最后一个全屏 pass 完成影调、细节、世界坐标表面纹理与晕光合成，交换颜色目标，避免回拷。低分辨率纹理显式采用 RGBA16F 保留高亮 alpha。请求深度，不请求法线或运动矢量。

每个 raster pass 显式登记读入颜色、局部光场和深度的资源依赖；纹理在执行回调通过 MaterialPropertyBlock 绑定，处理各输入的 UV 翻转与 RTHandle 缩放。所有图资源由 Render Graph 管理；没有常驻 RenderTexture、跨帧 TextureHandle 或每帧材质创建。材质引用由场景持有，保证构建时可达；禁用时解除订阅并销毁运行时克隆。

第三轮在原有合成 pass 中增加四邻域深度读取，按相对线性深度差形成细轮廓，不增加 pass、纹理或法线预处理。轮廓宽度按渲染像素计算；未测动态视角下细栏杆、远景轮廓或其他输出分辨率的稳定性。

在色调映射之后、现有受伤与黏液覆盖之前执行（事件 599；覆盖层为 600）。计算在显示色彩空间内完成，再转回线性空间输出，保留输入 alpha。蓝/青和黄褐色安全漆选择性映射为钴蓝，中性材质转为银白/黑色。不会给物体自动添加新的蓝色几何图案。场景卸载后不再注入，预览与反射相机跳过，无图形设备服务器跳过。现有玩家相机没有 Overlay stack；未覆盖未来相机堆栈配置。实现针对当前启用 Render Graph 的 Unity 6 URP，不支持旧版兼容渲染模式。

HUD 原生界面不作资产修改，现有警示覆盖仍在本效果之后；实际 Play 中 HUD、瞄具、敌人可见性尚需走图确认。

这是运行中的画面风格处理。参考图里的拼贴版式、巨大月球构图、建筑造型和真实磨损仍由对应美术资产决定，滤镜不能凭空重建这些内容。第二轮修改了太阳阴影与补光，没有更换地形、建筑、角色材质；不能保证贴墙、看地或所有随机取景都具有海报级构图。

月壤材质的后续扩展见 [银砂月壤](MoonSilverSand.md)，包含正式地图材质覆盖、参数与同机位对比。

## 已完成的必要验证

- 已确认连接项目为 `E:/UnityProjects/FPS_Template`，正式场景处于非 Play；第二轮对内存场景保存只读比较副本，与盘上场景没有差异后再修改。
- Unity 编辑器完成 C# 编译；Shader `ShaderHasError=False`、`isSupported=True`，实际渲染后没有 Shader 消息。
- 第一轮在正式地图中使用同一临时 Game 相机渲染原画/效果图，1600×900，位置 `(1800,182,2168)`、旋转 `(0,45,0)`、FOV 60，启用既有 Volume 后处理。
- 首轮黑位过重，根据实景调回暗部细节，并将初版蓝紫色改成纯钴蓝；最终配置已保存到正式场景。
- 发现 Scene View 相机没有 AdditionalCameraData，已补充使用 URP 默认 Renderer 的路径。
- 本地对比图：`Logs/SilverPrint/before.png` 与 `Logs/SilverPrint/after.png`（Logs 为忽略目录）。
- 第二轮最终截图为 `Logs/SilverPrint/Polish/final-exterior.png`、`final-crew.png`、`final-airlock.png`，全部使用正式 MC00BakedPipeline、既有 Volume、1600×900 SDR 输出和预热后的 Game 相机。位置分别为 `(1800,182,2168)`、`(1591.98,213.49,2178.96)`、`(1640,221,2317)`；后两者分别朝向 `(1609,214,2157)` 与 `(1640,221,2338)`。临时管线切换已恢复，相机和输出纹理已释放。
- 第二轮早期 HDR 输出 PNG 编码不适合作为显示亮度判断，且第一次切换管线的首帧缺少建筑；这批 `before-*`/`after-*` 中间图不作最终对比证据。最终图已改用正确的显示输出并预热管线。仅最终三个视点通过实景检查，不代表全图任意视角验收。
- 第三轮 C# 与 Shader 导入编译后，使用同一正式管线、Volume 与相机检查室外和走廊。最终图在 `Logs/SilverPrint/Crisp/after-exterior.png`、`after-crew.png`；同机位调整前图为同目录 `before-*`。导入后的编辑器场景短暂不可用时，配置检查主动中止；那次天空空帧不作证据，稳定后已重新保存参数并覆盖最终效果图。未扩大到 Play、网络或性能测试。
- 未进入 Play、未运行 Host/Client、未构建、未测 GPU/FPS。不能据编辑器渲染声称已通过实际战斗可读性或目标设备性能验收。
