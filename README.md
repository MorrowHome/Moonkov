# FPS_Template

Unity **6000.5.10f1 / URP 17.5** 的 FPS 模板，保留原有 `MainMenu → GameScene` 联机入口。

## 月球场景

在 Unity 选择 **Tools → Moon Environment → Open Demo**，然后点击 Play。

- 演示场景：`Assets/MoonEnvironment/Scenes/MoonDemo.unity`
- 可复用环境：`Assets/MoonEnvironment/Prefabs/MoonEnvironment.prefab`
- [中文使用与集成文档](Docs/Moon成果迁移/MoonEnvironment.md)
- [数据来源和迁移范围](Docs/Moon成果迁移/MoonSources.md)
- [项目结构说明](Docs/AI/UnityProjectContext.md)

月球模块全部集中在 `Assets/MoonEnvironment`；演示使用已有 Starter Assets 第一人称控制器，不需要连接 MMORPG 或运行服务器。

Git 已配置 LFS；共享项目时需要同时提交 Unity `.meta` 与 LFS 对象。不要提交 `Library`、`Temp`、`Logs`、生成的 IDE 工程。
