# Game UI Design Taste

本项目将 `Leonxlnx/taste-skill` 的 `design-taste-frontend` 适配为游戏 UI 设计与 Unity 实现 skill。安装标识保持不变，入口与自动触发描述以 [SKILL.md](SKILL.md) 为准。

适用于 HUD、背包、仓库、商店、主菜单、暂停、设置与结算界面的设计、实现和评审。强调题材一致性、战斗可读性、管理界面密度、交互状态、中文字体与真实游戏系统接入。

可这样调用：`使用 $design-taste-frontend 改进现有背包 UI，沿用项目框架，补齐选中、禁用和交易失败反馈。`

当前工作流的支持文档是 `references/game-screens.md` 与 `references/unity-game-ui.md`。`evals/evals.json` 保存人工评估用例；它们是验证素材，不表示已执行或通过。

其余参考文档、网页脚本、基础 CSS/JS 和 `library/` 保留上游内容，便于查阅来源或明确需要时复用。它们的网页审美禁令、照片和动效数量门槛、浏览器测量及研究评分不适用于游戏 UI，也不是本 skill 的默认依赖。仅使用游戏 UI 工作流不要求 Node、Chrome 或网络。

`skills-lock.json` 保留原安装来源记录，不代表本地适配文件的校验值；上游重装或更新可能覆盖本项目适配，应先检查 Git 差异。
