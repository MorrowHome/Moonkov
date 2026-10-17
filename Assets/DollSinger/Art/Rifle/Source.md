# 步枪光环图稿

2026-10-17：参照本项目美术交付的左轮、霰弹枪的平面色块语言，重新绘制步枪图形；没有改写或挪用两者的图稿。

可编辑源稿与导出脚本在 `SourceData/WeaponArt/RifleHalo/`。四份 SVG 使用明确的贝塞尔轮廓、切口和留白，不使用随机噪点、纹理生成或立体倒角。PNG 为白色透明底遮罩，由步枪专用的 `DollSinger/RifleHaloInk` 精灵材质染成粉红，沿用光环的平面发光方式。Shader 按 URP 的精灵协议读取每个 Renderer 的颜色、翻转与纹理，避免共用材质时丢失染色。

| 图稿 | 用途 | Pixels Per Unit |
|---|---|---|
| Petal | 六片长短交替的羽刃 | 4000 |
| OuterArc | 六段带断笔的外缘，充能时另复用一段 | 4000 |
| InnerArc | 三段反向旋转的细弧 | 4000 |
| Cell | 六组五枚弹量种子 | 8000 |

在项目根设置 `NODE_PATH` 指向含 `sharp` 的 `node_modules`，运行 `node SourceData/WeaponArt/RifleHalo/build.mjs`，再执行 `Tools/Doll Singer/Install Rifle Iris Halo`。安装器保留原预制体根物体及组件 ID，更新子级绘图对象；已有本地和网络角色通过嵌套预制体继承。源 PNG 无额外包依赖，运行时只调整固定精灵的位置、角度、大小和颜色。
