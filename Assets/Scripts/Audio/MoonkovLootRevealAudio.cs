// 搜刮 HUD 用它给逐件出现的战利品配音。记住已经见过的物品 ID，只给确认快照中新显示的顶层物品排队。
using System.Collections.Generic;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 以服务器确认快照中新揭示的物品为准，而且只数顶层物品。搜索计时不直接决定播放。
    public sealed class MoonkovLootRevealAudio {
        private readonly HashSet<string> m_Known = new();
        private int m_Raid, m_Loot, m_Pending;
        private uint m_Request;
        private bool m_Listening, m_Started;
        private float m_Next;

        public void Begin(int raid, uint request, int loot, InventoryGraph baseline) {
            // 切换战局后清除已见物品。打开新箱时先记录当时可见的物品，后来只用这份对照找新增。
            if (raid != m_Raid) Reset();
            Cancel();
            m_Raid = raid;
            m_Request = request;
            m_Loot = loot;
            m_Listening = loot >= 0;
            Remember(baseline);
        }

        public void Observe(RaidInventoryClientState state) {
            // 认准当前战局、当前箱子以及本次请求编号。旧快照和上一个打开的箱子都可能晚到，先确认属于同一次再数。
            if (state.Graph == null || state.RaidId != m_Raid || state.RequestId < m_Request) return;
            if (state.LootId != m_Loot) {
                Cancel();
                return;
            }
            bool searching = state.DissectingLootId == m_Loot;
            if (!searching && state.Error != InventoryError.None) {
                Cancel();
                return;
            }
            if (!m_Listening) return;
            // 已搜完的箱子会直接打开。漏收中间的进度快照时，
            // 还可能从服务端明确的搜索完成回执获知结果。
            // 开已经搜完的箱子可能直接拿到完整库存。要看到搜索进行过或服务端明确通知搜索完成，才把快照里的新物品算成逐件揭示。
            bool reveal = searching || m_Started || state.ActionFeedback == "搜索完成。";
            if (reveal)
                foreach (var item in state.Graph.Items)
                    if (item.Parent == LootInventoryExchange.Root && m_Known.Add(item.Id))
                        m_Pending++;
            Remember(state.Graph);
            m_Started |= searching;
            if (!searching) m_Listening = false;
        }

        private void Remember(InventoryGraph graph) {
            if (graph != null)
                foreach (var item in graph.Items)
                    m_Known.Add(item.Id);
        }

        public bool TryPlay(float now) {
            // 一份消息可能一次揭示好几件物品。先排队，再按间隔逐个发声，避免所有提示同时叠上来。
            if (m_Pending == 0 || now < m_Next) return false;
            m_Pending--;
            m_Next = now + 1.3f;
            return true;
        }

        public void Cancel() {
            m_Listening = m_Started = false;
            m_Pending = 0;
            m_Next = 0;
        }

        public void Reset() {
            Cancel();
            m_Known.Clear();
            m_Raid = 0;
        }
    }
}