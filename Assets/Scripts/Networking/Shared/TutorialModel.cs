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
            "Theophilus Research Station has gone silent. Inspect the facility, check on the crew, and bring back a sealed sample.",
            "Shen He on comms. I'll stay aboard to keep communications open and get you home. Check your gear first.",
            "Gu Heng on comms. Register at the MC00 entrance, then check the backup power and the sampler. Keep the sample sealed and bring it back to the ship."
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
