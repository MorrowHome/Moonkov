# Theophilus 正式游戏地图

2026-10-15，正式游戏加载入口 `GameManager.GameSceneName` 改为 `TheophilusGameScene`。从原 `MainMenu` 开始游戏，离线、Host、Client 和独立 Server 沿用既有共用场景加载流程。

正式场景：`Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`；出生子场景：同目录 `TheophilusSpawnPointsSubScene.unity`。两者均登记并启用在 Build Settings。原 Apollo12 地图和 Theophilus 研究场景保留。

正式副本承接研究场景截至 R8 的地形、基地、八楼、气闸、巨构、R7 接地和 R8 表面/渲染配置。气闸的 `AirlockPrototypeDemo` 与本地诊断读数移除，角色由正式 Ghost/玩家流程生成；不再生成本地验证角色。原 F/滚轮交互、气闸服务端规则及声音走现有正式系统。

地图包含一套现有 `GhostSpawnerManager`、`LeaderboardSpawner`，自动加载共享 `GameResourcesSubScene` 和新的 Theophilus 出生子场景。8 个出生点位于 A 起点南侧附近，使用实际地图地面定位并检查胶囊空间。原场景中 Apollo12 的出生坐标不会混入。

`MoonRaidMap` 复用原搜打撤规则，配置 12 个地面物资点、A 起点东南侧撤离区，保留 8 秒撤离和原局时/生存设置。原 DollSinger 敌人数量 3 保留；8 个异形遭遇位置迁移至 Theophilus 物资点附近，Profile/注册 Ghost 仍为现有正式资源。未添加新的网络协议、角色控制器或离线专用玩法。

此正式副本与研究场景是不同 Scene 资产。后续只指向研究场景的云端视觉增量不会自动覆盖正式地图；再升级正式地图时需明确同步相应场景实例变更。

本次必要检查覆盖保存重开、正式加载常量、两个自动加载子场景、Build Settings、出生点空间以及正式资源/搜打撤入口。没有截图、重复走图或完整联机/性能测试；Host + 第二客户端、独立构建与实际画面尚未验收。客户端和服务器需使用包含此地图入口的同版工程或构建。

接入记录在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/FormalMap/`。正式地图制作前研究场景回退为 `BeforeFormalIntegration.unity`，原研究资产未重写。
