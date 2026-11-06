# Halos 登录与加载动画

源项目为 Halos 仓库的 `Moonkov UIs`。版本和源文件哈希见 `source-manifest.json`。

- 保留原三角标志、终端窗口和噪声。原 GML 放在 `objects`。
- Unity 资源在 `Assets/Resources/Moonkov/UI/Halos`。标志只裁出上半部分，移除图片中的账号和密码文字。噪声缩到 1024×576。终端底图保持原图。
- `MoonkovWordmark.svg` 是本次新增的游戏字标。输入框底线来自原登录图。
- `SignalResidual.png` 用内置 Image 工具在原噪声上补蓝色显影。原标志、窗口底图和位置不改。生成说明见 `signal-prompt.txt`。用户的参考图不作为项目背景导入。
- 动画由 `HalosTerminalBackdrop` 驱动。登录开场可点击跳过。地图加载只跳过装饰，不跳过真实加载。
- 不导入 Halos 中与本任务无关的武器原型。
