# 注射动作来源

- 作者：BURNER，Sketchfab 用户名 `Alexander_Ovelar`。
- 原作：[Medical syringe healing FPS Animation](https://sketchfab.com/3d-models/medical-syringe-healing-fps-animation-8de32be8633a4c87ba7ed1d125b45ec9)。
- 授权：CC BY 4.0。原页和 Objaverse 单件元数据均为此授权。
- 公开分发：[Ai2 Objaverse](https://huggingface.co/datasets/allenai/objaverse)。资源 ID 和作者均已核对。下载地址和 SHA256 在 `Source.json`。

`MedicalSyringe.glb` 是原始分发文件。`Samples.json` 是读取出的 60 Hz 骨骼位置与方向。这里只使用作者动作。游戏继续使用正式角色和库存针剂。

该 GLB 包含拆分的前臂骨和特殊缩放。不要直接把其中的手臂网格放进游戏。原网格检查发现共面、重复顶点和 UV 重叠。此项目没有修改或输出这些网格。参考检查与两种多角度图保存在忽略目录 `LocalData/Actions/Research`。

动作先裁掉 150 毫秒的准备段。该段的右臂几乎完全伸直，会使肘部平面退化。最终动作在正式角色的 Blender 源中重做。保留源动作作为参考，按实际针筒、手部骨长和裸露前臂重新处理路径、握持、针帽与接触。

正式制作源是 `ArtSource/DollSinger/SurvivalActions.blend`。源文件和检查方法见 [生存动作](../../../Docs/Development/SurvivalActions.md)。新源尚未回灌 Unity。

以下是旧原型的研究流程，不能用于覆盖新 Blender 源：

1. 用 Blender 5.2 执行 `sample_source.py`。
2. 在 Unity 执行 `RaidActionAnimationSetup.BakeAll()`。
3. 用普通 Python 执行 `Tools/Blender/retarget_survival_injection.py`。
4. 在 Unity 执行 `RaidActionPropSetup.Build()`，再运行 `Tools/Validation/ExportSurvivalActionCalibration.cs` 的 `Run`。
5. 用普通 Python 执行 `Tools/Blender/fit_survival_actions.py`。它复用瞄准手势的解剖求解器，离线适配实际握点、嘴部和左臂表面。运行时不硬拉手腕。
6. 在 Unity 执行 `RaidActionAudioSetup.Install()`，再执行动作检查和原生预览。不要把源网格的检查结果当成正式角色的验收。

署名随 Player 发布在 `Assets/StreamingAssets/MoonkovSurvivalActionCredits.txt`。
