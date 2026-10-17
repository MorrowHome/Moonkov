# 头发与裙料边缘光可见性修正

2026-10-17，`character-lookdev`，紧接 `857b6833`。针对“裙子和头发看不出高亮”的反馈，本轮只改 Hair、Dress、InnerDress、DressDetail 的原生 lilToon Rim。已应用正式共享材质及两套 LookDev 副本。

![同条件全身与细节对比](Images/HairSkirtRim/HairSkirtComparison.png)

每组左为 `857b6833`，右为本次。全身图与局部裁剪均来自实际 Unity 相机；相同姿态、相机、白色主光、曝光、Bloom、管线与 Shader hook，只切换材质。没有修色或生成效果图。下排为背光近景裁剪，不能代替上排全身可见性判断。

- [全身原图排版](Images/HairSkirtRim/BeforeAfter.png)、[背面近景](Images/HairSkirtRim/BackComparison.png)、[白光强度 4](Images/HairSkirtRim/StrongLightComparison.png)。
- [正式地图对比](Images/HairSkirtRim/GameComparison.png)：正式离线 Theophilus 对局、固定姿态副本、实际运行时管线、无新增展示灯。两侧使用同一位置相机，游戏时钟仍在运行，因此严格材质对照以独立 LookDev 为准。
- [遮挡矩阵](Images/HairSkirtRim/ShadowComparison.png)、[运行时遮挡原始结果](Images/HairSkirtRim/LiveShadowResult.txt)。

## 原因与实际修改

上一版 Fresnel Power 与 Border 偏高，亮带只出现在很窄的掠射角；缩到全身画面就接近细线。裙料还被低强度 alpha 遮罩及主色乘算进一步削弱。此次扩大角度覆盖和蓝布遮罩响应，保留原发色与蓝白服装色彩，白布及金饰限制额外亮度。

| 原生参数 | 头发：之前 → 本次 | 三份裙料：之前 → 本次 |
| --- | --- | --- |
| Fresnel Power | 3.3 → 1.6 | 3.5 → 1.6 |
| Border | 0.55 → 0.24 | 0.50 → 0.23 |
| Blur | 0.65 → 0.60 | 0.75 → 0.60 |
| DirRange | 0 → 0.25 | 0 → 0.15 |
| MainStrength | 0.75，保持 | 0.8 → 1.0 |
| RimColor，材质 RGBA | (2.1, 2.4, 2.8, 0.34)，保持 | (2.2, 2.6, 3, 0.4) → (2.4, 2.8, 3.2, 0.55) |

`yifuRim` / `yifu2Rim` 的 alpha：白布约 0.28→0.10，饱和蓝布约 0.63→0.80，金饰约 0.15→0.06；是当前色板的平滑分类近似。MainStrength=1 让蓝色亮带充分保留蓝色。鞋子仍使用原参数和遮罩。

皮肤暖红 Rim、眼睛、眉睫、鞋、饰件等其余九份材质参数完全不变。头发原有各向异性 GGX、Smoothness 和 MatCap 保持；没有靠抬高全身 Smoothness 或曝光制造差异。没有改主色、原贴图、UV、法线、动画、Prefab、Shader Pass 或新建角色灯。朝向镜头的平整布面仍保留原色，新增亮度集中于受光的发束、裙料曲面与边缘。

Bloom 始终 0.65 / threshold 1.5 / scatter 0.68，曝光 +0.4 EV、Neutral。双方均使用已有直接光／环境光分离 hook。

## 验证与性能

[资产审计](Images/HairSkirtRim/AssetAudit.json) 逐个 Shader 属性比较最新基线：仅上述四份材质的 Rim 参数及两张 Rim 遮罩改变，其余九份不变；正式材质 CRC 与候选一致，Shader 错误 0，无 Emission／Unlit／最低亮度下限。[完整原始基线审计](Images/HairSkirtRim/OriginalBaselineAudit.json) 另验证了 `9507d1c0` 的应用→恢复→重应用、漫反射与阴影 ramp、UV/队列和 Volume 不变。编译结果见 [Compile.json](Images/HairSkirtRim/Compile.json)。

LookDev 覆盖侧光、背光、正面强光、室内点光、4m/8m 顶棚、遮挡内点光、全暗。8m 顶棚与全暗 A/B 整图像素差为零。当前固定侧光 FP16 HDR 探针中，超过 Bloom 阈值的像素 **296→2049**；白光强度 4 截图中，RGB 三通道均 >0.98 的整图比例均为 **0.00829%**。这证明本组镜头有更多局部泛光输入且没有增加纯白面积，不代表所有角度的美术验收。HDR 全图最大值 47.469 不变；旧字段 `shoulderPeak` 不是皮肤分区测量，不用于结论。

正式离线对局使用实际 `MoonSoftAnimePipeline (interior runtime)` 做日光／8m 顶棚／遮挡内点光／无太阳四阶段测试，标准 URP Lit 球作为对照。太阳脸部亮度 0.7351，顶棚 0.0049，局部灯 0.6445，无太阳 0.0047；顶棚及无太阳的**全帧峰值均 0.0078，约 2/255**，未出现暗处集中亮点。测试记录中各阶段环境填充/SH0 为零，保留真实阴影；结束销毁临时对象、恢复太阳/环境/管线/玩家名并退出 Play。角色既有战斗能量 VFX 在表面材质测试中关闭，未改变玩法。

Unity 6000.5.10f1 / Apple M5 / Metal，Editor Play 960×1080，交错 4 轮，每侧 480 帧，预热及 PNG 读取不计入采样。结果见 [Summary.json](Images/HairSkirtRim/Summary.json)、[Performance.json](Images/HairSkirtRim/Performance.json)。

| 中位数 | 之前 | 本次 |
| --- | ---: | ---: |
| Main Thread | 4.007 ms | 4.017 ms |
| 相机 begin/end CPU 墙钟 | 0.862 ms | 0.842 ms |
| Editor 帧间隔 | 4.030 ms | 4.040 ms |
| Draw / SetPass / 三角形 | 75 / 47 / 477345 | 75 / 47 / 477345 |

Editor 波动不作性能提升或退化结论。GPU recorder 不可用，原始 0 不是零 GPU 成本；更宽高光可能增加 Bloom 有效区域，尚无可靠 GPU 对照。当前游戏数据贴图仍 13 张，Editor 驻留估算 9,101,456 bytes；另存的 Before 贴图用于开发对比，不被正式游戏材质引用。未完成打包版、其他 GPU、多人或全地图性能验收。

## 复现与回退

- `Full Body/Build Copies` 重建本次候选，`Apply To Game` 应用。**既有 `Restore Previous Look` 仍恢复 `9507d1c0` 肩部版本**，不是此次上一个版本。
- `FullBody/HairSkirtBefore` 保存 `857b6833` 的 13 份材质和两张独立旧遮罩；旧遮罩指纹与 Git 基线一致，不引用本次覆盖的数据。用 Git 回退本次提交可完整恢复上一版。
- `Tools/LookDev/BuildHairSkirt.cs` 的 `BuildHairSkirt.Main` 首次冻结当前正式材质（已有快照不覆盖）后重建候选，不直接应用。已存在的快照是固定的 `857b6833`，不要删除后将新效果误当旧基线。
- 打开 `LilToonLookDev.unity`，Play 后 `run_script` 执行 `Tools/LookDev/CaptureHairSkirt.cs`，入口 `CaptureHairSkirt.Main`。它向 `MoonkovFullBodyCapture.Start` 传入独立基线和输出目录，默认旧捕获菜单行为不变。
- `python3 Tools/LookDev/MakeHairSkirtComparison.py` 只排版、裁剪和汇总，依赖 Pillow/NumPy，可设置 `LOOKDEV_FONT`。
- Edit 下审计 `ValidateHairSkirtAssets.Main`；正式离线对局下分别执行 `HairSkirtWorldCapture.Start` / `HairSkirtLiveShadowProbe.Start`。脚本均在 `Tools/LookDev`，通过 Unity CLI `run_script` 执行。

原有发束法线/UV 接缝、白发主色及模型硬边仍限制近景；新亮带也可能使这些接缝更容易看见。本次没有改模型或声称完成顶级展示作品的全部 LookDev。
