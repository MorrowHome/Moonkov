# 标准 Git + Git LFS 工作流

2026-10-22：因 GitButler Desktop 报告 Worktree Filters 会在工作区操作中被跳过，本项目退出 GitButler 工作区模式，恢复标准 Git + Git LFS。此次检查发现 2105 个 LFS 路径中有 110 个工作目录文件是指针文本；对应对象均已存在本地，已从缓存恢复。没有据此认定每个指针出现的具体时间或操作。

日常基线为 develop；main 由用户手动管理。新功能使用自己的 `feat/...` 分支，完成最小必要验证后仅提交所属文件，推送到同名远程分支；用户决定何时将功能合入 develop，再决定何时将 develop 合入 main。协作规则维护等非功能性修复可作为独立提交落在 develop。

共享工作目录一次只能检出一条分支。多个 AI 可以按文件分工，但不能同时在同一目录切换到不同分支；需要分支隔离时使用独立 worktree，并继续遵守最多一个 Unity Editor、避免同时导入多个项目的约定。标准 Git 分支也可建立依赖关系，PR 必须明确依赖，不把未完成的基础功能混入独立交付。

接入试验的两个已推送分支完整保留：`feat/gitbutler-workflow` 是历史安装配置，已由本文件与当前 AGENTS.md 替代，不应再次合入来启用旧规则；`feat/dev-style-toggle` 是 Ctrl+L 功能，仍保留在独立分支，局内验收尚未完成，未在退出操作中合入 develop。

官方 GitButler 软件和全局 Skill 保留，避免影响其他仓库；本项目的 AGENTS.md 明确排除该 Skill。旧对话或其他 AI 在继续工作前应重新读取项目规则。不要在此仓库重新初始化 GitButler 工作区。

退出前的引用清单、未提交二进制差异和原 AGENTS.md 保存在本地 `.git/codex-gitbutler-exit-20261022/`。GitButler teardown 也保存了操作快照；这次退出指定检出 develop，没有删除功能分支、强推或更改 main。

LFS 指针展开属于工作目录恢复，不产生新的资源提交。优先使用 `git lfs checkout` 从本地缓存恢复；缺少对象时再按需 `git lfs pull`。保留 `.gitattributes`、LFS 过滤器与 pre-push hook。恢复后按文件头和声明大小检查，不因 Git 状态干净就断言工作目录里的资源已经展开。
