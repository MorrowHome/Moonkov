# 环境滤镜 / 角色与 UI 保护

01 三列：原画；环境滤镜但未注册角色保护；环境滤镜与角色保护。使用真实 MenuCharacterVisual.prefab 与正式 TheophilusGameScene。橙色小块为 UI layer 的渲染探针，不是新游戏 UI。每列1200×900，相机/灯光/Bloom保持相同。

![原画、未保护角色、保护角色](01-character-world-ui.png)

02 是 UI-only 独立相机中的真实角色，与原画逐像素比较。

![独立角色预览](02-isolated-portrait.png)

UI layer 中心像素最大差值（0-255）：0
遮挡墙中心区域，角色藏在墙后 / 完全隐藏，最大差值：0
独立 UI-only 角色相机滤镜开 / 关，最大差值：0

Unity 编辑器 Game 相机渲染；未运行 Host/Client 或 GPU 性能验收。
