# GL125 地图接入

正式入口为 `TheophilusGameScene`，出生子场景沿用同一入口。源文件是交付包中的 `Moonkov_GL125_FULLMAP_LAYOUT_NATIVE_LINKED_STATIC_NOT_RUNTIME.blend`。主文件 SHA-256 为 `730d2651933762900f231ede202018e83bc1f347e1212d2c38bab7136afd4d73`。12 个实际链接库和包内其余 Blender 输入均已按交付清单核对。保留整包 `sources` 目录。

- `Tools/Blender/export_gl125.py` 在安全落地场景中分批求值。坐标单位为米。Unity 使用 Blender `(x, y, z)` 对应 Unity `(x, z, y)`，并翻转三角形绕序。已经写入世界坐标的网格不再套原父级变换。
- `Tools/Blender/pack_gl125.py` 用 NumPy 和 xatlas 切分渲染网格。输入包含交付包的建筑和碰撞清单，以及导出的 `Evaluated/ReceiverStates.json`。MC00 使用 16 米网格和分层图集，目标光照密度为 16 像素每米。地形保留原三角面及接地修补，按 256 米分块。
- `Tools/Blender/build_gl125_collision.py` 从当前可见结构生成静态碰撞，按实际尺寸筛掉小件和薄装饰。单面结构片补反面，地形直接使用显示网格。旧 V73 代理不进入正式场景。输出 `PreparedQuality/StaticCollision/Collision.json`，绑定原渲染 `World.json` 哈希。传入 `Evaluated`、`PreparedQuality` 和 `GL124_PLACEMENT_CONTRACT.json`。只修现有地图碰撞时执行 `GL125SceneInstall.ReplaceStaticCollision(工作目录)`。
- `Tools/Blender/bake_gl125.py` 用 Cycles OptiX 计算 486 盏固定灯的光照，分离线性 EXR 与材质。图集为 50 张 2K、24 张 4K，每张 128 次采样。主日照及阴影在 Unity 实时计算。按同一 `World.json` 哈希和各图集回执续烘。
- `GL125WorldAssets` 导入 Unity 原生网格、材质和光照图。URP Lit 使用实际能直接映射的基色、法线及 PBR 参数。复杂程序材质使用近似参数。角色探针基于新版灯位和新碰撞估算。局部反射需要单独制作。
- `GL125SceneInstall` 保留气闸、任务设备、补氧区和搜刮规则。旧抽屉的交互引用改为独立位置锚点。气闸结构传声引用指向新版 MC00 碰撞。灯钮按废弃处理。
- `GL125AirlockInstall.Install(工作目录)` 接入 MC00 东 EVA、西货运和北人员三处。西、北的内外门叶按原门框尺寸制作。每处有室内和室外呼叫台，舱内有去室内和去室外两台。按 F 后，气闸先关另一扇门，再均压并开启目标门。门洞有人时禁止关闭，关门中有人进入时反开并取消本次请求。
- `Tools/Blender/build_gl125_lifts.py Evaluated PreparedQuality LiftStage` 从原渲染和碰撞中拆出四台电梯的 47 组活动部件。`GL125LiftInstall.Install(LiftStage)` 写入正式场景。UL39 北侧直达上升 51 米，中央塔 N62 上升 48.3 米，FT55 上下两台各上升 1.3 米。每台有上下站呼叫和舱内换层，共 12 个控制点。N62 上站饰条随门叶移动。FT55 的开门行程在源动画基础上加高，使站立的玩家可以通过。

Windows 中间文件在 `D:/MoonkovBakeArchive/GL125Unity`。`Evaluated` 存放分批网格。`PreparedQuality` 存放 Unity 输入和烘焙回执。室内使用 `bake_gl125.py --scale 2`，55 和 59 号使用 `--scale 4`。超采样结果放入 `LightmapsHQ`，按运行时尺寸写出。仅更新光照时执行 `GL125SceneInstall.UpdateSupersampledLighting(工作目录)`。

重做正式切换时，先用 `Tools/Art/prepare_gl125_scene_shell.py` 从切换前场景生成临时副本，再在同一个 Unity Editor 执行 `GL125SceneInstall.QueueInstall(工作目录)`。临时副本先移除旧静态光照引用，避免加载两版完整地图。正式安装检查旧场景哈希后才写回原场景路径。使用 `GL125SceneInstall.VerifySaved(工作目录, "UnityFormal.png", true)` 重新打开地图、核对引用并更新战术地图底图。

试样的大面光照密度由约 2.7 提升至约 17 像素每米。初次接入时，Editor 临时场景通过 354 项定向物理检查，包括出生点胶囊、气闸开口、两条 GL125 新接路、任务站位和撤离点。10 个旧物资或敌人点位按相同水平位置调整了高度。核对了原东侧气闸引用、5 个任务设备和 62 个搜刮目标。源包其余入口和路段的可走性仍需逐区验收。Play、实际玩家路线和 Player 性能由后续验收确认。

正式场景重新打开后加载了 1062 个渲染器、74 张光照图和 7227 个角色探针。28 张室内光照图已逐张对照超采样输入。`UnityFinal.png` 和 `BlenderNative.png` 为同机位对照。重烘后暗墙和天花板噪点减少，Unity 画面整体更暗。

2026-11-08 已将普通箱、路线箱和怪物候选点从旧地形布局移到主要建筑群。保存后的 5966 项物理、生成规则和服务端导航检查通过。数量、掉落、补货、玩家出生及撤离规则保留。详见 [战局刷新](../RaidSpawning.md)。`TheophilusRaidLayout.asset` 仍引用已退役的 `MC00_ROOT`，不能直接重新发布覆盖 GL125；它的根路径迁移尚未完成。

2026-11-07 修复碰撞。旧名字筛选漏掉多座厂房外壳、楼板和井壁。MC00 东侧 L1 S21 的旧碰撞仍含 7 块已移除面板。621 个新结构碰撞块取代 486 个旧块。66 栋重排建筑的 132 个墙面和地面采样全部命中，20 个其他结构点和 5 个薄板背面通过，旧面板的 12 个空气墙采样全部清空。354 项玩法检查通过。1 个敌人出生点从地形接缝横移 0.45 米。回执为 `UnityCollisionRepair.txt`，备份为 `TheophilusGameScene.before-GL125Collision.unity`。实际走图和性能待 Play 验收。

2026-11-07 修复 MC00 室内呼叫。旧按钮落在内门靠舱侧，且直接开门受压差限制。新版 12 台控制台均通过实际玩家瞄准选取检查。三处气闸的均压循环、互锁、六扇门的开关通行和气压区通过编辑器检查。压力及互锁规则共 35 项通过。回执为 `UnityAirlocks.txt`，备份为 `TheophilusGameScene.before-GL125Airlocks.unity`。实际 Play 与联机待验收。

2026-11-08 接入全部四台电梯。正式安装验证覆盖 12 个按钮的瞄准选取、双向行程和两站门洞。重开场景后，用正式角色的碰撞尺寸和移动控制器验证了静止乘客双向随车，以及关门时遇人反开。回执和切换前备份在 `20261108`。活动部件用场景光照探针取光。更新静态碰撞时，同一碰撞清单复用电梯剔除块。更换碰撞清单后需重新拆分并安装，避免门叶或轿厢留在原位。联机版本需要客户端与服务端一起更新。

2026-11-08 修复 MC00 和中央塔亮面接缝的黑点。窄折边和旁边的板面属于不同光照块，缝口出现亮度断点。使用 `Tools/Blender/soften_gl125_lightmap_seams.py PreparedQuality 原始EXR目录 输出目录` 按三维位置匹配同材质的邻接表面。只在接缝附近取光，较宽的表面仅补窄边。脚本需要 NumPy、SciPy 和 OpenEXR。重新导入原始烘焙后执行一次，并用输出的同名 EXR 覆盖 Unity 光照图。43 张图更新，原 74 张图的尺寸、UV 和 BC6H 格式沿用。

正式场景用玩家相机参数比较 12 处机位，并在转运走廊、收货区、中央塔上层横移连拍。11 个室内机位里，平坦受光区域的局部暗像素候选从 6889 降到 4830。候选包含较窄的结构阴影，收货区仍有明显深色边缘。外部太阳直射的对照变化很小。对照图 `TwelveViewsBeforeAfter.png`、`MotionReview.png` 和改前贴图 `LightmapsBeforeSeamContinuity` 在 `20261108_FixtureNoise`。实际 Play 走图和 Player 性能待验收。

2026-11-08 清理表面重叠。用 `Tools/Blender/build_gl125_surface_cleanup.py PreparedQuality EXR目录 LiftStage/Lifts.json SurfaceCleanup.json` 生成面索引和哈希。执行 `GL125SurfaceCleanup.Install(SurfaceCleanup.json)`，在 234 个显示块中去除 340,290 个同向重合面。其中 1188 处异材质重合按外饰面或铺装层保留。保留三角面的顶点、材质和光照 UV 沿用原数据。网格包备份在 `20261108_FixtureNoise/MeshesBeforeOverlapCleanup`。

2026-11-08 补齐固定灯。用 `Tools/Blender/build_gl125_practical_lights.py Evaluated PreparedQuality PracticalLights.json` 生成灯位，再执行 `GL125PracticalLightInstall.Install(PracticalLights.json, 烘焙根目录)`。22 种灯罩恢复发光。178 个烘焙灯位保存在正式场景的 `GL125 Fixed Lights (Bake Sources)` 下，组默认关闭并标记为 EditorOnly。93 处只有发光网格的灯补入现有角色探针，含距离和碰撞遮挡限制。7227 个探针位置沿用原数据，其中 3599 个探针更新了光照。运行时沿用原有探针采样。回执和探针备份在 `20261108_FixtureNoise`。

旧 `SourceFidelity/World` 工作副本清理释放约 6.1 GiB。共享的 `SourceWorldForwardRenderer.asset` 和对应 `.meta` 保留。清理回执为 `OldWorldRemoval.json`。回退需要从替换前提交一起恢复正式场景、战术地图底图和旧 World 目录，再用 `git lfs checkout` 恢复旧目录的大文件。切换前场景的独立副本是 `TheophilusGameScene.before-GL125.unity`。
