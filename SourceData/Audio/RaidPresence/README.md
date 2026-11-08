# 游玩中的持续声音

- 通风源：lfyaudio 的 [Air vent (looped)](https://freesound.org/people/lfyaudio/sounds/848969/)。CC0 1.0。使用公开高质量 MP3 预览。工业场所通风实录，不含虚构人群。
- 起跳源：Cici Fyre 的 [Female RPG Voice Starter Pack](https://opengameart.org/content/female-rpg-voice-starter-pack)。CC0 1.0。保留 Type 1 的 jump1 至 jump3 原文件。
- 通风取原录音第 10 至 19 秒，经一秒交叉淡化形成八秒循环。室内版本保留风机和气流。服装版本收窄频段并降低配置电平。原录音不是航天服录音，此处用于生命维持设备拟音。
- 起跳保留演员原速度和音高。只滤波、去直流、做短淡入淡出及峰值调整。
- 升降机复用项目已有 Joseph SARDIN 的气压机、滑门、门锁实录及装备声。原来源见 `Assets/Audio/Moonkov/Airlock/Sources.json` 和原授权文件，不改气闸音库。
- 重建入口：`Tools/Audio/build_raid_presence_audio.py`。绑定入口：`MoonkovPresenceAudioSetup.Install()`。输入和输出哈希见 `RaidPresenceSources.json`。
