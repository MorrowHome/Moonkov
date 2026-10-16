# Theophilus 第一轮视觉精修

2026-10-16。直接接入正式 `TheophilusGameScene`，范围为正式调色、东侧 EVA 入口和 G03 服务间。其他地图、共享原材质与烘焙照明基底不改。

- 正式地图优先级 10 的 Volume 改用独立 `VisualPass01/TheophilusLook.asset`：ACES、曝光 +0.75 EV、对比度 7、饱和度 -7、轻微冷暖分离和 0.085 暗角。Bloom 强度 0.18、阈值 1.5，使用四分之一分辨率；没有景深、运动模糊或常驻色差。
- 六份独立工业材质使用 `MoonEnvironment/Industrial Dust`。保留原贴图、法线、金属/光滑度与 AO 通道，复用现有 R29 的光图、Forward+ 与反射采样路径；额外一次世界坐标遮罩采样控制朝上积尘和墙脚积尘。积尘降低金属度和光滑度，金属保留宽高光。只替换东侧入口、G03 及完全属于这两个区域的合批显示材质，共 410 个 Renderer 引用；原几何、UV、光图绑定及源材质保持。
- 入口走道增加警戒条与四枚静态鞋印，采样原碰撞表面放置；合并为一个 Renderer、20 顶点、10 三角形，使用一份共享透明印刷材质，无 Collider、灯或运行脚本。保留原门楣与门扇，新增 prefab 为 `VisualPass01/EntryMarkings.prefab`。
- G03 复用原回路内的房间 ReflectionProbe，使用重新捕获的 128px HDR 静态 cubemap，盒投影、强度 1.15、混合距离 0.65、重要性 110。捕获远距 24m。没有新增探针、实时反射更新或光照烘焙；原回路继续控制该探针的开关。

资源位于 `Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01`。重新应用入口为 `Tools → Moon Environment → Theophilus → Apply visual pass 01`；已有正式资源直接复用，不改写共享研究场景配置。

## 最小充分验证

- Unity Editor 脚本编译通过，新增 Shader 在正式 URP 管线渲染后无 Shader 编译错误。
- 正式地图两个接近玩家视线高度的固定相机检查入口和 G03，前后使用相同位置、68° FOV、1440×900，正确输出 sRGB。通过现有 `BuildingBakedLighting.Apply` 应用实际房间光图；检查后释放临时相机、光图状态与管线覆盖。不是实际角色走图或 Play 截图。
- 同机 RTX 4070 SUPER，离屏 `Camera.Render` 每阶段每机位预热 4 次、测量 12 次。G03 调用耗时中位数 3.3704 → 3.52885 ms。入口 24.30045 → 9.3276 ms，波动明显，不能解释为本次提高了帧率。该指标包含提交与可能的 GPU 等待，不是单独 CPU/GPU 时间，更不是游戏整帧 FPS。
- 新增运行成本控制为局部材质额外采样、一个低面数贴花 Renderer 及四分之一分辨率 Bloom；静态反射没有每帧捕获。
- 场景序列化范围：410 个 MeshRenderer 材质引用、一个 Volume 引用、一个现有 ReflectionProbe、SceneRoots 和一个新 prefab 实例。没有移除原场景记录；原先九个用户修改记录与本任务修改记录不重叠，提交时排除。
- Console 有原有 SubScene.AddSceneEntities 断言与 Package Manager 获取列表错误，不能称 Console 零错误。本轮未做独立构建、Host/Client、全图或 RTX 4060 Laptop 帧率验收。

本地对比和原始采样保存在 `Library/BuildValidation/TheophilusVisualPass`（不提交）。只提交本任务资源、工具、本文件和精确场景增量。

## 第二轮：柔光与油润高光

按更浓的灯光晕染方向，将正式调色升级为 Dual Bloom：强度 3、阈值 0.55、散射 0.92，保留四分之一分辨率和低成本过滤，HDR 峰值限制 32。曝光不继续提高；对比度改为 4、饱和度恢复至 0，强化冷阴影与暖高光。

东侧 EVA 门楣和 G03 两盏顶灯使用正式地图专属 HDR 发光材质，暖白发光为 `(18,13,8)`。只替换三处灯罩及原 `LightCircuitControls` 中对应的 onMaterial，原 offMaterial、光照强度、照明与网络权威不变。重新应用工具会重绑原回路适配器，更新缓存的材质槽，避免编辑器内旧缓存影响关灯表现。

灰漆、陶瓷漆和深色合金增加强度 0.3、光滑度 0.82 的清漆反射；积尘遮罩会削弱清漆。铝和不锈钢的基础光滑度改为 0.62。没有新增透明雾层、运行时更新脚本、实时灯或阴影捕获。

最终 Unity 编译及 Shader 检查通过，三处发光绑定关灯/开灯回归通过。使用原生 URP StandardRequest 进行同机位对比；临时红色调色探针确认后处理通路工作，探针已恢复。初期关于“未执行 URP 后处理”的判断没有得到证实，以最终检查为准。Console 仍有原有 Package Manager/SubScene 问题，临时诊断脚本的错误不属于交付代码。

RTX 4070 SUPER、1440×900、每机位预热 4 次、采样 12 次：参数/清漆对照中，入口提交与等待耗时中位数 13.90 → 11.34 ms，G03 为 5.62 → 4.87 ms。两阶段共用最终灯罩发光和静态反射；编辑器波动较大，不能称 FPS 改善或给出性能保证。没有运行 Play、Host/Client 或目标显卡整图验收。最终截图、原始数据和关灯回归位于 `Library/BuildValidation/TheophilusAtmosphere`。

本轮场景仅改三个灯罩材质引用和一个回路绑定组件；用户原先九个场景修改记录继续排除。重新应用入口为 `Tools → Moon Environment → Theophilus → Apply soft light atmosphere`，灯罩绑定入口为同目录 `Apply atmosphere lamp emission`。
