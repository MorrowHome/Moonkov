# 环境作用域与头罩玻璃：2026-10-22

## 本轮结论

本轮修复了颜色、作用范围与绘制阶段：恢复角色原有金紫配色、任务语义色、建筑暖色标识与冷暖灯光；第一人称能量密封罩增加克制的玻璃边缘和有限反射。主要美术收益是撤销旧滤镜的强制黑白/钴蓝映射，保留原材质与照明信息，并非新增高精度资产或达到 AAA 质量。

旧画面的问题来自 `MoonSilverPrint` 在 599 对已合成的整幅画面调色，依赖角色例外登记；`Accent()` 又将冷色与赭色都映射为钴蓝。透明效果已经混入背景时，最终深度或角色 mask 无法分离两者。

## 实际同机位图

每侧 1280×720，FOV60，FXAA，正式 TheophilusGameScene 与 MC00BakedPipeline，现有灯光、材质及 Volume。左为本轮改源码前，右为改后。前后均是实际 EditMode Game 相机静态渲染，插入同一个真实 MenuCharacterVisual 及 RGB/透明光束语义探针；不是正式装备流程、Play 或联机验收。

![外景：前／后](01-exterior.png)

外景 position=(1730,228,2384)，target=(1747,228,2403)。

![室内：前／后](02-interior.png)

室内 position=(1608.56,219.5,2145.47)，target=(1623,219.5,2126)。该机位位于室内窗边，显示现有照明及探针；未为截图增设正式光源。

### 截图颜色传输校正

项目为 Linear。最初两组真实截图使用 ARGBHalf / R16G16B16A16_SFloat，`sRGB=false`，直接保存 RGB24 PNG 会偏暗。保留四张 raw 后，对两组逐通道统一应用 IEC sRGB 编码（低段12.92×，高段1.055×c^(1/2.4)−0.055），没有调整曝光、局部亮度或正式灯光。本页两组图使用相同转换方法。

另以显式 ARGB32 / R8G8B8A8_SRGB（`sRGB=true`）重渲改后场景核对：两种采集方式平均绝对差外景0.91、室内1.64（0–255），99百分位5/7。初次 raw 已量化为线性8bit，暗部精度不能恢复，且角色/AA等有少量峰值差；不能声称逐像素完全一致。`transfer-comparison.json` 保留读数。原图、显式sRGB改后图及采集脚本留在 `Library/BuildValidation/EnvironmentOptics-20261022`。

## 正向材质范围与阶段

环境处理改在 449、透明绘制与 Bloom/tonemapping 之前执行。只有材质 Shader 声明 `MoonEnvironmentScope` native Pass 才参与，使用本相机真实 cullResults 和深度一致性测试；没有手动绘制人物代理，没有修改物理层、renderingLayerMask、灯光层或阴影层。

参与材质域：Regolith、LunarRock、Industrial Dust、R29 Industrial、R29 Local Ground，以及 Silver Sand Terrain。Terrain 使用原有 `TerrainInstancing`、高度图 patch/LOD、UV 与 `ClipHoles` 路径，继承全局 `_ALPHATEST_ON` fragment variant，不用普通 Mesh 覆盖材质替代原生地形。

正式场景约25,616个 stock URP Lit slots 和180个 Complex Lit slots保持正常 PBR 原色，不纳入滤镜。角色、武器、掉落物、普通粒子、UI和独立预览的 Shader 不声明该 Pass；处理不依赖不断新增角色例外。环境专用 Shader 是明确材质域，应避免将它用于非环境物品。

默认 `faithfulLighting` 在正式场景显式保存为1，在线性 HDR 中仅做3%去饱和，不夹断高光、不加颗粒/斑驳/块面轮廓，也不重映射灯色。Inspector 保留“立体银盐”“强对比海报”开发参考预设，只有主动选择时才启用旧曲线。独立头罩和既有 Dark Sight / 战术透明 / 屏幕警告顺序保持600/601/602。

![原生范围受控检查：关闭／旧银盐强度](04-native-scope.png)

这张图使用临时小型 preview fixture：真正 `drawInstanced=True` 的 Terrain，中心孔洞，环境方块、stock Lit 红色遮挡块与绿色孔洞底。旧银盐用于放大范围差异，退出即销毁，没有保存为正式资产。Terrain 最大采样差188，环境方块已点亮顶面 ROI 差188，stock Lit 遮挡物及可见孔洞底差0。方块最初的前面中心采样落在全黑面，读数0；原始读数和纠正后的顶面 ROI 均记录在 `scope-report.txt`。

## 头罩边界及初始化修复

![头罩：未装备／已装备的拥有者第一人称](03-visor.png)

通过实际 `EnergyShieldPresentation.Present` 设置静态快照条件，并执行真实 `LateUpdate` 后渲染。只有真实 HeadDomeEquipped 状态、拥有者、第一人称、匹配的 Game 相机共同满足时，才排入外围玻璃 Pass。边缘使用图内临时色彩副本；没有新生产相机、按键、扫描模式或持久RT，不改变全局灯光。第三人称外观和原有护盾命中/破盾逻辑保留。

读数：拥有者已装备全图最大差38，中央320×180区域差0；第三人称、非拥有者、不匹配相机全图差均0；SceneView与Preview条件均关闭。无头罩时盾命中响应仍为0.096。相机负例是相同静态场景的 API 条件检查，不是正式游戏视角切换或装备网络链路验收。详见 `head-done.txt`。

检查中发现两个 MaterialPropertyBlock 在 MonoBehaviour 字段初始化时触发 Unity `CreateImpl is not allowed ... constructor (or instance field initializer)`，正常 Present 后它们为null，LateUpdate稳定报错。已移到 EnsureReady 的主线程表现初始化路径创建；最小复现重跑显示两个属性块有效、LateUpdate通过，没有跳过故障调用。原始错误与修复读数保存在本轮 Library 诊断文件。

## 导入、Console 与清理

Unity 6000.5.10f1 / Metal，C# 重编译 completed、failed=false、errors=[]；九个相关 Shader 导入后 supported=true，实际上述路径已渲染，Shader消息无Error。现有共享HLSL仍报告潜在未初始化变量（SilverSandLighting、MoonLocalBounce，函数源码未改；未完整证明所有消息在本轮前已经编译出现），没有为消除警告扩大修复范围。详见 `shader-audit.txt`。

没有清Console。起始Console记录1个工具层错误（`editor status`旧命令）及24条警告；保留缓冲最终有4个错误：起始工具错误、一次5秒请求超时、两次修复前属性块构造异常。最终Unity Console当前计数0 Error、3 Warning，compilationFailed=false；缓冲仍保留此前证据。没有执行测试套件、Play、构建、联机、烘焙或Blender。

主Editor为60774，61129/61975为其AssetImportWorker。收尾仅MainMenu已加载、isDirty=false；临时预览场景均在finally关闭，EnergyShieldPresentation对象数0。Editor已通过CLI graceful关闭，OS复核无Unity/Blender进程，系统free76%。正式Scene相对本轮备份只插入一行 `faithfulLighting: 1`。Unity期间修改了两字体，Editor完全退出后已从本轮备份恢复用户原字节并核对哈希，没有checkout HEAD。EditorSettings与EditorUserSettings保持原样。PlayerPrefs实际产品名为FPS_Template，识别较晚；磁盘mtime03:03早于本轮开始，QA未调用PlayerPrefs API，收尾副本/哈希一致，不能把后补副本称为任务开始前备份。

## 仍有的限制

当前建筑大平面、材质精度与照明条件仍限制真实细腻度。`MoonSceneLighting` 的 ambientFill 默认黑、reflectionIntensity被设为0；本轮没有重写间接光、烘焙、重建建筑或增加贴图精度。截图颜色校正不能当成真实灯光升级。

未验证正式装备/复活/网络链路、运动中视觉、GPU预算、其他平台/图形API、所有Shader变体或头罩发冠/长双马尾贴合。第三人称头罩已有骨骼范围估计仍需后续实际角色近景确认；本轮头罩验收重点是新增拥有者外围玻璃和相机范围。
