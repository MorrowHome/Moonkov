# 出战五步：提案到原生 UI Toolkit

本次仅处理出战五个子页，不宣称其他页面获得新的艺术验收。游戏入口仍是 `Assets/Scenes/MainMenu.unity` 中既有主菜单的出击/准备按钮；最后调用原有 `m_Prepare`，由原控制器决定单人部署或联机连接。不是另建一个预览场景替代游戏入口。

## 提案与实现

| 页面 | 用户选择 | 保留的构图 | Unity 实际渲染 |
|---|---|---|---|
| 角色 | 19 D | 左侧月球/真实角色；中间确认与装备检查；右侧摄影任务摘要 | [1600](Native-20261018/operator.png) / [1280](Native-20261018/operator-compact.png) |
| 地点 | 20 C，按新指示转深色 | 左侧竖向五步；右侧大地点主视觉；下方区域/信标分栏；相邻返回/继续 | [1600](Native-20261018/location.png) / [1280](Native-20261018/location-compact.png) |
| 简报 | 21 D | 左侧连续月面弧形；右侧错位标题/场景；三张搜集/生存/撤离图文卡；下方宽携带控制带 | [1600](Native-20261018/briefing.png) / [1280](Native-20261018/briefing-compact.png) |
| 回收 | 22 D | 左侧月面拼贴；右侧大标题、红色无保障警告、信息条与禁用合约入口 | [1600](Native-20261018/recovery.png) / [1280](Native-20261018/recovery-compact.png) |
| 确认 | 23 C，按新指示转深色 | 左月球、右五条横向摘要；最终操作嵌在第五条右半区，无人物 | [1600](Native-20261018/ready.png) / [1280](Native-20261018/ready-compact.png) / [忙碌禁用](Native-20261018/ready-busy.png) |

选择图保留于 Approved-CD-20261018。提案中的示例坐标、等级、假装备和示例携带数量没有成为游戏数据。地点摄影仅作提案插图，不能当作实时地图、信标位置或正式地图截图。

## 可编辑源文件与组件

- `Assets/Resources/Moonkov/UI/Operation.uxml`：共享五步导航、品牌、返回、继续。五个 Operation*.uxml：各自独立构图；`layoutPreview` 是仅供编辑的 1600×900 包装，运行时按既有 TerminalLayout 提取 terminalPage 的内容。
- `Components/OperationEquipmentRow.uxml`：复用装备检查行，槽位/名称/图像/状态独立；数据来自 PlayerProfileClient.Inventory 的真实 Equipped 结果。没有账户快照时显示“—”，已知空槽显示 EMPTY 的本地化，不把示例装备标成已装备。
- `OperationProposal.uss`：页面专属作用域，避免旧 StashScreen 按钮样式覆盖高度、颜色和状态。图片、边线、原生 Label、Button、IntegerField 分开。UI Builder 菜单：Tools / Moonkov / Edit Layout / Operation Operator、Location、Briefing、Recovery、Ready。
- `MoonkovTerminal.Operations.cs`：只绑定步骤、真实装备、原生角色预览、携带量与原回调。角色只在第一步通过既有 MenuCharacterView 显示，离开即沿用原释放流程。未生成/改动人物模型、光环、头盔、胸挂或背包美术。
- `OperationArt`：四张 imagegen 清理的无字背景、九张仅含插图的矩形裁切；manifest.json 记录来源。地点复用此前清理过的 Operations 画布并仅给背景加深蓝 tint。功能文字/箭头/数字没有留在纹理中。

在 UI Builder 中改布局和 USS 即可调整，不需要重新生整屏图。动态装备行和角色渲染需要运行 presenter 才有真实数据；静态源文件不会假装已接入账户。

## 关键样式参数

| 角色 | 参数 |
|---|---|
| 基底 / 正文 | #08090C / #F1F2ED，次要正文 #C3C9D2 |
| 行动 / 焦点 | 主按钮 #164BFF，悬停/焦点 #153981 + 白色 2px 边；按下 #0A1A42，向下 2px |
| 已选步骤 | 深蓝 #102555 + 蓝色 3px 底线；地点页采用蓝底和左边线 |
| 无保障 | #FF5666 的标题、明确文字和警告图形；合约入口真实 disabled |
| 字号 | 1600 宽：主标题 54、组标题 24、正文 16；1280 宽：40 / 20 / 13，协议次要说明 11–12。主要按钮为 24 / 20 |
| 版式 | 角色检查区从 43% 开始，任务区从 72%；地点内容从 46% 开始；简报从 37%；回收从 44%；确认从 49% |
| 贴图 / 动效 | 静态纹理最高 2048、无 mipmap、Clamp、CompressedHQ；沿用 TerminalMotion 和原生控件反馈，没有新增实时后处理或循环故障动画 |

关闭装饰：移除五个 operation-step 背景及 op-photo / op-action-art 的 background-image，即可保留深色基底、文字、规则分区和所有操作。没有改变渲染管线、场景光照、质量或性能设置；本轮没有 GPU/帧时数据。

## 真实绑定与验证

携带输入和加减按钮沿用 PlayerProfileClient.SelectCarryCells，由原接口限制仓库数量与 RaidRules.BagCapacity。仅呈现页面不会写账户。确认页显示实际 CarryCells。保险尚未开放，合约按钮没有购买回调；最终动作沿用既有 m_Prepare。没有新玩法、物品属性、经济数值或在线服务。

Unity 已完成脚本编译、资源导入，以及 1600×900 / 1280×720 的五页原生 Play RenderTexture 检查。原生 NavigationSubmit 走通步骤栏、Continue 0→4、Back/Continue、最终回调恰好一次；busy 时电池输入/加减和最终按钮禁用。记录见 [专项结果](Native-20261018/Runtime-departure-20261019.txt)。截图明确标记 LOCAL REVIEW；本地账户未打开，因此装备字段显示“—”，不是库存截图。编译与截图均不能证明正式部署、网络事务或性能。

首轮发现旧 Button 34px 高度/黄色焦点覆盖新样式，以及角色初次加载未完成就捕获；已分别用页面作用域和实际渲染就绪检查修正。最终两种尺寸均复查过中文分行和操作区域；默认、选中、焦点、保险禁用与最终 busy 有渲染证据。悬停/按下有明确 USS 状态，本轮没有物理鼠标、键盘或手柄输入验收；未使用电脑接管工具。

尚未验证：实际登录账户的装备名称长文本/携带修改事务、真实单人部署、Host/client、物理设备操作、4K、超宽屏、正式关卡与 GPU 性能。原电池/装备外观仍是项目现有美术，后续专门美术可只替换图像组件。

## 自查

- 构图/尺度/材质：继承各自选定的月面大尺度、弧形拼贴、摄影颗粒和精密线条，未只换配色。C 的布局按最新要求转深色。
- 去掉主背景：原生文字层级、蓝色行动入口、规则分区、选中边线与禁用状态仍成立。
- 功能差异：五页采用双栏检查、竖向步骤/地点、三图协议/携带带、风险警告、五条摘要，未统一成单页模板。
- 中文/密集数据：检查两种尺寸的本地中文；装备数据尚无真实账户长名称验收，不把空行当完整库存验收。
- 状态：已选步骤、原生焦点、无保障警告、合约禁用和连接忙碌各有独立语法。未把保险禁用伪装成可购买。
- 分辨率/输入：1600×900、1280×720；程序发送原生导航事件；物理输入、4K/超宽未查。
- 真实性：项目内 UI Toolkit 实现和 Play 离屏渲染，非网页、非生成的整屏运行图；无正式部署/联机/性能数据。
