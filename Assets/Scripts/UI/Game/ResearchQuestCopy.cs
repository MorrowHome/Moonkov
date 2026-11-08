namespace Unity.MP_FPS.Client
{
    internal static class ResearchQuestCopy
    {
        internal sealed class Entry
        {
            internal readonly string Chinese, English, NodeEn, GoalZh, GoalEn, BriefZh, BriefEn, Art, Item;
            internal Entry(string chinese, string english, string nodeEn, string goalZh, string goalEn, string briefZh, string briefEn,
                string art = null, string item = null)
            {
                Chinese = chinese; English = english; NodeEn = nodeEn; GoalZh = goalZh; GoalEn = goalEn; BriefZh = briefZh; BriefEn = briefEn;
                Art = art; Item = item;
            }

            internal string Title => Text(Chinese, English);
            internal string NodeTitle => Text(Chinese, NodeEn);
            internal string Goal => Text(GoalZh, GoalEn);
            internal string Brief => Text(BriefZh, BriefEn);
        }

        internal static bool Chinese => MoonkovLocalization.CurrentLocaleCode?.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase) == true;
        internal static string Text(string zh, string en) => Chinese ? zh : en;

        private static readonly Entry[] Entries =
        {
            new Entry("月面适应", "Lunar Exposure", "Exposure", "在同一局中存活 5 分钟", "Survive for 5 minutes in one raid",
                "在研究站的月面环境中测试装备。存活满五分钟后，战局结算会记录测试结果。",
                "Test your equipment in the station's lunar environment. The raid records the result once you have survived for five minutes.",
                "PageArt/Base"),
            new Entry("光环实战测试", "Battle Halo Trial", "Halo Trial", "用战斗光环消灭 5 个异形", "Eliminate 5 specimens with a Battle Halo",
                "用战斗光环武器取得击杀。已确认的击杀随战局结算，可以分多局完成。",
                "Confirm kills using Battle Halo weaponry. Kills are recorded at settlement and can carry over between raids.",
                "PageArt/Technical"),
            new Entry("生物证据", "Biological Evidence", "Samples", "从 3 具异形遗骸采样并带回", "Extract with samples from 3 different specimens",
                "搜索异形遗骸取得封存样本。三份样本须在同一局成功带回。",
                "Dissect alien remains to obtain sealed samples. Bring all three back in the same successful extraction.",
                "OperationArt/OperatorMission"),
            new Entry("最后的记录", "Last Words", "Tablet", "找到研究员平板并成功撤离", "Find the personnel tablet and extract with it",
                "前往 MC00 的 L22，在隔离柜旁找回平板。平板装有研究员的最后几篇日志，在背包中占 1×2 格。",
                "Recover the tablet by the isolation cabinet in MC00 L22. It holds the crew's final entries and takes up 1×2 inventory cells.",
                item: ResearchQuestRules.Tablet),
            new Entry("静默之前", "Before the Silence", "Logs", "用配发 U 盘读取管理日志并带回", "Copy the administrator's logs and extract with the drive",
                "出战时配发 U 盘，请预留一个格子。在 MC00 的 L22 管理工作站写入日志，再带着原盘撤离。",
                "Leave a free cell for the drive issued on deployment. Copy the logs at the MC00 L22 administrator workstation and extract with that drive.",
                item: ResearchQuestRules.Flash),
            new Entry("表象之下", "Beneath the Surface", "Archive", "用配发硬盘拷贝研究数据库并交回", "Copy and return the research database",
                "出战时配发硬盘，请预留一个格子。在 MC00 的 L22 研究服务器拷贝资料。传输期间留在终端旁，完成后带回硬盘。",
                "Leave a free cell for the drive issued on deployment. Stay by the MC00 L22 research server until the transfer completes and bring the drive back.",
                item: ResearchQuestRules.Drive),
            new Entry("移动收纳", "Mobile Storage", "Halo Cargo", "把封存包装入光环并撤离", "Extract with the package inside a Battle Halo",
                "装备战斗光环，给出战时配发的封存包留一个格子。打开局内背包，把包拖进光环收纳格，再带回作业船。",
                "Equip a Battle Halo and leave a free cell for the issued package. In the raid inventory, move the package into the Halo cargo slot and extract.",
                "PageArt/Operations"),
            new Entry("自动防御", "Automated Protection", "Sentry", "用固定光环炮塔消灭 3 个异形", "Eliminate 3 specimens with a deployed Halo sentry",
                "把光环放在视线开阔的位置，切换到炮塔模式。击杀随战局结算，可以分多局完成。",
                "Anchor your Halo where it has a clear line of sight, then switch to sentry mode. Confirmed kills can carry over between raids.",
                "OperationArt/BriefingMission")
        };

        internal static Entry For(ResearchQuest quest) => Entries[(int)quest];
        internal static string State(ResearchQuestStatus status) => status switch
        {
            ResearchQuestStatus.Available => Text("可接取", "Available"),
            ResearchQuestStatus.Active => Text("进行中", "Active"),
            ResearchQuestStatus.Ready => Text("待交付", "Ready to turn in"),
            ResearchQuestStatus.Completed => Text("已交付", "Completed"),
            _ => Text("未解锁", "Locked")
        };

        internal static string ItemName(string code) => code switch
        {
            ResearchQuestRules.Tablet => Text("研究员信息平板", "Personnel information tablet"),
            ResearchQuestRules.Flash => Text("加密数据 U 盘", "Encrypted extraction drive"),
            ResearchQuestRules.Drive => Text("研究资料硬盘", "Research storage drive"),
            ResearchQuestRules.Sample => Text("封存生物样本", "Sealed biological sample"),
            ResearchQuestRules.Package => Text("封存研究包", "Sealed research package"),
            _ => null
        };
    }
}
