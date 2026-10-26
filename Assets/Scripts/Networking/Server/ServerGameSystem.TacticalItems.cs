using System;
using Math = System.Math;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    // All mutable tactical state belongs to this server World, not a static process singleton.
    public sealed class TacticalServerWorld : IComponentData
    {
        public sealed class Object
        {
            public uint Id; public int Owner; public Entity OwnerPlayer;public ClashDamageSource Source;
            public TacticalItemDefinition Definition; public TacticalWorldPhase Phase;
            public PhysicsScene Scene; public Transform Shooter;
            public Vector3 Position,Velocity; public bool Resting;
            public double Created,Until,NextSnapshot,ArmAt; public int Energy;
        }
        public sealed class Use
        {
            public Entity Player; public int RaidId,Version; public uint Request,Target;
            public string ItemId; public TacticalItemAction Action; public TacticalItemDefinition Definition;
            public Vector3 Origin,Placement; public double Until;
            public uint Hit,ShieldHit,Shot,Grenade,Reload,Weapon;
            public float Health,LastDamage;public BodyPart LastPart;
        }
        public sealed class Status
        {
            public double Fire,Poison,Bleed,Stamina,Antidote,Wire,Cough;
            public TacticalSprintState Sprint;public float3 SprintPosition;public bool SprintPositionKnown;
            public int FireOwner,PoisonOwner,BleedOwner;
            public ClashDamageSource FireSource,PoisonSource,BleedSource;
        }
        public readonly List<Object> Objects=new();
        public readonly Dictionary<Entity,Use> Uses=new();
        public readonly Dictionary<Entity,TacticalItemFeedback> Feedbacks=new();
        public readonly Dictionary<Entity,Status> Statuses=new();
        public readonly List<Entity> Scratch=new();
        public readonly List<Entity> Victims=new();
        public uint Epoch=1,NextId,Sequence; public object Map; public double NextOwnerSnapshot,Now;
        public RaycastHit[] Hits=new RaycastHit[32]; public readonly Collider[] Overlaps=new Collider[32];
    }
    public partial struct ServerGameSystem
    {
#if UNITY_EDITOR
        // Disposable-world authority fixture; never runs automatically or touches a player profile.
        public void TacticalTestTick(ref SystemState state,EntityCommandBuffer ecb) => UpdateTacticalItems(ref state,ecb);
        public void TacticalTestClear(ref SystemState state,EntityCommandBuffer ecb) => ClearTacticalItems(ref state,ecb);
#endif
        private TacticalServerWorld TacticalWorld(ref SystemState state)
        {
            if(!state.EntityManager.HasComponent<TacticalServerWorld>(m_PersistenceEntity))
                state.EntityManager.AddComponentObject(m_PersistenceEntity,new TacticalServerWorld());
            return state.EntityManager.GetComponentObject<TacticalServerWorld>(m_PersistenceEntity);
        }
        private void ClearTacticalItems(ref SystemState state,EntityCommandBuffer ecb)
        {
            var world=TacticalWorld(ref state);
            foreach(var pair in world.Uses)
                if(state.EntityManager.HasComponent<RaidInventoryState>(pair.Key))
                {var inventory=GetRaidInventory(ref state,pair.Key);inventory.TacticalPending=false;inventory.LastSentVersion=-1;}
            world.Uses.Clear();world.Feedbacks.Clear();world.Objects.Clear();world.Statuses.Clear();world.Epoch++;
            foreach(var health in SystemAPI.Query<RefRW<PredictedPlayerGhost>>())
            {health.ValueRW.TacticalWireSeconds=0;health.ValueRW.TacticalCoughSeconds=0;health.ValueRW.TacticalPoisonSeconds=0;health.ValueRW.TacticalSprintStamina=100;health.ValueRW.TacticalSprintExhausted=false;
                health.ValueRW.PendingBulletWound=0;health.ValueRW.PendingBulletWoundSource=default;}
            TacticalBroadcast(ecb,world,null,true);
        }
        private void UpdateTacticalItems(ref SystemState state,EntityCommandBuffer ecb)
        {
            var world=TacticalWorld(ref state);var map=MoonRaidMap.Active;
            if(!ReferenceEquals(world.Map,map))
            {ClearTacticalItems(ref state,ecb);world.Map=map;}
            double now=SystemAPI.Time.ElapsedTime;world.Now=now;
            TacticalConsumeBulletWounds(ref state,world,now);
            foreach(var (request,received,entity) in SystemAPI.Query<RefRO<RaidTacticalItemActionRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection=received.ValueRO.SourceConnection;var r=request.ValueRO;
                if(state.EntityManager.HasComponent<RaidInventoryState>(connection) && SystemAPI.HasComponent<RaidSession>(connection))
                {
                    var inventory=GetRaidInventory(ref state,connection);
                    if(r.RequestId>inventory.RequestId)
                    {
                        inventory.RequestId=r.RequestId;inventory.Error=InventoryError.Inaccessible;world.Feedbacks[connection]=TacticalItemFeedback.Unavailable;
                        if(r.RaidId==inventory.RaidId && SystemAPI.GetComponent<RaidSession>(connection).RaidId==r.RaidId)
                        {
                            if(r.Action==TacticalItemAction.Cancel)
                            {inventory.CancelActions();world.Uses.Remove(connection);inventory.Error=InventoryError.None;world.Feedbacks[connection]=TacticalItemFeedback.Cancelled;}
                            else if(TacticalActor(ref state,connection,out var player,out var health,out var root))
                            {
                                if(!inventory.ActionPending)
                                    TacticalBegin(ref state,world,inventory,connection,player,health,root,r,now);
                            }
                        }
                        inventory.LastSentVersion=-1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            world.Scratch.Clear();foreach(var pair in world.Uses)world.Scratch.Add(pair.Key);
            foreach(var connection in world.Scratch)
            {
                var use=world.Uses[connection];
                if(!state.EntityManager.HasComponent<RaidInventoryState>(connection)) {world.Uses.Remove(connection);continue;}
                var inventory=GetRaidInventory(ref state,connection);
                bool actorValid=TacticalActor(ref state,connection,out var player,out var health,out var root);
                bool valid=actorValid && inventory.TacticalPending && inventory.RaidId==use.RaidId && inventory.Graph.Version==use.Version;
                valid=valid && player==use.Player && Vector3.Distance(root.transform.position,use.Origin)<=.55f &&
                    health.LastHitTick==use.Hit && health.ShieldHitSequence==use.ShieldHit && health.CurrentHealth>=use.Health-.001f &&
                    (use.Action!=TacticalItemAction.Extinguish || health.LastHitPart==use.LastPart && Mathf.Abs(health.LastDamageAmount-use.LastDamage)<.001f) && health.LastShotTick==use.Shot && health.LastGrenadeShotTick==use.Grenade &&
                    health.LastReloadTick==use.Reload && health.EquippedWeaponID==use.Weapon;
                if(valid && now<use.Until)continue;
                inventory.TacticalPending=false;world.Uses.Remove(connection);
                inventory.Error=valid ? TacticalComplete(ref state,ecb,world,inventory,use,health,root,now) : InventoryError.Inaccessible;
                world.Feedbacks[connection]=!valid ? TacticalItemFeedback.Interrupted : inventory.Error==InventoryError.None ? TacticalItemFeedback.Completed :
                    inventory.Error==InventoryError.Full ? TacticalItemFeedback.NoRoom : inventory.Error==InventoryError.Stale ? TacticalItemFeedback.Stale : TacticalItemFeedback.Unavailable;
                inventory.LastSentVersion=-1;
            }
            world.Victims.Clear();
            foreach(var (health,link,entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>,GhostGameObjectLink>().WithEntityAccess())
                if(health.ValueRO.CurrentHealth>0 && (!health.ValueRO.ClashEnabled || health.ValueRO.ClashLife==ClashLife.Alive) && link.LinkedInstance)world.Victims.Add(entity);
            world.Scratch.Clear();foreach(var pair in world.Feedbacks)if(!state.EntityManager.Exists(pair.Key))world.Scratch.Add(pair.Key);foreach(var gone in world.Scratch)world.Feedbacks.Remove(gone);
            float dt=Mathf.Clamp(SystemAPI.Time.DeltaTime,0,.25f);
            uint tick=SystemAPI.TryGetSingleton<NetworkTime>(out var time) && time.ServerTick.IsValid ? time.ServerTick.TickIndexForValidTick : 0;
            // Resolve suppressing clouds before fire/poison, independent of insertion order.
            foreach(var obj in world.Objects)
                if(obj.Phase==TacticalWorldPhase.Choke && now<obj.Until)
                    foreach(var other in world.Objects)
                        if(other!=obj && TacticalWithin(obj,other.Position,.25f,other.Scene,null) &&
                            (other.Phase==TacticalWorldPhase.Fire || other.Phase==TacticalWorldPhase.Poison ||
                             other.Phase==TacticalWorldPhase.Flying && other.Definition.Kind==TacticalItemKind.Dynamite))other.Until=now;
            for(int i=world.Objects.Count-1;i>=0;i--)
            {
                var obj=world.Objects[i];
                if(!obj.Scene.IsValid() || now>=obj.Until)
                {
                    if(obj.Scene.IsValid() && obj.Phase==TacticalWorldPhase.Flying &&
                        (obj.Definition.Kind==TacticalItemKind.Smoke || !TacticalChoked(world,obj.Position,obj.Scene,now)))TacticalDetonate(ref state,ecb,world,obj,now,tick);
                    if(obj.Phase==TacticalWorldPhase.Flying || now>=obj.Until || !obj.Scene.IsValid())
                    {TacticalBroadcast(ecb,world,obj,true);world.Objects.RemoveAt(i);continue;}
                }
                if(obj.Phase==TacticalWorldPhase.Flying)
                {
                    var previous=obj.Velocity;
                    EnergyGrenadePhysics.Step(obj.Scene,ref obj.Position,ref obj.Velocity,ref obj.Resting,dt,
                        now-obj.Created<.25 ? obj.Shooter : null,ref world.Hits);
                    bool hit=(obj.Velocity-(previous+Vector3.down*EnergyGrenadeRules.Gravity*dt)).sqrMagnitude>.02f;
                    if(obj.Definition.Kind==TacticalItemKind.Fire && (hit || obj.Resting))
                        TacticalDetonate(ref state,ecb,world,obj,now,tick);
                    if(TacticalChoked(world,obj.Position,obj.Scene,now) && obj.Definition.Kind==TacticalItemKind.Dynamite)obj.Until=now;
                }
                else TacticalArea(ref state,world,obj,now,tick);
                if(now>=obj.NextSnapshot)
                {obj.NextSnapshot=now+(obj.Phase==TacticalWorldPhase.Flying ? .2 : .75);TacticalBroadcast(ecb,world,obj,false);}
            }
            TacticalTickStatuses(ref state,world,now,dt,tick);
            TacticalTickSprint(ref state,world,now,SystemAPI.Time.DeltaTime);
            if(now>=world.NextOwnerSnapshot)
            {
                world.NextOwnerSnapshot=now+.2;
                foreach(var (session,joined,connection) in SystemAPI.Query<RefRO<RaidSession>,RefRO<JoinedClient>>().WithEntityAccess())
                {
                    if(!state.EntityManager.HasComponent<RaidInventoryState>(connection))continue;
                    var inv=GetRaidInventory(ref state,connection);world.Uses.TryGetValue(connection,out var use);
                    world.Statuses.TryGetValue(joined.ValueRO.PlayerEntity,out var status);
                    var rpc=ecb.CreateEntity();
                    ecb.AddComponent(rpc,new TacticalItemOwnerRpc {RaidId=inv.RaidId,RequestId=inv.RequestId,Error=inv.Error,
                        Feedback=world.Feedbacks.TryGetValue(connection,out var feedback) ? feedback : TacticalItemFeedback.None,
                        SprintStamina=status?.Sprint.Value ?? 100,SprintExhausted=status?.Sprint.Exhausted ?? false,SprintTracked=status?.Sprint.Initialized ?? false,
                        ItemId=use?.ItemId ?? "",Remaining=use==null ? 0 : (float)Math.Max(0,use.Until-now),
                        FireSeconds=TacticalRemaining(status?.Fire ?? 0,now),PoisonSeconds=TacticalRemaining(status?.Poison ?? 0,now),
                        BleedSeconds=TacticalRemaining(status?.Bleed ?? 0,now),StaminaSeconds=TacticalRemaining(status?.Stamina ?? 0,now),
                        AntidoteSeconds=TacticalRemaining(status?.Antidote ?? 0,now),WireSeconds=TacticalRemaining(status?.Wire ?? 0,now),
                        CoughSeconds=TacticalRemaining(status?.Cough ?? 0,now)});
                    ecb.AddComponent(rpc,new SendRpcCommandRequest {TargetConnection=connection});
                }
            }
        }
        private void TacticalConsumeBulletWounds(ref SystemState state,TacticalServerWorld world,double now)
        {
            foreach(var (health,entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithEntityAccess())
            {
                float wound=health.ValueRO.PendingBulletWound;
                if(wound<=0)continue;
                var source=health.ValueRO.PendingBulletWoundSource;
                health.ValueRW.PendingBulletWound=0;health.ValueRW.PendingBulletWoundSource=default;
                if(wound<TacticalItemRules.BulletWoundThreshold || health.ValueRO.CurrentHealth<=0 || health.ValueRO.ClashEnabled)continue;
                var status=TacticalStatus(world,entity);
                TacticalRefreshBleed(status,now+TacticalItemRules.BulletWoundDuration,source.NetworkId,source);
            }
        }
        private static float TacticalRemaining(double until,double now)=>(float)Math.Max(0,until-now);
        private bool TacticalActor(ref SystemState state,Entity connection,out Entity player,out PredictedPlayerGhost health,out GhostGameObject root)
        {
            player=Entity.Null;health=default;root=null;
            if(!MoonRaidMap.Active)return false;
            if(!SystemAPI.HasComponent<RaidSession>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection) ||
                SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) || SystemAPI.GetComponent<RaidSession>(connection).Phase!=RaidPhase.Active)return false;
            player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if(!SystemAPI.Exists(player)||!SystemAPI.HasComponent<PredictedPlayerGhost>(player)||
                !state.EntityManager.HasComponent<GhostGameObjectLink>(player)||!BountyClashDamage.CanAct(state.EntityManager,player))return false;
            health=SystemAPI.GetComponent<PredictedPlayerGhost>(player);
            root=state.EntityManager.GetComponentObject<GhostGameObjectLink>(player).LinkedInstance;
            return health.CurrentHealth>0 && root;
        }
        private void TacticalBegin(ref SystemState state,TacticalServerWorld world,RaidInventoryState inventory,Entity connection,
            Entity player,in PredictedPlayerGhost health,GhostGameObject root,in RaidTacticalItemActionRpc r,double now)
        {
            if(r.ExpectedVersion!=inventory.Graph.Version){inventory.Error=InventoryError.Stale;world.Feedbacks[connection]=TacticalItemFeedback.Stale;return;}
            TacticalItemDefinition definition=null;Vector3 placement=default;float seconds=3;
            if(r.Action==TacticalItemAction.Use)
            {
                inventory.Error=TacticalItemRules.Validate(inventory.Graph,r.ItemId.ToString(),r.ExpectedVersion,out definition);
                if(inventory.Error!=InventoryError.None)return;
                seconds=definition.UseSeconds;
                if(definition.Deployable && !TacticalPlacement(root,health,world,out placement)) {inventory.Error=InventoryError.Inaccessible;world.Feedbacks[connection]=TacticalItemFeedback.NeedGround;return;}
                if((definition.Throwable||definition.Deployable) && !TacticalCapacity(world,definition,player,placement))
                {inventory.Error=InventoryError.Full;world.Feedbacks[connection]=TacticalItemFeedback.Limit;return;}
                if(definition.Kind==TacticalItemKind.Vitality && !TacticalCanHeal(health)) {inventory.Error=InventoryError.Invalid;world.Feedbacks[connection]=health.TacticalPoisonSeconds>0 ? TacticalItemFeedback.Poisoned : TacticalItemFeedback.NoWound;return;}
            }
            else if(r.Action==TacticalItemAction.Disarm || r.Action==TacticalItemAction.Resupply)
            {
                uint targetId=r.TargetId;var target=world.Objects.Find(o=>o.Id==targetId);
                if(target==null || now>=target.Until || !TacticalInteract(root,health,target,r.Action)) {inventory.Error=InventoryError.Inaccessible;world.Feedbacks[connection]=TacticalItemFeedback.NoTarget;return;}
                seconds=r.Action==TacticalItemAction.Resupply ? 2 : 3;
            }
            else if(r.Action==TacticalItemAction.Extinguish)
            {
                if(!world.Statuses.TryGetValue(player,out var status) || status.Fire<=now && status.Bleed<=now)
                {inventory.Error=InventoryError.Invalid;return;}
                seconds=TacticalItemRules.StopBleedSeconds;
            }
            else {inventory.Error=InventoryError.Invalid;return;}
            // Cancels mission/search/medical via their existing shared cancel contract.
            inventory.CancelActions();inventory.TacticalPending=true;
            CancelManualMission(ref state,connection,"道具操作开始。");
            world.Uses[connection]=new TacticalServerWorld.Use {Player=player,RaidId=r.RaidId,Version=r.ExpectedVersion,Request=r.RequestId,
                Target=r.TargetId,ItemId=r.ItemId.ToString(),Action=r.Action,Definition=definition,Origin=root.transform.position,Placement=placement,
                Until=now+seconds,Hit=health.LastHitTick,ShieldHit=health.ShieldHitSequence,Shot=health.LastShotTick,Grenade=health.LastGrenadeShotTick,
                Reload=health.LastReloadTick,Weapon=health.EquippedWeaponID,Health=health.CurrentHealth,LastDamage=health.LastDamageAmount,LastPart=health.LastHitPart};
            inventory.Error=InventoryError.None;world.Feedbacks[connection]=TacticalItemFeedback.Started;
        }
        private static bool TacticalCapacity(TacticalServerWorld world,TacticalItemDefinition definition,Entity player,Vector3 position)
        {
            if(world.Objects.Count>=TacticalItemRules.MaxWorldObjects)return false;
            if(definition.Trap && world.Objects.FindAll(o=>o.OwnerPlayer==player && o.Definition.Trap && o.Phase==TacticalWorldPhase.Armed).Count>=TacticalItemRules.MaxOwnerTraps)return false;
            return !definition.Deployable || !world.Objects.Exists(o=>o.Phase!=TacticalWorldPhase.Flying && Vector3.Distance(o.Position,position)<.65f);
        }
        private InventoryError TacticalComplete(ref SystemState state,EntityCommandBuffer ecb,TacticalServerWorld world,RaidInventoryState inventory,
            TacticalServerWorld.Use use,in PredictedPlayerGhost health,GhostGameObject root,double now)
        {
            var status=TacticalStatus(world,use.Player);
            if(use.Action==TacticalItemAction.Extinguish){status.Fire=status.Bleed=0;return InventoryError.None;}
            if(use.Action==TacticalItemAction.Disarm || use.Action==TacticalItemAction.Resupply)
            {
                var target=world.Objects.Find(o=>o.Id==use.Target);
                if(target==null || now>=target.Until || !TacticalInteract(root,health,target,use.Action))return InventoryError.Inaccessible;
                if(use.Action==TacticalItemAction.Resupply)
                {
                    var error=TacticalItemRules.PrepareResupply(inventory.Graph,use.Version,target.Energy,out var graph,out int energy);
                    if(error!=InventoryError.None)return error;
                    inventory.Graph.Items=graph.Items;inventory.Graph.Version=graph.Version;target.Energy-=energy;
                    if(target.Energy<=0){target.Until=now;target.Phase=TacticalWorldPhase.Removed;}
                    target.NextSnapshot=0;
                    var session=SystemAPI.GetComponentRW<RaidSession>(FindTacticalConnection(ref state,use.Player));
                    RaidInventoryState.UpdateTotals(inventory.Graph,ref session.ValueRW);
                    return InventoryError.None;
                }
                // Disarming destroys the deployed trap; it never fabricates a fresh inventory item.
                target.Until=now;target.Phase=TacticalWorldPhase.Removed;target.NextSnapshot=0;return InventoryError.None;
            }
            var definition=use.Definition;
            var validation=TacticalItemRules.Validate(inventory.Graph,use.ItemId,use.Version,out _);
            if(validation!=InventoryError.None)return validation;
            Vector3 position=root.transform.position,velocity=default;
            if(definition.Deployable && (!TacticalPlacement(root,health,world,out position)||Vector3.Distance(position,use.Placement)>.6f))return InventoryError.Inaccessible;
            if(definition.Throwable && !TacticalThrow(root,health,world,out position,out velocity))return InventoryError.Inaccessible;
            if((definition.Deployable||definition.Throwable) && !TacticalCapacity(world,definition,use.Player,position))return InventoryError.Full;
            if(definition.Kind==TacticalItemKind.Vitality && !TacticalCanHeal(health))return InventoryError.Invalid;
            var errorTake=TacticalItemRules.Take(inventory.Graph,use.ItemId,use.Version);if(errorTake!=InventoryError.None)return errorTake;
            if(definition.Kind==TacticalItemKind.Vitality)
            {
                var next=health;
                for(int p=0;p<RaidHealth.PartCount;p++)
                {var part=(BodyPart)p;float before=RaidHealth.Get(next,part);if(before<=0)continue;
                 RaidHealth.Set(ref next,part,RaidHealth.Maximum(next,part));}
                SystemAPI.SetComponent(use.Player,next);
            }
            else if(definition.Kind==TacticalItemKind.Stamina)status.Stamina=now+TacticalItemRules.StaminaDuration;
            else if(definition.Kind==TacticalItemKind.Antidote){status.Antidote=now+TacticalItemRules.AntidoteDuration;status.Poison=0;}
            else
            {
                var obj=new TacticalServerWorld.Object {Id=++world.NextId,Owner=SystemAPI.HasComponent<GhostOwner>(use.Player) ? SystemAPI.GetComponent<GhostOwner>(use.Player).NetworkId : 0,
                    OwnerPlayer=use.Player,Source=BountyClashDamage.Capture(state.World,use.Player),Definition=definition,Phase=definition.Throwable ? TacticalWorldPhase.Flying : definition.Trap ? TacticalWorldPhase.Armed : TacticalWorldPhase.Supply,
                    Scene=root.gameObject.scene.GetPhysicsScene(),Shooter=root.transform,Position=position,Velocity=velocity,Created=now,ArmAt=now+1,
                    Until=now+(definition.Throwable ? definition.Fuse : definition.Lifetime),Energy=definition.Kind==TacticalItemKind.AmmoBox ? TacticalItemRules.SupplyEnergy : 0};
                world.Objects.Add(obj);TacticalBroadcast(ecb,world,obj,false);
            }
            return InventoryError.None;
        }
        private Entity FindTacticalConnection(ref SystemState state,Entity player)
        {
            foreach(var (joined,connection) in SystemAPI.Query<RefRO<JoinedClient>>().WithEntityAccess())if(joined.ValueRO.PlayerEntity==player)return connection;
            return Entity.Null;
        }
        private static bool TacticalCanHeal(in PredictedPlayerGhost health)
        {
            if(health.TacticalPoisonSeconds>0 || !health.BodyHealthInitialized)return false;
            for(int p=0;p<RaidHealth.PartCount;p++)
            {float value=RaidHealth.Get(health,(BodyPart)p);if(value>0 && value<RaidHealth.Maximum(health,(BodyPart)p)-.01f)return true;}
            return false;
        }
        private static TacticalServerWorld.Status TacticalStatus(TacticalServerWorld world,Entity entity)
        {if(!world.Statuses.TryGetValue(entity,out var status))world.Statuses[entity]=status=new TacticalServerWorld.Status();return status;}
        private static void TacticalRefreshBleed(TacticalServerWorld.Status status,double until,int owner,ClashDamageSource source)
        {
            // A shorter wound cannot shorten an existing one or steal its attribution.
            if(until<=status.Bleed)return;
            status.Bleed=until;status.BleedOwner=owner;status.BleedSource=source;
        }
        private static Vector3 TacticalEye(GhostGameObject root)
        {var controller=root.GetComponent<PlayerGhost>()?.Controller?.CharacterController;return root.transform.position+Vector3.up*Mathf.Max(.3f,(controller ? controller.height : 1.8f)-.2f);}
        private static Vector3 TacticalDirection(in PredictedPlayerGhost health)=>Quaternion.Euler(health.ControllerState.PitchDegrees,health.ControllerState.YawDegrees,0)*Vector3.forward;
        private static bool TacticalThrow(GhostGameObject root,in PredictedPlayerGhost health,TacticalServerWorld world,out Vector3 position,out Vector3 velocity)
        {
            var eye=TacticalEye(root);var direction=TacticalDirection(health);var scene=root.gameObject.scene.GetPhysicsScene();
            position=eye;velocity=direction*14+Vector3.up*2;
            if(!float.IsFinite(velocity.x)||!float.IsFinite(velocity.y)||!float.IsFinite(velocity.z))return false;
            if(Ballistics.Sweep(scene,eye,eye+direction*.55f,.13f,EnergyGrenadePhysics.HitMask,root.transform,ref world.Hits,out var hit))position=eye+direction*Mathf.Max(0,hit.distance-.02f);
            else position+=direction*.55f;
            int count=scene.OverlapSphere(position,.13f,world.Overlaps,EnergyGrenadePhysics.HitMask,QueryTriggerInteraction.Ignore);
            if(count==world.Overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!world.Overlaps[i].transform.IsChildOf(root.transform))return false;
            return true;
        }
        private static bool TacticalPlacement(GhostGameObject root,in PredictedPlayerGhost health,TacticalServerWorld world,out Vector3 position)
        {
            position=default;var scene=root.gameObject.scene.GetPhysicsScene();var eye=TacticalEye(root);var direction=TacticalDirection(health);
            int mask=LayerMask.GetMask("Default","Ground");
            if(!scene.Raycast(eye,direction,out var hit,3,mask,QueryTriggerInteraction.Ignore)||hit.normal.y<.7f ||
                Mathf.Abs(hit.point.y-root.transform.position.y)>.6f || Vector3.Distance(root.transform.position,hit.point)>2.2f)return false;
            var center=hit.point;
            for(int i=0;i<4;i++)
            {var offset=i==0?Vector3.right:i==1?Vector3.left:i==2?Vector3.forward:Vector3.back;
             if(!scene.Raycast(center+offset*.25f+Vector3.up*.2f,Vector3.down,out var support,.4f,mask,QueryTriggerInteraction.Ignore)||Mathf.Abs(support.point.y-center.y)>.12f)return false;}
            position=center+Vector3.up*.05f;
            int count=scene.OverlapSphere(position+Vector3.up*.15f,.12f,world.Overlaps,mask,QueryTriggerInteraction.Ignore);
            return count==0 && EnergyGrenadePhysics.ClearSight(scene,eye,position+Vector3.up*.15f,root.transform);
        }
        private static bool TacticalInteract(GhostGameObject root,in PredictedPlayerGhost health,TacticalServerWorld.Object target,TacticalItemAction action)
        {
            if(target==null || target.Scene!=root.gameObject.scene.GetPhysicsScene() ||
                action==TacticalItemAction.Disarm && target.Phase!=TacticalWorldPhase.Armed ||
                action==TacticalItemAction.Resupply && (target.Phase!=TacticalWorldPhase.Supply || target.Energy<=0))return false;
            var eye=TacticalEye(root);var delta=target.Position+Vector3.up*.15f-eye;
            return delta.magnitude<=TacticalItemRules.InteractionRange && Mathf.Abs(target.Position.y-root.transform.position.y)<.7f &&
                Vector3.Dot(delta.normalized,TacticalDirection(health))>.6f && EnergyGrenadePhysics.ClearSight(target.Scene,eye,target.Position+Vector3.up*.15f,root.transform);
        }
        private static bool TacticalWithin(TacticalServerWorld.Object area,Vector3 point,float targetRadius,PhysicsScene scene,Transform target)
        {
            var delta=point-area.Position;float radius=area.Definition.Radius+targetRadius;
            if(scene!=area.Scene || Mathf.Abs(delta.y)>1.5f || new Vector2(delta.x,delta.z).sqrMagnitude>radius*radius)return false;
            // Use a low ray to stop wire/cloud transfer through both walls and floor slabs.
            var origin=area.Position+Vector3.up*.2f;var end=point+Vector3.up*.2f;
            return EnergyGrenadePhysics.ClearSight(scene,origin,end,target);
        }
        private static bool TacticalChoked(TacticalServerWorld world,Vector3 point,PhysicsScene scene,double now)
        {foreach(var area in world.Objects)if(area.Phase==TacticalWorldPhase.Choke && now<area.Until && TacticalWithin(area,point,0,scene,null))return true;return false;}
        private void TacticalDetonate(ref SystemState state,EntityCommandBuffer ecb,TacticalServerWorld world,TacticalServerWorld.Object obj,double now,uint tick)
        {
            if(obj.Definition.Kind==TacticalItemKind.Smoke)
            {
                obj.Phase=TacticalWorldPhase.Smoke;obj.Velocity=Vector3.zero;
                obj.Until=now+obj.Definition.Lifetime;obj.NextSnapshot=0;return;
            }
            if(obj.Definition.Kind==TacticalItemKind.Fire || obj.Definition.Kind==TacticalItemKind.Choke)
            {
                if(obj.Definition.Kind==TacticalItemKind.Fire && TacticalChoked(world,obj.Position,obj.Scene,now)){obj.Until=now;return;}
                obj.Phase=obj.Definition.Kind==TacticalItemKind.Fire ? TacticalWorldPhase.Fire : TacticalWorldPhase.Choke;
                obj.Velocity=Vector3.zero;obj.Until=now+obj.Definition.Lifetime;obj.NextSnapshot=0;return;
            }
            if(obj.Definition.Kind!=TacticalItemKind.Dynamite && obj.Definition.Kind!=TacticalItemKind.Frag)return;
            foreach(var victim in world.Victims)
            {
                if(!TacticalCanBeHurt(ref state,victim))continue;
                var link=state.EntityManager.GetComponentObject<GhostGameObjectLink>(victim).LinkedInstance;
                if(!link || link.gameObject.scene.GetPhysicsScene()!=obj.Scene)continue;
                var point=link.transform.position+Vector3.up*.8f;
                float damage=TacticalItemRules.BlastDamage(obj.Definition.Kind,Vector3.Distance(obj.Position,point));
                if(damage<=0 || !EnergyGrenadePhysics.ClearSight(obj.Scene,obj.Position,point,link.transform))continue;
                float actual=BountyClashDamage.Apply(state.World,victim,obj.Source,BodyPart.Auto,damage,tick,RaidDamageKind.Explosion);
                if(actual>0 && obj.Definition.Kind==TacticalItemKind.Frag)
                    TacticalRefreshBleed(TacticalStatus(world,victim),now+TacticalItemRules.BleedDuration,obj.Owner,obj.Source);
            }
            if(obj.Definition.Kind==TacticalItemKind.Dynamite)
                foreach(var other in world.Objects)
                    if(other!=obj && (other.Phase==TacticalWorldPhase.Wire || other.Phase==TacticalWorldPhase.Armed) &&
                        other.Scene==obj.Scene && Vector3.Distance(other.Position,obj.Position)<=8 &&
                        EnergyGrenadePhysics.ClearSight(obj.Scene,obj.Position,other.Position+Vector3.up*.2f,null))other.Until=now;
            obj.Phase=TacticalWorldPhase.Explosion;obj.Until=now+.6;obj.NextSnapshot=0;TacticalBroadcast(ecb,world,obj,false);
        }
        private void TacticalArea(ref SystemState state,TacticalServerWorld world,TacticalServerWorld.Object obj,double now,uint tick)
        {
            if(obj.Phase!=TacticalWorldPhase.Armed && obj.Phase!=TacticalWorldPhase.Fire && obj.Phase!=TacticalWorldPhase.Poison &&
                obj.Phase!=TacticalWorldPhase.Wire && obj.Phase!=TacticalWorldPhase.Choke)return;
            foreach(var victim in world.Victims)
            {
                if(!TacticalCanBeHurt(ref state,victim))continue;
                var root=state.EntityManager.GetComponentObject<GhostGameObjectLink>(victim).LinkedInstance;
                if(!root)continue;var pos=root.transform.position;
                if(!TacticalWithin(obj,pos,0,root.gameObject.scene.GetPhysicsScene(),root.transform))continue;
                if(obj.Phase==TacticalWorldPhase.Armed)
                {
                    if(now<obj.ArmAt || Mathf.Abs(pos.y-obj.Position.y)>TacticalItemRules.TrapVerticalTolerance)continue;
                    obj.Phase=obj.Definition.Kind==TacticalItemKind.AlertTrap ? TacticalWorldPhase.Alarm :
                        obj.Definition.Kind==TacticalItemKind.WireTrap ? TacticalWorldPhase.Wire : TacticalWorldPhase.Poison;
                    obj.Until=now+(obj.Phase==TacticalWorldPhase.Alarm ? 8 : obj.Phase==TacticalWorldPhase.Wire ? 45 : 30);obj.NextSnapshot=0;
                }
                var status=TacticalStatus(world,victim);
                if(obj.Phase==TacticalWorldPhase.Choke){status.Fire=status.Poison=0;status.Cough=now+1;}
                else if(obj.Phase==TacticalWorldPhase.Fire && !TacticalChoked(world,pos,obj.Scene,now))
                {status.Fire=now+TacticalItemRules.BurningDuration;status.FireOwner=obj.Owner;status.FireSource=obj.Source;}
                else if(obj.Phase==TacticalWorldPhase.Poison && status.Antidote<=now && !TacticalChoked(world,pos,obj.Scene,now))
                {status.Poison=now+2;status.PoisonOwner=obj.Owner;status.PoisonSource=obj.Source;}
                else if(obj.Phase==TacticalWorldPhase.Wire)
                {status.Wire=now+.5;TacticalRefreshBleed(status,now+TacticalItemRules.BleedDuration,obj.Owner,obj.Source);}
            }
        }
        private void TacticalTickStatuses(ref SystemState state,TacticalServerWorld world,double now,float dt,uint tick)
        {
            world.Scratch.Clear();
            foreach(var pair in world.Statuses)
            {
                var victim=pair.Key;var status=pair.Value;
                if(!SystemAPI.Exists(victim)||!SystemAPI.HasComponent<PredictedPlayerGhost>(victim)) {world.Scratch.Add(victim);continue;}
                var health=SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
                bool canHurt=health.CurrentHealth>0 && (!health.ClashEnabled || health.ClashLife==ClashLife.Alive);
                if(!canHurt){status.Fire=status.Poison=status.Bleed=status.Wire=status.Cough=0;}
                health.TacticalWireSeconds=TacticalRemaining(status.Wire,now);health.TacticalCoughSeconds=TacticalRemaining(status.Cough,now);
                health.TacticalPoisonSeconds=TacticalRemaining(status.Poison,now);SystemAPI.SetComponent(victim,health);
                if(!canHurt)continue;
                bool treating=false;
                foreach(var pending in world.Uses.Values)if(pending.Player==victim && pending.Action==TacticalItemAction.Extinguish){treating=true;break;}
                if(status.Fire>now)BountyClashDamage.Apply(state.World,victim,status.FireSource,BodyPart.Auto,8*dt,tick,RaidDamageKind.Fire);
                if(status.Poison>now && status.Antidote<=now)BountyClashDamage.Apply(state.World,victim,status.PoisonSource,BodyPart.Auto,6*dt,tick,RaidDamageKind.Poison);
                if(status.Bleed>now)BountyClashDamage.Apply(state.World,victim,status.BleedSource,BodyPart.Auto,4*dt,tick,RaidDamageKind.Bleed);
                if(treating && status.Poison<=now)
                {
                    // Damage still happens. Accept only this operation's known status damage as
                    // the new baseline, provided there was no external impact before this update.
                    // Shield sequence detects fully absorbed hits within the same tick too.
                    // Start/cancel never pauses damage.
                    var after=SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
                    foreach(var pending in world.Uses.Values)
                        if(pending.Player==victim && pending.Action==TacticalItemAction.Extinguish &&
                            pending.Hit==health.LastHitTick && pending.ShieldHit==health.ShieldHitSequence &&
                            health.CurrentHealth>=pending.Health-.001f && pending.LastPart==health.LastHitPart && Mathf.Abs(pending.LastDamage-health.LastDamageAmount)<.001f)
                        {pending.Hit=after.LastHitTick;pending.ShieldHit=after.ShieldHitSequence;pending.Health=after.CurrentHealth;pending.LastDamage=after.LastDamageAmount;pending.LastPart=after.LastHitPart;}
                }
                if(!status.Sprint.Initialized && Math.Max(Math.Max(Math.Max(status.Fire,status.Poison),Math.Max(status.Bleed,status.Stamina)),Math.Max(status.Antidote,Math.Max(status.Wire,status.Cough)))<=now)world.Scratch.Add(victim);
            }
            foreach(var entity in world.Scratch)world.Statuses.Remove(entity);
        }
        private bool TacticalCanBeHurt(ref SystemState state,Entity victim)
        {
            if(!SystemAPI.Exists(victim)||!SystemAPI.HasComponent<PredictedPlayerGhost>(victim))return false;
            var health=SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
            // An alive disconnected body cannot act but remains vulnerable. Downed bodies
            // keep the first-version no-burning/no-execution policy.
            return health.CurrentHealth>0 && (!health.ClashEnabled || health.ClashLife==ClashLife.Alive);
        }
        private void TacticalTickSprint(ref SystemState state,TacticalServerWorld world,double now,float dt)
        {
            if(!MoonRaidMap.Active)return;
            foreach(var (session,joined) in SystemAPI.Query<RefRO<RaidSession>,RefRO<JoinedClient>>())
            {
                var player=joined.ValueRO.PlayerEntity;
                if(!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player))continue;
                var health=SystemAPI.GetComponent<PredictedPlayerGhost>(player);var status=TacticalStatus(world,player);
                bool allowed=session.ValueRO.Phase==RaidPhase.Active && health.CurrentHealth>0 && (!health.ClashEnabled || health.ClashLife==ClashLife.Alive);
                bool canRun=allowed && BountyClashDamage.CanAct(state.EntityManager,player);
                float3 position=SystemAPI.HasComponent<LocalTransform>(player) ? SystemAPI.GetComponent<LocalTransform>(player).Position : health.ControllerState.CurrentPosition;
                float3 travel=position-status.SprintPosition;float threshold=Mathf.Max(.005f,dt*.15f);
                bool moved=status.SprintPositionKnown && travel.x*travel.x+travel.z*travel.z>threshold*threshold;
                bool sprinting=canRun && health.ControllerState.Sprinting && health.ControllerState.MovementSpeed>.1f && moved;
                status.SprintPosition=position;status.SprintPositionKnown=allowed;
                TacticalSprintRules.Tick(ref status.Sprint,dt,sprinting,status.Stamina>now,allowed,health.ClashEnabled ? 0 : health.CarriedWeight);
                health.TacticalSprintStamina=status.Sprint.Value;health.TacticalSprintExhausted=status.Sprint.Exhausted;
                SystemAPI.SetComponent(player,health);
            }
        }
        private static void TacticalBroadcast(EntityCommandBuffer ecb,TacticalServerWorld world,TacticalServerWorld.Object obj,bool removed)
        {
            var entity=ecb.CreateEntity();ecb.AddComponent(entity,new TacticalWorldStateRpc {Epoch=world.Epoch,Id=obj?.Id ?? 0,Sequence=++world.Sequence,
                Kind=obj?.Definition.Kind ?? default,Phase=removed ? TacticalWorldPhase.Removed : obj.Phase,
                Position=obj?.Position ?? default,Velocity=obj?.Velocity ?? default,Radius=obj?.Definition.Radius ?? 0,Energy=obj?.Energy ?? 0,
                Remaining=obj==null ? 0 : (float)Math.Max(0,obj.Until-world.Now)});
            ecb.AddComponent(entity,new SendRpcCommandRequest());
        }
    }
}
