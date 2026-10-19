# Moonkov 原生 UI 渲染记录

当前接入说明见 ../Integration-20261019.md。本目录为真实 Unity UXML/presenter 的离屏渲染记录，不是网页稿。LOCAL REVIEW 标记表示本地展示数据，不代表账号、交易或联机验收。

最新主题为全深色：主菜单固定 D、账户使用深色画布，管理/准备/局内背包/连接弹窗/结算均为深色表面与浅色文字；蓝色强调保持。页顶三个摄影入口与次级导航、构筑页模块排布、Operation 单页准备台均保持本轮之前的布局。deployment.png 是默认状态，deployment-busy.png 是忙碌禁用状态。item.png 由真实右键菜单进入独立原生检查窗口。

Runtime-interaction-20261019.txt 记录：原生 NavigationSubmit 只触发一次准备回调、装备与底部准备入口往返、busy 禁用，以及原生物品检查菜单。所有交易回调使用明确的本地 fixture，没有账号写入。原换页效果未更改。

主菜单与登录从实际 MainMenu.unity 验证；actual-mainmenu-entry-20261019.png 是本轮实际 Game View，核对真实 MainMenu 控制器、原节点绑定和 ProposalSystem 样式连接。actual-mainmenu-entry.png 是上一轮记录。本轮各页面视觉检查以 1600×900、仓库/设置/准备台 1280×720 为限。原提案保留在 ../Approved-CD-20261018，不能当作运行截图。

Runtime-review-all.txt、Runtime-review-layout.txt 和 before-*.png 为历史记录；Runtime-review.txt 为最后一批针对性结果。原 operator/location/briefing/recovery/ready.png 是废弃的五步旧结构，不再表示运行入口。

全黑离屏输出不计为通过，工具已增加拒绝条件。没有控制桌面，没有修改本次 UI 范围外的渲染管线、光照或性能设置；没有真实性能数据。实际账号/交易、Host/client、物理手柄、4K/超宽屏与正式地图动态明暗仍未验收。

物品美术纠正：当前 halo/warehouse/raidpack/item/shop 等目标截图使用恢复后的原 HaloIcons 与原 StashItemArt；已撤掉生成的战术头盔/胸挂/背包图集。旧提案与历史截图保留供追溯，不是这些物品的最终美术。

Runtime-art-restore-20261019.txt 记录恢复物品外观后的 12 个受影响页面；构筑页使用新布局，保留真实物品外观与数据。Runtime-review.txt 的末次专项检查另含原生模块选择、右侧检查区更新，以及真实 HaloIcons 资源断言。

Runtime-navigation-20261019.txt 记录一级导航和构筑页纠偏的首批检查。最新全深色检查以 Runtime-dark-20261019.txt 为准：home、account、session、host、halo、deployment、warehouse、raidpack、loot、result、cargo、storage、settings-compact。其余截图为此前记录，部分含已退役的浅色主题；并非当前运行时全部仍混用浅色。本次没有新生成物品美术。
