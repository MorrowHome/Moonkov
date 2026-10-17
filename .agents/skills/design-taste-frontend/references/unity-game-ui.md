# Unity 游戏 UI 落地

## 框架与现有接入

先根据目标屏幕确认实际实现：UXML / USS / UIDocument 属于 UI Toolkit，Canvas / RectTransform 属于 uGUI，OnGUI 属于 IMGUI。同项目可能并存，沿用目标界面的框架。可用时结合对应 Unity UI skill 获取实现细节，不把技能包当成已经连接的 Editor 工具。

通过 Unity MCP 或 CLI 操作前确认工具连接的项目根路径与当前仓库一致，并读取必要能力；不能通过 provider 名称或哈希猜测项目归属。遵守用户对 Play Mode 的控制约定。

优先修改现有样式、组件、资源和绑定入口。运行时生成的界面也检查生成代码，避免只改编辑器资产导致运行时被覆盖。保留稳定的元素名称、类名、序列化引用与事件契约；不得不变更时同步消费者。

## UI Toolkit

- 先确认现有主题 USS、PanelSettings 与目标文档关系。将共享色彩、间距和状态整理到项目支持的样式机制，局部样式保留合理作用域。
- 使用项目支持的 USS / UXML 能力，不把 CSS Grid、浏览器滤镜、DOM 或网页媒体查询当成 Unity API。适配沿用现有 PanelSettings 缩放、布局约束或尺寸响应逻辑。
- 需要事件或数据更新时，复用绑定与控制器模式，处理文档重新启用、视图重建后的重新绑定及回调解除。不能仅在首次创建时绑定，却在重开界面后失效。
- 模态、拖动和游戏输入相互协调；不交互的覆盖层不应意外截断目标操作，隐藏界面不应继续拦截输入。

## uGUI

- 沿用 CanvasScaler、锚点、布局组和安全区域方案。确认目标比例下的尺寸关系，不用一组固定屏幕坐标替代响应布局。
- 复用现有 Button / Selectable 导航、事件和字体。装饰层按实际需要关闭 Raycast Target，检查模态遮罩与控制器焦点。
- Sprite 需考虑切片、九宫格边框与导入设置，避免通过拉伸破坏纹理边缘。只有实际受影响时才改图集或资源导入策略。

## 字体、素材与运行成本

中文字体与本地化沿用项目字体资产、fallback 和字符串表。UI Toolkit 的 TextCore FontAsset 与 uGUI 的 TMP 使用各自已有路径，不能假定互换。检查目标文本是否缺字、截断或行高失衡；不要为一个标签无依据地重建整套字库。

复用美术资源与音效路由。新增位图时按已有资源组织与导入规范保存；图标需要透明背景时明确提出该要求。可编辑文字、交互命中区和动态数值保留为真实 UI，不烘焙成整屏图片。

性能检查围绕具体改动：避免每帧查找整个层级、重建大列表、重复注册事件或播放高开销特效。有大量滚动物品或透明叠层的新风险时，再做针对性观察；不为小样式调整开展全项目性能审计。

## FPS_Template 中的已知入口

以下入口在此次适配时核对过，后续使用仍以实际仓库为准：

- `Assets/UI Toolkit/GameUI/`：包含 `MainMenu.uxml`、`PauseMenu.uxml`、`PlayerHUD.uxml`、`PlayerHUD.uss` 和共享样式。
- `Assets/UI Toolkit/PanelSettings.asset`：现有 UI Toolkit 面板配置，修改前检查影响范围。
- `Assets/Scripts/UI/Game/MoonkovLocalization.cs`：UI Toolkit 本地化与字体入口，使用 `MoonkovUI`、`MoonkovFonts` 表并处理中文字体与字距。
- `Assets/Scripts/Audio/MoonkovUIAudio.cs`：现有 UI Toolkit 控件音效绑定，新增回调需避免与其重复。

这些是定位线索，不要求每次读取全部文件或限定其他项目使用相同架构。对正式游戏界面的改动，应在实际入口验证受影响页面，原型与研究场景不能替代已接入的证据。
