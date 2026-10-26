#nullable disable
using System;
using System.Collections.Generic;

namespace Unity.MP_FPS.Campaign
{
    // Semantic anchors survive a building replacement. The scene owns the coordinates.
    // ExistingObjectiveId observes MissionCatalog.Mooncore; this does not duplicate its device rules.
    public static class CampaignCatalog
    {
        public const int Version = 1;
        public static readonly TaskDefinition[] Tasks = Build();

        public static TaskDefinition Find(string id) => Array.Find(Tasks, t => t.Id == id);

        private static NodeDefinition Node(string id, string anchor, string action, string location,
            CampaignObjectiveKind kind, float seconds = 0, string objective = null, CampaignSavePolicy save = CampaignSavePolicy.RunOnly) =>
            new NodeDefinition { Id = id, AnchorId = anchor, Action = action, Location = location, Kind = kind,
                Seconds = seconds, ExistingObjectiveId = objective, SavePolicy = save };

        private static NodeDefinition Extract() => Node("extract", "ship.return", "返回撤离点，带回后保存", "已知撤离点",
            CampaignObjectiveKind.Extract, save: CampaignSavePolicy.OnExtraction);

        private static TaskDefinition[] Build()
        {
            var tasks = new[]
            {
                new TaskDefinition
                {
                    Id = "CM01", Title = "报到", Chapter = 1, ChapterTitle = "静默着陆",
                    Briefing = "Theophilus 研究站失联。你受雇调查事故、回收样本。沈禾留在作业船保障舱，顾衡通过通信指引。先找到 MC00 入口，登记清理队身份，并记住返回撤离点的路线。",
                    CompletionText = "沈禾：入口和回程路线已记录。船这边正常，接下来检查备用供电。",
                    Nodes = new[] {
                        Node("arrive", "entry", "前往 MC00 入口", "MC00 入口", CampaignObjectiveKind.ReachArea, save: CampaignSavePolicy.OnExtraction),
                        Node("report", "entry", "登记清理队身份", "MC00 入口终端", CampaignObjectiveKind.Interact, 2, "report", CampaignSavePolicy.OnExtraction),
                        Extract()
                    }
                },
                new TaskDefinition
                {
                    Id = "CM02", Title = "备用线路", Chapter = 1, ChapterTitle = "静默着陆", PrerequisiteTask = "CM01",
                    Briefing = "取样设备缺少备用供电。配电 A 的烧损支路需要隔离，随后恢复配电 B。污染体仍穿着研究站工作服，它们曾经是这里的人员；光环是公司配发的专用武器。电池与医疗用品要为回程留出余量。",
                    CompletionText = "沈禾：备用线路接通。A 柜记录 18:42 热损，B 柜在 18:47 带过电。先保留这两个时间，等取得原始记录再判断。",
                    Nodes = new[] {
                        Node("power_a", "power_a", "隔离烧损支路，保留备用母线", "MC00 配电 A", CampaignObjectiveKind.Configure, 1, "power_a"),
                        Node("power_b", "power_b", "恢复维修配电 B", "MC00 配电 B", CampaignObjectiveKind.Interact, 4, "power_b"),
                        Extract()
                    }
                },
                new TaskDefinition
                {
                    Id = "CM03", Title = "第一份样本", Chapter = 1, ChapterTitle = "静默着陆", PrerequisiteTask = "CM02",
                    Briefing = "两路备用供电已就绪。启动取样机后，切割会自动运行 45 秒。设备声可能引来附近敌人，留意返回路线。输出槽里的月核碎片要真实装入背包，成功撤离才算回收；背包不足时可以先整理，样本不会凭提示自动入库。",
                    CompletionText = "顾衡：样本保持封存，等待后续分析。沈禾：接收结果显示，实验场信号仍在维持。事故已经过去，为什么它还在运行？第一章调查记录已保存。",
                    Nodes = new[] {
                        Node("start", "sampler", "启动月核切割", "MC00 取样机", CampaignObjectiveKind.Interact, 3, "start"),
                        Node("cutting", "sampler", "等待切割完成，留意返回路线", "MC00 取样机", CampaignObjectiveKind.Process, 45, "cutting"),
                        Node("recover", "sampler", "从输出槽取得月核碎片", "MC00 取样机输出槽", CampaignObjectiveKind.RecoverItem, save: CampaignSavePolicy.OnExtraction),
                        Extract()
                    }
                }
            };
            tasks[0].Nodes[0].Briefing = "沈禾：船这边正常。我守着回程通道，你先确认里面能不能走。";
            tasks[0].Nodes[1].Briefing = "顾衡：先恢复本地终端。清理权限只覆盖设施检查和样本回收，人员情况也请一并核实。";
            tasks[1].Nodes[0].Briefing = "烧损支路不能直接合上。核对线路说明，保留备用母线。";
            tasks[1].Nodes[1].Briefing = "供电后的导引灯可以帮助辨认回程。恢复设备不要求清空整栋建筑。";
            tasks[2].Nodes[0].Briefing = "顾衡：启动切割后，设备声可能引来附近敌人。样本保持屏蔽封存。";
            tasks[2].Nodes[1].Briefing = "机器继续运行时可以移动、观察或撤退。不要为了守在设备旁耗尽医疗和电池。";
            tasks[2].Nodes[2].RequiredItemCode = "mooncore_fragment";
            tasks[2].Nodes[2].OutputItemCode = "mooncore_fragment";
            tasks[2].Nodes[2].OutputQuantity = 2;
            tasks[2].Nodes[2].Briefing = "至少携带 1 枚合法来源的月核碎片撤离。取样完成、取得物品与成功入库是不同的结果。";
            foreach (var task in tasks)
            {
                for (int i = 0; i < task.Nodes.Length; i++)
                {
                    var node = task.Nodes[i];
                    node.SourceId = "campaign." + task.Id + "." + node.Id;
                    node.Prerequisites = i == 0 ? 0 : (1 << i) - 1;
                }
            }
            return tasks;
        }

        public static string Validate()
        {
            if (Tasks == null || Tasks.Length != 3) return "campaign_catalog";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in Tasks)
            {
                if (task == null || !ids.Add(task.Id) || task.Chapter != 1 || task.Optional || string.IsNullOrWhiteSpace(task.Title) ||
                    string.IsNullOrWhiteSpace(task.Briefing) || task.Nodes == null || task.Nodes.Length < 1 || task.Nodes.Length > 8 ||
                    task.PrerequisiteTask != null && !ids.Contains(task.PrerequisiteTask)) return "campaign_task";
                var nodes = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < task.Nodes.Length; i++)
                {
                    var node = task.Nodes[i];
                    if (node == null || !nodes.Add(node.Id) || string.IsNullOrWhiteSpace(node.AnchorId) ||
                        node.SourceId != "campaign." + task.Id + "." + node.Id || string.IsNullOrWhiteSpace(node.Action) ||
                        node.Prerequisites != (i == 0 ? 0 : (1 << i) - 1) || float.IsNaN(node.Seconds) || float.IsInfinity(node.Seconds) ||
                        node.Seconds < 0 || node.Seconds > 600 || node.OutputQuantity < 1 || node.RequiredQuantity < 1)
                        return "campaign_node";
                }
                if (task.Nodes[task.Nodes.Length - 1].Kind != CampaignObjectiveKind.Extract) return "campaign_return";
            }
            return null;
        }
    }
}
