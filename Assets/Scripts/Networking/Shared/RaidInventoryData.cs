using System;
using System.Linq;
using System.Collections.Generic;
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
        public static string EncodeSnapshot(InventoryGraph graph,int lootId,int lootVersion,int dissectingLootId=-1,float dissectionRemaining=0,string medicalItemId=null,float medicalRemaining=0,string actionFeedback=null,int corpseCharacterIndex=-1,Dictionary<string,string> corpseEquipmentSlots=null,RaidResultManifest result=null,bool medicalIsConsumable=false,int searchTotal=-1) => Newtonsoft.Json.JsonConvert.SerializeObject(
            new RaidInventorySnapshot {Graph=graph,LootId=lootId,LootVersion=lootVersion,
                DissectingLootId=dissectingLootId,DissectionRemaining=dissectionRemaining,MedicalItemId=medicalItemId,MedicalRemaining=medicalRemaining,MedicalIsConsumable=medicalIsConsumable,ActionFeedback=actionFeedback,
                CorpseCharacterIndex=corpseCharacterIndex,CorpseEquipmentSlots=corpseEquipmentSlots,Result=result,SearchTotal=searchTotal},
            new Newtonsoft.Json.JsonSerializerSettings {StringEscapeHandling=Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii});
    }
    public sealed class RaidInventorySnapshot
    {
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int LootId=-1, LootVersion;
        public int CorpseCharacterIndex=-1;
        public Dictionary<string,string> CorpseEquipmentSlots;
        public int DissectingLootId=-1;
        public float DissectionRemaining;
        public int SearchTotal=-1;
        public string MedicalItemId;
        public float MedicalRemaining;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
    }
    public sealed class RaidLootContainers : IComponentData
    {
        public readonly System.Collections.Generic.Dictionary<int,InventoryGraph> Containers = new System.Collections.Generic.Dictionary<int,InventoryGraph>();
        public readonly System.Collections.Generic.Dictionary<int,RaidCorpseRecord> DeathBags = new System.Collections.Generic.Dictionary<int,RaidCorpseRecord>();
        public readonly System.Collections.Generic.Dictionary<int,RaidLootRefillState> RefillStates = new System.Collections.Generic.Dictionary<int,RaidLootRefillState>();
        public readonly System.Collections.Generic.Dictionary<int,Unity.Mathematics.float3> MissionCaches = new System.Collections.Generic.Dictionary<int,Unity.Mathematics.float3>();
        public readonly System.Collections.Generic.Dictionary<int,Unity.Mathematics.float3> FurnitureCaches = new System.Collections.Generic.Dictionary<int,Unity.Mathematics.float3>();
        public readonly Random LootRandom = new Random(Guid.NewGuid().GetHashCode());
        public bool Initialized { get; private set; }
        public object MapIdentity { get; private set; }
        public double NextRefillCheck;
        public const int FirstDeathBagId=64;
        public const int DroppedItemCharacterIndex=-2;
        public const double EmptyDropGraceSeconds=10;
        public const float DropInterestRadius=80;
        private readonly System.Collections.Generic.List<int> m_ExpiredDrops = new System.Collections.Generic.List<int>();
        public const int FirstMissionCacheId = 1000000000;
        private int m_NextDeathBagId=FirstDeathBagId;
        public int RefillGeneration(int id) => RefillStates.TryGetValue(id,out var refill) ? refill.RefillCount : 0;
        // A fresh alien bag starts at version 1. Dissection advances it before
        // any inventory transfer is allowed; human corpses retain their old flow.
        public bool RequiresDissection(int id) => DeathBags.TryGetValue(id,out var corpse) && corpse.CharacterIndex==-1 &&
            Containers.TryGetValue(id,out var graph) && graph.Version==1;
        public int Drop(InventoryGraph carried,Unity.Mathematics.float3 position,Unity.Mathematics.quaternion rotation=default,int characterIndex=2,double createdAt=0)
        {
            var slots = carried.Children(InventoryCatalog.Equipment).ToDictionary(i => i.Region, i => i.Id);
            var graph=LootInventoryExchange.DropOnDeath(carried);
            int id = RegisterDrop(graph,position,rotation,characterIndex,createdAt);
            DeathBags[id].EquipmentSlots = slots.Where(p => graph.Find(p.Value) != null).ToDictionary(p => p.Key, p => p.Value);
            return id;
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
            if(m_NextDeathBagId>=FirstMissionCacheId)throw new ArgumentException("Ground loot ID space exhausted.");
            int id=m_NextDeathBagId++;
            Containers.Add(id,graph);
            try { DeathBags.Add(id,new RaidCorpseRecord {Position=position,
                Rotation=Unity.Mathematics.math.lengthsq(rotation.value)>0 ? rotation : Unity.Mathematics.quaternion.identity,
                CharacterIndex=characterIndex,CreatedAt=createdAt}); }
            catch { Containers.Remove(id);throw; }
            return id;
        }
        public bool IsEmptyDrop(int id) => DeathBags.TryGetValue(id,out var bag) && bag.CharacterIndex==DroppedItemCharacterIndex &&
            Containers.TryGetValue(id,out var graph) && !RaidLootRefillState.HasContents(graph);
        public void RecycleEmptyDrops(double now,System.Collections.Generic.HashSet<int> protectedIds)
        {
            m_ExpiredDrops.Clear();
            foreach(var pair in DeathBags)
            {
                if(pair.Value.CharacterIndex!=DroppedItemCharacterIndex)continue;
                if(!IsEmptyDrop(pair.Key) || protectedIds.Contains(pair.Key)) {pair.Value.EmptySince=-1;continue;}
                if(pair.Value.EmptySince<0)pair.Value.EmptySince=now;
                if(now-pair.Value.EmptySince>=EmptyDropGraceSeconds)m_ExpiredDrops.Add(pair.Key);
            }
            foreach(int id in m_ExpiredDrops) {Containers.Remove(id);DeathBags.Remove(id);}
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
                Containers.Clear();DeathBags.Clear();RefillStates.Clear();MissionCaches.Clear();FurnitureCaches.Clear();
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
        public const float ItemActionMovementLimit = .75f;
        public RaidSurvivalMotion SurvivalMotion;
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int RaidId;
        public int MissionFirstsBefore, MissionFirstsSaved;
        public int LastSentVersion = -1;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public int OpenedLootId=-1, LastSentLootVersion=-1;
        public int DissectingLootId=-1;
        public double NextLootNoiseAt;
        public readonly System.Collections.Generic.HashSet<int> SearchedLoot = new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.Dictionary<int,int> m_SearchedGenerations = new System.Collections.Generic.Dictionary<int,int>();
        public string MedicalItemId;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
        public int MedicalVersion;
        public BodyPart MedicalPart;
        public double MedicalUntil;
        public Unity.Mathematics.float3 MedicalOrigin;
        public uint MedicalDamageSequence,MedicalShieldHitSequence,MedicalShotTick,MedicalGrenadeTick,MedicalReloadTick,MedicalWeapon;
        public bool NeedsSearch(RaidLootContainers caches,int id) => !SearchedLoot.Contains(id) || caches.RequiresDissection(id) ||
            caches.RefillStates.TryGetValue(id,out var refill) && (!m_SearchedGenerations.TryGetValue(id,out var generation) || generation!=refill.RefillCount);
        public void ClearSearchRecords() { SearchedLoot.Clear();m_SearchedGenerations.Clear(); }
        public uint DeathBagSequence;
        public double NextDeathBagResync;
        public readonly System.Collections.Generic.List<int> HiddenDeathBags = new System.Collections.Generic.List<int>();
        public readonly System.Collections.Generic.List<int> RemovedSearches = new System.Collections.Generic.List<int>();
        public bool TacticalPending;
        public bool ActionPending => TacticalPending || DissectingLootId>=0 || MedicalItemId!=null;
        public void CloseLoot()
        {
            // Closing a view owns the search, not an unrelated self-use action.
            if(DissectingLootId>=0)
            {
                DissectingLootId=-1;
                ActionFeedback="已取消搜索；未获取物品。";
            }
            OpenedLootId=-1;LastSentVersion=LastSentLootVersion=-1;
        }
        public void CancelActions(string feedback=null)
        {
            TacticalPending=false;DissectingLootId=-1;MedicalItemId=null;MedicalIsConsumable=false;ActionFeedback=feedback;LastSentVersion=-1;
        }
        public InventoryError BeginMedical(in PredictedPlayerGhost health,string id,int version,BodyPart part,
            Unity.Mathematics.float3 origin,double now,float seconds=3)
        {
            if(RaidHealth.HandsBusy(health))return InventoryError.Inaccessible;
            // Validate on copies: neither item consumption nor healing occurs before the deadline.
            var probe=health;var error=RaidHealth.UseMedical(ref probe,Graph.Clone(),id,version,part);
            if(error!=InventoryError.None)return error;
            CancelActions(ActionPending ? "原操作已取消：开始治疗；未获取物品或消耗医疗物品。" : null);
            MedicalPart=part==BodyPart.Auto ? RaidHealth.MostInjured(health) : part;
            StartItemAction(health,id,version,origin,now,seconds,false);
            return InventoryError.None;
        }
        public InventoryError BeginConsumable(in PredictedPlayerGhost health,string id,int version,
            Unity.Mathematics.float3 origin,double now)
        {
            if(ActionPending || RaidHealth.HandsBusy(health))return InventoryError.Inaccessible;
            // Validate without granting resources or consuming a stack before the deadline.
            var probe=health;var error=RaidNutrition.Consume(ref probe,Graph.Clone(),id,version);
            if(error!=InventoryError.None)return error;
            var definition=ConsumableCatalog.Get(Graph.Find(id).Code);
            CancelActions();
            StartItemAction(health,id,version,origin,now,
                definition.Food ? RaidNutrition.FoodUseSeconds : RaidNutrition.DrinkUseSeconds,true);
            return InventoryError.None;
        }
        private void StartItemAction(in PredictedPlayerGhost health,string id,int version,Unity.Mathematics.float3 origin,
            double now,float seconds,bool consumable)
        {
            MedicalItemId=id;MedicalVersion=version;MedicalIsConsumable=consumable;
            MedicalOrigin=origin;MedicalUntil=now+System.Math.Max(.1f,seconds);
            MedicalDamageSequence=health.NonPhysiologicalDamageSequence;
            MedicalShieldHitSequence=health.ShieldHitSequence;MedicalShotTick=health.LastShotTick;
            MedicalGrenadeTick=health.LastGrenadeShotTick;MedicalReloadTick=health.LastReloadTick;MedicalWeapon=health.EquippedWeaponID;
            LastSentVersion=-1;
        }
        public void RefreshMedical(ref PredictedPlayerGhost health,bool accessible,Unity.Mathematics.float3 position,double now)
        {
            if(MedicalItemId==null)return;
            string reason=!accessible ? "治疗中断：玩家死亡、断线或战局已结束。" :
                Unity.Mathematics.math.distancesq(position,MedicalOrigin)>ItemActionMovementLimit*ItemActionMovementLimit ? "治疗中断：移动超过允许范围。" :
                health.NonPhysiologicalDamageSequence!=MedicalDamageSequence ||
                    health.ShieldHitSequence!=MedicalShieldHitSequence ? "治疗中断：受到攻击。" :
                health.LastShotTick!=MedicalShotTick || health.LastGrenadeShotTick!=MedicalGrenadeTick ? "治疗中断：开火或投掷。" :
                health.LastReloadTick!=MedicalReloadTick || health.EquippedWeaponID!=MedicalWeapon || RaidHealth.HandsBusy(health) ? "治疗中断：换弹或切换武器。" : null;
            if(Graph.Version!=MedicalVersion)reason="治疗中断：库存已变化，请重新选择物品。";
            if(reason==null && now<MedicalUntil)return;
            var id=MedicalItemId;bool consumable=MedicalIsConsumable;MedicalItemId=null;MedicalIsConsumable=false;LastSentVersion=-1;
            Error=reason==null ? consumable ? RaidNutrition.Consume(ref health,Graph,id,MedicalVersion) : RaidHealth.UseMedical(ref health,Graph,id,MedicalVersion,MedicalPart) :
                Graph.Version!=MedicalVersion ? InventoryError.Stale : InventoryError.Inaccessible;
            if(consumable)
                ActionFeedback=reason!=null ? reason.Replace("治疗中断", "使用中断")+" 未消耗物品。" :
                    Error==InventoryError.None ? "补给使用完成。" : "使用失败：状态或物品已变化，未消耗物品。";
            else ActionFeedback=reason!=null ? reason+" 未消耗医疗物品。" :
                Error==InventoryError.None ? "治疗完成。" : "治疗失败：目标或物品状态已变化，未消耗物品。";
        }
        public double DissectionUntil, DissectionStartedAt;
        public int SearchRevealed;
        public Unity.Mathematics.float3 DissectionOrigin;
        public int DissectionGeneration;
        public uint DissectionDamageSequence,DissectionShieldHitSequence,DissectionShotTick,DissectionGrenadeTick,DissectionReloadTick,DissectionWeapon;
        public bool BeginDissection(int id,double now,Unity.Mathematics.float3 origin,in PredictedPlayerGhost health,float seconds=AlienHarvest.DissectionSeconds,int refillGeneration=0)
        {
            if(RaidHealth.HandsBusy(health))return false;
            CancelActions(ActionPending ? "原操作已取消：开始搜索；未消耗物品。" : ActionFeedback);DissectingLootId=id;DissectionUntil=now+System.Math.Max(.1f,seconds);
            DissectionStartedAt=now;SearchRevealed=0;
            DissectionOrigin=origin;DissectionGeneration=refillGeneration;DissectionDamageSequence=health.NonPhysiologicalDamageSequence;DissectionShieldHitSequence=health.ShieldHitSequence;
            DissectionShotTick=health.LastShotTick;DissectionGrenadeTick=health.LastGrenadeShotTick;
            DissectionReloadTick=health.LastReloadTick;DissectionWeapon=health.EquippedWeaponID;OpenedLootId=-1;LastSentVersion=-1;
            return true;
        }
        public void RefreshDissection(RaidLootContainers caches,bool accessible,Unity.Mathematics.float3 position,in PredictedPlayerGhost health,double now)
        {
            if(DissectingLootId<0)return;
            bool contentsChanged=caches.RefillStates.TryGetValue(DissectingLootId,out var refill) && refill.RefillCount!=DissectionGeneration;
            string reason=!accessible ? "搜索中断：离开范围、死亡或战局结束。" :
                contentsChanged ? "搜索中断：容器内容已刷新。" :
                Unity.Mathematics.math.distancesq(position,DissectionOrigin)>.75f*.75f ? "搜索中断：移动超过允许范围。" :
                health.NonPhysiologicalDamageSequence!=DissectionDamageSequence || health.ShieldHitSequence!=DissectionShieldHitSequence ? "搜索中断：受到攻击。" :
                health.LastShotTick!=DissectionShotTick || health.LastGrenadeShotTick!=DissectionGrenadeTick ? "搜索中断：开火或投掷。" :
                health.LastReloadTick!=DissectionReloadTick || health.EquippedWeaponID!=DissectionWeapon || RaidHealth.HandsBusy(health) ? "搜索中断：换弹或切换武器。" : null;
            if(reason==null && now<DissectionUntil)
            {
                int total=caches.Containers.TryGetValue(DissectingLootId,out var cache) ? cache.Children(LootInventoryExchange.Root).Count() : 0;
                int revealed=(int)(total*System.Math.Clamp((now-DissectionStartedAt)/(DissectionUntil-DissectionStartedAt),0,1));
                if(revealed!=SearchRevealed){SearchRevealed=revealed;LastSentVersion=-1;}
                return;
            }
            int id=DissectingLootId;DissectingLootId=-1;LastSentVersion=-1;
            Error=reason==null ? InventoryError.None : InventoryError.Inaccessible;
            ActionFeedback=reason==null ? "搜索完成。" : reason+" 未获取物品。";
            if(reason==null)
            {
                if(caches.RequiresDissection(id))caches.Containers[id].Version++;
                SearchedLoot.Add(id);
                if(caches.RefillStates.TryGetValue(id,out refill))m_SearchedGenerations[id]=refill.RefillCount;
                OpenedLootId=id;
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
        public Dictionary<string,string> EquipmentSlots;
        public double CreatedAt;
        public double EmptySince=-1;
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
    // Atomic visibility + metadata update, with a fresh generated serializer identity.
    public struct RaidGroundItemMarkerRpc : IRpcCommand
    {
        public FixedString64Bytes ItemCode;
        public uint Sequence;
        public int LootId, Version, CharacterIndex;
        public Unity.Mathematics.float3 Position;
        public Unity.Mathematics.quaternion Rotation;
        public float Age;
        public bool Empty, Removed, Deleted;
    }
    public sealed class RaidDeathBagClientState : IComponentData
    {
        public readonly System.Collections.Generic.Dictionary<int,RaidCorpseRpc> Bags = new System.Collections.Generic.Dictionary<int,RaidCorpseRpc>();
        public readonly System.Collections.Generic.Dictionary<int,string> ItemCodes = new System.Collections.Generic.Dictionary<int,string>();
        private readonly System.Collections.Generic.Dictionary<int,uint> m_Sequences = new System.Collections.Generic.Dictionary<int,uint>();
        // Keep at most a window of per-ID sequence records. Updates older than
        // the window are rejected globally, including for already removed bags.
        private const uint SequenceWindow=4096;
        private uint m_HighestSequence;
        private readonly System.Collections.Generic.List<int> m_ExpiredSequences = new System.Collections.Generic.List<int>();
        public void Receive(RaidGroundItemMarkerRpc update)
        {
            uint floor=m_HighestSequence>SequenceWindow ? m_HighestSequence-SequenceWindow : 0;
            if(update.Sequence<=floor)return;
            if(update.LootId<RaidLootContainers.FirstDeathBagId || update.Sequence==0 ||
                m_Sequences.TryGetValue(update.LootId,out uint sequence) && update.Sequence<=sequence)return;
            m_HighestSequence=System.Math.Max(m_HighestSequence,update.Sequence);
            m_Sequences[update.LootId]=update.Sequence;
            if(m_Sequences.Count>SequenceWindow)
            {
                floor=m_HighestSequence-SequenceWindow;m_ExpiredSequences.Clear();
                foreach(var pair in m_Sequences)if(pair.Value<=floor)m_ExpiredSequences.Add(pair.Key);
                foreach(int id in m_ExpiredSequences)m_Sequences.Remove(id);
            }
            if(update.Removed) {Bags.Remove(update.LootId);ItemCodes.Remove(update.LootId);return;}
            Receive(new RaidCorpseRpc {LootId=update.LootId,Version=update.Version,CharacterIndex=update.CharacterIndex,
                Position=update.Position,Rotation=update.Rotation,Age=update.Age,Empty=update.Empty});
            if(Bags.TryGetValue(update.LootId,out var accepted) && accepted.Version==update.Version)
                ItemCodes[update.LootId]=update.ItemCode.ToString();
        }
        public void Receive(RaidCorpseRpc bag)
        {
            if(bag.LootId<RaidLootContainers.FirstDeathBagId || bag.Version<1 || bag.CharacterIndex<RaidLootContainers.DroppedItemCharacterIndex ||
                bag.CharacterIndex>1 && !GameSettings.IsPlayableCharacter(bag.CharacterIndex) ||
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
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int RaidId;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public int LootId=-1, LootVersion;
        public int CorpseCharacterIndex=-1;
        public Dictionary<string,string> CorpseEquipmentSlots;
        public int DissectingLootId=-1;
        public float DissectionRemaining;
        public int SearchTotal=-1;
        public string MedicalItemId;
        public float MedicalRemaining;
        public bool MedicalIsConsumable;
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
                CorpseCharacterIndex=snapshot?.CorpseCharacterIndex ?? -1; CorpseEquipmentSlots=snapshot?.CorpseEquipmentSlots;
                DissectingLootId=snapshot?.DissectingLootId ?? -1;
                DissectionRemaining=snapshot?.DissectionRemaining ?? 0;
                SearchTotal=snapshot?.SearchTotal ?? -1;
                MedicalItemId=snapshot?.MedicalItemId;MedicalRemaining=snapshot?.MedicalRemaining ?? 0;MedicalIsConsumable=snapshot?.MedicalIsConsumable ?? false;ActionFeedback=snapshot?.ActionFeedback;
                Result = snapshot?.Result != null && snapshot.Result.Valid(chunk.RaidId) ? snapshot.Result : null;
                Graph = graph; RaidId = chunk.RaidId; Sequence = chunk.Sequence; RequestId = chunk.RequestId; Error = chunk.Error; return true;
            }
            catch (Exception) { return false; }
        }
    }
}
