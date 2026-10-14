# B 加密巨构展示灰盒接入

2026-10-14，云端已交付的 B 加密巨构指定集合已追加到现有 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。场景保存重开后干净、不在 Play。这里只完成展示导入，不是可游玩区域或路线验收。

后续 R2 已在同一展示根追加 26 个视觉模块、移除 58 个旧包覆对象，当前 1318 个展示 MeshRenderer；七个现有材质及旧网格资产保留。详见 `Docs/AI/Theophilus_Dense_Ring_R2_Visual_Delta.md`。以下 R1 导入数量保留为历史来源证据。

## 入口与实际范围

- 场景根：`B_RING_CONCEPT_ROOT`，Unity 世界位置 `(2015, 0, 2130)`、旋转 identity、缩放 1，独立于 MC00。
- 可整体移除的展示 Prefab：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/Prefabs/B_Dense_Ring_Display_Only.prefab`。
- 专属资产目录：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/`，1350 个原生 Mesh 资产、1350 个 MeshRenderer、7 个专属材质。总几何为 53,880 个展开顶点、17,960 个三角形；这是导出计数，不是性能结论。
- 只读取 `CONCEPT_B_DENSE_RADIAL_MEGASTRUCTURE`；显式排除其嵌套 `B_PROPOSED_LOCAL_EARTHWORKS_NOT_APPLIED` 的 3 个对象。参考 DEM、原布局参考及展示灯不导入。
- 保留合同所述 464 m 外径、290° 断环、Y=233 m 平台、三座径向桥、中央下降设施与约 Y=105.50 m 的封闭井盖，以及 16 组工业壳体和 12 条连接带组成的四片展示区。各区的名字与逻辑层级保留。

源文件是持久目录 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/Moonkov_Theophilus_B_Dense_Ring_Concept_Increment.blend`，13,020,085 字节，读取前一次校验 SHA-256：`4296ed283ae54a7e4e058d25594383ee68b07dcddab5dea017fc2063751ec88f`。没有改源文件或分享权限。

## 坐标处理

只读取一次源 root/坐标合同：Blender 根为 `(2015, 2130, 0)`，标准转换 Unity XYZ = Blender XZY。导出时先将各对象的世界矩阵移回该根局部，应用模型/修改器，再把顶点和法线按 XZY 转换，三角绕序反转一次。导出的逻辑节点均采用 identity 变换，最后只在 Unity 展示根上设置世界 `(2015,0,2130)`。

没有套 MC00 矩阵，没有把源世界坐标和场景根位置叠加两遍。1350 个网格和七个材质通过已安装 Blender 的后台导出及 Unity 原生 `.asset`/Prefab API 接入，不安装新的导入包，也不加入 Assets 运行时代码。

初次保存后 Unity 使用 Prefab 文件名 `B_Dense_Ring_Display_Only` 作为实例根名称，导致导入器按合同根名读取失败。几何、材质和场景已保存，因此只将这个场景实例重命名为 `B_RING_CONCEPT_ROOT` 并保存重开；没有重复创建或导入整批资产。

## 限制与回退

新增碰撞体 0、新增 Light 0；没有实际 DEM/地形高度修改或开洞，没有复制原地图，没有编辑原 118 灯/135 按钮的控制链、八栋外楼或专属 B 配色材质。新概念材质只用于这个展示增量。

平台及工业区入口到自然地面的连接尚未完成，连接带不能视为经过正式角色验证的步行道路。中央设施为封闭表面井盖和下降构型展示，未挖真实井筒。没有进行 Play、路线、截图、碰撞矩阵、性能或主观画面验收，也没有大范围保护文件重哈希、安装、提交或推送。

删除本场景的 `B_RING_CONCEPT_ROOT` 实例即可撤回展示增量。追加前的八楼 B 配色场景备份在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/BeforeDenseRing_PaletteB.unity`；它保留此前配色及所有楼体，恢复前应先保留后续编辑，不能直接覆盖新工作。

仅做正确项目/占用检查、临时导入脚本编译、指定导入根的明显组件检查与保存重开确认，结果 `dirty=false`、`playing=false`。导出清单和 Unity 导入记录分别在持久交接目录的 `PreparedNative/manifest.json` 与 `UnityDenseRingImport.json`，汇总见 `Docs/AI/Theophilus_Dense_Ring_Concept_B_Import.json`。
