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
        public const int MaxJsonCharacters = 800000;
        public static string Encode(InventoryGraph graph) => Newtonsoft.Json.JsonConvert.SerializeObject(graph,
            new Newtonsoft.Json.JsonSerializerSettings { StringEscapeHandling=Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii });
        public static InventoryGraph Decode(string json) => Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryGraph>(json);
        public static string EncodeSnapshot(InventoryGraph graph,int lootId,int lootVersion) => Newtonsoft.Json.JsonConvert.SerializeObject(
            new RaidInventorySnapshot {Graph=graph,LootId=lootId,LootVersion=lootVersion},
            new Newtonsoft.Json.JsonSerializerSettings {StringEscapeHandling=Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii});
    }
    public sealed class RaidInventorySnapshot
    {
        public InventoryGraph Graph;
        public int LootId=-1, LootVersion;
    }
    public sealed class RaidLootContainers : IComponentData
    {
        public readonly System.Collections.Generic.Dictionary<int,InventoryGraph> Containers = new System.Collections.Generic.Dictionary<int,InventoryGraph>();
        public readonly System.Collections.Generic.Dictionary<int,RaidCorpseRecord> DeathBags = new System.Collections.Generic.Dictionary<int,RaidCorpseRecord>();
        public const int FirstDeathBagId=24;
        private int m_NextDeathBagId=FirstDeathBagId;
        public int Drop(InventoryGraph carried,Unity.Mathematics.float3 position,Unity.Mathematics.quaternion rotation=default,int characterIndex=2,double createdAt=0)
        {
            var graph=LootInventoryExchange.DropOnDeath(carried);
            int id=m_NextDeathBagId++;
            Containers.Add(id,graph);DeathBags.Add(id,new RaidCorpseRecord {Position=position,
                Rotation=Unity.Mathematics.math.lengthsq(rotation.value)>0 ? rotation : Unity.Mathematics.quaternion.identity,
                CharacterIndex=characterIndex,CreatedAt=createdAt});return id;
        }
        public void Initialize(int count)
        {for(int index=0;index<count;index++)if(!Containers.ContainsKey(index))Containers.Add(index,LootInventoryExchange.CreateCache(index));}
    }
    public sealed class RaidInventoryState : IComponentData
    {
        public InventoryGraph Graph;
        public int RaidId;
        public int LastSentVersion = -1;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public int OpenedLootId=-1, LastSentLootVersion=-1;
        public readonly System.Collections.Generic.Dictionary<int,int> SentDeathBags = new System.Collections.Generic.Dictionary<int,int>();
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
    public struct RaidMedicalUseRpc : IRpcCommand
    {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        public BodyPart Part;
    }
    public struct RaidLootOpenRpc : IRpcCommand
    {
        public int RaidId, LootId;
        public uint RequestId;
    }
    public sealed class RaidCorpseRecord
    {
        public Unity.Mathematics.float3 Position;
        public Unity.Mathematics.quaternion Rotation;
        public int CharacterIndex;
        public double CreatedAt;
    }
    // Separate identity from the earlier bag marker RPC: generated/Burst layouts
    // must never reuse the smaller component after adding corpse presentation data.
    public struct RaidCorpseRpc : IRpcCommand
    {
        public int LootId, Version, CharacterIndex;
        public Unity.Mathematics.float3 Position;
        public Unity.Mathematics.quaternion Rotation;
        public float Age;
        public bool Empty;
    }
    public sealed class RaidDeathBagClientState : IComponentData
    {
        public readonly System.Collections.Generic.Dictionary<int,RaidCorpseRpc> Bags = new System.Collections.Generic.Dictionary<int,RaidCorpseRpc>();
        public void Receive(RaidCorpseRpc bag)
        {
            if(bag.LootId<RaidLootContainers.FirstDeathBagId || bag.Version<1 || bag.CharacterIndex<0 || bag.CharacterIndex>2 ||
                !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(bag.Position)) || !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(bag.Rotation.value)) ||
                Unity.Mathematics.math.abs(Unity.Mathematics.math.lengthsq(bag.Rotation.value)-1)>.01f || !float.IsFinite(bag.Age) || bag.Age<0)return;
            if(!Bags.TryGetValue(bag.LootId,out var old) || bag.Version>old.Version)Bags[bag.LootId]=bag;
        }
    }
    public struct RaidLootMoveRpc : IRpcCommand
    {
        public int RaidId, LootId, ExpectedLootVersion, ExpectedVersion, X, Y, Quantity;
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
        public int LootId=-1, LootVersion;
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
                var snapshot=Newtonsoft.Json.JsonConvert.DeserializeObject<RaidInventorySnapshot>(json);
                var graph=snapshot?.Graph ?? decode(json);
                if (graph == null || graph.Find("stash") != null || graph.Validate() != InventoryError.None) return false;
                if(snapshot?.Graph!=null && (snapshot.LootId>=0)!=(graph.Find(LootInventoryExchange.Root)!=null)) return false;
                LootId=snapshot?.Graph!=null ? snapshot.LootId : -1; LootVersion=snapshot?.Graph!=null ? snapshot.LootVersion : 0;
                Graph = graph; RaidId = chunk.RaidId; Sequence = chunk.Sequence; RequestId = chunk.RequestId; Error = chunk.Error; return true;
            }
            catch (Exception) { return false; }
        }
    }
}
