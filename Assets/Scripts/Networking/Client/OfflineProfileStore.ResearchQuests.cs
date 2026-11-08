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
                if (ending == ResearchEnding.Corporate && !ResearchQuestRules.GrantCorporateReward(next.Inventory)) return false;
                Commit(next);
                return true;
            }
        }

        public bool ApplyResearchCommand(ResearchQuestCommand command)
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0 || m_Save.Clash?.ActiveMatchId != null) return false;
                var next = Copy();
                if (!ResearchQuestRules.ApplyCommand(next.ResearchQuests, command)) return false;
                if (next.ResearchQuests.Revision == m_Save.ResearchQuests.Revision) return true;
                if (next.ResearchQuests.Ending == ResearchEnding.Corporate && m_Save.ResearchQuests.Ending == ResearchEnding.None &&
                    !ResearchQuestRules.GrantCorporateReward(next.Inventory)) return false;
                Commit(next);
                return true;
            }
        }

        private void ApplyResearchSettlement(SaveData next, RaidPersistenceContext.Settlement payload, InventoryGraph raid, bool extracted)
        {
            if (payload.ResearchQuests == null || m_LoadingCheckpoints != 0) return;
            ResearchQuestRules.Record(next.ResearchQuests, payload.ResearchQuests,
                RaidInventoryTransport.Decode(next.RaidInventoryJson), raid, extracted);
        }
    }
}
