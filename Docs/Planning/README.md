# Moonkov 项目策划阅读入口 / Project planning index

这里保存完整的双语策划，不是摘要。两份文件具有相同需求ID、内容范围、阶段计划和验收边界。

- [中文完整策划](Moonkov_Project_Plan.zh-CN.md)
- [Complete English plan](Moonkov_Project_Plan.en.md)

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
