# R24 中央关节显示精修接入

2026-10-15。正式目标 `Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`，以 MC00 区域显示裁剪提交 `61501afb` 后的已保存地图为基线；没有回写旧快照。

## 交付

- 按固定映射导入全部 **14 件显示网格**，合计 144369 个求值后三角形；源网格坐标 SHA-256、顶点／面／循环数、父级、矩阵、材质槽顺序均匹配。只链接指定 Collection 到后台导出场景并求值倒角，不保存或改写源 Blender 文件。
- `R24_HERO_FINISH_DISPLAY_ROOT` 位于 Unity `(2015,0,2130)`，旋转为单位四元数、缩放为 1。网格预先从 Blender 世界坐标减 `(2015,2130,0)`，再按 XZY 转轴并反转一次三角形绕序；不额外旋转或重复位移。内嵌 UV0 保留，不重放旧 R23 循环索引补丁。
- **11 个实际材质全部绑定**：8 个 R24 新材质和 3 个精确名称的现有 R13／R23 材质。R24 标量严格使用映射的 sRGB 色值、金属度和 `1 - roughness` 光滑度；工业灰漆使用现有 URP Complex Lit 的独立 clear-coat 权重 0.16、光滑度 0.68，其余使用现有 URP Lit。不添加 Shader、包或运行时代码。
- **3 张内嵌贴图全部提取归档**，SHA-256 与字节数在 `SourceData/Models/TheophilusR24/ExportManifest.json`；材质继续复用现有 URP 法线和金属／光滑度依赖，不重复导入纹理或重算已接入的 R23 通道，不改写共享旧材质。
- 唯一关闭的旧 Renderer：`R20_02_Grouped_Actuator_Muscles_And_Heavy_Nodes`。替换显示为 `R24_00_R20_02_Actuators_With_Hero_Knee_Replaced` 和本批关节细件。原对象、Transform、MeshFilter、MeshCollider 及其他组件保留。

## 保留及验证

原完整 38 件 R21／R23 的网格、材质和 Transform 引用不变，其中仅上述 Renderer 关闭；三桥、外圈、R7、Terrain／TerrainCollider 引用不作写入。原 15322 个 Collider 对象保持，新根只含 Transform、MeshFilter 和 MeshRenderer，**没有新增／替换碰撞、灯、相机或玩法组件**。MC00 `InteriorVisibility` 与岩石 `MoonRockLod` 保留。

资产导入和场景绑定的 Editor 脚本编译通过；14 件网格的子网格／材质槽顺序、缺失引用与 Shader 支持／错误检查通过。正式地图已保存重开，14 件显示开启、唯一旧显示关闭，场景不脏且未进入 Play。未截图、走图、性能测试、Host、账号／存档／SubScene 排查，不宣称最终视觉或玩法验收。

输入固定校验：

- Blender：2012830 B，SHA-256 `7d732a3dbf58eb95238af19a86bb96977538a5fcfae9104dded843c35484e0ee`。
- 映射：42976 B，SHA-256 `735a3a007c4265510edac8f5e9d932d17ec748baaa32c3e0376a9951705f77c4`。

原始输入、导出清单、提取贴图及导出脚本归档在 `SourceData/Models/TheophilusR24/`；运行显示资产在 `Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R24/`。详细绑定结果见 `Docs/AI/Theophilus_R24_Validation.json`。本地修改前备份为 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R24/BeforeR24_Formal.unity`。

本批不依赖 R22。云端完整预览中周边 R22 衬板／井壁细节不在本次 14 件增量内，正式地图保留 R21 对应表面。回退只需停用 R24 新根并恢复上述旧 Renderer；历史资产与提交保留。

资产阶段提交：`09694df8`。场景与本记录单独作后续本地提交；未 push。
