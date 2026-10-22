# 能量护具与近战界面

2026-10-22 实施中；规则与数值由 Shared、服务器及 `Docs/Planning/EnergyGear-Melee-20261022.md` 定义。

- 正式仓库/随身库存沿用 `ContainerInventoryView` 和原 JUI `EquipmentSlot`，角色左侧头罩、护盾、胸挂、手枪，右侧背包、主武器、副武器、近战，共八个真实装备槽。旧 `LoadoutSlot`/`EquipmentKind` 值未重排；独立旧 sample 预览仍保留原六槽。
- 所有装备与拖放发送原 `InventoryCommand`；头罩仍使用 `Helmet` region，护盾为 `Shield`，近战为 `Melee`。装备槽和商店共用新增头罩/发生器/能量剑/匕首矢量图标；扩展图标枚举时限定原光环图片索引范围，避免新增类型误入光环数组。
- 电池右键菜单的“为护盾补能”读取 `EnergyShieldInventory.CanRecharge`。该方法使用当前本地角色 Ghost 的电量，避免被动库存数值暂未刷新时误禁用；交易仍经原 `RaidConsumableUseRpc`，不在 UI 扣电、加盾或制造电池。
- 新仪表只读 `ShieldEquipped`、`ShieldCharge`、`ShieldEnergy`、`ShieldRecoveryDelay`、`ShieldRegenerating`、破盾序号与原 `Oxygen`。远征放在现有快捷栏像素视口内，赏金模式附在既有侧栏自然流中。存储物品的资源值显示在 tooltip/检查页，不用它们代替实时 Ghost 状态。
- 新物品从正式商店/库存流程进入；拳头不产生库存物品。近战主 HUD glue 已核对实际 core partial 后接入初始化、逐帧呈现和释放；未把约定中的未来 API 当作可编译依赖。

47 条护具与近战 UI 本地化键已按现有 YAML 结构定向补齐 en/zh/SharedData 三表，并与 `Translations.json` 核对；原有 ID、顺序和元数据保留。另补 15 条 M 大图键，共 62 条，三表各 1144 个唯一且对应的 ID。本轮只做文本源码和轻量静态检查，未启动 Unity、游戏或测试进程；协调方另有隔离源码编译记录。新增装备布局、补盾按钮、仪表、近战行为与地图尚未完成本轮实机验收；小模式选择器的 800 视口修正也仍待重拍，不把源码检查当作视觉通过。地图接口、输入门禁和授权范围见 `Docs/TacticalMap.md`。两份 Fonts 资产未修改。
