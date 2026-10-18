# Moonkov 原生 UI 记录

分支：feat/better-UI。登录固定白色 C，主菜单 C/D 各自保留原构图。

最新素材结构：连续的干净背景 + 独立无字图片控件 + 原生 Unity UI Toolkit 文字。HomeCleanC/D 和 AccountCleanC-v2 由认可的原提案清除人物与功能控件/标题得到，背景不再用矩形拼接。主菜单角色仍由项目原 MenuCharacterView 渲染。

TextlessControls 的 22 张 PNG 仅含边框、插画与图标；按钮文案、编号、箭头、账户标题、字段标签、错误信息和数据均为 Button/Label/TextField。控件图片不承担任何可翻译文字。登录/注册边框采用 USS 9-slice，摄影插画使用图片层；文字字号独立于图片尺寸。

编辑入口：AccountAccess.uxml + AccountProposal.uss；Ship.uxml + Components/HomeCommandCard.uxml + ApprovedHome.uss。各入口保留原名称、事件与导航，TerminalMotion.cs 未修改。后续特效可叠加至各控件状态层。已保留淡蓝悬停着色、蓝色焦点边、按下变暗和禁用透明度。

最新验证：正式 Assets/Scenes/MainMenu.unity 启动及真实 MainMenu 控制器，账户原生字段和标题存在，Game View 2560×1440 截图 actual-mainmenu-entry.png。主菜单 C/D 为真实 UXML/控制器的离屏本地数据验证（1600×900），另检查 C 的 1280×720。home*.png 的 NATIVE UNITY UI / LOCAL REVIEW DATA 标记明确其数据来源。没有提交登录/注册、没有账号/交易写入。

此前全案例渲染见 Runtime-review-all.txt，属于较早一轮；除更新的 home*.png、actual-mainmenu-entry.png 外，其他截图是早期组件接入记录，不能代表最新美术验收。其余页面已接入组件与样式，但仍不能声称全部达到选定提案。部分物品仍使用旧图标。

未验证：真实登录/注册响应、购买/结算、Host/client 联机、物理手柄、4K/超宽屏、GPU 帧率或正式地图动态明暗。没有本次 UI 的真实性能测量。没有改渲染管线、场景光照、性能设置，未使用电脑接管工具。

入口修复：恢复 MainMenu.uxml 原有 HidingBackground 节点，避免 MainMenu.OnEnable 既有绑定空引用。验证不保存或替换用户当前场景；只结束本工具启动的 Play，并恢复此前 playModeStartScene。

交互状态截图 actual-account-focus.png 来自同一实际入口，展示控件的蓝色状态反馈；它不等于物理键盘/手柄的完整导航验收。单人/退出两个次级入口已核对世界坐标不重叠。
