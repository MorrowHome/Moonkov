# M 战术地图

2026-10-22。赏金速战与远征共用一个中央 UI Toolkit 大图，默认关闭，常驻只保留 `[M] 地图` 提示；旧圆图不再进入 RaidHUD 的呈现路径。

- M 打开/关闭，Esc 优先关图而不打开暂停菜单；Tab 打开背包、远征 J 打开任务详情时先关图，M 从背包/详情切入时先关原界面。操作请求或耗时动作尚未允许关闭背包时不强行开图。开图不修改 Time.timeScale，不暂停服务端或真实对局。
- 滚轮以指针位置为锚在 1–8 倍内缩放，左键拖动平移；按钮可缩放、回到自身或查看全图。北向固定朝上，自己为朝向箭头。所有标记、地图范围、粗区域、缩放和平移使用同一 XZ 投影。画布裁剪溢出，800/1280 使用真实 Screen 视口抵消面板缩小，不读取菜单/世界相机像素尺寸。
- 赏金图仍由 `BountyClashView → BountyClashMapView.Present` 消费授权快照及 OwnTeam 名单；自己位置/朝向来自客户端 owned query。地面赏金和本队携标画精确菱形，不再重复取整；敌队携标只按服务端 25 米粗格信号画不确定区域，不画精确携标人。未生成赏金时画目标，已撤离赏金不继续画旧位置。没有敌方 Ghost 扫描或私有 Server World 查询。
- 远征只使用现有 MoonRaidMap 的补给数组、撤离点和 TakenMask，画补给/撤离示意，无新增捕捉相机或 RenderTexture。首次及全图范围包含当前自己的位置；保留 RaidMinimapView 的原方法、Project/HalfRange 和旧文档节点给旧消费者，RaidHUD 仅 Hide，不再调用其 Present/Capture。
- `RaidHUD.Map` 管理开关、光标和 World/raid/match/epoch 生命周期。`PointerRequested` 将地图接入现有 `GameplayActionsBlocked` 门禁，并预留开图键所在输入帧及关图帧，拦截射击、瞄准、滚轮、镜头与快捷栏。沿用 Runtime 已存在的 GameplayInputBlocked 委托，未添加 DollSinger.Runtime 对 Assembly-CSharp 的反向依赖。地图还拦截 UI 指针/滚轮传播，取消暗视与救援续租；死亡、倒地、断线、换局、暂停、离开及禁用 HUD 时关图、释放拖动捕获并清理数据。

本轮验证为源文件静态检查：资源引用、UXML 名称/无内联样式、回调和门禁链、授权数据来源、JSON 占位符，以及 47 条护具/近战键 + 15 条地图键的 JSON/en/zh/shared ID 对应关系。字符串表按已有 Unity YAML 定向追加稳定唯一 ID，不全量重序列化；Fonts 不变。首次新增警告已修正为 ResetData 并移除过时 PreventDefault；协调方随后使用现有 Bee response files 和 Unity 自带 Roslyn 最终隔离复编，主程序集退出码 0（6.16 秒、仅两条旧警告），Editor 程序集退出码 0（2.36 秒、19 条旧警告），本轮新增警告为零。证据位于 `Library/BuildValidation/SourceOnly-20261022/`，未覆盖实际 Bee/ScriptAssemblies DLL，不包含 Unity 导入、IL 后处理或渲染。

没有启动 Unity、Play、独立游戏客户端、测试、Blender 或构建；M/Esc/滚轮/拖动、光标恢复、800×600/1280×720 的实际布局和帧行为仍待实机验收，不以静态检查或旧 fixture 图代替。
