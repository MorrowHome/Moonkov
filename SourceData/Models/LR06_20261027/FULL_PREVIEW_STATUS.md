# LR06 全图展示准备

2026-10-27。独立全图展示已导入 Unity，并已重新打开检查。它不是正式游戏地图。

- 场景：`Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity`。
- 显示预制体：`Assets/MoonEnvironment/Variants/LR06FullPreview/LR06_Full_Display.prefab`。
- 实际 Unity 预览图：[整体](FullPreviewStaging/UnityOverview.png)、[中央设施](FullPreviewStaging/UnityCentralStructure.png)、[北侧走廊](FullPreviewStaging/UnityNorthCorridor.png)。
- 同机位对照：[Blender 源场景](FullPreviewStaging/BlenderPBRBuilding.png)、[Unity 修改前](FullPreviewStaging/UnityPBRBuildingBefore.png)、[Unity 材质和主光调整后](FullPreviewStaging/UnityPBRBuildingLit.png)。Blender 使用 Cycles、AgX 和源灯，图为 16 采样；Unity 使用展示灯且未启用相机后期。图中亮度不能直接当作材质误差。
- 更新后的 Unity 展示图：[全图](FullPreviewStaging/UnityPBRFullOverview.png)、[建筑近景](FullPreviewStaging/UnityWarmBuilding.png)。图像来自重新打开后的场景。

- 源文件的可见清单有 30212 个实例。已去除 633 组来源、变换和材质完全相同的重复文字或曲线。
- 显示几何合并为 65 个 FBX 分片。源分片合计 3707404 个三角形。全部分片已核对哈希、三角形索引和材质名称。
- Unity 导入后有 3691162 个三角形，少 16242 个。对应分片至少含 16770 个零面积三角形。差额与清理退化面相符，但尚未逐面证明原因。全部分片的导入包围盒均在源范围内，最大超出误差约 0.86 毫米。
- 导出副本修正了 1102 个源网格中的朝内闭合部分。867 个源网格含开放或非流形部分，保持原样。另有一个网格包含零体积部分，尚未判定。源文件未保存或改写。
- 材质清单有 449 项，其中 66 项引用 51 张源基础颜色图，19 项保留发光参数。已为 7 种共享 R8 材质接入源法线及金属／光滑度图，覆盖 33 个合并 Renderer。颜色图使用 sRGB；法线和金属／光滑度使用线性色彩。金属度取红通道，光滑度取透明通道。源图最长边限制为 1024 像素。
- R6_T06 代表建筑的有效 UV 和切线已检查。三处同机位取样命中同一材质，位置偏差最大约 1.6 厘米。全图还有 UV 无效面：浅色陶瓷涂层约占该材质表面积的 18%，粗糙裸钢约占 52%，主要集中在 `Map_22`、`Map_29` 和 `Map_31` 等分片。这些面不能认为已完成贴图验收。程序节点也尚未完整转换，其余复杂材质仍为基础颜色近似。
- 新增的 13 张共享贴图在本机 Unity Editor 中报告约 34.7 MiB。贴图通道经 GPU 抽样核对；这不是目标设备的显存或性能结论。明细见 `FullPreviewStaging/UnityUVReport.json` 和 `UnityPBRChannels.json`。
- 全部 406 盏源灯的原始参数留在清单中。展示场景保留 31 盏暖色代理灯，位置参考源灯具，均不投实时阴影。展示主光参考 Blender 预览主光的方向，使用一盏带软阴影的方向光。代理灯只用于展示，不是源灯的等价转换或最终烘焙。
- 本机 Editor 预览中，主光阴影图为 2048×2048，报告约 8 MiB。项目现有管线的阴影距离为 50 米，使用四级级联。这不是目标设备的性能或显存结论。
- 展示灯不接按钮。正式场景原有 118 灯和 135 按钮的映射保持不变。固定灯与可控灯在正式接入前仍需逐项分类；不能把可关闭的照明全部烘焙成无法关闭的结果，也不能假设关闭 Mixed 灯会移除已烘焙间接光。
- 重新打开场景后，65 个 Renderer、两份原显示地形、32 盏灯和材质引用均可读取。展示场景没有碰撞、玩法或灯控。正式地图、地形、灯控和交互不改动。尚未做正式地图画面、动态探针、性能或玩法验收。

可重建入口：`prepare_full_preview.py`、`build_full_preview_plan.py`、`check_full_preview_fbx.py`、`copy_full_preview_assets.py` 和 `AssembleFullPreview.cs`。`FullPreviewStaging` 的大文件是本地中间数据，不作为正式地图。
