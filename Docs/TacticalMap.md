# M 战术地图

2026-10-22。赏金速战与远征共用一个中央 UI Toolkit 大图，默认关闭，常驻只保留 `[M] 地图` 提示；旧圆图不再进入 RaidHUD 的呈现路径。

- M 打开/关闭，Esc 优先关图而不打开暂停菜单；Tab 打开背包、远征 J 打开任务详情时先关图，M 从背包/详情切入时先关原界面。操作请求或耗时动作尚未允许关闭背包时不强行开图。开图不修改 Time.timeScale，不暂停服务端或真实对局。
- 滚轮以指针位置为锚在 1–8 倍内缩放，左键拖动平移；按钮可缩放、回到自身或查看全图。北向固定朝上，自己为朝向箭头。所有标记、地图范围、粗区域、缩放和平移使用同一 XZ 投影。画布裁剪溢出，800/1280 使用真实 Screen 视口抵消面板缩小，不读取菜单/世界相机像素尺寸。
- 赏金图仍由 `BountyClashView → BountyClashMapView.Present` 消费授权快照及 OwnTeam 名单；自己位置/朝向来自客户端 owned query。地面赏金和本队携标画精确菱形，不再重复取整；敌队携标只按服务端 25 米粗格信号画不确定区域，不画精确携标人。未生成赏金时画目标，已撤离赏金不继续画旧位置。没有敌方 Ghost 扫描或私有 Server World 查询。
- 远征只使用现有 MoonRaidMap 的补给数组、撤离点和 TakenMask，画补给/撤离示意，无新增捕捉相机或 RenderTexture。首次及全图范围包含当前自己的位置；保留 RaidMinimapView 的原方法、Project/HalfRange 和旧文档节点给旧消费者，RaidHUD 仅 Hide，不再调用其 Present/Capture。
- `RaidHUD.Map` 管理开关、光标和 World/raid/match/epoch 生命周期。`PointerRequested` 将地图接入现有 `GameplayActionsBlocked` 门禁，并预留开图键所在输入帧及关图帧，拦截射击、瞄准、滚轮、镜头与快捷栏。沿用 Runtime 已存在的 GameplayInputBlocked 委托，未添加 DollSinger.Runtime 对 Assembly-CSharp 的反向依赖。地图还拦截 UI 指针/滚轮传播，取消暗视与救援续租；死亡、倒地、断线、换局、暂停、离开及禁用 HUD 时关图、释放拖动捕获并清理数据。

本轮验证为源文件静态检查：资源引用、UXML 名称/无内联样式、回调和门禁链、授权数据来源、JSON 占位符，以及 47 条护具/近战键 + 15 条地图键的 JSON/en/zh/shared ID 对应关系。字符串表按已有 Unity YAML 定向追加稳定唯一 ID，不全量重序列化；Fonts 不变。首次新增警告已修正为 ResetData 并移除过时 PreventDefault；协调方随后使用现有 Bee response files 和 Unity 自带 Roslyn 最终隔离复编，主程序集退出码 0（6.16 秒、仅两条旧警告），Editor 程序集退出码 0（2.36 秒、19 条旧警告），本轮新增警告为零。证据位于 `Library/BuildValidation/SourceOnly-20261022/`，未覆盖实际 Bee/ScriptAssemblies DLL，不包含 Unity 导入、IL 后处理或渲染。

没有启动 Unity、Play、独立游戏客户端、测试、Blender 或构建；M/Esc/滚轮/拖动、光标恢复、800×600/1280×720 的实际布局和帧行为仍待实机验收，不以静态检查或旧 fixture 图代替。


## 2026-10-24 卫星底图与地图信息

M 地图改为低饱和卫星地图：主体是正式 Theophilus 场景的真实正交俯视缩略图，紧凑灰绿工具栏与项目深色装备界面相衔接。这是既有设计系统的局部扩展，不替换全局设计文件。桌面侧栏使用普通 VisualElement（不是 ScrollView），承载撤离、附近补给、物资分类统计、自身坐标与朝向；小于 1000 逻辑像素时侧栏默认收起，可通过「地图详情」展开。首次开图以 2 倍缩放居中自身，仍可查看全图。

- 底图：`Assets/Resources/Moonkov/Maps/TheophilusSatellite.png`，4096×4096 像素，对应世界 X/Z 的 0–4096 米，北为 +Z。来自正式场景当前实际地形与静态建筑渲染，经过 0.5–99.5 百分位灰度拉伸，图内嵌来源元数据；没有使用生成图片代替地形。边界与来源在同目录 `TheophilusSatelliteBounds.json`，其中 MC00 静态地标坐标取自真实场景根节点变换。
- `Tools/Moonkov/Bake Current Scene Satellite Map` 重新烘焙当前正式场景底图；编辑期创建临时摄像机并清理，不保存场景。以后移动建筑或修改地形后应重新烘焙；底图不表示动态门状态、敌人或实时光照。
- 运行时只读取静态压缩贴图，底图与标记共用 XZ 投影，拖动和缩放一起变化，不增加地图摄像机或 RenderTexture。没有底图的其他场景继续使用示意图，不套用错误地形。
- 玩家位置、高度与朝向，撤离的水平距离、方位、范围及停留秒数，以及补给编号、类别、可搜索数量和最近可用箱，来自既有 owned pose、MoonRaidMap 和 TakenMask。空箱使用叉号，悬停查看补给类型与距离。
- 右键设置单个个人导航点，金色标记与连线随缩放平移；可清除，换局/断线等原有 ResetData 路径清空。距离为水平直线距离，不承诺道路通行或自动寻路。
- 赏金模式侧栏显示本队名单统计、授权目标与撤离阻挡/停留状态；敌队携标仍只显示服务端粗区域，不输出精确目标距离或朝向。补给读数仅开图时每 0.2 秒刷新。
- 地标、撤离、自身、最近补给与个人导航点标签在应用位置偏移后，按最终标签矩形约束到地图边界内；这项边缘修正尚无后续截图验收。

用户明确选择真实场景缩略图、低饱和卫星观感和紧凑灰绿控件，并拒绝此前没有图像的网格方案。已参考 [LROC QuickMap 文章](https://www.lroc.im-ldi.com/news/1167)及其 [Theophilus WAC 图像](https://lroc.sese.asu.edu/news/uploads/LROCiotw/Theophilus_WAC.png)；这些仅为审美参考，没有作为游戏底图导入。

验证边界：正式场景底图已在现有 Editor 烘焙并直接查看，场景未保存或切换。最终标签边缘修正后的两个地图脚本已用 Unity 自带 Roslyn 和现有项目引用完成定向编译（Library/BuildValidation/TacticalMapInfo/MapCompileCheck.dll，退出码 0，无 C# 源文件诊断；仅编译器配置警告 CS2023）。烘焙工具此前也完成隔离编译。1280×720 原生预览 `.impeccable/review/tactical-map-1280.png` 使用真实场景底图和补给配置加示例玩家位置，已直接查看并经独立复核，地形与建筑可辨识、方向符合所选方案；该截图早于最终标签边缘修正。共享 Editor 持续重编译其他并行改动，800×600 截图尚未取得；整工程仍有无关编译错误（最近一次为 `DollSingerView.TransitionCamera` 缺失）。因此尚不宣称紧凑视口视觉、修正后截图、整工程编译、正式 Play、物理输入、联机或性能验收通过。
