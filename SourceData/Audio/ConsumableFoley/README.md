# 吃喝、补氧和医疗声音

针筒抽排水和拆敷料盒的听感不合适，已停用这两组医疗绑定。原文件保留作来源存档。当前打针和布料包扎使用 `SourceData/Audio/MedicalFoley`，安装链会覆盖旧绑定。

- 针筒：taure 的 [syringe.wav](https://freesound.org/people/taure/sounds/465495/)。CC0。原录音包含针筒抽排水和敲击。本项目截取操作声音。
- 医疗敷料：SpliceSound 的 [Band aids, opening box and unwrapping.wav](https://freesound.org/people/SpliceSound/sounds/188189/)。CC0。原录音包含开盒和拆独立敷料。曾用于医疗操作拟音，现已停用。
- 两份文件来自作者页面的公开高质量 MP3 预览。地址见 `Sources.json`。保留原始文件，不改速度和音高。
- 拧回瓶盖使用现有 qubodup CC0 瓶装饮水录音。气流使用现有 Joseph SARDIN CC0 吹气录音作阀门拟音。原来源见 `SurvivalActionSources.json` 和 `Airlock/Sources.json`。
- 重建用 `Tools/Audio/build_consumable_foley.py`。裁剪位置、处理和输入输出哈希见 `ConsumableFoleySources.json`。整套动作音效安装用 `RaidActionAudioSetup.Install()`。
