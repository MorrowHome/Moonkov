# LR06 全图展示准备

2026-10-27。独立全图展示已导入 Unity，并已重新打开检查。它不是正式游戏地图。

- 场景：`Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity`。
- 显示预制体：`Assets/MoonEnvironment/Variants/LR06FullPreview/LR06_Full_Display.prefab`。
- 实际 Unity 预览图：[整体](FullPreviewStaging/UnityOverview.png)、[中央设施](FullPreviewStaging/UnityCentralStructure.png)、[北侧走廊](FullPreviewStaging/UnityNorthCorridor.png)。
- 同机位对照：[Blender 源场景](FullPreviewStaging/BlenderPBRBuilding.png)、[Unity 修改前](FullPreviewStaging/UnityPBRBuildingBefore.png)、[Unity 材质和主光调整后](FullPreviewStaging/UnityPBRBuildingLit.png)。Blender 使用 Cycles、AgX 和源灯，图为 16 采样；Unity 使用展示灯且未启用相机后期。图中亮度不能直接当作材质误差。
- 更新后的 Unity 展示图：[全图](FullPreviewStaging/UnityPBRFullOverview.png)、[建筑近景](FullPreviewStaging/UnityWarmBuilding.png)。图像来自重新打开后的场景。
- 同机位阴影改进：[Unity 调整后](FullPreviewStaging/UnityShadowImproved.png)。与上方 Blender 源图使用相同机位和 960×600 分辨率；Unity 截图启用现有的 2 倍抗锯齿。
- UV 修复后的 [同机位图](FullPreviewStaging/UnityUVFinal.png)，以及 Map_29 储罐的[修复前](FullPreviewStaging/UnityUVMap29Before.png)和[修复后](FullPreviewStaging/UnityUVMap29After.png)。

- 源文件的可见清单有 30212 个实例。已去除 633 组来源、变换和材质完全相同的重复文字或曲线。
- 显示几何合并为 65 个 FBX 分片。源分片合计 3707404 个三角形。原始导出已核对哈希、三角形索引和材质名称。
- Unity 导入后有 3691162 个三角形，少 16242 个。对应分片至少含 16770 个零面积三角形。差额与清理退化面相符，但尚未逐面证明原因。全部分片的导入包围盒均在源范围内，最大超出误差约 0.86 毫米。
- 导出副本修正了 1102 个源网格中的朝内闭合部分。867 个源网格含开放或非流形部分，保持原样。另有一个网格包含零体积部分，尚未判定。源文件未保存或改写。
- 材质清单有 449 项，其中 66 项引用 51 张源基础颜色图，19 项保留发光参数。已为 7 种共享 R8 材质接入源法线及金属／光滑度图，覆盖 33 个合并 Renderer。颜色图使用 sRGB；法线和金属／光滑度使用线性色彩。金属度取红通道，光滑度取透明通道。源图最长边限制为 1024 像素。
- R6_T06 代表建筑的有效 UV 和切线已检查。三处同机位取样命中同一材质，位置偏差最大约 1.6 厘米。浅色陶瓷涂层和粗糙裸钢的无效 UV 原先分别占各自表面积约 18% 和 52%。已修复 `Map_22`、`23`、`27`、`29`、`30`、`31` 的 2836 个塌缩三角面；Unity 重查这两种材质已无塌缩面。修复仅修改显示 FBX 的 UV0，按原有贴图密度逐面投影，并非找回作者的 UV，接缝和纹理方向仍需检查。
- 其他材质仍有无效 UV：深色防护钢约 2.72%，安全黄约 2.26%，矿物支撑约 0.95%。程序节点也尚未完整转换，其余复杂材质仍为基础颜色近似。Map_29 的几何检查在修复前后均提示疑似共面、朝内岛和独立部件；这些既有问题未改。不能把本次修复当作全图材质或模型验收。
- 新增的 13 张共享贴图在本机 Unity Editor 中报告约 34.7 MiB。贴图通道经 GPU 抽样核对；这不是目标设备的显存或性能结论。修复前后 UV 明细见 `FullPreviewStaging/UnityUVReport.json` 和 `UnityUVAfterRepair.json`，验证摘要见 `UVRepairVerification.json`。
- 全部 406 盏源灯的原始参数留在清单中。展示场景保留 31 盏暖色代理灯，位置参考源灯具，均不投实时阴影。展示主光参考 Blender 预览主光的方向，使用一盏带软阴影的方向光。代理灯只用于展示，不是源灯的等价转换或最终烘焙。
- 原共享管线的阴影距离为 50 米。隔离试验表明，改为 200 米后，桥底和远处结构的投影恢复。展示场景为编辑器活动场景时，临时使用独立的管线副本；切回其他场景时恢复原设置。共享管线文件未修改。超出 200 米的总览视角仍可能缺少远处阴影。
- 本机 Editor 预览中，主光阴影图为 2048×2048，报告约 8 MiB，使用四级级联。更远的阴影可能增加绘制开销；尚未测目标设备性能或显存。
- 正面断续细线在去掉贴图的对照中仍存在。2 倍抗锯齿可明显缓解采样锯齿；是否存在其他共面或法线问题仍需检查。
- 2026-10-28 补充同机位检查：4 倍 MSAA 进一步减少墙缝、窗框和屋檐的亚像素断线。PC 和展示管线均已改为 4 倍，原渲染比例不变。双面显示与提高近裁面没有同等改善；不能据此排除全图其他几何问题。源 R6_T06 主体以平面硬边为主，未改模型或强制平滑法线。原因、测试和性能边界见 [画面对照](../../../Docs/Development/BlenderUnityLook.md)。
- 展示灯不接按钮。正式场景原有 118 灯和 135 按钮的映射保持不变。固定灯与可控灯在正式接入前仍需逐项分类；不能把可关闭的照明全部烘焙成无法关闭的结果，也不能假设关闭 Mixed 灯会移除已烘焙间接光。
- 重新打开场景后，65 个 Renderer、两份原显示地形、32 盏灯和材质引用均可读取。展示场景没有碰撞、玩法或灯控。正式地图、地形、灯控和交互不改动。尚未做正式地图画面、动态探针、性能或玩法验收。

可重建入口：`prepare_full_preview.py`、`build_full_preview_plan.py`、`check_full_preview_fbx.py`、`copy_full_preview_assets.py` 和 `AssembleFullPreview.cs`。`repair_display_fbx_uv.py` 读取未修改的原始展示分片，在 `Library` 中生成修复候选，不会直接覆盖项目资产；需先重新生成 `FullPreviewStaging` 中的原始 FBX。`FullPreviewStaging` 的大文件是本地中间数据，不作为正式地图。
