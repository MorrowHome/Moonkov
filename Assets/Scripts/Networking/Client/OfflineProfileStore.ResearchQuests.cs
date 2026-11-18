// 把 MOONRAT 的研究任务进度锁在同一份单人档案事务里。仓库接取、交付和选择结局从这里入档，局内证据在结算时由这里合入。
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class OfflineProfileStore {
        // 接取和交付修改同一份进度。活动出战、加载和极速赏金进行中都要等，这样局内任务与局外仓库不会同时改这份档。
        public bool AdvanceResearchQuest(ResearchQuest quest, bool claim) {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0 || m_Save.Clash?.ActiveMatchId != null) return false;
                var next = Copy();
                bool changed = claim
                    ? ResearchQuestRules.Claim(next.ResearchQuests, quest)
                    : ResearchQuestRules.Accept(next.ResearchQuests, quest);
                if (!changed) return false;
                Commit(next);
                return true;
            }
        }

        public bool ChooseResearchEnding(ResearchEnding ending) {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0 || m_Save.Clash?.ActiveMatchId != null) return false;
                var next = Copy();
                if (!ResearchQuestRules.ChooseEnding(next.ResearchQuests, ending)) return false;
                // 结局进度和公司的实物奖励要能一起保存。奖励放不下时这次选择仍没写入，可整理后再选。
                if (ending == ResearchEnding.Corporate && !ResearchQuestRules.GrantCorporateReward(next.Inventory))
                    return false;
                Commit(next);
                return true;
            }
        }

        public bool ApplyResearchCommand(ResearchQuestCommand command) {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0 || m_Save.Clash?.ActiveMatchId != null) return false;
                var next = Copy();
                if (!ResearchQuestRules.ApplyCommand(next.ResearchQuests, command)) return false;
                // 服务器式研究命令允许已完成的同一动作重试。进度版本没动代表没有新增写入需要提交。
                if (next.ResearchQuests.Revision == m_Save.ResearchQuests.Revision) return true;
                if (next.ResearchQuests.Ending == ResearchEnding.Corporate &&
                    m_Save.ResearchQuests.Ending == ResearchEnding.None &&
                    !ResearchQuestRules.GrantCorporateReward(next.Inventory))
                    return false;
                Commit(next);
                return true;
            }
        }

        private void ApplyResearchSettlement(
            SaveData next,
            RaidPersistenceContext.Settlement payload,
            InventoryGraph raid,
            bool extracted) {
            // 局内没上报研究数据，或加载尚未完成时跳过。剩余情况根据是否撤离合入对应证据。
            if (payload.ResearchQuests == null || m_LoadingCheckpoints != 0) return;
            ResearchQuestRules.Record(next.ResearchQuests, payload.ResearchQuests,
                RaidInventoryTransport.Decode(next.RaidInventoryJson), raid, extracted);
        }
    }
}