# 仓库布局（2026-10-23）

feat/warehouse-inventory-layout 依赖 feat/raid-loot-layout。沿用原生 StashScreen / ContainerInventoryView 账户事务；人物静态全身像与身体装备槽、胸挂及背包入口和内容、个人仓库三栏。右侧仓库独立滚动，扩充分配宽度，十列格子按空间扩展到 38–76px，物品标签 12px。取消此界面的实时角色摄像机。

Unity 编译通过；1600×900 原生渲染与从仓库拖至衣兜的真实 InventoryGraph 事务通过。未进行正式菜单操作或联机验收。原有恢复收件箱与研究／武器改装入口保留。
