using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.DollSinger;

/// <summary>Shared prediction/authority rules and actual ECS damage in isolated physics scenes.</summary>
public static class HaloArsenalValidation
{
    private static readonly List<string> Report=new List<string>();
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Halo Arsenal Rules")]
    public static void Run()
    {
        Require(EditorApplication.isPlaying,"Run these physics/health checks in Play mode.");
        Report.Clear();
        var registry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        foreach(var entry in HaloArsenal.Entries) CheckEquipment(registry,entry);
        CheckBurst(registry); CheckSpread(registry); CheckBlast(registry); CheckProjectiles(registry); CheckLocalDriver(registry);
        HaloModuleChecks.Run();
        Report.Add("Legacy halo module rules passed. No live Host/Client or standalone build is implied by these checks.");
        Directory.CreateDirectory("Temp/HaloArsenal");File.WriteAllLines("Temp/HaloArsenal/rules.txt",Report);
        Debug.Log("HALO ARSENAL RULES PASS\n"+string.Join("\n",Report));
    }
    private static void CheckLocalDriver(WeaponRegistry registry)
    {
        var staging=new GameObject("Inactive standalone arsenal check");staging.SetActive(false);
        try
        {
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab"),staging.transform);
            var halo=root.GetComponentInChildren<DollSingerHaloAim>(true);var driver=halo.GetComponent<LocalHaloArsenalDriver>();Require(driver,"Local arsenal driver binding");
            foreach(var entry in HaloArsenal.Entries.Where(e=>e.BurstCount>1))
            {
                driver.CancelBurst();driver.Step(halo,entry.Gesture,false,false,false,true,.3f);
                int shots=0;
                for(int frame=0;frame<90;frame++)if(driver.Step(halo,entry.Gesture,true,frame==0,false,true,1f/60))shots++;
                Require(shots==entry.BurstCount&&driver.Ammo(entry.Gesture)==entry.Capacity-entry.BurstCount,entry.Code+" standalone held trigger");
                driver.CancelBurst();driver.Step(halo,DollSingerHaloAim.HaloWeapon.Cicada,false,false,false,true,.3f);driver.CancelBurst();
                Require(driver.Ammo(entry.Gesture)==entry.Capacity-entry.BurstCount,entry.Code+" standalone persisted magazine");
                for(int frame=0;frame<180;frame++)driver.Step(halo,entry.Gesture,false,false,frame==0,true,1f/60);
                Require(driver.Ammo(entry.Gesture)==entry.Capacity&&!driver.Reloading,entry.Code+" standalone recharge");
                Report.Add(entry.Code+": standalone player held trigger fires exactly "+entry.BurstCount+"; slot switch preserves ammo and finite-battery reload restores capacity. Cosmetic character, not authoritative combat.");
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(staging);}
    }
    private static PredictedPlayerGhost Ready(WeaponData weapon,uint id,int ammo)=>new PredictedPlayerGhost
    {CurrentHealth=100,MaxHealth=100,EquippedWeaponID=id,CurrentAmmo=ammo,WeaponCooldown=10,EquippedWeaponSlot=1};
    private static void CheckEquipment(WeaponRegistry registry,HaloArsenal.Entry entry)
    {
        var weapon=registry.GetWeaponData(entry.Id);
        Require(weapon && weapon.MagazineSize==entry.Capacity && weapon.EnergyPerRound==entry.Energy && weapon.ProjectileGhostPrefab!=null,entry.Code+" data/projectile");
        Require(weapon.Behavior==(entry.BlastRadius>0?ProjectileBehavior.AreaOfEffect:ProjectileBehavior.DirectDamage)&&
            (entry.BlastRadius==0||Mathf.Approximately(weapon.AoeRadius,entry.BlastRadius)),entry.Code+" blast configuration");
        Require(DollSingerWeapons.WeaponId(entry.Code)==entry.Id && InventoryCatalog.Get(entry.Code)?.Kind==ItemKind.LongGun,entry.Code+" mapping");
        foreach(string path in new[]{"Assets/DollSinger/Prefabs/DollSingerPlayer.prefab","Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"})
        {
            var halo=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<DollSingerHaloAim>(true);
            var visual=halo.arsenalVisuals[(int)entry.Id-6];Require(visual && visual.weapon==entry.Gesture && visual.core.sprite && visual.accent.sprite && visual.nodes.All(n=>n&&n.sprite),entry.Code+" prefab sprites");
        }
        var graph=InventoryGraph.Create(starter:false);graph.AddSupply("dust",200,"stash");
        Require(ShopRules.Apply(graph,new ShopCommand{RequestId=Guid.NewGuid().ToString(),Operation=ShopOperation.Buy,Code=entry.Code,Quantity=1,ExpectedVersion=graph.Version},out var bought)==null,entry.Code+" purchase");
        Require(bought.Count("dust")==200-ShopCatalog.Find(entry.Code).BuyDust,entry.Code+" price");
        var item=bought.Items.Single(i=>i.Code==entry.Code);
        Require(!ShopCatalog.CanClaim(bought),entry.Code+" duplicate starter prevention");
        Require(bought.TryApply(new InventoryCommand{ExpectedVersion=bought.Version,ItemId=item.Id,Parent="equipment",Region="Primary"})==InventoryError.None,entry.Code+" equip");
        var state=new PredictedPlayerGhost{CurrentHealth=100,EquippedWeaponID=DollSingerWeapons.None};DollSingerWeapons.SyncEquipment(ref state,bought,registry);
        Require(state.EquippedWeaponID==entry.Id && state.CurrentAmmo==entry.Capacity,entry.Code+" initial magazine");
        state.CurrentAmmo=entry.Capacity-2;DollSingerWeapons.SaveActiveAmmo(ref state,bought);
        // Move the exact item, then select it again: no magazine reset or login grant.
        Require(bought.TryApply(new InventoryCommand{ExpectedVersion=bought.Version,ItemId=item.Id,Parent="equipment",Region="Secondary"})==InventoryError.None,entry.Code+" move slot");
        DollSingerWeapons.SyncEquipment(ref state,bought,registry);
        Require(state.EquippedWeaponID==entry.Id && state.CurrentAmmo==entry.Capacity-2 && bought.Clone().Find(item.Id).LoadedAmmo==entry.Capacity-2,entry.Code+" persisted ammo");
        state.CurrentAmmo=0;DollSingerWeapons.SaveActiveAmmo(ref state,bought);
        bought.AddSupply("cells",1,"pockets",cellCharge:entry.Energy*2+1);
        Require(bought.TryRecharge(0,entry.Capacity,entry.Energy,out int target)&&target==2&&bought.CellEnergy()==1,entry.Code+" partial energy charge");
        state.ReloadTargetAmmo=target;DollSingerWeapons.CompleteReload(ref state,weapon);DollSingerWeapons.SaveActiveAmmo(ref state,bought);
        Require(state.CurrentAmmo==2 && !bought.TryRecharge(2,entry.Capacity,entry.Energy,out _),entry.Code+" no free refill");
        Report.Add(entry.Code+": purchased at "+ShopCatalog.Find(entry.Code).BuyDust+" dust; primary/secondary equip, exact magazine persistence, 2-round partial charge, insufficient energy and both production sprite bindings passed.");
    }
    private static List<(float time,int sequence)> Simulate(WeaponData weapon,ref PredictedPlayerGhost state,Func<int,bool> trigger,int frames)
    {
        var result=new List<(float,int)>();
        for(int frame=0;frame<frames;frame++)
        {
            HaloModuleFire.AdvanceTimers(ref state,1f/60);
            if(HaloModuleFire.TryFire(ref state,weapon,trigger(frame),true,false))
            { result.Add((frame/60f,state.BurstSequence));state.CurrentAmmo--;HaloModuleFire.CommitShot(ref state,weapon,1f/60); }
        }
        return result;
    }
    private static void CheckBurst(WeaponRegistry registry)
    {
        foreach(var entry in HaloArsenal.Entries.Where(e=>e.BurstCount>1))
        {
        var weapon=registry.GetWeaponData(entry.Id);
        foreach(int ammo in new[]{1,2,weapon.MagazineSize})
        {
            var state=Ready(weapon,entry.Id,ammo);var shots=Simulate(weapon,ref state,_=>true,90);
            Require(shots.Count==Mathf.Min(ammo,weapon.BurstCount)&&state.CurrentAmmo==ammo-shots.Count,"Trine held trigger / partial "+ammo);
            Require(shots.Select(s=>s.sequence).SequenceEqual(Enumerable.Range(0,shots.Count)),"Trine sequence");
            for(int i=1;i<shots.Count;i++)Require(shots[i].time-shots[i-1].time>=weapon.BurstInterval-1f/60-.0001f,entry.Code+" interval");
        }
        var original=Ready(weapon,entry.Id,weapon.MagazineSize);var replay=original;
        var a=Simulate(weapon,ref original,f=>f<20||f>=40,90);var b=Simulate(weapon,ref replay,f=>f<20||f>=40,90);
        Require(a.Count==weapon.BurstCount*2&&a.SequenceEqual(b)&&original.CurrentAmmo==replay.CurrentAmmo,"Trine deterministic restored prediction state");
        foreach(string cancel in new[]{"death","reload","switch","aim"})
        {
            var state=Ready(weapon,entry.Id,weapon.MagazineSize);Require(HaloModuleFire.TryFire(ref state,weapon,true,true,false),"Trine starts");
            HaloModuleFire.CommitShot(ref state,weapon,1f/60);HaloModuleFire.AdvanceTimers(ref state,.1f);
            if(cancel=="death")state.CurrentHealth=0;if(cancel=="reload"){state.ControllerState.IsReloadingState=true;state.ReloadTimer=1;}
            Require(!HaloModuleFire.TryFire(ref state,weapon,false,cancel!="aim",cancel=="switch")&&state.BurstRemaining==0,"Trine cancel "+cancel);
        }
        var equip=Ready(weapon,entry.Id,weapon.MagazineSize);HaloModuleFire.BeginEquip(ref equip,weapon);
        Require(!HaloModuleFire.TryFire(ref equip,weapon,true,true,false),"Trine equip gate");
        var buffered=Ready(weapon,entry.Id,weapon.MagazineSize);buffered.WeaponCooldown=weapon.BurstCooldown-.08f;
        var c=Simulate(weapon,ref buffered,f=>f==0,30);Require(c.Count==weapon.BurstCount,"Trine buffered press near cooldown");
        Report.Add(entry.Code+": "+weapon.BurstCount+" actual shots per press; held trigger, 1/2 remaining rounds, sequences, interval, release/repress, short input buffer, equip gate, reload/death/switch/aim cancellation and deterministic restored-state prediction passed at 60 Hz.");
        }
    }
    private static void CheckSpread(WeaponRegistry registry)
    {
        foreach(uint id in HaloArsenal.Entries.Where(e=>e.Spread>0).Select(e=>e.Id))
        {
            var weapon=registry.GetWeaponData(id);var directions=new HashSet<Vector3>();
            for(uint tick=100;tick<140;tick++)for(int p=0;p<weapon.PelletCount;p++)
            {
                var ray=WeaponSpread.Direction(Vector3.forward,weapon,p,tick);directions.Add(ray);
                Require(ray==WeaponSpread.Direction(Vector3.forward,weapon,p,tick)&&Vector3.Angle(ray,Vector3.forward)<=weapon.SpreadDegrees+.01f,"Deterministic spread cone "+id);
            }
            Require(directions.Count>1,"Single projectile spread inactive "+id);
        }
        var thorn=registry.GetWeaponData(10);var state=Ready(thorn,10,8);
        Require(HaloModuleFire.TryFire(ref state,thorn,true,true,false),"Thorn shot");state.CurrentAmmo--;HaloModuleFire.CommitShot(ref state,thorn,1f/60);
        Require(thorn.PelletCount==5&&state.CurrentAmmo==7&&thorn.EnergyPerRound==10,"Thorn one round / five projectiles / 10 energy");
        var sundown=registry.GetWeaponData(11);var point=Ballistics.Position(Vector3.zero,Vector3.forward*sundown.ProjectileSpeed,sundown.ProjectileGravity,1);
        Require(Mathf.Abs(point.y+.81f)<.001f&&Mathf.Abs(point.z-22)<.001f,"Sundown gravity");
        Report.Add("Shared tick-based spread: 40 ticks per weapon, deterministic rays within configured cone. Thorn = 5 directions, 1 magazine round, 10 energy per replacement round. Sundown at 1 s = 22 m forward, 0.81 m drop.");
        var tempest=registry.GetWeaponData(DollSingerWeapons.Tempest);var continuous=Ready(tempest,DollSingerWeapons.Tempest,tempest.MagazineSize);
        Require(Simulate(tempest,ref continuous,_=>true,120).Count==8&&continuous.CurrentAmmo==6,"Tempest sustained cadence");
        Require(Simulate(tempest,ref continuous,_=>true,180).Count==6&&continuous.CurrentAmmo==0,"Tempest empty magazine gate");
        var arc=Ballistics.Position(Vector3.zero,Vector3.forward*tempest.ProjectileSpeed,tempest.ProjectileGravity,1);
        Require(Mathf.Abs(arc.z-34)<.001f&&Mathf.Abs(arc.y+1.62f)<.001f,"Tempest arc");
        Report.Add("Tempest: held fire produces 8 actual shared-rule shots in 2 seconds at 60 Hz, consumes remaining 6 then stops at zero; 1-second arc = 34 m forward / 1.62 m drop.");
    }
    private static void CheckBlast(WeaponRegistry registry)
    {
        var scene=SceneManager.CreateScene("Arsenal blast authority check",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var other=SceneManager.CreateScene("Arsenal separate world",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        using var world=new World("Arsenal authoritative health check");var manager=world.EntityManager;
        try
        {
            Entity Victim(string name,Vector3 position,Scene target,int? owner,bool body=false)
            {
                var root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,target);root.transform.position=position;root.layer=LayerMask.NameToLayer("ServerPlayer");
                root.AddComponent<SphereCollider>().radius=.2f;var extra=new GameObject("second limb collider");extra.transform.SetParent(root.transform,false);extra.layer=root.layer;extra.AddComponent<SphereCollider>().radius=.12f;
                var ghost=root.AddComponent<GhostGameObject>();var health=new PredictedPlayerGhost{CurrentHealth=200,MaxHealth=200};if(body)RaidHealth.Initialize(ref health);
                var entity=manager.CreateEntity(typeof(PredictedPlayerGhost));manager.SetComponentData(entity,health);manager.AddComponentObject(entity,new GhostGameObjectLink{LinkedInstance=ghost});
                if(owner.HasValue)manager.AddComponentData(entity,new GhostOwner{NetworkId=owner.Value});return entity;
            }
            var alien=Victim("Alien without GhostOwner",new Vector3(1,0,0),scene,null);
            var pmc=Victim("PMC with body health",new Vector3(-1,0,0),scene,2,true);
            var shooter=Victim("Shooter immunity",new Vector3(0,1,0),scene,1);
            var hidden=Victim("Victim behind wall",new Vector3(0,0,2.5f),scene,3);
            var isolated=Victim("Victim in another physics scene",new Vector3(1,0,0),other,4);
            var wall=new GameObject("Occluding wall");SceneManager.MoveGameObjectToScene(wall,scene);wall.transform.position=new Vector3(0,0,1.7f);wall.AddComponent<BoxCollider>().size=new Vector3(1,2,.15f);
            Physics.SyncTransforms();scene.GetPhysicsScene().Simulate(.02f);other.GetPhysicsScene().Simulate(.02f);
            var weapon=registry.GetWeaponData(11);int count=ProjectileBlast.Apply(world,scene.GetPhysicsScene(),Vector3.zero,weapon.AoeRadius,weapon.Damage,1,123,out byte flags);
            Require(count==2&&flags==1,"AOE victim count/flags: "+count+" "+flags);
            Require(Mathf.Approximately(manager.GetComponentData<PredictedPlayerGhost>(alien).CurrentHealth,104),"AOE alien damage or duplicate collider");
            Require(Mathf.Approximately(manager.GetComponentData<PredictedPlayerGhost>(pmc).CurrentHealth,344),"AOE body damage conservation");
            foreach(var entity in new[]{shooter,hidden,isolated})Require(manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth==200,"AOE immunity/occlusion/world isolation");
            ProjectileBlast.Apply(world,scene.GetPhysicsScene(),Vector3.zero,weapon.AoeRadius,2000,1,124,out flags);
            Require(flags==3&&manager.GetComponentData<PredictedPlayerGhost>(alien).CurrentHealth==0,"AOE lethal flags");
            Require(ProjectileBlast.Apply(world,scene.GetPhysicsScene(),Vector3.zero,weapon.AoeRadius,96,1,125,out flags)==0,"AOE dead victims ignored");
            RaycastHit[] hits=new RaycastHit[16];Require(Ballistics.Sweep(scene.GetPhysicsScene(),new Vector3(0,0,1.3f),new Vector3(0,0,2.1f),.055f,EnergyGrenadePhysics.HitMask,null,ref hits,out var hit)&&hit.collider.gameObject==wall,"Sundown swept collision with thin wall");
            Report.Add("Actual ECS health + isolated PhysicsScene AOE: unowned alien damaged once despite two colliders, initialized PMC takes exactly 96 total damage, shooter immune, other PMC is damageable, wall occlusion and cross-scene isolation respected, lethal flags and dead victim filtering passed. Actual swept-radius collision hits thin wall.");
        }
        finally
        {
            foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
            foreach(var root in other.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);SceneManager.UnloadSceneAsync(other);
        }
    }
    private static void CheckProjectiles(WeaponRegistry registry)
    {
        var scene=SceneManager.CreateScene("Arsenal real projectile fixture",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        using var world=new World("Arsenal projectile authority",WorldFlags.GameServer);
        var manager=world.EntityManager;world.GetOrCreateSystemManaged<ServerPlayerMovementSystem>();world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            var victim=new GameObject("Unowned alien projectile victim");SceneManager.MoveGameObjectToScene(victim,scene);victim.layer=LayerMask.NameToLayer("ServerPlayer");victim.AddComponent<SphereCollider>().radius=.5f;
            var target=manager.CreateEntity(typeof(GhostGameObjectGuid),typeof(PredictedPlayerGhost));
            var ghost=victim.AddComponent<GhostGameObject>();ghost.LinkGhost(world,target,MultiplayerRole.Server);manager.AddComponentObject(target,new GhostGameObjectLink{LinkedInstance=ghost});
            var flank=new GameObject("Off-ray splash victim");SceneManager.MoveGameObjectToScene(flank,scene);flank.transform.position=new Vector3(.8f,0,-.7f);flank.layer=victim.layer;flank.AddComponent<SphereCollider>().radius=.12f;
            var flankEntity=manager.CreateEntity(typeof(GhostGameObjectGuid),typeof(PredictedPlayerGhost));var flankGhost=flank.AddComponent<GhostGameObject>();flankGhost.LinkGhost(world,flankEntity,MultiplayerRole.Server);manager.AddComponentObject(flankEntity,new GhostGameObjectLink{LinkedInstance=flankGhost});
            Physics.SyncTransforms();scene.GetPhysicsScene().Simulate(.02f);
            foreach(var entry in HaloArsenal.Entries)
            {
                var weapon=registry.GetWeaponData(entry.Id);manager.SetComponentData(target,new PredictedPlayerGhost{MaxHealth=1000,CurrentHealth=1000});
                manager.SetComponentData(flankEntity,new PredictedPlayerGhost{MaxHealth=1000,CurrentHealth=1000});
                for(int pellet=0;pellet<weapon.PelletCount;pellet++)
                {
                    var root=new GameObject(entry.Code+" actual authoritative projectile");SceneManager.MoveGameObjectToScene(root,scene);
                    var entity=manager.CreateEntity(typeof(GhostGameObjectGuid));var projectileGhost=root.AddComponent<GhostGameObject>();projectileGhost.LinkGhost(world,entity,MultiplayerRole.Server);
                    var projectile=root.AddComponent<Projectile>();projectile.enabled=false;projectile.SetGhostGameObject(projectileGhost);
                    var origin=Vector3.back*2;var direction=WeaponSpread.Direction(Vector3.forward,weapon,pellet,100);
                    typeof(Projectile).GetField("m_Weapon",fields).SetValue(projectile,weapon);
                    typeof(Projectile).GetField("m_Data",fields).SetValue(projectile,new Projectile.ProjectileData{WeaponID=entry.Id,OwnerNetworkId=1,SpawnTick=100,Origin=origin,InitialVelocity=direction*weapon.ProjectileSpeed,PelletIndex=pellet});
                    typeof(Projectile).GetField("m_HitMask",fields).SetValue(projectile,EnergyGrenadePhysics.HitMask);
                    typeof(Projectile).GetMethod("Advance",fields).Invoke(projectile,new object[]{.2f,true});
                    Require((bool)typeof(Projectile).GetField("m_Stopped",fields).GetValue(projectile),entry.Code+" projectile collision");
                    UnityEngine.Object.DestroyImmediate(root);manager.DestroyEntity(entity);
                }
                float health=manager.GetComponentData<PredictedPlayerGhost>(target).CurrentHealth;
                Require(Mathf.Abs(health-(1000-weapon.Damage*weapon.PelletCount))<.01f,entry.Code+" actual server projectile damage: "+health);
                float splash=1000-manager.GetComponentData<PredictedPlayerGhost>(flankEntity).CurrentHealth;
                Require(Mathf.Abs(splash-(entry.BlastRadius>0?weapon.Damage*weapon.PelletCount:0))<.01f,entry.Code+" off-ray splash damage: "+splash);
                Report.Add(entry.Code+": real Projectile.Advance(server=true) swept collision -> Impact -> ECS damage = "+(1000-health)+" from "+weapon.PelletCount+" projectile(s). Fixture supplies spawn data, not a network transport.");
                if(entry.BlastRadius>0)Report.Add(entry.Code+": a separate victim outside the projectile ray received "+splash+" actual splash damage inside radius "+entry.BlastRadius+" m.");
            }
        }
        finally
        {foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);SceneManager.UnloadSceneAsync(scene);}
    }
}
