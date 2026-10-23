# Moonkov

<!-- impeccable:product-schema 1 -->

## Platform

Unity 桌面游戏，当前开发与验证环境为 Windows。界面使用原生 UI Toolkit、UXML、USS 与 C#。

## Users

远征或极速赏金对局中的玩家，需要在有战斗风险的情况下查看装备、整理物品、绑定快捷物品和搜刮。

## Product Purpose

在现有撤离射击游戏中，让穿戴、储物、取用和搜刮形成可理解、可恢复的操作链路。

## Capabilities and Constraints

- 装备与物品数据沿用现有 InventoryGraph、服务端校验及库存回包，不用界面动画伪造事务成功。
- 用户明确指定局内背包：左侧显示装备，中间显示自己的容器，右侧显示搜刮容器或尸体；未装备时仍有空槽，尸体同时显示装备与容器内容。
- 保留中文、本项目物品图标、现有物品身份和快捷键；使用深色界面。
- 每个独立 Bug／需求使用独立分支。最多一个 Unity Editor，不开展联机验收，不使用 Windows 鼠标操控工具。

## Evidence on Hand

`Assets/Scripts/UI/Game/ContainerInventoryView.cs`、`QuickSlotBindings.cs`、`Assets/Scripts/Gameplay/UI/RaidHUD.cs` 与 `Assets/Resources/Moonkov/` 是实际运行入口。

## Product Principles

- 查看、选择、拖动和提交是不同状态；网络等待不能让玩家连物品都无法查看。
- 有效目标必须可见，失败后保留原物品并解释原因。
- 用户指定的空间分区优先于装饰与角色展示。
