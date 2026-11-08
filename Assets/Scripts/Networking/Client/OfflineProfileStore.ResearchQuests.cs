using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class OfflineProfileStore
    {
        public bool AdvanceResearchQuest(ResearchQuest quest, bool claim)
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0 || m_Save.Clash?.ActiveMatchId != null) return false;
                var next = Copy();
                bool changed = claim ? ResearchQuestRules.Claim(next.ResearchQuests, quest) : ResearchQuestRules.Accept(next.ResearchQuests, quest);
                if (!changed) return false;
                Commit(next);
                return true;
            }
        }

        public bool ChooseResearchEnding(ResearchEnding ending)
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0 || m_Save.Clash?.ActiveMatchId != null) return false;
                var next = Copy();
                if (!ResearchQuestRules.ChooseEnding(next.ResearchQuests, ending)) return false;
                if (ending == ResearchEnding.Corporate && !DepositCorporateReward(next.Inventory)) return false;
                Commit(next);
                return true;
            }
        }

        private static bool DepositCorporateReward(InventoryGraph inventory)
        {
            string weapon = Guid.NewGuid().ToString("D"), module = Guid.NewGuid().ToString("D");
            if (!DepositResearchReward(inventory, "dust", ResearchQuestRules.CorporateDust) ||
                !DepositResearchReward(inventory, ResearchQuestRules.CorporateHalo, 1, weapon) ||
                !DepositResearchReward(inventory, ResearchQuestRules.CorporateModule, 1, module)) return false;
            return inventory.TryApply(new InventoryCommand { ExpectedVersion = inventory.Version, Operation = InventoryOperation.Move,
                ItemId = module, Parent = weapon, Region = "Storage" }) == InventoryError.None;
        }

        private static bool DepositResearchReward(InventoryGraph inventory, string code, int quantity, string id = null)
        {
            InventoryError error;
            while ((error = inventory.AddSupply(code, quantity, InventoryCatalog.Stash, id: id)) == InventoryError.Full && inventory.StashRows < 4096)
                inventory.StashRows = Math.Min(4096, inventory.StashRows * 2);
            return error == InventoryError.None;
        }

        private void ApplyResearchSettlement(SaveData next, RaidPersistenceContext.Settlement payload, InventoryGraph raid, bool extracted)
        {
            if (payload.ResearchQuests == null || m_LoadingCheckpoints != 0) return;
            ResearchQuestRules.Record(next.ResearchQuests, payload.ResearchQuests,
                RaidInventoryTransport.Decode(next.RaidInventoryJson), raid, extracted);
        }
    }
}
