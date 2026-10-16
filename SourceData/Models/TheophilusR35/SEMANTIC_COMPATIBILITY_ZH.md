# Moonkov 4.3.2 → 官方 Blender 5.1.1：窄语义兼容补丁 v2

日期：2026-10-16。只含交接脚本/参考/校验清单，不含替换资产。旧三包及 v1 ZIP 均保留。

## 结论与使用顺序

本补丁已经逐项审查实际 5.1.1 三份 BLOCKED 报告，并完成云端 4.3.2 回归。它没有在本地 5.1.1 执行，不能称已通过 5.1.1 验收。

1. 在原三包的独立工作副本中，按 PATCH_MANIFEST.json 的目标锚点核对文件。每个子目录仅覆盖相应包根目录；不要套入 .blend 所在的任意目录。允许的覆盖前 SHA 同时列出原始包和 v1 补丁版本。
2. 先只处理 R29_R35。覆盖本子目录文件，保留原始 ZIP、原脚本、原报告，重启 Blender 5.1.1，重新打开未接入任何增量的 R29 工作副本。
3. 在 Blender Scripting 中运行该包 COMPAT_CHECK_ONLY.py。它不会自动应用，只有实际报告 status=PASS 才可运行原 apply_combined_display.py。
4. 预检失败时停止，查看 errors / differences / capabilities；不改资产、UV flag、材质参数、expected hash 来迎合检查。
5. 首包实际应用且其守卫通过后，再到 R36_R38 包运行 COMPAT_CHECK_ONLY.py，然后才可运行 apply_display_supplement.py。最后对 R39 包同样预检后运行 apply_r39_material_uv_patch.py。
6. 三个包的 compat_readonly.py 和 compat_semantics.py 字节完全相同。替换脚本后必须重启，禁止只 reload 某一个模块。预检 receipt 绑定实际二进制、加载代码、包文件、原内容与未投影能力状态；改包、换二进制或改受守卫内容后需重新预检。

精确运行许可：官方 Blender 5.1.1，build hash b70da489d7f4（完整上游提交 b70da489d7f4eae7760cc0a50cc6c60cd5464205）。其他 5.1.1 构建继续阻塞。原 4.3 分支继续走原比较。

## 接受什么、怎样保持严格

不接受新的材质 SHA 名单，不忽略材质 hash。只在只读 Python 序列化视图中，将有精确官方证据的 5.1.1 表达投影回原 4.3.2 表达，重新计算 SHA，并与原映射里的原 SHA 完全相等。原 payload、JSON 映射、纹理及参考文件的 SHA 不变。

完整材质 descriptor 仍比较：节点身份、全部原可写 RNA 属性、输入和输出身份/默认值、节点树/连接、图像字节与色彩空间、ramp/mapping。除以下明示规则外，任何新增、缺失、数值、结构或内容差异都继续阻塞。

### 1. UV clone：只比较当前有效克隆来源，不写 flag

5.1.1 从旧索引改成命名选择。只有实际 mesh.uv_layer_clone 为空、全部 raw active_clone=false、且当前 active UV 正是原 clone 所在层时，无明确选择的绘制回退与旧选择等效。多 UV 也必须逐层保留名称、顺序、坐标 SHA、render flag、active index。有效来源不同，原签名立即不匹配。

预检直接读取 clone 指针、全部 raw flags、active 层；不只看 index=0，因为未选择时该 index 本身也会返回0。若有明确选择，原始指针/派生 flags 必须自洽，并继续与原签名比较。首包给原护罩增加 R34 UV 前后验证未投影 clone 状态不变；应用 receipt 保存前后原始状态；回滚前后再验证，不修改 clone flag。

这证明当前有效来源相同，不宣称底层 flag 相同或今后用户切换 active UV 后的行为永远相同。

[命名迁移](https://github.com/blender/blender/commit/442463239b7769fbb6f17a72b0d26fceb516db77)
[指针和index getter](https://github.com/blender/blender/blob/v5.1.1/source/blender/makesrna/intern/rna_mesh.cc#L847-L875)
[绘制实际回退](https://github.com/blender/blender/blob/v5.1.1/source/blender/editors/sculpt_paint/mesh/paint_image_proj.cc#L4164-L4192)

### 2. lightprobe culling：精确反转，不能改回旧值

use_backface_culling_lightprobe_volume 的 true→false 来自旧文件迁移对存储位 XOR，加上引擎修正此前反向解释，保持光探针捕获的实际双面语义。比较视图按布尔值精确反转后与原字段对比；不把它当 UI 忽略，不写回材质。

[旧引擎](https://github.com/blender/blender/blob/v4.3.2/source/blender/draw/engines/eevee_next/eevee_pipeline.cc#L1487-L1496)
[新引擎](https://github.com/blender/blender/blob/v5.1.1/source/blender/draw/engines/eevee/eevee_pipeline.cc#L1673-L1682)
[明确旧文件迁移](https://github.com/blender/blender/blob/v5.1.1/source/blender/blenloader/intern/versioning_500.cc#L4351-L4356)

### 3. 节点身份与位置

bl_static_type 未删除；5.1.1 从可写 enum 改成只读 string。预检显式读取只读值，在完整 descriptor 中与原身份精确比较，另保留 bl_idname 比较。旧 legacy serializer 的 p.is_array 访问本来漏掉 enum/string；不向原 hash 额外插入原先不存在的字段。

location_absolute 是新增画布坐标，只对实际无 parent Frame 且它精确等于原 location 的节点允许剔除冗余表达。任何父 Frame 或坐标差异阻塞。并禁止这些受比较材质/节点树的动画或驱动，以免新增参数被动态改变。

[只读旧类型身份](https://github.com/blender/blender/blob/v5.1.1/source/blender/makesrna/intern/rna_nodetree.cc#L9045-L9051)
[局部与画布位置](https://github.com/blender/blender/blob/v5.1.1/source/blender/makesrna/intern/rna_nodetree.cc#L8847-L8857)

### 4. Normal Map

仅 ShaderNodeNormalMap 新增 base=DISPLACED、convention=OPENGL 可投影。其它值阻塞，原 UV、strength、color、图像、Non-Color 色彩空间及连线继续严格比较。DISPLACED 是旧文件明确迁移值；OPENGL 保持旧版不翻绿通道的处理。

[base 旧文件迁移](https://github.com/blender/blender/blob/v5.1.1/source/blender/blenloader/intern/versioning_500.cc#L3834-L3848)
[convention 分支](https://github.com/blender/blender/blob/v5.1.1/source/blender/nodes/shader/nodes/node_shader_normal_map.cc#L100-L110)

### 5. 补充包 MixRGB / Bump / Group

实际报告只出现 ShaderNodeMixRGB 输入0的展示名 Fac→Factor。要求 identifier=Fac、NodeSocketFloat、展示名精确 Factor；只把该展示名投影成 Fac，不全局替换字符串，不改自定义组 socket。原值和连线保持精确比较。

Bump 仅允许精确五输入 Strength/Distance/Filter Width/Height/Normal，类型、name、identifier全对。Filter Width 必须1.0且无连接；0.1是新建节点默认值，不是本批旧文件兼容值。剔除经验证的新增输入后，保留原四输入全部值，并按实际 socket 身份映射 links，非裸索引豁免。完整 descriptor 对照原参考会限定为原来的六个 Bump 路径实例。

Group 仅 ShaderNodeGroup 的类型 UI 上限 bl_width_max=700 投影为原400，实际 width、接口、节点、连接不变。未知其它节点不套用。

[Fac 展示名迁移](https://github.com/blender/blender/commit/f47a84e7101141ebaa2178189b0a8f9aa1510154)
[Bump 新字段](https://github.com/blender/blender/commit/f987ef7b6e4c02defd0ab5a168040c6f51cfb6d2)
[Bump 旧文件设1.0](https://github.com/blender/blender/blob/v5.1.1/source/blender/blenloader/intern/versioning_440.cc#L385-L397)
[Bump 迁移版本门](https://github.com/blender/blender/blob/v5.1.1/source/blender/blenloader/intern/versioning_440.cc#L514-L523)
[Group UI 上限统一](https://github.com/blender/blender/commit/cd789b2c49421d9c2a51c8f006523204cc5cba7d)

## 本地只需要一次补齐的只读能力证据

已整合进 COMPAT_CHECK_ONLY.py；无需手工改场景或另外多轮全测。
- 受守卫 mesh 的实际 clone 指针、有效来源、active index、完整层名和 raw flags。
- 每个原节点的 readonly bl_static_type、RNA数组能力、parent、location_absolute，及 Normal Map 精确新增值。
- 补充包完整五个 Bump 输入（含原报告未列出的新增尾项），Filter Width 值和连接状态，完整投影后的连接对照原图。
- 材质/节点树无 animation_data；精确官方 build 与所需 RNA/API。

任何能力读取失败都直接 BLOCKED，不能仅把 error 写进附加报告后 PASS。原报告列表 zip 截断问题已修正，新列表尾项会完整报告。

R29_R35 首包先跑即可。R36_R38 当前13个前序对象缺失、原护罩尚无 R34 材质槽/UV，仍是真实前置不足，不属于兼容豁免。报告另列 prerequisite_blockers，但不会从 errors/differences 中移除这些阻塞。

## 仍未解决 / 不作承诺

- 尚未获得本补丁在真实本地5.1.1的 PASS；上述能力字段在本地实际读到且全等后才允许应用。
- 真正 append 行为仍由应用时的原内容守卫验证。R34 材质库直开 use_fake_user=True，而正确 append 状态为实际False；绝不规范化此字段。异常时在改原mesh前拒绝，并只清理本次新增数据。
- 跨 Blender 渲染器版本的最终外观仍需美术检查；内容语义对应不等于像素一致保证。
- R39 回滚保留后来被其它材质使用的图片及其 fake-user 标记，不清理其它资源。
- 没有安装软件、操作用户电脑、保存/导出场景、运行渲染或接触 MC00/运行时/DEM/碰撞。

## 证据

evidence/OBSERVED_HASH_RECONSTRUCTION.json：三项本地变化的材质原始 SHA，均由原4.3序列化副本仅套已解释字段，精确复算为报告中5.1实际 SHA。没有改 Blender 数据块。
evidence/REGRESSION_V2_432.json 与 REGRESSION_V2_R39_432.json：4.3.2 基础回归。
evidence/SEMANTIC_UNIT_TESTS.json：纯 Python 合成5.1分支的正负例；不是5.1运行验证。
PATCH_MANIFEST.json：逐目标原始/v1/v2 SHA 和覆盖定位。
