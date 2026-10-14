# B 巨构 R2 视觉增量

2026-10-14，已按云端 `B_RING_R2_VISUAL_DELTA.json` 更新现有展示 Prefab：只新增 26 个视觉模块，移除清单指定的 58 个旧包覆对象。没有重跑完整 R1 导入或建立重复巨构根。

## 实际更新

- 根仍为 `B_RING_CONCEPT_ROOT`，世界位置 `(2015,0,2130)`。
- 更新同一个专属 Prefab：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/Prefabs/B_Dense_Ring_Display_Only.prefab`。
- 新增 26 个 Mesh 资产只放在该目录的 `R2VisualDelta/Meshes/`。内容为十二个环段、四个承载模块、六个转运舱模块、中央服务脊柱/卷扬头及两个独立卷筒；没有导入源文件中的可编辑零散部件副本。
- 删除的是展示 Prefab 内的 58 个精确命名旧包覆对象，旧 Mesh 资产全部保留。非清单对象未删除；原甲板、西侧桥/升降展示接口、楼群和其他结构保留。
- 复用已有七个灰盒恒定 PBR 材质，新增材质资产为 0；保留原粗糙外墙、适度金属钢构、少量工程橙及既有纹理/法线属性，没有调整太阳、曝光或全场颜色。
- 展示 MeshRenderer 数由 1350 变为 1318，即 1350−58+26。新增碰撞体 0、新增灯 0；没有编辑任何独立本地接入或碰撞组、八栋外楼、配色、地形、正式场景或角色系统。

## 保存与回退

目标场景仍是 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。临时增量脚本编译无诊断错误；保存重开后根位置正确，场景 `dirty=false`、`playing=false`。除此没有 Play、截图、测试矩阵、整地形检查、性能测量或大范围保护重哈希。

回退文件在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R2/`：

- `BeforeVisualR2.unity`：更新前场景。
- `BeforeVisualR2.prefab`：更新前展示 Prefab；原 `.meta` GUID 和旧网格资产保留。恢复时先保留后续编辑，再将此内容恢复至原 Prefab 路径，不能覆盖未保存的新工作。

输入文件读取时各校验一次 SHA-256：

| 文件 | 字节数 | SHA-256 |
| --- | --- | --- |
| `Moonkov_Theophilus_B_Dense_Ring_Concept_Increment_R2.blend` | 13,596,830 | `94e4772c8dc787464435e026307893ee7bf8154d2ae84821a0a58daa59535c74` |
| `B_RING_R2_VISUAL_DELTA.json` | 62,903 | `0d154f43b2151b87f40ea4504c840ac4f8637932c6d5c3731698f0fca5e546ae` |

R1 文件没有覆盖，源 Blend 没有保存修改。更新通过仅导出清单中的 26 个模块、再修改专属 Prefab 完成；根局部矩阵与 Blender XZY 转 Unity 只应用一次，没有套 MC00 变换。

## 当前限制

R2 仍是展示灰盒。更新完成不代表写实效果、概念图级细节或主观视觉已达标；没有生成新贴图或进行主观画面验收。后续 R3 折返维护楼梯已作为独立增量接入，详见 `Docs/AI/Theophilus_West_Stair_R3_Delivery.md`；已加路径代理但尚未实走，不提升为完整可游玩结论。没有新增电梯运行时代码。

原始增量结果位于持久交接目录的 `PreparedNative/manifest.json` 与 `UnityR2VisualDelta.json`；汇总见 `Docs/AI/Theophilus_Dense_Ring_R2_Visual_Delta.json`。
