# 正式地图银砂月壤

目标是在当前黑白银盐画面中补出干燥矿物的细碎反光，以及压实地面、碎岩更连续的光泽。属于材质层的风格化处理，沿用现有灯光、Bloom 和银盐后处理，不额外增加全屏通道，也不添加紫色。

## 实际覆盖

`TheophilusGameScene.unity` 中两个可见 Terrain，以及使用 `Moon/R29 Local Ground Display` 的 9 个局部地表 Renderer。共 5 个独立材质副本保存在 `Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01/SilverSand/`。

- 两个 Terrain 原先实际使用 URP Terrain/Lit。新增 `Moon/Silver Sand Terrain`，保留地形层混合、法线、遮罩、孔洞、实例化、投影、深度和烘焙通道。
- 压实地面、细尘、岩块沿用 R29 的世界连续纹理、粗糙度与法线，只给正式地图副本启用新的光照响应。
- 原材质、TerrainData、高度、孔洞、碰撞、路径和光照烘焙资产均未修改。旧的 `Regolith.shader` 不承担本轮正式地形效果。

## 光照与过滤

`SilverSand.hlsl` 让世界坐标固定的矿物微表面响应主光源、Forward+ 附加方向光和局部灯。高光随观察方向和光照半角变化，乘上表面朝向、距离衰减、灯光层和阴影衰减；在地形上也乘材质遮蔽。弱的无阴影海报补光不驱动明亮颗粒。

颗粒尺寸和长宽不一，使用屏幕导数估算覆盖范围，做面积衰减与淡出；最远 45 米淡出，不使用时间噪声。地面仍是非金属的粗糙表面，镜面光泽来自原有 PBR 与额外粗糙高光，不做环境镜像或湿地效果。

参数：

| 材质 | 颗粒强度 | 单位米网格 | 颗粒密度 | 最低光滑度 | 粗糙光泽强度 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Terrain / 细尘 | 18 | 8 | 0.48 | 0.20 | 0.8 |
| 压实地面 | 14 | 8 | 0.40 | 0.38 | 1.8 |
| 碎岩 | 24 | 8 | 0.48 | 0.28 | 1.1 |

`_SilverSand` 为强度、单位米网格、密度、淡出距离；`_SilverSheen` 为粗糙光泽强度、最低光滑度、微表面法线分散度、保留值。地形材质可在 Inspector 的 Debug 模式调整这两个向量。

工具菜单：`Tools/Moon Environment/Theophilus/Apply silver sand`。重复运行会复用副本并恢复上述参数，不生成多份材质；遇到 Play、未保存场景编辑、Shader 错误或超过四个地形层时拒绝应用。共享 R29 材质默认强度和光滑度下限为零。

## 对比与验证边界

见 [四组同机位对比](ArtDirection/20261019-SilverSand/README.md)。左侧是原始地表材质，右侧是银砂材质；两侧后处理、Bloom、灯光、曝光和相机相同。01、02 为不同受光方向的地形，03 为压实地面与碎岩，04 包含建筑支撑下的暗部。

已检查 Editor 中 C# 命令/工具编译、Shader 当前平台渲染及正式地图材质绑定。未执行 Play 移动闪烁、手电扫描、GPU 耗时或目标平台验收。导数过滤只是降低闪烁风险，静态截图不能证明运动稳定性。

地形扩展基于项目安装的 URP 17.5 源码，版本升级时应同步 `SilverSandTerrain.shader`、`SilverSandTerrainInput.hlsl`、`SilverSandTerrainPasses.hlsl`；许可见同目录 `SilverSandTerrain-LICENSE.md`。颗粒扩展面向当前 Forward / Forward+；Deferred 通道保留原 PBR 和光滑度调整，未增加矿物高光。远距离 basemap 与额外地形层仍使用标准 URP 依赖；应用工具限制当前地形层数不超过四层。
