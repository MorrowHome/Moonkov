# 库存改动分支交付（2026-10-23）

以下分支各自对应独立问题／需求，并已保留依赖历史，不自动合入 develop：

- fix/raid-inventory-pointer：避免游标重置与非结构数值快照销毁按住的按钮。
- fix/raid-equipment-quickbar：物品身份绑定及装备／快捷栏拖放。
- feat/raid-loot-layout：局内人物装备、随身容器与搜刮三栏。
- feat/warehouse-inventory-layout：同结构的个人仓库与更大格子。
- feat/secure-container：保险箱与本地／后端死亡保留结算。
- fix/inventory-space-density：按用户反馈删去占位文案／卡片，中栏内容格铺满宽度。
- feat/special-equipment-slots：三工具槽、指南针／测距仪与显示。

依赖顺序按上述顺序从前到后累积；各分支新增差异保持单一。共同基线仍依赖 feat/lan-rooms；main 未改动。工作目录最后停在特殊装备分支，可检查完整组合效果。

最小验证：原生 UXML/USS 渲染（1600×900、1280×720）和实际库存事务回调；死亡／超时／正常撤离／放弃、重复收据、磁盘重载共 70 断言；后端构建通过。物理鼠标对局、正式地图工具读数、PostgreSQL 在线结算和联机不在此次验收内。

新增独立分支 feat/corpse-character-portrait：对方角色静态全身像、原始装备槽身份和搜刮传输。依赖 feat/special-equipment-slots。特殊槽最终固定 52×52；21 项尸体身份／快照／事务断言及两尺寸原生画像检查通过。独立 UI 复核对当前画像与工具尺寸返回 ship；不替代正式对局验收。
