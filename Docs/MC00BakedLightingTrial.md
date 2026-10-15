# MC00 G01 烘焙照明样板

该样板用于验证 MC00 按房间／回路切换照明。正式 Theophilus 地图尚未接入，不能据此声称正式地图的十几 FPS 已解决。

- 场景：`Assets/MoonEnvironment/Variants/MC00BakedRoomTrial/G01BakedRoom.unity`。
- G01 静态表面按材质和空间分块，生成 UV2；实际完成开、关两次 GPU 烘焙，各保存 Lightmap 和完整 LightProbes。
- 八盏灯中两盏主灯为 Mixed，六盏为 Baked。开关同时切换烘焙结果、探针、主灯和灯具发光材质。
- 主灯使用 **Distance Shadowmask**，在 40 米阴影距离内保留建筑与角色之间的实时遮挡。六盏 Baked 补光灯仅通过探针照亮动态角色，不提供各自精确的动态阴影。
- 普通 Shadowmask 让静态建筑从实时阴影绘制中退出，角色对静态遮挡主要依赖探针；它不能满足精确墙边阴影落在角色身上的要求，已不再作为样板默认模式。

## 已有测量的边界

忽略目录 `Library/BuildValidation/MC00BakedRoom/` 保存原始结果。此前隔离房间的普通 Shadowmask 测量为 3.044 ms / 373 draw calls，对照八盏实时灯为 3.834 ms / 1188 draw calls。两者均使用同一份分块几何，未包含正式地图的地形、其他建筑、AI 和网络。

这些数据属于旧的普通 Shadowmask 方案，不能代表后来要求的精确角色受影配置，也不能外推正式地图 FPS。实时对照的探针照明也不同，截图不能作为画面等价验收。

## 使用与限制

通过 `Tools > Moon Environment > Baked Room Trial > Open completed G01 sample` 打开样板；运行后可切换回路与角色位置。编辑器工具负责保存并恢复原场景、管线、阴影模式和 Play 起始场景。

`BakedRoomLighting` 是单个隔离房间的全局 Lightmap／LightProbes 所有者，不能直接在正式地图每个房间各挂一份。生产接入仍需独立回路的图集及探针管理、跨房间光照处理，以及正式地图中同位置的前后对比。移动门与家具也需要明确的动态对象清单。

角色 Shader 对室内附加灯的阴影接收必须单独验证；仅确认角色在地面上投下阴影，不构成角色受影验收。

## 角色受影修正与验证

2026-10-15 检查发现项目自有 MoonToon 钩子调用的 lilToon 附加灯路径只乘距离衰减，未使用阴影衰减；模板还剔除了附加灯阴影变体。现已在自有钩子中使用 URP 带探针遮挡输入的附加灯 API，并保留对应变体。没有修改第三方包。

Unity 导入及当前 Play 变体编译通过。Distance Shadowmask 样板中，先比较静态建筑投影开关，再保持角色、光源、探针和相机不动，仅启用一个临时遮挡物：头部、衣服和腿部均出现直接光减弱，确认接收阴影有效。原始截图为 `CharacterUnoccluded.png` / `CharacterOccluded.png`。该控制实验不替代完整角色美术验收；没有验证所有动画、门和跨房间边界。

修正后的独立样板单次采样为 3.468 ms、526 draw calls、243 shadow casters，记录在 `DistanceShadowmask.json`。它比普通 Shadowmask 多绘制静态遮挡物，不能沿用旧方案的性能降幅；此次没有重新测量整栋正式地图。测试已退出 Play 并恢复原工作区，临时遮挡物没有写入场景。
