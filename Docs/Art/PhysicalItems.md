# Moonkov 实体物品美术

2026-10-23。沿用月面科幻设定，补齐 64 件非光环物品的实体模型、共享 URP 材质、预制体和模型渲染库存图。光环武器及 `halo_*` 模块不在这批范围。完整清单见 `Tools/Art/item_manifest.json`。

## 资源与接入

- `Assets/Resources/Moonkov/Items/`：64 个 FBX、64 个预制体、64 张透明库存图、12 个共享材质。每件一个静态 MeshRenderer，无碰撞体、脚本或网络权威组件；实际米制尺寸、底面原点。FBX 自带单位换算 Transform，尺寸检查使用 Renderer 世界包围盒，不能以原始 Mesh 局部尺寸判断。
- `SourceData/Models/PhysicalItems/`：64 份可编辑 Blender 文件。几何细节与标识来自原创建模脚本，没有复制塔科夫资产。
- `PhysicalItemVisuals` 是公共资源入口。库存、商店、研究、快捷栏和拖拽预览使用相同模型渲染图；旧枚举图标接口保留兼容。透明边界裁切、NPOT 保留比例避免长条物品在纵向格子中缩成小图。
- 普通地面掉落由服务器发送顶层物品 code，客户端实例化对应模型。内容清空时隐藏实体，仍保留原有权威回收宽限期。物品身份变化时重建显示；乱序移除不能复活模型身份。
- 新增 `RaidGroundItemMarkerRpc` 替代旧标记类型，使用独立生成序列化身份；客户端和游戏服务器必须同步更新。没有改变库存 code、存档 schema、物品价格、拾取权限或碰撞规则。
- 战术道具手持／投掷外壳与部署补给箱复用预制体。现有角色穿戴、近战动作、能量弹特效和展开后的陷阱功能结构继续使用原实现；本次不是所有装备穿戴绑定的重做。
- 共享目录内其他音频任务已经把战术显示的资源入口一并提交到 `830625e5`；本批资源和公共类型补齐该接入，未重写那个提交。

## 造型与质量边界

工业件使用倒角、密封圈、接头、锁扣、接口和功能色；项圈保留猫铃／蝴蝶结，背包保留亚空间环。器官按腺体、囊体、心脏、神经束和根瘤区分轮廓。材质以共享金属度／光滑度为主；Blender 预览另有微表面程序噪声，尚未烘焙为 Unity 纹理。

这是统一风格、可投入现有物品展示链路的实体美术版本，不代表已达到《逃离塔科夫》的扫描贴图、布料细节或 AAA 写实标准。没有宣称帧率提升；当前全目录 274,730 三角形，逐件数据见 `PhysicalItemsGeometry.json`。

## 工具选择与复现

实际使用 Blender 5.2.2 LTS。检索核实：

- [ahujasid/mcp-for-blender](https://github.com/ahujasid/mcp-for-blender)：检索时 32,106 stars、2,912 forks，未归档，最近 push 为 2026-10-20；上游 SHA `5a3618fccb2d982d0dd61e7a1879912c3100073b`。本次启用专用 Blender 场景中的插件，通过它的 127.0.0.1 本地命令接口执行、查询和导出；未向 Codex 永久注册新的 MCP 服务。
- [RobLe3/cc-blender-skill](https://github.com/RobLe3/cc-blender-skill)：119 stars、12 forks，最近 push 为 2026-10-12；SHA `a2634b588e80265e796aa53f8ab178d75105c2af`。审阅并使用 modeling、materials、export 及后端／宿主参考。完整包安装下载中断；所需指导文件已单独读取，未把不完整安装宣称为成功。该 Skill 社区规模远小于 MCP，不能据此声称“口碑极佳且社区巨大”已经得到充分证明。

MCP 是执行通道，质量依据是模型、导出和实际渲染检查，而不是使用 MCP 本身。

复现时使用新建的专用 Blender 场景，`Tools/Art/item_assets.py` 可直接作为 Blender Python 脚本执行，也可通过 MCP 分批调用 `setup()` / `publish(item)`。`MOONKOV_PROJECT` 可指定仓库根，`MOONKOV_ITEM_OUTPUT` 可指定输出暂存目录，默认 `/tmp/moonkov-item-art`。先生成到暂存目录，运行 `prepare_item_icons.py`（Pillow）裁切，然后在 **Edit Mode** 调用 `PhysicalItemAssetSetup.Import(stagingDirectory)`。长操作用 Unity CLI `run_script` 并设足够 `timeout_ms`；不要因 CLI 超时重复发起导入，应先检查编辑器结果。

## 验证

- Unity 实际编译完成，`scriptCompilationFailed=false`。
- `PhysicalItemAssetSetup.Validate()`：64/64 模型、图标和 URP 材质引用通过；全目录 274,730 三角形。
- `Tools/Validation/PhysicalItemPresentationChecks.cs`：64 个真实库存图控件和 64 个落地预制体通过；8 张 Unity 预览实际生成并查看，见 `PhysicalItemsUnity.png`。Blender 全目录总览为 `PhysicalItemsOverview.png`。
- 现有 `MoonkovRaidActionChecks.CheckGroundLifecycle`：包括新物品 code 的生成 RPC 往返、乱序删除、兴趣范围重入、回收和边界检查，通过。
- 完整 `MoonkovRaidActionChecks.Run()` 运行到 `Preview/slot input layering wrong` 失败：旧断言要求 `slotLayer` 为 Ignore，但当前 `RaidContainerInventory.uxml` 没有该设置。本次没有修改 UXML 或槽位 pickingMode，未把完整检查报告为通过。
- 未运行联机验收、第二 Editor、额外游戏客户端、目标平台构建或实际战局帧率测量。没有改动正式地图场景或用户烟雾弹测试场景。
