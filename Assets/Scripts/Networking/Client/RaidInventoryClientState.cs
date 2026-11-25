using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed class RaidDeathBagClientState : IComponentData {
        public readonly Dictionary<int, RaidCorpseRpc> Bags = new();
        public readonly Dictionary<int, string> ItemCodes = new();
        private readonly Dictionary<int, uint> m_Sequences = new();

        // 每个物品 ID 的序号只保留最近一段。早于这段窗口的更新
        // 统一拒绝，已经移走的遗体包也一样。
        private const uint SequenceWindow = 4096;
        private uint m_HighestSequence;
        private readonly List<int> m_ExpiredSequences = new();

        // 地面标记可能晚到，因此以每个 ID 上次接受的序号判断。删除与隐藏分开，缓存的旧包不能重建已移除物体。
        public void Receive(RaidGroundItemMarkerRpc update) {
            uint floor = m_HighestSequence > SequenceWindow ? m_HighestSequence - SequenceWindow : 0;
            if (update.Sequence <= floor) return;
            if (update.LootId < RaidLootContainers.FirstDeathBagId || update.Sequence == 0 ||
                (m_Sequences.TryGetValue(update.LootId, out uint sequence) && update.Sequence <= sequence))
                return;
            m_HighestSequence = Math.Max(m_HighestSequence, update.Sequence);
            m_Sequences[update.LootId] = update.Sequence;
            // 只保留有限的近期序号。更早的归入一个总下限，释放字典空间也还能拒绝过时消息。
            if (m_Sequences.Count > SequenceWindow) {
                floor = m_HighestSequence - SequenceWindow;
                m_ExpiredSequences.Clear();
                foreach (var pair in m_Sequences)
                    if (pair.Value <= floor)
                        m_ExpiredSequences.Add(pair.Key);
                foreach (int id in m_ExpiredSequences) m_Sequences.Remove(id);
            }
            if (update.Removed) {
                Bags.Remove(update.LootId);
                ItemCodes.Remove(update.LootId);
                return;
            }
            Receive(new RaidCorpseRpc {
                LootId = update.LootId, Version = update.Version, CharacterIndex = update.CharacterIndex,
                Position = update.Position, Rotation = update.Rotation, Age = update.Age, Empty = update.Empty
            });
            if (Bags.TryGetValue(update.LootId, out var accepted) && accepted.Version == update.Version)
                ItemCodes[update.LootId] = update.ItemCode.ToString();
        }

        public void Receive(RaidCorpseRpc bag) {
            if (bag.LootId < RaidLootContainers.FirstDeathBagId || bag.Version < 1 ||
                bag.CharacterIndex < RaidLootContainers.DroppedItemCharacterIndex ||
                (bag.CharacterIndex > 1 && !GameSettings.IsPlayableCharacter(bag.CharacterIndex)) ||
                !Mathematics.math.all(Mathematics.math.isfinite(bag.Position)) ||
                !Mathematics.math.all(Mathematics.math.isfinite(bag.Rotation.value)) ||
                Mathematics.math.abs(Mathematics.math.lengthsq(bag.Rotation.value) - 1) > .01f ||
                !float.IsFinite(bag.Age) || bag.Age < 0)
                return;
            if (!Bags.TryGetValue(bag.LootId, out var old) || bag.Version > old.Version) Bags[bag.LootId] = bag;
        }
    }

    public sealed class RaidInventoryClientState : IComponentData {
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int RaidId;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public int LootId = -1, LootVersion;
        public int CorpseCharacterIndex = -1;
        public Dictionary<string, string> CorpseEquipmentSlots;
        public int DissectingLootId = -1;
        public float DissectionRemaining;
        public int SearchTotal = -1;
        public List<RaidSearchFootprint> SearchFootprints;
        public float SearchItemSeconds;
        public string MedicalItemId;
        public float MedicalRemaining;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
        private uint m_IncomingSequence;
        private int m_IncomingRaid, m_Received;
        private string[] m_Chunks;

        // 同一个 RaidId 和快照序号的分片收齐才解码，更新背包、搜刮进度和请求结果。迟到的旧分片不能盖过新背包。
        public bool Receive(RaidInventoryChunkV2Rpc chunk, Func<string, InventoryGraph> decode) {
            if (chunk.Count < 1 || chunk.Count > RaidInventoryTransport.MaxChunks || chunk.Index < 0 ||
                chunk.Index >= chunk.Count || chunk.Sequence <= Sequence)
                return false;
            // 更新一批分片开始到达时，放弃上一批未齐的内容。之后每块必须与这一批的局号、序号和总块数都匹配。
            if (m_Chunks == null || chunk.Sequence > m_IncomingSequence) {
                m_IncomingSequence = chunk.Sequence;
                m_IncomingRaid = chunk.RaidId;
                m_Chunks = new string[chunk.Count];
                m_Received = 0;
            }
            if (chunk.Sequence != m_IncomingSequence || chunk.RaidId != m_IncomingRaid ||
                chunk.Count != m_Chunks.Length)
                return false;
            // 同一个索引重复送只算一次。必须所有不同索引都齐全才拼整份 JSON。
            if (m_Chunks[chunk.Index] == null) {
                m_Chunks[chunk.Index] = chunk.Json.ToString();
                m_Received++;
            }
            if (m_Received != m_Chunks.Length) return false;
            string json = string.Concat(m_Chunks);
            m_Chunks = null;
            if (json.Length > RaidInventoryTransport.MaxJsonCharacters) return false;
            try {
                var snapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<RaidInventorySnapshot>(json);
                // 既支持当前带读条元数据的快照，也能读旧格式纯物品图。版本和箱子根关系必须正确才让 HUD 更新。
                var graph = snapshot?.Graph ?? decode(json);
                if (graph == null || graph.Find("stash") != null || graph.Validate() != InventoryError.None)
                    return false;
                if (snapshot?.Graph != null && snapshot.LootId >= 0 != (graph.Find(LootInventoryExchange.Root) != null))
                    return false;
                LootId = snapshot?.Graph != null ? snapshot.LootId : -1;
                LootVersion = snapshot?.Graph != null ? snapshot.LootVersion : 0;
                CorpseCharacterIndex = snapshot?.CorpseCharacterIndex ?? -1;
                CorpseEquipmentSlots = snapshot?.CorpseEquipmentSlots;
                DissectingLootId = snapshot?.DissectingLootId ?? -1;
                DissectionRemaining = snapshot?.DissectionRemaining ?? 0;
                SearchTotal = snapshot?.SearchTotal ?? -1;
                SearchFootprints = snapshot?.SearchFootprints;
                SearchItemSeconds = snapshot?.SearchItemSeconds ?? 0;
                MedicalItemId = snapshot?.MedicalItemId;
                MedicalRemaining = snapshot?.MedicalRemaining ?? 0;
                MedicalIsConsumable = snapshot?.MedicalIsConsumable ?? false;
                ActionFeedback = snapshot?.ActionFeedback;
                Result = snapshot?.Result != null && snapshot.Result.Valid(chunk.RaidId) ? snapshot.Result : null;
                Graph = graph;
                RaidId = chunk.RaidId;
                Sequence = chunk.Sequence;
                RequestId = chunk.RequestId;
                Error = chunk.Error;
                return true;
            } catch (Exception) { return false; }
        }
    }
}
