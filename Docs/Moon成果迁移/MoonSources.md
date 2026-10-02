# 月球场景来源与迁移记录

迁移日期：2026-10-02。

## 直接来源

这次成果的直接来源是 `MMORPG/Src/Client/Assets/MoonTerrainLab`，迁移时读取磁盘上的 `MoonTerrain.unity`、两份 TerrainData、三个材质和实际引用的素材。源 MMORPG 工程未被改写。

保留全部 **6000** 块岩石的模型选择、位置、四元数、缩放、接地平面、尘土过渡高度、反照率变化和新鲜度。`Editor/Source/layout.json` 是精确摆放配方，环境预制体已经包含完整实例。

## DEM

| 项目 | 值 |
| --- | --- |
| 产品 | LROC NAC_DTM_THEOPHILUS3，v1.9 |
| 原始栅格 | 3494 × 14547，3 m 原生间距 |
| 裁切窗口 | `[473, 11481, 1367, 1367]` |
| 有效比例 | 100%（来源元数据） |
| 导入网格 | 2049 × 2049，2 m 间距，4096 m 范围 |
| 近场 | 512 m 范围，0.25 m 重建网格 |
| 高程倍率 | 1:1，Terrain 使用统一零点偏移储存 |

2 m 重采样和 0.25 m 重建网格不会增加真实测量分辨率。小陨坑、岩石位置和近场颗粒是合成内容；地质遮罩与新鲜度是艺术控制，不能视为实测矿物或真实岩石年龄。

- [LROC 产品页](https://data.lroc.im-ldi.com/lroc/view_rdr_product/NAC_DTM_THEOPHILUS3)
- [原始 PDS GeoTIFF](https://pds.lroc.im-ldi.com/data/LRO-L-LROC-5-RDR-V1.0/LROLRC_2001/DATA/SDP/NAC_DTM/THEOPHILUS3/NAC_DTM_THEOPHILUS3.TIF)

来源元数据位于 `Assets/MoonEnvironment/Editor/Source/dem.json`，包含地理变换、轴向、裁切、有效比例和原始文件 SHA-256。原始约 203 MB TIF 未重复搬进游戏项目；迁移了直接供作者工作流使用的 float32 高度数据。

高度字节数据 SHA-256：

```text
36a9b778ff6e8df285eef6098d6a539b397439393dcc4d012ca0d3bdeaa71f35
```

## 表面素材

源项目使用的素材来自 Poly Haven 的 `moon_01`、`moon_flat_macro_01`、`moon_rock_01` 和 `moon_rock_03`。这些素材用于月壤与岩石的视觉类比，不代表月球现场扫描。

- [moon_01](https://polyhaven.com/a/moon_01)
- [moon_flat_macro_01](https://polyhaven.com/a/moon_flat_macro_01)
- [moon_rock_01](https://polyhaven.com/a/moon_rock_01)
- [moon_rock_03](https://polyhaven.com/a/moon_rock_03)

`Editor/Source/*-files.json` 保留源项目下载时的各分辨率文件清单、URL、MD5 和尺寸。此次只迁移被材质使用的贴图和两份模型，不附带无用的其他分辨率或位移贴图。源文件清单包含更多可选文件，不表示全部文件已迁移。

## 适配范围

源场景的 Built-in Surface Shader 转换成 URP 的显式 Forward、ShadowCaster、DepthOnly、DepthNormals Pass。保留原有表面采样、盘函数、相位峰和材质属性名称，材质与模型 GUID 保留，便于检验引用。

以下内容未迁入：MMORPG 角色、动画、业务 Services／Managers、网络协议、第三人称相机、旧的场景备份／截图路径、Built-in 全局 QualitySettings 脚本、旧实验场景。

月球模块自己的 `MoonPipeline`／`MoonRenderer` 不含模板专用全屏效果，运行演示时临时使用。项目已有的 GraphicsSettings、QualitySettings、Build Settings、包清单和联机场景文件未被修改。

`Editor/Source/migration-hashes.json` 记录迁移输入的 SHA-256；它用于来源追溯，不是对后续编辑的限制。TerrainData 与高度数据复制后经过哈希对比，布局通过实例数和序列化参数提取保留。

验证范围见 [迁移验证记录](MoonValidation.md)。最终视觉效果、太阳低角度观感与性能预算仍需在目标机器上由使用者判断。
