# 生存动作制作资料

正式动作源是 `ArtSource/DollSinger/SurvivalActions.blend`。制作与验收说明见 [生存动作](../../../Docs/Development/SurvivalActions.md)。

- `AuthoringSeed.json`：初始转接姿态。参考 Quaternius CC0 的 Consume、PickUp_Table 与 BURNER CC BY 4.0 的注射动作。只作可复现的制作起点，不能作为最终姿态验收。
- `ReviewProps.json`：从实际库存交互变体导出的尺寸、锚点、顶点、材质和 UV。
- `Calibration.json`：三套角色的嘴部、前臂与物品测量。
- `BlenderExport.json`：保存的 Blender 源动作与道具网格。含源文件哈希。
- `RigGeometry.json`：三套正式手臂的蒙皮、手掌厚度与库存物品尺寸。
- `RuntimeBake.json`：各角色、物品的离线适配结果。
- `RuntimeSourceCheck.json`：源骨架及道具轨迹的往返误差。
- `RuntimeValidation.json`：本次 Unity 接入检查摘要。

不要用旧拟合脚本覆盖新 Blender 动作。动作署名见 `Assets/StreamingAssets/MoonkovSurvivalActionCredits.txt`。
