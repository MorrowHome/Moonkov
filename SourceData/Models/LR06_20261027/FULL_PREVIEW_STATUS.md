# LR06 全图展示准备

2026-10-27。独立全图展示已导入 Unity，并已重新打开检查。它不是正式游戏地图。

- 场景：`Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity`。
- 显示预制体：`Assets/MoonEnvironment/Variants/LR06FullPreview/LR06_Full_Display.prefab`。
- 实际 Unity 预览图：[整体](FullPreviewStaging/UnityOverview.png)、[中央设施](FullPreviewStaging/UnityCentralStructure.png)、[北侧走廊](FullPreviewStaging/UnityNorthCorridor.png)。

- 源文件的可见清单有 30212 个实例。已去除 633 组来源、变换和材质完全相同的重复文字或曲线。
- 显示几何合并为 65 个 FBX 分片。源分片合计 3707404 个三角形。全部分片已核对哈希、三角形索引和材质名称。
- Unity 导入后有 3691162 个三角形，少 16242 个。对应分片至少含 16770 个零面积三角形。差额与清理退化面相符，但尚未逐面证明原因。全部分片的导入包围盒均在源范围内，最大超出误差约 0.86 毫米。
- 导出副本修正了 1102 个源网格中的朝内闭合部分。867 个源网格含开放或非流形部分，保持原样。另有一个网格包含零体积部分，尚未判定。源文件未保存或改写。
- 材质清单有 449 项，其中 66 项引用 51 张源基础颜色图，19 项保留发光参数。程序节点尚未完整转换，展示版使用作者的基础颜色近似。源贴图在展示版最长边限制为 1024 像素。未发现无法映射的材质槽。
- 全部 406 盏源灯的原始参数留在清单中。场景使用 31 盏无实时阴影的暖色代理灯和一盏无阴影的中性主光。这不是原灯的等价转换或最终烘焙。
- 重新打开场景后，65 个 Renderer、两份原显示地形、32 盏无阴影灯和材质引用均可读取。展示场景没有碰撞、玩法或灯控。正式地图、地形、灯控和交互不改动。尚未做正式地图画面、动态探针、性能或玩法验收。

可重建入口：`prepare_full_preview.py`、`build_full_preview_plan.py`、`check_full_preview_fbx.py`、`copy_full_preview_assets.py` 和 `AssembleFullPreview.cs`。`FullPreviewStaging` 的大文件是本地中间数据，不作为正式地图。
