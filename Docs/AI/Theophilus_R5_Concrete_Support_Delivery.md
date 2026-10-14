# R5 两件支承竖面材质更新

2026-10-14，R5 重试已成功保存。修改限独立 Theophilus 场景中的 `B_CONCEPT_PIER_0112` 和 `R2_MODULE_RING_BEARING_03` 显示 UV0、竖面材质分配及必要显示 Mesh。

- 场景：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。
- 新资产：`DenseRingConceptB/R5ConcreteSupports/`，2 个显示 Mesh、1 个标准 URP Lit 材质、3 张内嵌 2K PNG。
- 两件对象的源 polygon 2–5 使用新增槽 6、槽 2。旧槽 0–5、0–1 的引用全部保留；顶底和其他面的 R4 UV/材质保持。
- 在原 Unity 显示 Mesh 的副本上只写入受影响竖面 UV、从旧槽 0 分离对应三角面到新增槽，并重算切线。顶点、法线和其他面的索引对应通过本次写入前的局部比对；未另建 GameObject 或替换组件。
- 更新保存为场景实例覆盖。原 Prefab、旧显示资源、碰撞、Transform、父链、其他 R4/R3、八楼、DEM、正式和并行地图均未编辑；未改灯光或运行时代码。

底色 sRGB、白色 Tint；NormalGL 以 NormalMap 导入，OpenGL +Y、Strength 1，不翻绿；MetallicGloss 线性，R 金属、A 光滑度，Metallic Alpha、Smoothness 1。三图同 UV0，Tiling 1、Offset 0；2.710 m 扫描尺度已写入源 UV，不再次缩放或翻 V。Standalone 底色/金属图 BC7、法线 BC5，保留 Mipmap。

贴图仅从已下载 R5 Blend 的 packed images 提取，并匹配内嵌清单的三份文件摘要。按清单声明，来源为 Poly Haven `concrete_wall_008`，CC0；摄影 Charlotte Baglioni，处理 Dario Barresi。源交付中的底色暖色已降低、法线直接复用、粗糙度已打包为光滑度，本次未再处理或生成贴图。这是地面混凝土扫描表面，不构成月球施工材质实证。

源文件：`LocalData/AI/AssetHandoffs/TheophilusDenseRing/R5/Moonkov_Theophilus_B_Dense_Ring_Concept_Increment_R5.blend`，45,693,535 字节，沿用交接已核对的 SHA-256 `ac91dbf7ba9a3219e454f982e500aac6e8af5ffbbde778ae8f8b9db3485673f7`。读取内嵌 `R5_MINIMUM_IMPORT_ZH.txt`、`B_RING_R5_CONCRETE_DELTA.json`，未保存源 Blend 或下载其他素材。

回退：同一持久目录中的 `BeforeConcreteR5.unity` 和 `BeforeConcreteR5_References.tsv` 保存修改前场景及两件旧显示引用；恢复前先保留后续编辑。

必要验证仅覆盖正确目标、用户 dirty/Play 占用、两件显示输入与三图绑定、场景保存重开。临时接入脚本编译无诊断，保存重开后旧槽和新增绑定成立，`dirty=false`、`playing=false`。没有截图、试走、全场测试或保护扫描，也没有画面、性能或路线验收声明。按用户收尾约定做本地限定提交，不推送。
