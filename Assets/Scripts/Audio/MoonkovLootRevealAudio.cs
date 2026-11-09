using System.Collections.Generic;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // Count newly visible top-level items in accepted snapshots, never search timer ticks.
    public sealed class MoonkovLootRevealAudio
    {
        private readonly HashSet<string> m_Known = new HashSet<string>();
        private int m_Raid, m_Loot, m_Pending;
        private uint m_Request;
        private bool m_Listening, m_Started;
        private float m_Next;

        public void Begin(int raid, uint request, int loot, InventoryGraph baseline)
        {
            if (raid != m_Raid) Reset();
            Cancel(); m_Raid = raid; m_Request = request; m_Loot = loot; m_Listening = loot >= 0;
            Remember(baseline);
        }
        public void Observe(RaidInventoryClientState state)
        {
            if (state.Graph == null || state.RaidId != m_Raid || state.RequestId < m_Request) return;
            if (state.LootId != m_Loot) { Cancel(); return; }
            bool searching = state.DissectingLootId == m_Loot;
            if (!searching && state.Error != InventoryError.None) { Cancel(); return; }
            if (!m_Listening) return;
            // A fully searched box opens directly. A missed progress snapshot can instead
            // arrive as the server's explicit search-complete receipt.
            bool reveal = searching || m_Started || state.ActionFeedback == "搜索完成。";
            if (reveal)
                foreach (var item in state.Graph.Items)
                    if (item.Parent == LootInventoryExchange.Root && m_Known.Add(item.Id)) m_Pending++;
            Remember(state.Graph);
            m_Started |= searching;
            if (!searching) m_Listening = false;
        }
        private void Remember(InventoryGraph graph)
        {
            if (graph != null) foreach (var item in graph.Items) m_Known.Add(item.Id);
        }
        public bool TryPlay(float now)
        {
            if (m_Pending == 0 || now < m_Next) return false;
            m_Pending--; m_Next = now + 1.3f; return true;
        }
        public void Cancel() { m_Listening = m_Started = false; m_Pending = 0; m_Next = 0; }
        public void Reset() { Cancel(); m_Known.Clear(); m_Raid = 0; }
    }
}
