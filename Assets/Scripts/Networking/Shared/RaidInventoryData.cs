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
        public static string EncodeSnapshot(InventoryGraph graph,int lootId,int lootVersion,int dissectingLootId=-1,float dissectionRemaining=0,string medicalItemId=null,float medicalRemaining=0,string actionFeedback=null) => Newtonsoft.Json.JsonConvert.SerializeObject(
            new RaidInventorySnapshot {Graph=graph,LootId=lootId,LootVersion=lootVersion,
                DissectingLootId=dissectingLootId,DissectionRemaining=dissectionRemaining,MedicalItemId=medicalItemId,MedicalRemaining=medicalRemaining,ActionFeedback=actionFeedback},
            new Newtonsoft.Json.JsonSerializerSettings {StringEscapeHandling=Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii});
    }
    public sealed class RaidInventorySnapshot
    {
        public InventoryGraph Graph;
        public int LootId=-1, LootVersion;
        public int DissectingLootId=-1;
        public float DissectionRemaining;
        public string MedicalItemId;
        public float MedicalRemaining;
        public string ActionFeedback;
    }
    public sealed class RaidLootContainers : IComponentData
    {
        public readonly System.Collections.Generic.Dictionary<int,InventoryGraph> Containers = new System.Collections.Generic.Dictionary<int,InventoryGraph>();
        public readonly System.Collections.Generic.Dictionary<int,RaidCorpseRecord> DeathBags = new System.Collections.Generic.Dictionary<int,RaidCorpseRecord>();
        public readonly System.Collections.Generic.Dictionary<int,RaidLootRefillState> RefillStates = new System.Collections.Generic.Dictionary<int,RaidLootRefillState>();
        public readonly System.Collections.Generic.Dictionary<int,Unity.Mathematics.float3> MissionCaches = new System.Collections.Generic.Dictionary<int,Unity.Mathematics.float3>();
        public readonly Random LootRandom = new Random(Guid.NewGuid().GetHashCode());
        public bool Initialized { get; private set; }
        public object MapIdentity { get; private set; }
        public double NextRefillCheck;
        public const int FirstDeathBagId=24;
        public const int DroppedItemCharacterIndex=-2;
        public const int FirstMissionCacheId = 1000000000;
        private int m_NextDeathBagId=FirstDeathBagId;
        // A fresh alien bag starts at version 1. Dissection advances it before
        // any inventory transfer is allowed; human corpses retain their old flow.
        public bool RequiresDissection(int id) => DeathBags.TryGetValue(id,out var corpse) && corpse.CharacterIndex==-1 &&
            Containers.TryGetValue(id,out var graph) && graph.Version==1;
        public int Drop(InventoryGraph carried,Unity.Mathematics.float3 position,Unity.Mathematics.quaternion rotation=default,int characterIndex=2,double createdAt=0)
        {
            var graph=LootInventoryExchange.DropOnDeath(carried);
            return RegisterDrop(graph,position,rotation,characterIndex,createdAt);
        }
        public int DropHarvest(InventoryGraph cache,Unity.Mathematics.float3 position,Unity.Mathematics.quaternion rotation=default,double createdAt=0)
        {
            if(cache==null || cache.Validate()!=InventoryError.None || cache.Find(LootInventoryExchange.Root)==null || cache.Find("stash")!=null ||
                cache.Items.Exists(i=>cache.Carried(i) && InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root))
                throw new ArgumentException("Alien harvest must be a validated loot container.");
            var graph=cache.Clone();graph.Version=1;
            return RegisterDrop(graph,position,rotation,-1,createdAt);
        }
        public InventoryError DropItem(InventoryGraph carried,string itemId,int expectedVersion,
            Unity.Mathematics.float3 position,double now,out int id)
        {
            id=-1;
            if(!Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(position)) || !double.IsFinite(now) || now<0)return InventoryError.Invalid;
            var error=LootInventoryExchange.PrepareDrop(carried,itemId,expectedVersion,out var remaining,out var dropped);
            if(error!=InventoryError.None)return error;
            // All fallible validation precedes the two-owner commit; no item is removed on failure.
            try {id=RegisterDrop(dropped,position,Unity.Mathematics.quaternion.identity,DroppedItemCharacterIndex,now);}
            catch(ArgumentException) {id=-1;return InventoryError.Invalid;}
            carried.Items=remaining.Items;carried.Version=remaining.Version;
            return InventoryError.None;
        }
        private int RegisterDrop(InventoryGraph graph,Unity.Mathematics.float3 position,Unity.Mathematics.quaternion rotation,int characterIndex,double createdAt)
        {
            int id=m_NextDeathBagId++;
            Containers.Add(id,graph);
            try { DeathBags.Add(id,new RaidCorpseRecord {Position=position,
                Rotation=Unity.Mathematics.math.lengthsq(rotation.value)>0 ? rotation : Unity.Mathematics.quaternion.identity,
                CharacterIndex=characterIndex,CreatedAt=createdAt}); }
            catch { Containers.Remove(id);throw; }
            return id;
        }
        public void Initialize(int count, Func<int,RaidLootProfile> profileFor = null, object mapIdentity = null)
        {
            if(Initialized && ReferenceEquals(MapIdentity,mapIdentity))return;
            if(count<1 || count>FirstDeathBagId)throw new ArgumentOutOfRangeException(nameof(count));
            int version=1;
            if(Initialized)
            {
                // Queued transfer requests from the previous map must never match its replacement.
                foreach(var cache in Containers.Values)version=Math.Max(version,checked(cache.Version+1));
                Containers.Clear();DeathBags.Clear();RefillStates.Clear();MissionCaches.Clear();
            }
            for(int index=0;index<count;index++)
            {
                var profile=profileFor!=null ? profileFor(index) : (RaidLootProfile)(index%4);
                if(!Containers.ContainsKey(index))
                {
                    var graph=RaidLootTable.Create(profile,LootRandom);graph.Version=version;
                    Containers.Add(index,graph);
                }
                RefillStates.Add(index,new RaidLootRefillState(profile));
            }
            Initialized=true;MapIdentity=mapIdentity;NextRefillCheck=0;
        }
    }
    public sealed class RaidInventoryState : IComponentData
    {
        public InventoryGraph Graph;
        public int RaidId;
        public int MissionFirstsBefore, MissionFirstsSaved;
        public int LastSentVersion = -1;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public int OpenedLootId=-1, LastSentLootVersion=-1;
        public int DissectingLootId=-1;
        public readonly System.Collections.Generic.HashSet<int> SearchedLoot = new System.Collections.Generic.HashSet<int>();
        public string MedicalItemId;
        public string ActionFeedback;
        public int MedicalVersion;
        public BodyPart MedicalPart;
        public double MedicalUntil;
        public Unity.Mathematics.float3 MedicalOrigin;
        public uint MedicalDamageTick,MedicalShotTick,MedicalGrenadeTick,MedicalReloadTick,MedicalWeapon;
        public bool NeedsSearch(RaidLootContainers caches,int id) => !SearchedLoot.Contains(id) || caches.RequiresDissection(id);
        public bool ActionPending => DissectingLootId>=0 || MedicalItemId!=null;
        public void CancelActions(string feedback=null)
        {
            DissectingLootId=-1;MedicalItemId=null;ActionFeedback=feedback;LastSentVersion=-1;
        }
        public InventoryError BeginMedical(in PredictedPlayerGhost health,string id,int version,BodyPart part,
            Unity.Mathematics.float3 origin,double now,float seconds=3)
        {
            if(ActionPending)return InventoryError.Inaccessible;
            // Validate on copies: neither item consumption nor healing occurs before the deadline.
            var probe=health;var error=RaidHealth.UseMedical(ref probe,Graph.Clone(),id,version,part);
            if(error!=InventoryError.None)return error;
            ActionFeedback=null;MedicalItemId=id;MedicalVersion=version;MedicalPart=part==BodyPart.Auto ? RaidHealth.MostInjured(health) : part;
            MedicalOrigin=origin;MedicalUntil=now+System.Math.Max(.1f,seconds);
            MedicalDamageTick=health.LastHitTick;MedicalShotTick=health.LastShotTick;
            MedicalGrenadeTick=health.LastGrenadeShotTick;MedicalReloadTick=health.LastReloadTick;MedicalWeapon=health.EquippedWeaponID;
            LastSentVersion=-1;return InventoryError.None;
        }
        public void RefreshMedical(ref PredictedPlayerGhost health,bool accessible,Unity.Mathematics.float3 position,double now)
        {
            if(MedicalItemId==null)return;
            string reason=!accessible ? "治疗中断：玩家死亡、断线或战局已结束。" :
                Unity.Mathematics.math.distancesq(position,MedicalOrigin)>.75f*.75f ? "治疗中断：移动超过允许范围。" :
                health.LastHitTick!=MedicalDamageTick ? "治疗中断：受到伤害。" :
                health.LastShotTick!=MedicalShotTick || health.LastGrenadeShotTick!=MedicalGrenadeTick ? "治疗中断：开火或投掷。" :
                health.LastReloadTick!=MedicalReloadTick || health.EquippedWeaponID!=MedicalWeapon ? "治疗中断：换弹或切换武器。" : null;
            if(Graph.Version!=MedicalVersion)reason="治疗中断：库存已变化，请重新选择物品。";
            if(reason==null && now<MedicalUntil)return;
            var id=MedicalItemId;MedicalItemId=null;LastSentVersion=-1;
            Error=reason==null ? RaidHealth.UseMedical(ref health,Graph,id,MedicalVersion,MedicalPart) :
                Graph.Version!=MedicalVersion ? InventoryError.Stale : InventoryError.Inaccessible;
            ActionFeedback=reason!=null ? reason+" 未消耗医疗物品。" :
                Error==InventoryError.None ? "治疗完成。" : "治疗失败：目标或物品状态已变化，未消耗物品。";
        }
        public double DissectionUntil;
        public Unity.Mathematics.float3 DissectionOrigin;
        public uint DissectionDamageTick;
        public void BeginDissection(int id,double now,Unity.Mathematics.float3 origin,uint damageTick,float seconds=AlienHarvest.DissectionSeconds)
        {
            ActionFeedback=null;DissectingLootId=id;DissectionUntil=now+System.Math.Max(.1f,seconds);
            DissectionOrigin=origin;DissectionDamageTick=damageTick;OpenedLootId=-1;LastSentVersion=-1;
        }
        public void RefreshDissection(RaidLootContainers caches,bool accessible,Unity.Mathematics.float3 position,uint damageTick,double now)
        {
            if(DissectingLootId<0)return;
            accessible=accessible && Unity.Mathematics.math.distancesq(position,DissectionOrigin)<=.75f*.75f && damageTick==DissectionDamageTick;
            if(accessible && now<DissectionUntil)return;
            int id=DissectingLootId;DissectingLootId=-1;LastSentVersion=-1;
            Error=accessible ? InventoryError.None : InventoryError.Inaccessible;
            ActionFeedback=accessible ? "搜索完成。" : "搜索中断：移动、受伤、离开范围或战局结束；未获取物品。";
            if(accessible)
            {
                if(caches.RequiresDissection(id))caches.Containers[id].Version++;
                SearchedLoot.Add(id);OpenedLootId=id;
            }
        }
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
    public struct RaidInventoryDropRpc : IRpcCommand
    {
        public int RaidId,ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
    }
    public struct RaidActionCancelRpc : IRpcCommand
    {
        public int RaidId;
        public uint RequestId;
    }
    public struct RaidMedicalUseRpc : IRpcCommand
    {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        public BodyPart Part;
    }
    public struct RaidConsumableUseRpc : IRpcCommand
    {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
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
        public bool RequiresDissection => CharacterIndex==-1 && Version==1;
    }
    public sealed class RaidDeathBagClientState : IComponentData
    {
        public readonly System.Collections.Generic.Dictionary<int,RaidCorpseRpc> Bags = new System.Collections.Generic.Dictionary<int,RaidCorpseRpc>();
        public void Receive(RaidCorpseRpc bag)
        {
            if(bag.LootId<RaidLootContainers.FirstDeathBagId || bag.Version<1 || bag.CharacterIndex<RaidLootContainers.DroppedItemCharacterIndex || bag.CharacterIndex>2 ||
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
        public int DissectingLootId=-1;
        public float DissectionRemaining;
        public string MedicalItemId;
        public float MedicalRemaining;
        public string ActionFeedback;
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
                DissectingLootId=snapshot?.DissectingLootId ?? -1;
                DissectionRemaining=snapshot?.DissectionRemaining ?? 0;
                MedicalItemId=snapshot?.MedicalItemId;MedicalRemaining=snapshot?.MedicalRemaining ?? 0;ActionFeedback=snapshot?.ActionFeedback;
                Graph = graph; RaidId = chunk.RaidId; Sequence = chunk.Sequence; RequestId = chunk.RequestId; Error = chunk.Error; return true;
            }
            catch (Exception) { return false; }
        }
    }
}
