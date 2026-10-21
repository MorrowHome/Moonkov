R22 → 已完成 R21 的 DOT00：两项小型、显示专用增量

本包不是完整地图，也不是自动安装/自动应用包。完整 R22 已另行交付。
先由父侧确认本包，再交给原 DOT00。不要联系或重开其他本地线程。
本地已完成 R21 的事实：5 Renderer、5 私有显示 mesh clone、1 私有材质，
150 源面 / 300 tris 已唯一匹配并应用；记录的 commits 为 fc975c6b / 46169763。
DOT00 必须读取当前本地记录及真实 scene 引用：
E:/UnityProjects/FPS_Template/Docs/AI/Moonkov_Rebuild_R21_RingFinish_Delivery.json
云端没有该记录的当前文件内容、GUID 或 Renderer 身份；本包没有虚构它们。
对象名、最近距离、中心、包围盒或数量都不构成唯一归属证明。

A. 0541 脚底收口：默认只导入资源，不实例化
只有 R22_FOOT0541_DISPLAY_ONLY.fbx 一个 FBX：1 新网格、24 三角、原脚印内楔形。
没有旧脚、柱、环桥、地形、相机、灯、动画或贴图。不要添加 Collider。
FBX 材质 R8_Scanned_Mineral_Support 只是无贴图名称占位，不是新美术材质。
关闭自动创建/提取新材质，明确复用已核实本地既有矿物支承材质；不覆盖共享材质。
所有坐标、原 0541 几何/UV/世界变换、底面参考和门槛在
FOOT0541_GEOMETRY_AND_LOCAL_GATE.json。Blender 原坐标为 Z-up 米制；
FBX -Z forward / Y up，UnitScaleFactor 100 cm，即每源单位 1 m。
不要猜测 Unity 轴/符号/额外 100 倍缩放；先从现有已验证 R21 映射核实。

脚件应用必须全部证明：
1. 当前本地原 0541 是唯一对应，几何、UV 和世界变换在经核实坐标转换后相符。
2. 当前实际显示地表在整个楔形脚印内兼容。可用完全对应的显示三角来源/变换，
   或对实际局部三角面与参考底面的完整平面覆盖做相交比对，最大高度差 ≤0.02 m。
   散点抽样、对象名字或云端图像不能代替完整覆盖证明。
3. 云端参考只是背景 16 m LOD/context，目标还在可靠肩部范围以东约150.235 m。
   它不是权威 DEM。不得修改用户原月面 DEM、显示地形、旧基础或碰撞来迁就新件。
4. 无重复 R22 实例，无额外现有几何/碰撞干涉；旧脚/柱/入口/优化/Collider 引用不动。
5. 核实本地导入单位、绕序、UV、法线/材质引用和实际显示。任何项不能证明，
   只保留导入资源，不应用脚件，准确报告不兼容处。不得靠移动/缩放旧资产补偿。

B. 环桥：只追加 12 个既有面的材质分配，绝不追加几何
RING_12_FACE_DELTA_CONTRACT.json 包含：
- 唯一源所有者 R2_MODULE_RING_SECTOR_00 的完整 R21 前置面清单：1604 面 /2604 tris；
- lower_chord 和 upper_chord 各6面，合计12 quads /24现有tris；
- 每角源坐标、UV、法线、组件、原/目标材质以及两种合法四边形对角线方案；
- 完整世界变换及本地所有权证据门槛。
源面索引1334–1345只用于诊断，不能直接拿来当 Unity 三角索引。
这24tris原为 R8_Dark_Protective_Steel，只转到本地 R21 已存在的私有石墨材质。
Blender 槽0→4不是对本地槽号的猜测。本地须核对实际 clone 的子网格与Renderer槽。
不重三角化、不改顶点/法线/UV、不复制整件、不增加材质、不使用完整环桥 FBX 替换。
UV 有意超出0–1，禁止 clamp、归一化、翻转或重新展开。

原 R21 150面 /300tris全部保持，保护清单见 PROTECTED_R21_150_FACES_300_TRIS.json。
其中 R2 内126面 /252tris，其他四 Renderer 合计24面 /48tris。
新12面不重叠；应用后仅材质目标合计162面 /324tris，不是新增324个三角。
保持原合并优化，不把现有 clone 拆回五个新对象，不修改 Collider/shared collision mesh。

只读验证器
validate_local_ring_capture.py 没有 apply 模式、没有 Unity API、不会写项目。
按 LOCAL_CAPTURE_SCHEMA.txt 由 DOT00 读取当前 clone/scene 生成真实 capture 与证据 receipt。
receipt 必须绑定实际资产、场景、Collider、完整capture及本包合同哈希，并从本地真实
引用填 Renderer GlobalObjectId、mesh GUID 等；独立检查后再固定 receipt SHA。
不能给未经审核的自填 receipt 取个哈希就当作证明。
验证器检查完整2604tris唯一对应、绕序、每角UV、材质、坐标转换及原R21保护集，
通过仅输出24条“待审核分配计划及原分配回退记录”，不是自动执行或 Unity 验收。
若本地克隆包含额外合并几何而不满足完整唯一匹配，停止，不裁剪、不重建来通过。
必须用当前状态重新采集；任何缺失、歧义、UV改变、旧材质不符或收据不可信均阻止计划。
使用已有 Python 即可，无须安装。--schema 可查看完整格式；测试数据仅合成验证器测试。

静态验证及边界
STATIC_EXPORT_READBACK.json 为实际 FBX 二进制与 Blender 读回结果。
最终FBX逐角法线、坐标、UV和原flat面标记直接检查；导入自定义法线及按flat面
重算法线两种读回均通过。早期未写flat标记的原型被拦住，未装入本包。
这些结果不冒充 Unity 显示通过，本地仍需查看实际导入法线与材质接缝。
verify_fbx_static.py 只可在“全新后台 Blender 进程”运行，不能在有未保存编辑的会话执行。
云端没有新增渲染；仅此前单脚隔离前后图经父侧通过，未验证全场景光照/遮挡。
没有权威DEM接地、Unity编译/实机、碰撞、通行、灯控或性能通过声明。

回退及变更限制
详见 ROLLBACK_仅本增量.txt。执行前保留本次显示引用和24条精确旧分配。
只回退这次脚件和24tris，不撤销R21原300tris，不改地形/交互/118灯135按钮稳定映射。
不修改运行时代码，不重启 Host/SubScene，不 push，不混入用户修改。
本包没有令牌、图库或完整地图副本；SHA与文件清单见 MANIFEST.json。
