# 游戏原声音频来源

每行对应一条实际成品。时间为来源视频中的秒数。

这些是原游戏录音。版权归原权利人。用户指定用于个人非商用作品；此记录不声明额外授权。

成品统一为 48 kHz 单声道 PCM。保留原音高和速度。做剪辑、去直流、音量和边缘淡化；塔科夫片段另压低瞬时尖峰以提高主体音量，包扎循环加 40 毫秒交叉淡化。猎杀针剂不做压缩。

| 成品文件 | 游戏内用途 | 原游戏 / 权利人 | 录音出处 | 原片段 |
| --- | --- | --- | --- | --- |
| `Clips/Medical/InjectorHuntShot.wav` | 扎入、推注、拔针 | Hunt: Showdown v0.309 / Crytek GmbH | [Kylan Klein Lenderink / Noise Nerd Audio](https://www.youtube.com/watch?v=i3-9okS87S8) | 8.64–8.94、9.22–10.34、11.25–11.62 秒；依次放在成品 0、0.24、1.00 秒 |
| `Clips/GameFoley/TarkovEat1.wav` | 食物咬合与咀嚼 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 5.220–5.920 秒 |
| `Clips/GameFoley/TarkovEat2.wav` | 食物咬合与咀嚼 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 11.500–12.150 秒 |
| `Clips/GameFoley/TarkovEat3.wav` | 食物咬合与咀嚼 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 17.450–18.150 秒 |
| `Clips/GameFoley/TarkovDrink1.wav` | 喝水吞咽 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 193.350–194.050 秒 |
| `Clips/GameFoley/TarkovDrink2.wav` | 喝水吞咽 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 194.050–194.750 秒 |
| `Clips/GameFoley/TarkovDrink3.wav` | 喝水吞咽 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 194.750–195.500 秒 |
| `Clips/GameFoley/TarkovPacketOpen.wav` | 撕开食物包装 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 7.280–8.100 秒 |
| `Clips/GameFoley/TarkovBottleOpen.wav` | 旋开水瓶盖 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 191.750–192.900 秒 |
| `Clips/GameFoley/TarkovBottleClose.wav` | 旋回水瓶盖 | Escape from Tarkov / Battlestate Games Limited | [Ultimate Weapon Games](https://www.youtube.com/watch?v=a4G8H5r1n6A) | 196.200–197.050 秒 |
| `Clips/GameFoley/TarkovBandageOpen.wav` | 撕开绷带包装 | Escape from Tarkov / Battlestate Games Limited | [Joe Iz Gaming](https://www.youtube.com/watch?v=0EQEZ7RMr5I) | 1.720–2.500 秒 |
| `Clips/GameFoley/TarkovBandageWrap.wav` | 持续缠绕绷带 | Escape from Tarkov / Battlestate Games Limited | [Joe Iz Gaming](https://www.youtube.com/watch?v=0EQEZ7RMr5I) | 3.250–4.650 秒 |
| `Clips/GameFoley/TarkovBandageTighten.wav` | 包扎结束收紧 | Escape from Tarkov / Battlestate Games Limited | [Joe Iz Gaming](https://www.youtube.com/watch?v=0EQEZ7RMr5I) | 4.850–5.350 秒 |
| `Clips/GameFoley/HL2AirlockButton.wav` | 气闸按钮按下成功反馈 | Half-Life 2 / Valve Corporation | [sourcesounds 公开资源镜像](https://github.com/sourcesounds/hl2/blob/84f5de61421d95ce1eb9dcf9625a90782b41b278/sound/buttons/button17.wav) | sound/buttons/button17.wav（完整 0.081 秒） |

路径前缀为 `Assets/Audio/Moonkov/`。视频截录保留原录背景，不作为解包无损文件标注。

原始短片段和哈希：`SourceData/Audio/GameFoley/Sources.json`；成品校验和：`Assets/Audio/Moonkov/GameFoleySources.json`。
猎杀针剂详表：`SourceData/Audio/MedicalFoley/Sources.json` 与 `Assets/Audio/Moonkov/MedicalFoleySources.json`。
重建：`python3 Tools/Audio/build_game_foley.py`，然后在 Unity 执行 `GameFoleySetup.Install()` 与 `AirlockButtonAudioSetup.Install()`。
本表登记所有当前主动引入的其他游戏原声。既有 CC0、CC BY 和 Sonniss 素材保留各自原署名文件。
