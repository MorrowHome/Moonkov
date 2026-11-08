# 打针与包扎

- Coniela，Syringe single pop.m4a，CC0 1.0。取 1.735 秒附近的干燥活塞弹响，仅用于针帽。
  https://freesound.org/people/Coniela/sounds/458423/
- 注射主体直接使用《猎杀：对决》原声。来源为原设计师 Kylan Klein Lenderink / Noise Nerd Audio 发布的《Hunt Showdown - Syringe Sound Design》，画面标注游戏 v0.309、版权 Crytek GmbH。用户明确指定用于个人非商用作品。此项不是 CC0，也不声明取得商业复用授权。
  https://www.youtube.com/watch?v=i3-9okS87S8
- `HuntSyringeOriginal.wav` 保留视频 8.60–11.68 秒的原声，转为 48 kHz 单声道 PCM。它来自演示录屏，带原录环境底声，不是游戏解包的独立无损素材。
- 成品使用视频 8.64–8.94 秒的扎入、9.22–10.34 秒的推注、11.25–11.62 秒的拔针。移到成品 0、0.24、1.00 秒，合计 1.38 秒。只裁剪停顿、调整线性电平和边界，不变速、变调、压缩或叠加自制拟音。
- MutilatorBCB，Insulin Pen - 34 units，CC0 1.0。旧机械推注录音留档，不再用于注射主体。
  https://freesound.org/people/MutilatorBCB/sounds/692581/
- Frostnox，Bandage Wrap Heal 1、Bandage Wrap Heal 2，CC BY 4.0。作者用手指摩擦棉毯录制。两段完整摩擦交替排列，模拟布料一圈圈绕过手臂。也用棉布纹理作为涂抹拟音。
  https://freesound.org/people/Frostnox/sounds/796014/
  https://freesound.org/people/Frostnox/sounds/796015/
- SecureSubset，Tape Bandages Rip，CC0 1.0。完整撕拉绷带录音，用于开始处理包扎材料。
  https://freesound.org/people/SecureSubset/sounds/800275/
- Freesound 素材来自作者页面公开的高质量 MP3 预览。原文件留存。针筒抽排水、敷料盒、机械注射笔及此前生成的拟音均不再用于注射主体。
- CC BY 4.0：https://creativecommons.org/licenses/by/4.0/ 。CC0：https://creativecommons.org/publicdomain/zero/1.0/ 。署名随 `MoonkovMedicalFoleyCredits.txt` 打包，说明了改编。
- 重建用 `Tools/Audio/build_medical_foley.py`。输入输出哈希及配方见 `MedicalFoleySources.json`。`ConsumableAudioSetup.Install()` 自动调用 `MedicalFoleySetup.Install()`，防止旧绑定恢复。
- 来源和动作触发检查不等于主观听感验收。不能仅凭音量、波形或音源正在播放，就断言音色符合要求。
