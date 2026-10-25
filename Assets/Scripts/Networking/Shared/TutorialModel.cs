using System;
using System.Linq;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Campaign
{
    public enum TutorialStatus { Available, Active, Skipped, Completed }

    [Serializable]
    public sealed class TutorialProgress
    {
        public int Version = 1;
        public TutorialStatus Status;
        public int IntroPage;
        public int CompletedSteps;
        public int SupplyCycle;
    }

    // Presentation progress never grants chapter facts or inventory rewards.
    public static class TutorialRules
    {
        public const int Equipped = 1, Cells = 2, Medicine = 4, Moved = 8, Looked = 16, Aimed = 32, Fired = 64;
        public const int AllSteps = 127;
        public static readonly string[] Introduction = {
            "Theophilus 研究站失联。你是受雇前来的清理员，负责检查设施、核实人员情况，并带回封存样本。",
            "沈禾：我留在作业船，保障通信和回程。先检查装备。进入研究站以后，一步一步来。",
            "顾衡：先在 MC00 入口登记，再检查备用供电和取样装置。样本取出后保持封存，带回作业船。"
        };
        public static string Validate(TutorialProgress state) => state == null || state.Version != 1 ||
            !Enum.IsDefined(typeof(TutorialStatus), state.Status) || state.IntroPage < 0 || state.IntroPage > Introduction.Length ||
            (state.CompletedSteps & ~AllSteps) != 0 || state.SupplyCycle < 0 ? "tutorial_invalid" : null;
        public static int Preparation(InventoryGraph graph)
        {
            if (graph == null) return 0;
            int steps = graph.Items.Any(i => i.Parent == "equipment" && IsWeapon(i.Code)) ? Equipped : 0;
            if (graph.CellEnergy(accessibleOnly: true) > 0) steps |= Cells;
            if (graph.Items.Any(i => i.Code == "medkit" && graph.MedicalAccessible(i))) steps |= Medicine;
            return steps;
        }
        public static bool IsWeapon(string code)
        {
            var kind = InventoryCatalog.Get(code)?.Kind;
            return kind == ItemKind.LongGun || kind == ItemKind.Pistol;
        }
    }
}
