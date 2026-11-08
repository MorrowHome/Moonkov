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

        private void ApplyResearchSettlement(SaveData next, RaidPersistenceContext.Settlement payload, InventoryGraph raid, bool extracted)
        {
            if (payload.ResearchQuests == null || m_LoadingCheckpoints != 0) return;
            ResearchQuestRules.Record(next.ResearchQuests, payload.ResearchQuests,
                RaidInventoryTransport.Decode(next.RaidInventoryJson), raid, extracted);
        }
    }
}
