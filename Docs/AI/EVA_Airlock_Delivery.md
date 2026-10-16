# Apollo12 EVA 气闸交付

Apollo12 的独立体验副本接入一处 EVA 气闸，沿用已有门控、氧气规则和池化音频。原 Apollo12GameScene、原建筑 Prefab、地形和灯控资产保留。阀组、通风格栅、泵机检修模块作为 3 个静态网格增量装在气闸内；没有新增灯、按钮、音频或动态机构。

资产绑定及新地图 Play 验证已完成。2026年10月14日使用正式游戏当前的 `DollSingerNetworkPlayer.prefab` 实例完成 28 条地图观察，并补测保持正式移动组件启用时的前进、跳跃和落地。上一轮独立气闸的原始证据仍保留。

## Unity 入口与回退

- 地图体验场景：`Assets/MoonEnvironment/Variants/Apollo12EVA/Scenes/Apollo12_EVA_Walkthrough.unity`。
- Unity 菜单：`Tools > Moonkov > Airlock > Open Apollo12 EVA walkthrough`。
- 气闸基线 Prefab：`Assets/MoonEnvironment/Variants/EVAAirlockCloudDrive/Prefabs/EVA_EAST_01_CloudIncrement.prefab`。
- 独立硬件 Prefab：`Assets/MoonEnvironment/Variants/Apollo12EVA/Hardware/EVA_AirHardware.prefab`，只按局部位置 `(1.89, 0, -1.69)` 安装一次。
- 原独立验证场景：`Assets/MoonEnvironment/Variants/EVAAirlockCloudDrive/Scenes/EVA_CloudDrive_LocalValidation.unity`。

打开体验场景并进入 Play 后，角色出生在外门前。点击 Game 视图捕获鼠标；WASD 移动，鼠标看向按钮组，滚轮上下切换同组按钮，F 执行当前选中动作，Esc 释放鼠标。HUD 列出同组动作，以黄色标记当前行和对应实体按钮，并显示压力、内外门行程、操作反馈和禁止执行的原因。离开交互距离、移开视线或释放鼠标后隐藏提示；不会跨到另一侧按钮组。头盔随身氧状态在本地验证读数中显示。

进入时开外门，进入舱内并离开门洞，关外门，在靠外门一侧的按钮组均压至室内并开内门，再穿过内门。返回时在靠内门一侧的按钮组关内门、向外泄压、开外门。过道中的门洞占用会阻止关门；关门途中侵入会反开。

```mermaid
flowchart LR
    O[外部真空 随身氧消耗] --> A[外门开启 进入气闸]
    A --> B[外门关闭 双门密封]
    B --> C[向室内均压]
    C --> D[达到压力门槛 开内门]
    D --> I[有压室内入口 停止随身氧消耗]
    I --> E[回到舱内 关内门]
    E --> F[双门密封 向外泄压]
    F --> G[达到外压门槛 开外门]
    G --> O
```

回退时停止 Play，打开原 `Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12GameScene.unity` 或 MainMenu。Build Settings、主菜单入口和正式地图引用没有切换到体验副本。副本中的 4 个网络资源/出生根保留但停用，本地 DollSinger 体验角色负责行走；这不是联机地图发布。副本保留全部 249 个 Light 组件和 135 个原灯按钮，只有重复的 12 个旧门叶与 12 个车架在副本中停用。

## 运行实现与配置

`Assets/Scripts/Gameplay/Airlocks/` 中的 `AirlockRules` 管理双门互锁、均压和压力门槛；`AirlockController` 管理逐叶行程、门洞占用和门叶碰撞扫掠；`AirlockButton` 提供相机射线交互与状态；`AirlockAudio` 按实际门运动和阀泵状态触发录音。CHAMBER 保留 3 个 Box 的并集，开门时启用门洞气压桥。`BreathableZone` 提供压力及氧分压采样，`RaidHealth.TickOxygen` 继续拥有随身氧规则。

Controller 的 Settings 可调室内/外压力、供氧比例、均压耗时、门行程耗时、开门压差门槛、可呼吸压力及氧分压门槛、交互距离等。默认室内压力 101.325 kPa，外部 0 kPa，供氧比例 21%，均压/泄压各 6 秒，门行程 1.2 秒。

本地体验通过 `AirlockPrototypeDemo` 实例化正式 `DollSingerNetworkPlayer.prefab`。只在本地运行实例中停用网络桥和网络呈现，由 `AirlockGameplayPlayer` 复用当前正式 `FirstPersonController`、移动参数、模型、镜头和输入；正式预制体资产没有改动。standalone 模式的地面检测跳过网络角色读取，避免离线 Ghost 尚未链接时的空引用。通过相同 `RaidHealth` 规则维护诊断氧气状态。可呼吸范围为当前气闸与内门后的有限入口 Box；没有为整栋基地推定可呼吸气氛。正式服务器已有氧气采样入口，门控网络实现保留在 `Assets/Scripts/Networking/Shared/AirlockNetworkSystem.cs`；本轮没有验证实际 Host/Client 传输。

## 资产与音频来源

门控包 ZIP SHA256 为 `140edb893ad9e272c024eada0534ff9fd77f65b81fe68305d4ed1d5535bf90ce`。硬件 ZIP SHA256 为 `afc627a38173df2e181115942b7194745cd69d48a2ab8aa758cfcccc50fffd88`，1758752 字节；本机校验 ZIP 和包清单通过。硬件导入后的 3 个网格局部边界最大角点偏差约 0.227 毫米；重用 7 个已有材质，新增材质使用 URP Lit，文字图集使用 sRGB BaseColor 且不发光。所有原音源锚的位置、旋转和 ID 保留。

采用的五段日常机械设备录音来自 Joseph SARDIN / BigSoundBank，各来源页和[许可页](https://bigsoundbank.com/licenses.html)明确标为 CC0，许可在本轮在线核对。没有主观试听，也没有将这些录音验收为真实航天器声音。

| 事件 | 原始录音 | 来源 |
|---|---|---|
| 阀流循环 | 压缩空气吹手 | [0747](https://bigsoundbank.com/blow-on-hand-s0747.html) |
| 泵循环 | 小型电动空气压缩机 | [1487](https://bigsoundbank.com/air-compressor-s1487.html) |
| 门电机循环 | 金属自动滑门 | [0513](https://bigsoundbank.com/portail-automatique-coulissant-s0513.html) |
| 锁止 | 谷仓门锁上锁 | [1540](https://bigsoundbank.com/barn-lock-locking-s1540.html) |
| 解锁 | 谷仓门锁解锁 | [1539](https://bigsoundbank.com/barn-lock-unlocking-s1539.html) |

`Assets/Audio/Moonkov/Airlock/Sources.json` 保留裁剪范围、原/处理文件哈希、衰减和端点处理记录，主观试听字段仍为 pending。泵声仅在向室内均压时触发，阀声随均压流动触发，电机声随实际门行程变化触发；真空外空气声增益为 0。结构接触传声单独计算，头盔内部和无线电增益路径不依赖外部气压。本轮未接入或修改既有头盔/无线电播放链路，未设置全局 AudioListener 静音。

Apollo12 金属踏板原来位于原建筑层级，结构传声只识别气闸子物体，因此真空下站在金属踏板上仍无机械声。地图现在将结构接触限定绑定到原 `MC00GameExport` 建筑根，该根不包含 Terrain；在金属踏板接触下播放经过低通的机械传声，站在外部月面仍不添加空气声。监听器明确绑定到当前角色镜头，并选择启用的正式角色碰撞胶囊进行接地检查。

## 验证证据

原独立场景的完整证据位于 `Docs/AI/EVA_Airlock_LocalValidation.json`：28 条实际观察、8 类带录音的机械声事件、舱内非零音量池化音源。验证使用 Play 帧中的实际 DollSinger Prefab、`CharacterController.Move`、原生地面/碰撞以及真实相机射线按钮操作；移动和输入组件暂时停用以驱动固定路线，没有真人键盘输入或主观试听。

已验证外部真空耗氧、101.325 kPa 舱内停止耗氧并沿用原恢复规则、泄压后恢复耗氧；门洞角色占用拒绝关门，关门途中角色侵入安全反开；双门互锁、未密封禁止均压、压差和均压期间禁止开门；3 段舱体积包含舱内且排除外部真空。

地图副本证据位于 `Docs/AI/EVA_Airlock_MapValidation.json`，当前地图循环 28 条观察通过。使用 Input System 注入的 F 键事件经过实际 `AirlockPrototypeDemo.Update` 和相机射线打开外门；完整路线仍为脚本驱动胶囊移动。监听器输出 RMS 峰值约为 0.04346，泵声期间约 0.03815，外门电机期间约 0.00744，确认信号到达实际 `AudioListener`，并非仅检查 `AudioSource.volume`。这不是主观试听验收。

恢复正式移动组件后曾出现离线 Ghost 的 Role 空引用；修复 standalone 的两处网络角色判断后，单独保持 `AirlockGameplayPlayer` 启用，通过输入事件前进约 1.62 米、跳起约 1.28 米并落地，5.5 秒观察期间新增运行错误为 0。完整循环中的脚本移动与这项真实移动组件检查分开记录。

地图绑定与硬件边界记录分别位于 `Temp/MoonAirlockIntegration/DOT00_map_build.json`、`DOT00_hardware_bind.json`。验证入口为 `AirlockPlayValidation.Prepare(AirlockApollo12Tools.ScenePath)`，进入 Play 后调用 `Begin()`；停止 Play、等 Editor 稳定后调用 `Restore()`。

## 本轮变更与保留范围

新增地图目录 `Assets/MoonEnvironment/Variants/Apollo12EVA/`，包含 Apollo12 场景副本及硬件 Prefab、3 个 Mesh、材质和纹理。新增 `AirlockApollo12Tools.cs` 与 `AirlockHardwareTools.cs`；已有 `AirlockPlayValidation.cs` 仅增加允许验证地图副本和独立证据输出的入口。门控运行代码和前一轮证据保留。本报告与新的地图证据保存在 `Docs/AI`。

本次修正另增加 `AirlockGameplayPlayer.cs`；调整 `AirlockPrototypeDemo.cs` 的正式角色运行实例设置和 F 键，`AirlockAudio.cs` 的监听器及金属结构接触，`RaidHUD.cs` 的气闸 F 键输入。`FirstPersonController.cs` 只在已有 standalone 模式的两处地面检测中跳过网络角色判断，保留文件内并行武器改动。该修复的回归证据仅覆盖前述离线前进、跳跃和落地，不能推定联机回归通过。

交互提示使用项目现有 UI Toolkit 与 PanelSettings，新增 `AirlockInteractionPrompt.cs` 和 `Assets/Resources/Moonkov/AirlockPrompt.uxml/.uss`，同时接入本地体验 HUD 与正式 `RaidHUD`。`AirlockButton.TryPressFromCamera` 优先执行 HUD 选中按钮，并保持原来的距离与门控/服务器规则。场景副本保存了 PanelSettings 引用；没有修改正式角色 Prefab 或原地图。背包、暂停、结算及角色失效时隐藏交互提示。

地图副本的 8 个 `TextMesh` 状态标签按操作侧调整朝向和字号；原气闸基线 Prefab 不变。本轮曾在实际正式角色 Game 镜头中观察到三项按钮组、黄色选中标记、101.3 kPa 和操作反馈，现有局部灯光能照清控制器，因此没有调整灯光或渲染设置。随后用户要求停止截图验证，未继续截图或完成视觉验收。滚轮模拟事件的端到端检查没有得到稳定结论，不标记为通过；滚轮手感、F 与当前选项的一致性仍待用户实测。未重新运行完整 28 条循环。临时输入设备已移除，正式移动、模型与输入组件已恢复，已请求停止本轮检查 Play。

不提交、不推送、不安装、不付费。异形、蜘蛛、并行武器模块、原地图及其材质的修改由原写入者保留。本轮不进行性能分析或联机测试。

## 2026年10月14日入口与 L14 视觉增量：部分接入

两份新输入已在本机核实：入口静态 ZIP 为 9928495 字节，SHA256 `9f868b0a1f532afc07427fdabdae86eeed5815c7793d8833dc3934d140cff0d3`；study v10 为 70588474 字节，SHA256 `7f7db9969b060b20ed808b22b7caaa888a69adc465776eb88be29d83c90e992a`。ZIP 内部清单的 4 份文件也通过字节数及 SHA256 校验。原文件保存在 `Temp/DOT00_20261014_AssetHandoff/`，没有覆盖作者文件。

入口 ZIP 未接入。其合同要求更新既有 v4 小包的 101 个 `SLICE_*`/study 静态对象，另加 2 个细节组；这并不是当前 Full v02 中按入口/EVA 名称筛出的 161 个对象。按包内全名、旧名和 12 个明确截断名映射核对，当前副本唯一匹配为 0/101，`MOONKOV_ENTRY_STATIC_VISUAL_ROOT` 也不存在。因此没有导入或追加 103 个网格，没有应用 `(1660,210,2130)` 原点，也没有推算其与现有 Apollo12 的关联。继续接入前需要先解决该旧 v4 基线与当前地图的对象/坐标关联。

study v10 的默认隐藏集合 `GAME_CANDIDATE_L14_SINGLE_UV0` 已通过本机 Blender 5.1.1 后台读取，自动执行关闭、未保存 study。13 个候选中有 9 个可唯一匹配现有对象：抽屉 `05911/05915/05931/05935` 和电子模块 `05960/05962/05964/05966/05968`。候选与现有几何的局部顶点及三角形核对通过；Unity FBX 再导入后，最大顶点距离和包围盒偏差均为 0。这 9 个对象不属于合并渲染组。

只导出了上述 9 个候选的视觉数据，在导出进程内消除 study 世界摆放，绑定回现有对象的原 Transform。未导出整栋建筑或 study，也没有复制摆放第二套物体。副本新增 `Assets/MoonEnvironment/Variants/Apollo12EVA/L14Visuals/`，含 9 网格 FBX、10 份 URP Lit 材质、30 张实际关联贴图及导入清单；场景只保存这些对象的 MeshFilter/MeshRenderer 覆盖。`WQ_L14_SKIN_01_West_Closed_Cover_A`、`02_West_Closed_Cover_B`、`03_Rear_Closed_Cover_A`、`04_Rear_Closed_Cover_B` 均无目标，未导入其网格、材质或贴图。

实际导入读回确认：BaseColor 使用 sRGB/BC7，法线使用 Normal Map、线性/BC5 且未翻转绿色通道，URP 打包图使用线性/BC7。10 张打包图的 G 为 AO 常量 1，B 未用，R 为金属度、A 为光滑度；不是 glTF ORM。材质绑定到受支持的 URP Lit，Metallic Alpha 光滑度、标量 1，法线及遮蔽路径启用，无发光。9 个网格有 UV0、法线和计算切线，没有第二 UV。这验证了导入格式与绑定，未验证实际室内 PBR 观感或性能；每个网格从 1 个材质分区变为 2 个，增加的绘制成本未测量。

替换前后全场景层级、Transform 矩阵、组件类型及全部 Collider 序列化哈希一致，既有碰撞、门灯/抽屉组件及物理通路条件没有改变；这不等于真人实走或抽屉操作验收。副本保存并重新打开后 9 个绑定仍在，场景无未保存修改，Play 停止；编译无失败，收尾 Console 错误为 0。未截图，未重跑气闸 28 条循环、联机或性能检查。391 个保护文件哈希核对无变化，包含正式地图、Full v02 建筑和并行代码/材质。

本轮数据证据在 `Temp/DOT00_20261014_AssetHandoff/L14_bind_validation.json`、`L14_texture_format_readback.tsv`、`L14_packed_channel_check.json` 与 `protection_check.json`；摘要合并到既有地图验证报告。接入前副本备份为同目录 `Apollo12_EVA_Walkthrough_before_L14.unity`。回退可恢复该副本备份，或直接打开原 Apollo12GameScene；原正式入口没有切换。入口 ZIP、缺失 4 个 L14 外壳、视觉/实走验收均明确保持未完成。

### 云端合同对齐事实补充

缺失对象的一对一名称为：

- `WQ_L14_SKIN_01_West_Closed_Cover_A` / `GAME10_L14_01`。
- `WQ_L14_SKIN_02_West_Closed_Cover_B` / `GAME10_L14_02`。
- `WQ_L14_SKIN_03_Rear_Closed_Cover_A` / `GAME10_L14_03`。
- `WQ_L14_SKIN_04_Rear_Closed_Cover_B` / `GAME10_L14_04`。

当前副本的建筑父链前缀为 `Apollo12MC00Candidate/Base 3 / modular FBX/MC00GameExport/MC00_Render/MC00_ROOT`。外入口在该前缀下的 `ORG_EXTERIOR_12a68d/ORG_East_EVA_Pressure_Entry_01a046`；内气闸原静态组织在 `ORG_FLOOR_L0_e8ed5d/ORG_SHARED_BOUNDARIES_203004/ORG_EVA_DUST_HOLD_7e400f`。两者的原始来源均为 `Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Models/MC00_Render.fbx`，外层使用同目录的 `Prefabs/MC00GameExport.prefab` 和 `Prefabs/Apollo12MC00Candidate.prefab`。独立运动气闸的父链另为 `EVA_SINGLE_MODULE_ORIGINAL_LOCAL_YUP_FRAME/EVA_EAST_01_CloudIncrement`，对应 `EVAAirlockCloudDrive/Prefabs/EVA_EAST_01_CloudIncrement.prefab`，不可与入口静态包原点混用。

现有建筑版本有本地来源证据：`SourceData/Models/MC00LightControlsFullV02/Delivery.json` 记录 `revision=full_rollout_v02`；`unity-manifest.json` 记录源文件 `MC00_Game_Light_Controls_Full_v02.blend`，SHA256 `e61f2fab253a0801f3ebb7084fbb7163e48bd156c9ce79074825e4e81d5ca5f0`。不据此推定云端 Library 版本号。新入口 ZIP 的包内 manifest 记录其 study 基线版本为 7，而身份映射关联的是旧 v4 小包，不是上述 Full v02 建筑。

少量现有相似对象为 `MC_East EVA aimed worklight housing_04205` 和 `MC_East EVA approach ramp edge rail handrail_04221`，均是外入口 `ORG_East_EVA_Pressure_Entry_01a046` 的直接子物体，网格及原始对象均来自该 Full v02 `MC00_Render.fbx`。它们未出现在新包提供的 101 组当前/旧名映射中，也不是 `SLICE_RECEIVING_*` 或 `SLICE_STORY_*` 目标。当前场景未发现已安装这套 SLICE 包的证据；没有审计历史，不能断言从未安装过。

贴图计数口径不同：完整候选合同列出 13 张新法线、13 张新表面 URP 打包图和 2 张继承机身 URP 打包图，共 28 张新增数据图；另复用 1 张继承法线和 15 张颜色图，完整候选唯一关联图共 44 张（15 BaseColor、14 Normal、15 Packed）。本次 9 个匹配目标实际引用 30 张：10 BaseColor、10 Normal、10 Packed。其中颜色为 9 张原 v9 印刷颜色图加 1 张继承灰色机身颜色图；数据为 9 张候选法线、9 张候选表面打包图、1 张机身打包图及 1 张继承法线，即 19 张新增数据图加 1 张既有数据图。未匹配外壳使用的机身 Hull 材质和其专用图未导入。所有 30 张都从 study 已打包字节复制，没有本轮重烘焙、重编码或修改原项目材质。

本轮实际 Play、性能和主观试听均未测；材质通道/压缩核对不等于主观 PBR 验收。报告补充仅进行当前副本及已知合同的只读核对，未新增场景修改。

## 2026年10月14日 v11 六件 L14 饰壳接入完成

本节更新上面的 v10 阶段停点：四盖板已经作为完整六件新增静态视觉模块接入，不替换原设备。入口 v8 仍未安装，现有坡道、路线和地面不变。输入为 `Temp/DOT00_20261014_AssetHandoff/v11/Moonkov_Theophilus_Current_Space_Contract_Study.blend`，71152705 字节，SHA256 `93dc734d6170f975b850387b694be830c2402bf37f8e8dddd10d2baa4bc002e1`；旧 v10 文件保留。

从已审阅的内嵌 `EXPORT_L14_CLOSED_CASING_MODULE.py` 限定导出六个候选，只处理 `GAME_CANDIDATE_L14_CLOSED_CASING_MODULE`。导出进程关闭自动执行、不保存 study。25 张打包贴图字节哈希和六网格房间局部包围盒核对通过。新增资产目录为 `Assets/MoonEnvironment/Variants/Apollo12EVA/L14CasingV11/`：6 个 Mesh、9 个 URP Lit 材质、25 张贴图（9 BaseColor、7 Normal、9 Packed）、一个 Prefab、导入合同及局部净空证据。没有新增 shader 或运行脚本。

场景新增 `L14_Casing_Room_YUp_Frame_V11/L14_Closed_Casing_Static_V11`，挂在现有 `ORG_ROOM_L14_b1cdce` 下；下属为 `L14_CASING_Part_01` 到 `06`。现有 FBX 房间使用镜像 X 的 Blender 局部网格基底，新增框架仅进行一次正确的旋转基底适配：列向量为 X=(-1,0,0)、Y=(0,0,1)、Z=(0,1,0)。模块自身保持单位姿态，网格使用合同 `(Bx,Bz,By)` 房间局部坐标；没有套用 study 世界平移或 37 度，也没有带入电子参考架的 90 度改动。原有 9 项视觉替换保留。

前下板 `05893`、前上板 `05894`、窗口 `_05` 的三份源快照按内嵌 little-endian float64 顶点、int32 三角形及组合 SHA 规则核验通过。本地导入器拆分了法线/UV 顶点，按合同采用坐标修正后的几何容差比对，而非要求跨导入器字节哈希相等；三对象均为 24 个唯一位置、44 个三角形，最大顶点距离约 0.164 毫米。矩阵和包围盒字段也在 0.25 毫米等效容差内。

局部检查只覆盖六候选对当前前上/下板、窗口和三窗框的 36 组。六目标均通过凸性前提检查，按 1 微米数值容差做三角形/凸体半空间裁剪，没有候选三角形进入这些目标内部。三个退让连通端面按合同组件包围盒唯一识别，其相对当前两前板背面的最小 Y 向间隙为约 2.108 毫米；该值与源快照约 2.001 毫米的差别在上述跨导入器坐标误差范围内。实际导入的六网格顶点/三角形与被检查数据一致。侧后壳内嵌搭接、两处承托实体搭接按候选保留；本轮没有对全壳做“无交叠”验收。

旧 `check_clearance.py` 的约 -44.98 毫米不是有效的端面侵入量。其公式是 `min(整个 GAME11_L14_CASING_05 的所有顶点 Y) - max(当前 05893/05894 两板的所有顶点 Y)`，坐标为 Blender 房间局部 XYZ，Y 为前后方向，+Y 为退让。该公式混入了不属于三个退让端面的顶点，没有匹配具体组件与目标投影范围；也没有做三角形或实体相交判断。修正后先找合同指定的三个连通组件，再计算各端面最小 Y 与两前板背面最大 Y 的间隙。未进一步给那些全网格极值顶点标定具体设计角色，因此不把旧负数声称为保留搭接深度。

安装前后全部既有 Transform、组件类型、激活状态和 Collider 序列化一致，没有新增 Collider。六件水平范围留在既有底座内，与四抽屉的静态包围盒最小间距约 6.963 米；四组抽屉静态射线和地面查询前后结果相同。这是局部静态保留证据，不等于动态开抽屉、角色实走或完整控制交互验收。原门灯 IDs、源 Prefab、正式 Apollo12 场景及并行代码/材质未改，478 个保护文件哈希无变化。

25 张实际导入图的压缩、色彩空间和 wrap 已核对：BaseColor 为 sRGB/BC7，法线为线性 Normal Map/BC5、不翻转绿色通道，打包图为线性/BC7；印刷面 Clamp，继承机身 Repeat。9 个受支持的 URP Lit 材质使用 R 金属/G AO/A 光滑度、Metallic Alpha 光滑度标量 1，无发光。六网格具有 UV0、法线和计算切线，没有 UV1。副本保存并重开后六件及原九项均在，场景干净，Play 停止，编译状态无失败。未截图、未重跑 28 项、未运行 Play/联机/性能检查，动态交互、主观 PBR 与试听未测。

本轮只修改上述新目录、`Apollo12_EVA_Walkthrough.unity` 和既有两份交付报告。数据证据位于 v11 Temp 下的 `local_clearance_validation.json`、`installation_validation.json`、`save_readback.json`、`protection_check.json` 与 `old_formula_explanation.json`。回退备份为 `Apollo12_EVA_Walkthrough_before_casing_v11.unity`，恢复此副本会保留前一轮九项 L14 接入；原正式地图入口继续保留。不提交、不推送、不安装升级。
