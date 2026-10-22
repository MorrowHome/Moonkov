# 战术道具客户端空引用修复

从混合提交 `bc50c344` 拆分；保留共同游戏基线 `feat/lan-rooms@0725c2ed`，包含其尚未集成的功能。修复分支共同继承仅修改 AGENTS.md 的 `fix/task-branch-rules`，彼此不依赖。

显式构造 TacticalItemClientState 托管组件；命令缓冲区使用 using 保证异常路径释放。

原提交已通过独立 ECS World 的托管实例创建和旧 RPC 排序检查；入口 `Tools > Moonkov > Check Tactical Item Client State`。

本次仅重组 Git 提交，未重新运行 Unity。此前的定向检查不替代正式对局或联机验收。
