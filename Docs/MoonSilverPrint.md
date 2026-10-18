# Theophilus 黑白银盐与钴蓝风格

2026-10-18，直接接入正式地图 `Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`。

参考用户提供的视频截图，以及已认可的 `Assets/Resources/Moonkov/UI/ApprovedHome/HomeLightBackdrop.png`、`HomeDarkBackdrop.png`、`CardIndustrial.png` 和 `Docs/UI/Approved-CD-20261018` 图片。配色按最新要求限定为黑、白、纯钴蓝，不使用紫色。

## 调整与关闭

在正式场景 Hierarchy 中选择 `Theophilus Silver Print`，调整 `MoonSilverPrint` 组件：

- `Intensity`：0 为原画，1 为完整风格。也可直接禁用组件。
- `Exposure / Black Point / White Point / Contrast`：曝光、黑白端点、反差。黑位过高会使地面、敌人和掩体消失在暗部。
- `Silver Relief / Grain`：细节增强和细颗粒；颗粒固定，避免瞄准时闪烁。
- `Accent Color`：默认 `(0, 0.04, 1)`，纯钴蓝。`Accent Strength / Threshold` 控制点色强度和选择范围。
- `Preview In Scene View`：是否在 Scene 视图预览。Game 相机自动生效。

默认曝光 0.3、黑位 0.035、白位 0.82、反差 1.15、纹理增强 0.32、颗粒 0.022、点色强度 0.92、点色阈值 0.22。

## 实现与范围

场景组件在 `beginCameraRendering` 注入 URP Render Graph pass。复用工程已有的相机注入方式；源颜色读取、输出纹理和交换方式遵循本地 URP 17.5 的 BlitAndSwapColor 示例。一个全屏材质 pass，五次颜色采样，交换颜色目标，避免回拷；不请求深度、法线、运动矢量。材质引用由场景持有，保证构建时可达；运行时克隆材质，禁用时解除订阅并销毁克隆。

在色调映射之后、现有受伤与黏液覆盖之前执行（事件 599；覆盖层为 600）。计算在显示色彩空间内完成，再转回线性空间输出，保留输入 alpha。蓝/青和黄褐色安全漆选择性映射为钴蓝，中性材质转为银白/黑色。不会给物体自动添加新的蓝色几何图案。场景卸载后不再注入，预览与反射相机跳过，无图形设备服务器跳过。现有玩家相机没有 Overlay stack；未覆盖未来相机堆栈配置。实现针对当前启用 Render Graph 的 Unity 6 URP，不支持旧版兼容渲染模式。

HUD 原生界面不作资产修改，现有警示覆盖仍在本效果之后；实际 Play 中 HUD、瞄具、敌人可见性尚需走图确认。

这是运行中的画面风格处理。参考图里的拼贴版式、巨大月球构图、建筑造型、局部光照和材质磨损仍由对应美术资产决定，滤镜不能凭空重建这些内容。本次没有更换地形、建筑或灯光，也没有修改角色材质。

## 已完成的必要验证

- 已确认连接项目为 `E:/UnityProjects/FPS_Template`，正式场景处于非 Play、无未保存修改状态后再安装。
- Unity 编辑器完成 C# 编译；Shader `ShaderHasError=False`、`isSupported=True`，实际渲染后没有 Shader 消息。
- 正式地图中使用同一临时 Game 相机渲染原画/效果图，1600×900，位置 `(1800,182,2168)`、旋转 `(0,45,0)`、FOV 60，启用既有 Volume 后处理。临时相机和 RenderTexture 已释放。
- 首轮黑位过重，根据实景调回暗部细节，并将初版蓝紫色改成纯钴蓝；最终配置已保存到正式场景。
- 发现 Scene View 相机没有 AdditionalCameraData，已补充使用 URP 默认 Renderer 的路径。
- 本地对比图：`Logs/SilverPrint/before.png` 与 `Logs/SilverPrint/after.png`（Logs 为忽略目录）。
- 未进入 Play、未运行 Host/Client、未构建、未测 GPU/FPS。不能据编辑器渲染声称已通过实际战斗可读性或目标设备性能验收。
