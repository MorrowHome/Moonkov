using System;
using System.Linq;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GrenadeHandlingChecks
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks++; }
    private sealed class Fixture : IDisposable
    {
        public World World;
        public EntityManager Em => World.EntityManager;
        public Entity Player, Connection, Clock;
        public RaidInventoryState Inventory;
        public TacticalServerWorld Tactical;
        public EnergyGrenadeServerSystem System;
        public GameObject Root;
        public string Item;
        public double Now;
        public uint TickId = 100, Request;
        private readonly Scene scene;
        private readonly MoonRaidMap previous;
        public Fixture(string code = "energy_grenade")
        {
            previous = MoonRaidMap.Active; scene = EditorSceneManager.NewPreviewScene();
            var map = new GameObject("Grenade fixture map"); map.SetActive(false); SceneManager.MoveGameObjectToScene(map, scene);
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, map.AddComponent<MoonRaidMap>());
            World = new World("Held grenade authority fixture", WorldFlags.GameServer);
            System = World.GetOrCreateSystemManaged<EnergyGrenadeServerSystem>(); Clock = Em.CreateEntity(typeof(NetworkTime));
            Root = new GameObject("Thrower"); SceneManager.MoveGameObjectToScene(Root, scene);
            var ghost = Root.AddComponent<GhostGameObject>();
            Player = Em.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner));
            Em.AddComponentObject(Player, new GhostGameObjectLink { LinkedInstance = ghost });
            var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); Em.SetComponentData(Player, state);
            Em.SetComponentData(Player, new GhostOwner { NetworkId = 7 });
            var graph = InventoryGraph.Create(false);
            var added = graph.AddSupply(code, 1, InventoryCatalog.Pockets);
            if (added != InventoryError.None) added = graph.AddSupply(code, 1, graph.Equipped("ChestRig").Id);
            if (added != InventoryError.None)
            { Dispose(); throw new InvalidOperationException("Fixture item does not fit: " + code); }
            Item = graph.Items.Single(item => item.Code == code).Id;
            Inventory = new RaidInventoryState { Graph = graph, RaidId = 4 };
            Connection = Em.CreateEntity(typeof(RaidSession), typeof(JoinedClient));
            Em.SetComponentData(Connection, new RaidSession { RaidId = 4, Phase = RaidPhase.Active });
            Em.SetComponentData(Connection, new JoinedClient { PlayerEntity = Player }); Em.AddComponentObject(Connection, Inventory);
            Tactical = new TacticalServerWorld(); Em.AddComponentObject(Em.CreateEntity(), Tactical);
            Tick();
        }
        public PredictedPlayerGhost Health { get => Em.GetComponentData<PredictedPlayerGhost>(Player); set => Em.SetComponentData(Player, value); }
        public GrenadePhase Phase => (GrenadePhase)Health.GrenadePhase;
        public int Stock => Inventory.Graph.Find(Item)?.Quantity ?? 0;
        public void Tick(float dt = 1f/60)
        {
            Now += dt; World.SetTime(new TimeData(Now, dt)); Em.SetComponentData(Clock, new NetworkTime { ServerTick = new NetworkTick(++TickId) }); System.Update();
        }
        public void Wait(float seconds) { double end = Now + seconds; while (Now < end - .000001) Tick((float)Math.Min(1.0/60, end-Now)); }
        public void Send(GrenadeCommand command, bool low = false, int? version = null, uint? hold = null, uint? replay = null, int raid = 4)
        {
            var rpc = Em.CreateEntity();
            Em.AddComponentData(rpc, new RaidGrenadeThrowRpc { Command = command, RaidId = raid, ItemId = Item,
                ExpectedVersion = version ?? Inventory.Graph.Version, RequestId = replay ?? ++Request,
                HoldId = hold ?? Health.GrenadeHoldId, Underhand = low });
            Em.AddComponentData(rpc, new ReceiveRpcCommandRequest { SourceConnection = Connection }); Tick();
        }
        public void Equip() { Send(GrenadeCommand.Equip); Wait(GrenadeHandling.DrawSeconds); Check(Phase == GrenadePhase.Safe, "Equip reached safe hold"); }
        public EnergyGrenadeStateRpc[] Poses()
        { using var query = Em.CreateEntityQuery(typeof(EnergyGrenadeStateRpc)); using var poses = query.ToComponentDataArray<EnergyGrenadeStateRpc>(Allocator.Temp); return poses.ToArray(); }
        public Entity Target(string name, Vector3 position)
        {
            var root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=position;
            var target=Em.CreateEntity(typeof(PredictedPlayerGhost));var health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);
            Em.SetComponentData(target,health);Em.AddComponentObject(target,new GhostGameObjectLink {LinkedInstance=root.AddComponent<GhostGameObject>()});return target;
        }
        public GameObject Wall(Vector3 position, Vector3 size)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(wall,scene);
            wall.transform.position=position;wall.transform.localScale=size;Physics.SyncTransforms();return wall;
        }
        public void Dispose()
        { World.Dispose(); typeof(MoonRaidMap).GetProperty("Active").SetValue(null, previous); EditorSceneManager.ClosePreviewScene(scene); }
    }
    [MenuItem("Tools/Moonkov/Grenades/Check held grenade authority")]
    public static string Run()
    {
        checks = 0;
        using (var f = new Fixture())
        {
            int version = f.Inventory.Graph.Version;
            f.Equip(); f.Wait(8);
            Check(f.Stock == 1 && f.Inventory.Graph.Version == version && f.Poses().Length == 0, "Unprimed hold neither spends nor launches");
            Check(f.Phase == GrenadePhase.Safe && f.Health.GrenadeFuseRemaining == 0, "Safe hold has no fuse");
            Check(f.Inventory.InventoryMoveBlocked && f.Inventory.ActionPending && RaidHealth.HandsBusy(f.Health), "Hold owns hands and reserves inventory");
            f.Send(GrenadeCommand.Release); Check(f.Phase == GrenadePhase.Safe && f.Stock == 1, "Release cannot bypass priming");
            f.Send(GrenadeCommand.Stow); f.Wait(GrenadeHandling.StowSeconds);
            Check(f.Phase == GrenadePhase.None && f.Stock == 1 && !f.Inventory.GrenadePending, "Stow returns safe grenade without spending");
            f.Send(GrenadeCommand.Equip, version: version-1); Check(f.Inventory.Error == InventoryError.Stale && f.Phase == 0, "Stale equip rejected");
            f.Send(GrenadeCommand.Equip, raid: 3); Check(f.Phase == 0, "Wrong raid rejected");
            var h = f.Health; h.ControllerState.IsReloadingState = true; f.Health = h;
            f.Send(GrenadeCommand.Equip); Check(f.Phase == 0, "Reload cannot equip a grenade");
        }
        using (var f = new Fixture())
        {
            f.Inventory.MedicalItemId="fixture-treatment";
            f.Equip();Check(f.Inventory.MedicalItemId==null && f.Health.LastGrenadeShotTick!=0,"Equip interrupts prior hand action without consumption");
        }
        foreach (bool low in new[] { false, true }) using (var f = new Fixture())
        {
            f.Equip(); f.Send(GrenadeCommand.Prime, hold: f.Health.GrenadeHoldId+1);
            Check(f.Stock == 1 && f.Phase == GrenadePhase.Safe, "Wrong hold token cannot prime");
            f.Send(GrenadeCommand.Prime, low); uint primeRequest = f.Request;
            Check(f.Stock == 0 && f.Phase == GrenadePhase.Priming, "Prime consumes exactly one");
            f.Send(GrenadeCommand.Prime, low, replay: primeRequest);
            Check(f.Stock == 0 && f.Phase == GrenadePhase.Priming, "Duplicate prime stays on same timeline");
            f.Inventory.CancelActions(); Check(f.Inventory.GrenadePending, "Generic cancel cannot erase live grenade");
            f.Wait(GrenadeHandling.PrimeSeconds + 1.5f);
            Check(f.Phase == GrenadePhase.Live && f.Health.GrenadeFuseRemaining < 1.6f, "Holding cooks the fuse");
            float remaining = f.Health.GrenadeFuseRemaining;
            f.Send(GrenadeCommand.Stow); Check(f.Phase == GrenadePhase.Live, "Cannot put live grenade back");
            f.Send(GrenadeCommand.Release); f.Wait(GrenadeHandling.ReleaseSeconds-.04f);
            Check(f.Poses().Length == 0, "No projectile before the hand release marker");
            f.Wait(.05f);
            var poses = f.Poses(); Check(poses.Length > 0 && poses[0].Exploded == 0, "Release creates projectile");
            Check(poses[0].Remaining < remaining-.25f && poses[0].Remaining > remaining-.45f, "Throw preserves cooked fuse");
            Check(Mathf.Abs(poses[0].Velocity.z-(low ? GrenadeHandling.LowSpeed : EnergyGrenadeRules.Speed)) < .01f, "Overhand and low throw speeds differ");
            f.Wait(GrenadeHandling.RecoverySeconds); Check(f.Phase == 0 && !f.Inventory.GrenadePending, "Follow-through releases hands");
            Check(f.Stock == 0, "Throw does not consume twice");
        }
        using (var f = new Fixture())
        {
            f.Equip(); f.Send(GrenadeCommand.Prime); f.Send(GrenadeCommand.Release);
            f.Wait(GrenadeHandling.PrimeSeconds-.1f); Check(f.Poses().Length == 0, "Quick tap still completes activation");
            f.Wait(.5f); Check(f.Poses().Count(p => p.Exploded == 0) > 0, "Early release is buffered through activation");
        }
        using (var f = new Fixture())
        {
            f.Equip(); f.Send(GrenadeCommand.Prime); f.Wait(GrenadeHandling.PrimeSeconds+EnergyGrenadeRules.Fuse+.1f);
            Check(f.Poses().Count(p => p.Exploded != 0) == 1, "Overcooking detonates once in the hand");
            Check(f.Health.CurrentHealth == 0 && f.Phase == 0 && f.Stock == 0, "In-hand blast damages owner and clears hold");
        }
        foreach (bool disconnect in new[] { false, true }) using (var f = new Fixture())
        {
            f.Equip(); f.Send(GrenadeCommand.Prime); f.Wait(.8f);
            float remaining = f.Health.GrenadeFuseRemaining;
            if (disconnect) f.Em.AddComponent<NetworkStreamRequestDisconnect>(f.Connection);
            else { var h=f.Health; h.CurrentHealth=0; f.Health=h; }
            f.Tick(); var pose=f.Poses().First();
            Check(pose.Velocity.Equals(Unity.Mathematics.float3.zero) && pose.Remaining < remaining && pose.Remaining > remaining-.04f, "Death/disconnect drops remaining fuse");
            Check(f.Phase == 0 && !f.Inventory.GrenadePending && f.Stock == 0, "Death/disconnect cannot refund primed item");
        }
        using (var f = new Fixture())
        {
            f.Equip();f.Send(GrenadeCommand.Prime);f.Send(GrenadeCommand.Release);f.Wait(GrenadeHandling.PrimeSeconds+GrenadeHandling.ReleaseSeconds+.03f);
            var first=f.Poses().First(p=>p.Exploded==0);
            Vector3 end=Ballistics.Position(first.Position,first.Velocity,EnergyGrenadeRules.Gravity,first.Remaining);
            f.Root.transform.position=end-Vector3.up*.8f;
            var open=f.Target("Exposed victim",end+Vector3.left*2-Vector3.up*.8f);
            var covered=f.Target("Covered victim",end+Vector3.right*2-Vector3.up*.8f);
            f.Wall(end+Vector3.right,new Vector3(.08f,10,10));
            var listener=f.Target("Explosion listener",end+Vector3.forward*20-Vector3.up*.8f);
            f.Em.AddComponentData(listener,LocalTransform.FromPosition(end+Vector3.forward*20-Vector3.up*.8f));
            f.Em.AddComponent<DollSingerEnemy>(listener);f.Em.AddComponent<Simulate>(listener);
            var brain=new DollSingerEnemyBrain {Action=PmcAction.Scavenge};f.Em.AddComponentObject(listener,brain);
            f.Wait(first.Remaining+.1f);
            Check(f.Health.CurrentHealth==0,"Released grenade can kill thrower");
            Check(Mathf.Abs(f.Em.GetComponentData<PredictedPlayerGhost>(open).CurrentHealth-(440-EnergyGrenadeRules.DamageAt(2)))<.1f,"Released blast conserves body damage");
            Check(f.Em.GetComponentData<PredictedPlayerGhost>(covered).CurrentHealth==440,"Cover still blocks released grenade damage");
            var explosion=f.Poses().Single(p=>p.Exploded!=0);
            Check(brain.MissionAlarmId==RaidEnvironmentalNoise.ExplosionId(true,0,explosion.Id)&&brain.Target==Entity.Null,"Released explosion still supplies anonymous AI hearing clue");
        }
        using(var f=new Fixture())
        {
            f.Equip();f.Send(GrenadeCommand.Prime);f.Wait(.7f);
            f.Wall(new Vector3(0,1.35f,.17f),new Vector3(2,3,.06f));
            f.Send(GrenadeCommand.Release);f.Wait(GrenadeHandling.ReleaseSeconds+.02f);
            var pose=f.Poses().First();Check(pose.Position.z<.14f&&pose.Velocity.Equals(Unity.Mathematics.float3.zero),"Wall clips release without cancelling live grenade");
            Check(f.Stock==0,"Blocked release cannot refund grenade");
        }
        foreach (var definition in TacticalItemRules.Definitions.Where(d => d.Throwable)) using (var f=new Fixture(definition.Code))
        {
            f.Equip(); f.Send(GrenadeCommand.Prime); Check(f.Tactical.GrenadeReservations==1, "Tactical slot reserved before consumption: "+definition.Code);
            f.Wait(GrenadeHandling.PrimeSeconds+.7f); float remaining=f.Health.GrenadeFuseRemaining;
            f.Send(GrenadeCommand.Drop);
            Check(f.Tactical.Objects.Count==1 && f.Tactical.Objects[0].Definition.Code==definition.Code, "Generic flow creates correct tactical type: "+definition.Code);
            Check(Math.Abs(f.Tactical.Objects[0].Until-f.Now-remaining)<.04f && f.Tactical.GrenadeReservations==0, "Tactical fuse and capacity survive release: "+definition.Code);
            Check(f.Stock==0 && f.Tactical.Objects[0].Velocity==Vector3.zero, "Drop spends once: "+definition.Code);
        }
        using (var f = new Fixture("frag_bomb"))
        {
            f.Equip(); f.Tactical.GrenadeReservations=TacticalItemRules.MaxWorldObjects;
            f.Send(GrenadeCommand.Prime); Check(f.Inventory.Error==InventoryError.Full && f.Stock==1, "Capacity rejection never spends an item");
        }
        foreach (GrenadePhase phase in Enum.GetValues(typeof(GrenadePhase)))
        {
            if(phase==GrenadePhase.None)continue;
            var health=new PredictedPlayerGhost {CurrentHealth=100,GrenadePhase=(byte)phase};
            var input=new PlayerInput { InputFlags=uint.MaxValue, MoveInput=new Unity.Mathematics.float2(0,1)};
            var filtered=RaidHealth.RestrictInput(health,input);
            Check(!filtered.Shoot&&!filtered.FireHeld&&!filtered.Aim&&!filtered.Reload&&!filtered.EquipHalo&&!filtered.EquipPistol&&!filtered.EquipRevolver&&!filtered.HaloDefense&&!filtered.HaloTransfer, "Held state blocks weapon input: "+phase);
            Check(filtered.MoveInput.y==1 && !HaloEnergyRules.CanDefend(health) && !HaloTransferRules.CanSend(health), "Hold preserves movement and blocks halo actions: "+phase);
        }
        return "PASS "+checks+" held grenade authority checks";
    }
}
