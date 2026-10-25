# Moonkov 项目策划阅读入口 / Project planning index

这里保存长期规划和专项策划。两份双语长期规划具有相同需求ID、内容范围、阶段计划和验收边界。

- [单人战役完整策划](Campaign/README.md)：2026-10-25 新增。八章故事、24 个主线、8 个可选支线、持久追踪与任务系统规格。结局为携证乘船返回地球。本轮为待评审策划，确认后实装。
- [中文完整策划](Moonkov_Project_Plan.zh-CN.md)
- [Complete English plan](Moonkov_Project_Plan.en.md)
- [随机局内任务系统](Missions/README.md)：无需接取的随机任务、MC00 具体流程、界面追踪与后端结算；分阶段实现与验证范围见其中的实施状态。

单人战役的新方向以 Campaign 为阅读入口。历史长期规划中的“任务后加”和随机事件设计，不能当成已完成这条战役。现有随机事件仍是可复用基础。

The new [solo campaign proposal](Campaign/README.md) contains eight chapters, 24 main missions, eight optional missions, and the tracking and persistence specification. The ending brings the protagonist and the evidence back to Earth by spacecraft. It is a design deliverable awaiting review, not an implemented campaign.

## 中文阅读顺序

1. 先读 MK-STATUS、MK-REQUIREMENTS、MK-SNAPSHOT，区分已确认目标、提案、待决定事项及已验证事实。
2. 阅读任务对应的完整章节；健康见MK-HEALTH，地图/角色/武器见MK-QUALITY，异形见MK-ENEMIES，网络见MK-NETWORK。
3. 实现前阅读MK-TESTING、MK-DECISIONS、MK-RISKS；Issue/PR引用稳定需求ID、精确提交和实际测试证据。
4. 同时核对当前代码、项目指引及相关模块文档。纯.NET检查通过不等于Unity、实际地图或LAN通过，草稿发布不等于验收或合并。

v0.3.1由24页v0.3全文转为Markdown，纳入后续异形地面/墙面/天花板和许可核查要求。建议中文作为语义主版本；若有差异，记录并请负责人确认，再同步两份文件。该维护建议不增加玩法授权。两次展示日期是硬节点，其余逐日安排仍为建议。日期快照不会自动更新。

## English reading order

1. Read MK-STATUS, MK-REQUIREMENTS and MK-SNAPSHOT first to distinguish confirmed goals, proposals, open decisions and verified facts.
2. Read the full sections relevant to the task: MK-HEALTH for health, MK-QUALITY for map/characters/weapons, MK-ENEMIES for aliens and MK-NETWORK for networking.
3. Before implementation, read MK-TESTING, MK-DECISIONS and MK-RISKS. Cite stable requirement IDs, exact commits and actual test evidence in issues/PRs.
4. Also inspect current code, project guidance and relevant module documentation. Pure .NET passes are not Unity, actual-map or LAN passes, and draft publication is not acceptance or merge.

v0.3.1 converts the complete 24-page v0.3 into Markdown and incorporates later alien floor/wall/ceiling and license-check requirements. Chinese is recommended as the semantic source of truth. If the versions differ, record the discrepancy, ask the owner to resolve it and update both. This maintenance recommendation grants no new gameplay authority. The two presentation dates are fixed; other daily scheduling remains proposed. The dated snapshot does not update automatically.

## 相关实现文档 / Related implementation documentation

- [项目结构 / Project structure](../AI/UnityProjectContext.md)（存在历史漂移，需核对代码 / contains historical drift; verify against code）
- [单人模式 / Solo mode](../SinglePlayer.md)
- [库存与容器 / Inventory and containers](../ContainerInventory.md)
- [持久化 / Persistence](../PostgreSQLPersistence.md)
- [人形AI / Humanoid AI](../AI/DollSingerEnemies.md)
- [月面来源 / Lunar source data](../Moon成果迁移/MoonSources.md)

本目录只提供策划参考，不覆盖或替换适用的AGENTS.md及既有仓库操作规则。  
This directory provides planning references; it does not override or replace applicable AGENTS.md files or existing repository operating rules.
