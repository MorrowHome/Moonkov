# 生存动作制作资料

正式动作源是 `ArtSource/DollSinger/SurvivalActions.blend`。制作与验收说明见 [生存动作](../../../Docs/Development/SurvivalActions.md)。

- `AuthoringSeed.json`：初始转接姿态。参考 Quaternius CC0 的 Consume、PickUp_Table 与 BURNER CC BY 4.0 的注射动作。只作可复现的制作起点，不能作为最终姿态验收。
- `ReviewProps.json`：从实际库存交互变体导出的尺寸、锚点、顶点、材质和 UV。
- `Calibration.json`：三套角色的嘴部、前臂与物品测量。
- `ContactFit.json`：旧 Unity 原型的拟合报告。新 Blender 源不由此报告生成。

不要用旧拟合脚本覆盖新 Blender 动作。动作署名见 `Assets/StreamingAssets/MoonkovSurvivalActionCredits.txt`。
