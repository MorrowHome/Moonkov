# 光环模块几何图源

`ModuleArtwork.svg` 是当前有效图源。白色实心几何形状用于运行时染色。供能用暖黄，射击用青蓝，辅助用淡紫；UI 图标复用相同颜色。每格只有一簇；左右镜像由运行组件生成。

图集为 1024×512。每格为 256×256，按从左到右、从上到下排列：

| 顺序 | Sprite 名 | 形状 |
|---|---|---|
| 1 | `halo_capacitor` | 三块并排的实心竖短条 |
| 2 | `halo_charge_bridge` | 两端接点和实心桥 |
| 3 | `halo_mooncore_recycler` | 两个折返箭头 |
| 4 | `halo_focus_lens` | 两个内收夹片 |
| 5 | `halo_fan_array` | 三片扇形射线 |
| 6 | `halo_pulse_controller` | 三个菱形 |
| 7 | `halo_hit_marker` | 带缺口菱形 |
| 8 | 无 | 完全透明 |

复用本机已有的 `rsvg-convert`。在仓库根目录执行：

```sh
python3 ArtSource/HaloModules/render_artwork.py
```

此命令只生成 PNG，不启动 Unity，不修改 `.meta`。随后在现有 Editor 中运行 `HaloModuleArtworkSetup.Prepare()`，按透明轮廓切片，并复用 `RifleHaloInk` 材质。

先前的宝石风格图像生成尝试已被否定。当前图集只从本目录的 SVG 生成。
