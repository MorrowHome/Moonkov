using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static class RaidInventoryTransport
    {
        // Keep each ASCII JSON fragment comfortably below the transport's 1378-byte packet.
        public const int ChunkCharacters = 400;
        public const int MaxChunks = 2048;
        public const int MaxJsonCharacters = 500000;
        public static string Encode(InventoryGraph graph) => Newtonsoft.Json.JsonConvert.SerializeObject(graph,
            new Newtonsoft.Json.JsonSerializerSettings { StringEscapeHandling=Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii });
        public static InventoryGraph Decode(string json) => Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryGraph>(json);
    }
    public sealed class RaidInventoryState : IComponentData
    {
        public InventoryGraph Graph;
        public int RaidId;
        public int LastSentVersion = -1;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public static void UpdateTotals(InventoryGraph graph, ref RaidSession session)
        {
            session.Dust = graph.Count("dust", true); session.Alloy = graph.Count("alloy", true); session.Cells = graph.Count("cells", true);
            session.SnapshotTimer = 0;
        }
    }
    public struct RaidInventoryMoveRpc : IRpcCommand
    {
        public int RaidId, ExpectedVersion, X, Y, Quantity;
        public uint RequestId;
        public InventoryOperation Operation;
        public FixedString64Bytes ItemId, Parent, Region, TargetId;
        public bool Rotated;
    }
    // V2 has a 512-byte string. Keep a distinct identity from the old 4096-byte RPC:
    // generated/Burst RPC executors must not reuse the old component layout.
    public struct RaidInventoryChunkV2Rpc : IRpcCommand
    {
        public int RaidId, Index, Count;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public FixedString512Bytes Json;
    }
    // Partial transport is never exposed to UI. A newer sequence supersedes old fragments.
    public sealed class RaidInventoryClientState : IComponentData
    {
        public InventoryGraph Graph;
        public int RaidId;
        public uint Sequence, RequestId;
        public InventoryError Error;
        private uint m_IncomingSequence;
        private int m_IncomingRaid, m_Received;
        private string[] m_Chunks;
        public bool Receive(RaidInventoryChunkV2Rpc chunk, Func<string, InventoryGraph> decode)
        {
            if (chunk.Count < 1 || chunk.Count > RaidInventoryTransport.MaxChunks || chunk.Index < 0 || chunk.Index >= chunk.Count || chunk.Sequence <= Sequence) return false;
            if (m_Chunks == null || chunk.Sequence > m_IncomingSequence)
            { m_IncomingSequence = chunk.Sequence; m_IncomingRaid = chunk.RaidId; m_Chunks = new string[chunk.Count]; m_Received = 0; }
            if (chunk.Sequence != m_IncomingSequence || chunk.RaidId != m_IncomingRaid || chunk.Count != m_Chunks.Length) return false;
            if (m_Chunks[chunk.Index] == null) { m_Chunks[chunk.Index] = chunk.Json.ToString(); m_Received++; }
            if (m_Received != m_Chunks.Length) return false;
            string json = string.Concat(m_Chunks); m_Chunks = null;
            if (json.Length > RaidInventoryTransport.MaxJsonCharacters) return false;
            try
            {
                var graph = decode(json); if (graph == null || graph.Find("stash") != null || graph.Validate() != InventoryError.None) return false;
                Graph = graph; RaidId = chunk.RaidId; Sequence = chunk.Sequence; RequestId = chunk.RequestId; Error = chunk.Error; return true;
            }
            catch (Exception) { return false; }
        }
    }
}
