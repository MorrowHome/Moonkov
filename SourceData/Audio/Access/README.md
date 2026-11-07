# 开场与登录音效源

- 人声源：SpliceSound 的 [Indoor adult murmur, medium group.wav](https://freesound.org/people/SpliceSound/sounds/260122/)。
- 许可：[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)。2026-11-07 核对来源页。
- `IndoorMurmur.mp3` 是来源页公开的高质量预览。原始 WAV 下载需要账号，本项目未下载它。
- 人声经过拉伸、叠层、滤波、短混响和循环交叉淡化。用于远处模糊交谈，没有新增对白。
- glitch 源：xhunterko 的 [Static](https://opengameart.org/content/static)。`ScatterNoise1.mp3` 为 CC0 1.0。剪成三个短片，进行带通滤波、声道延迟和断续处理。
- pop 源：EZduzziteh 的 [Pop sounds](https://opengameart.org/content/pop-sounds-0)。选择 `pop1.wav`，许可为 CC0 1.0。保留原音高和速度，增加瞬态饱和、短低频撞击与高频起音。每扇窗口使用同一声音，禁止随堆叠变调或随机变调。音库增益为 -8 dB。
- impact 主体：zazz.sound.design 的 [Deep Cinematic Impact 2](https://freesound.org/people/zazz.sound.design/sounds/754421/)。`DeepImpact.mp3` 是公开高质量预览，许可为 CC0 1.0。
- impact 边缘与空间层：Kierham 的 [Sci-Fi Impact](https://freesound.org/people/Kierham/sounds/646831/)。`ForceFieldImpact.mp3` 是公开高质量预览，许可为 CC0 1.0。
- 揭幕按频段叠加这两层，保留原声音设计的立体声空间尾音，再加短早期反射。总长 3.2 秒。用户要求有游戏音效的冲击感，不能用单独镲片代替。正常揭幕结束后保留尾音。
- 闭幕倒放同组 impact，再叠带宽逐渐变宽的噪声 rise。最后 120 毫秒增加高频密度、加速颗粒和饱和失真，形成明确的躁动顶点。总长 0.75 秒，从确认渐暗开始，到完全合拢时骤停。
- 带限白噪声、设备低鸣、集中闪断和低频冲击为项目离线合成。
- 生成入口：`Tools/Audio/build_access_audio.py`。需要 Python、numpy 和 ffmpeg。
- 产物与源文件 SHA-256 见 `Assets/Audio/Moonkov/AccessAudioSources.json`。
- 试听文件输出到忽略目录 `LocalData/AccessAudio/OpeningAndLogin.wav`。它仅用于参考，游戏按实际画面事件触发声音。
