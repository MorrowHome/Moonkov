# R29 本地显示接入源档

此目录保留本批轻量 Blender 库、精确清单、修复入口、权威材质定义及已通过的预检／离线导出记录。原三份 ZIP 和独立修复包保留于 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R29/`，没有改写；大小与 SHA-256 见 `LocalPackageValidation.json`。不包含完整云端场景、DEM、R22、碰撞或灯光资产。

PNG 的 Unity 工作副本只放在对应显示资产目录：

- 工业：`Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R29/Industrial/Textures/`
- 局部地表：`Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R29/LocalGround/Textures/`

Blender 库中 `Textures/` 是交接时的相对路径；如从本档重新加载库，需显式把外部图片路径指向上述对应目录，或者使用保留的原始解包目录。此处不再复制一套 PNG。

`PrepareR29Corrected.py` 是本次实际离线导出脚本的归档，原运行位置在 LocalData 交接根目录；它依赖同级的修复和清单文件，以及相邻 R21／R23／R24 交接。不要执行旧顶层 `APPLY_IN_BLENDER_SOURCE_ONLY.py`。先调用修复包的只读入口确认本地 R23 序列化 UV 与 R13 权威内容指纹，再经修复应用入口处理离线副本。精确别名仅允许已核验的 `R13_Machined_Steel_Contact.001`，不通用去后缀。

UV 通道分配：原 UVMap → UV0；**Unity UV1 保留现有光图数据**；R26、R27、R28 图集分别 → UV2、UV3、UV4；R28 米制细节 → UV5；两种源地表辅助 UV → UV6／UV7。工业 Shader 显式选择图集与细节通道；切线按该材质真实法线采样的 UV 计算。19 件现有网格使用副本，保留原顶点、法线、UV0、UV1；新增 UV 后 Blender 重新插值的两件 R24 倒角 UV0 不覆盖本地原值。

地表 Shader 只绑定本批局部显示网格；按交接组合世界 64 m 高度／颜色／粗糙度、256 m 宏观变化、局部 4 m 细节和限定边界遮罩，不覆盖主 Terrain 材质或贴图。R28 可选作业遮罩没有安装或启用。Shader 适配与编译不代表 Unity／Blender 图像等价，当前按用户范围不截图或运行 Play。
