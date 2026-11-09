# 体力粗重呼吸

现用录音：mikeask，Breathing Tired。CC0-1.0。

来源：https://opengameart.org/content/breathing-tired

`BreathingTired.wav` 保存原 WAV，与普通 `SuitBreath` 同源。只剪辑吸气、呼气和停顿，保留原速度与音高。加少量低频厚度，降低尖锐气流声。不加入呻吟或喉音。
裁剪、处理和哈希见 Assets/Audio/Moonkov/StaminaBreathSources.json。
重建：python3 Tools/Audio/build_stamina_breath.py。然后执行 MoonkovRaidAudioSetup.InstallBreathing()。

旧 `FemaleRunBreath.mp3` 保留但停用。它来自 MelvinJaepel 的 CC0 woman run and breath.wav：https://freesound.org/people/MelvinJaepel/sounds/435468/ 。保存的是高质量 MP3 预览。用户明确否定将这类发声很重的喘叫用于体力耗尽。
