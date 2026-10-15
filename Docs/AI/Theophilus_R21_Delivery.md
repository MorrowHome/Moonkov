# R21 正式地图接入

2026-10-15。直接修改 `TheophilusGameScene.unity`，不再维护研究副本。历史场景、R17 等旧文件和已有提交均保留。

## 内容

- 增加交付声明的 13 件 R20 机械构件和 6 件 R21 井体显示构件，根位置 Unity `(2015, 0, 2130)`。不导入两件参考地形或旧版本集合。
- 复用 7 种现有材质，按源参数新增 4 种恒定 PBR 材质依赖；复用原法线纹理，没有调整重复纹理的频率。R23 材质修正未接入。
- 禁用 10 个旧封盖 Renderer，保留对象与原组件。交付名单中的 3 个 R19 对象本来就不在场景，记录缺席，不补导入。旧封盖没有需禁用的独立 Collider，其他岩石碰撞未批量关闭。
- 正式近景与远景各自保留显示、碰撞两份 TerrainData，共四份，先用 AssetDatabase.CopyAsset 复制并重绑，再单独修改 holes。四份共享源文件的 SHA-256 与基线相同。源 DEM 未修改。
- 仅将交付 JSON 的 112 m、64 边形内四角均包含的格子设为洞。近景 0.25 m，远景 2 m；近／远景接缝两侧均处理，保留边界外已有 holes。近景每份新增 133512 格，远景每份新增 330 格（其余选中格子原已是洞）。未编辑高度、地形层、材质或尺寸。
- 保留三座原径向桥的网格和位置，以及 R7 工作台、12 个物资点和撤离配置。没有新增纹理、灯光、Collider、导航或运行时脚本。

## 必要验证

连接现有 Editor；检查正确正式场景、非 Play、非编译。保存重开后场景干净；19 件 Mesh／材质引用、四份 TerrainData 绑定、洞口写入值、原桥和 R7／搜打撤配置通过检查。场景序列化 diff 无原对象删除，既有对象仅增加 10 个 Renderer 禁用覆盖和新根引用。

地形写入脚本首次在矩形 GetHoles 返回尺寸处抛出 IndexOutOfRangeException，发生于 SetHoles 前。检查确认 19 件显示已在内存、四份地形未裁切；后续使用正方形区域读写完成，避免矩形数组尺寸异常，并在保存重开后比较写入区域。没有重新导入已完成的构件。

固定射线从 Y=450 向下 700 m，忽略 Trigger：

| 探针 | 世界 X/Z | 结果 |
| --- | --- | --- |
| 井内中心 | 2015 / 2130 | 0 命中 |
| 井内西侧 | 1987 / 2130 | 0 命中 |
| 井内东侧，跨近景边界 | 2059 / 2130 | 0 命中 |
| 井外西侧 | 1943 / 2130 | 1 个 TerrainCollider 命中 |
| 井外东侧 | 2087 / 2130 | 1 个 TerrainCollider 命中 |

Console 错误审阅发现 1 条 `Unity.Scenes.SubScene.AddSceneEntities` / `OnEnable` 断言，包源码第 379 行检查 `_AddedSceneGUID == default` 失败。未确认这条记录的产生时点，不能断言它是既有问题或本次引入；未修改包代码或扩大到子场景生命周期修复。脚本最终执行成功且编译诊断为空，不等于全项目 Console 无错误。

不做截图、走图、性能矩阵、全场景扫描或 Host／第二客户端测试。R21 新构件是静态显示，井壁／廊道／井底没有新增碰撞与导航；射线通过仅证明所列位置不再被旧地形挡住。画面、音效、联机与深井通行未验收。

## 存档与回退

- `7579bbd6`：记录正式地图基线。`Docs/AI/Theophilus_R21_Baseline.json` 含四份源哈希、基线探针与配置。
- `e527888f`：四份正式专属 TerrainData 复制、分别重绑，尚未开洞。记录在 `Docs/AI/Theophilus_R21_Terrain_Ownership.json`。
- `9c62130`：新增 R20／R21 显示网格、材质及 Unity 元数据。
- 正式场景／洞口与本交付记录单独 commit，具体提交见 Git 日志。

固定输入位于忽略目录 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R21/`：源 Blend 的 SHA-256 为 `45fcfeb79c68e2b6e5bc827f996ee0a6bf2875e18efd4f35fc222fd6743f0fc8`；坐标 JSON 为 `2b9553e508596c41d748317260f0cc5c6ee98942dfbfc1b80105de9d78c65751`。已确认的输入不重复下载。转换一次 XZY 并反转绕序，保留 UV。

该目录还保留 `BeforeR21_Formal.unity`、`BaselineSources/`、`BeforeR21Cut_OwnedData.unity` 和旧 Renderer 状态，以及导入辅助脚本。它们是本机回退资料；已提交的基线和分阶段提交用于 Git 存档。最终机器可读结果为 `Docs/AI/Theophilus_R21_Application.json`。
