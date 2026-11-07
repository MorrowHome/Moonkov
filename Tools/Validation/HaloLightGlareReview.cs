using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class HaloLightGlareReview
{
    public static object Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use the existing idle Editor.");
        int checks=0;var failures=new List<string>();
        void Check(bool value,string label){checks++;if(!value)failures.Add(label);}
        var source=State();source.ControllerState.Aiming=true;
        float Exposure(PredictedPlayerGhost state,Vector3 eye,Vector3 forward)=>HaloLightGlareRules.Strength(state,Vector3.zero,Vector3.forward,eye,forward);
        Check(Exposure(source,Vector3.forward*5,Vector3.back)>.8f,"Aimed light dazzles a facing enemy");
        Check(Exposure(source,Vector3.forward*5,Vector3.forward)==0,"Looking away prevents exposure");
        Check(Exposure(source,new Vector3(20,0,5),Vector3.back)==0,"Outside the beam is unaffected");
        Check(Exposure(source,Vector3.forward*81,Vector3.back)==0,"Range cutoff");
        var off=source;off.ControllerState.Aiming=false;
        Check(Exposure(off,Vector3.forward*5,Vector3.back)==0,"Idle light is not a combat glare");
        off=source;off.ControllerState.HaloLightEnabled=false;
        Check(Exposure(off,Vector3.forward*5,Vector3.back)==0,"Disabled light");
        off=source;off.ControllerState.HaloLightingSettings=HaloLighting.Pack(8,100,0);
        Check(Exposure(off,Vector3.forward*5,Vector3.back)==0,"Zero brightness");
        off=source;off.HaloDefenseHeld=true;
        Check(!HaloLightGlareRules.Emits(off),"Defense hides the aimed beam");
        off=source;off.ControllerState.IsReloadingState=true;
        Check(!HaloLightGlareRules.Emits(off),"Charging hides the aimed beam");
        off=source;off.HaloTransferPhase=HaloTransferPhase.Sending;
        Check(!HaloLightGlareRules.Emits(off),"Deployed light cannot use the held aiming direction");
        Check(HaloLightGlareRules.SightScale(1)<HaloLightGlareRules.SightScale(0) && HaloLightGlareRules.AimErrorDegrees(1)>0,"Glare reduces NPC sight and precision");
        Check(Exposure(source,new Vector3(float.NaN,0,0),Vector3.back)==0,"Invalid pose cannot create exposure");

        var scene=EditorSceneManager.NewPreviewScene();
        using var world=new World("Glare authority review, no transport",WorldFlags.GameServer);
        var em=world.EntityManager;var system=world.GetOrCreateSystemManaged<HaloLightGlareSystem>();
        var objects=new List<GameObject>();
        try
        {
            em.CreateEntity(typeof(NetworkStreamInGame));
            Entity Spawn(string name,Vector3 position,float yaw,int owner,bool emitting)
            {
                var go=new GameObject(name);objects.Add(go);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=position;
                var link=go.AddComponent<GhostGameObject>();go.AddComponent<PlayerGhost>();
                var state=State();state.ControllerState.YawDegrees=yaw;state.ControllerState.Aiming=emitting;
                var entity=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(LocalTransform),typeof(GhostOwner));
                em.SetComponentData(entity,state);em.SetComponentData(entity,LocalTransform.FromPosition(position));
                em.SetComponentData(entity,new GhostOwner{NetworkId=owner});em.AddComponentObject(entity,new GhostGameObjectLink{LinkedInstance=link});
                return entity;
            }
            var lamp=Spawn("Aiming player",Vector3.zero,0,1,true);
            var enemy=Spawn("Facing enemy",Vector3.forward*8,180,-1,false);
            var ally=Spawn("Cooperative teammate",new Vector3(0,0,7),180,2,false);
            double now=1;
            void Step(){world.SetTime(new TimeData(now,.11f));now+=.11;system.Update();}
            Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare>.8f,"Real server system writes enemy exposure");
            Check(em.GetComponentData<PredictedPlayerGhost>(ally).HaloLightGlare==0,"Cooperative teammates are excluded");
            var sent=em.GetComponentData<PredictedPlayerGhost>(enemy);sent.HaloRechargeEnergy=17;
            var serializer=typeof(PredictedPlayerGhost).Assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_PredictedPlayerGhostGhostComponentSerializer",true);
            object[] to={new GhostSerializerState{GhostFromEntity=system.GetComponentLookup<GhostInstance>(true)},
                Activator.CreateInstance(serializer.GetNestedType("Snapshot",BindingFlags.NonPublic)),sent};
            serializer.GetMethod("CopyToSnapshotGenerated",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,to);
            using(var ghostMap=new NativeParallelHashMap<SpawnedGhost,Entity>(1,Allocator.Temp))
            {
                object[] from={new GhostDeserializerState{GhostMap=ghostMap.AsReadOnly(),SnapshotTick=new NetworkTick(10)},new PredictedPlayerGhost(),0f,0f,to[1],to[1]};
                serializer.GetMethod("CopyFromSnapshotGenerated",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,from);
                var received=(PredictedPlayerGhost)from[1];
                Check(Mathf.Abs(received.HaloLightGlare-sent.HaloLightGlare)<.011f && math.distancesq(received.HaloGlareSource,sent.HaloGlareSource)<.001f,
                    "Generated network snapshot preserves glare strength and source");
                Check(Mathf.Abs(received.HaloRechargeEnergy-17)<.011f,"Generated snapshot preserves reserved battery charge");
            }
            var wall=new GameObject("Temporary occluder");objects.Add(wall);SceneManager.MoveGameObjectToScene(wall,scene);
            wall.transform.position=new Vector3(0,1.5f,4);wall.AddComponent<BoxCollider>().size=new Vector3(3,3,.2f);
            Step();Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare==0,"Physics wall blocks the real server glare");
            wall.SetActive(false);Step();Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare>.8f,"Removing the occlusion restores exposure");
            var smoke=new TacticalServerWorld{Now=1};
            smoke.Objects.Add(new TacticalServerWorld.Object{Phase=TacticalWorldPhase.Smoke,Scene=scene.GetPhysicsScene(),
                Position=new Vector3(0,0,4),Until=15,Definition=new TacticalItemDefinition("smoke",TacticalItemKind.Smoke,1,3,4,20)});
            em.AddComponentObject(em.CreateEntity(),smoke);
            Step();Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare==0,"Smoke blocks the authoritative light exposure");
            smoke.Objects.Clear();Step();Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare>.8f,"Clearing smoke restores exposure");
            var state=em.GetComponentData<PredictedPlayerGhost>(enemy);state.ControllerState.YawDegrees=0;em.SetComponentData(enemy,state);
            Step();Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare==0,"Real target can turn away");
            state.ControllerState.YawDegrees=180;state.OnlineHuman=true;state.OnlineTeam=2;em.SetComponentData(enemy,state);
            state=em.GetComponentData<PredictedPlayerGhost>(lamp);state.OnlineHuman=true;state.OnlineTeam=1;em.SetComponentData(lamp,state);
            Step();Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare>.8f,"Hostile online teams receive glare");
            state.CurrentHealth=0;em.SetComponentData(lamp,state);Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare==0,"Source death clears exposure");
            var shader=Resources.Load<Shader>("Moonkov/HaloLightGlareScreen");
            Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader),"Glare shader imports and is supported");
            return new{passed=failures.Count==0,checks,failures,scope="Rules, real isolated server system and physics; no Play or network connection."};
        }
        finally
        {
            foreach(var go in objects)if(go)Object.DestroyImmediate(go);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static PredictedPlayerGhost State()
    {
        var state=new PredictedPlayerGhost{CurrentHealth=100,HaloEquipped=true,EquippedWeaponID=DollSingerWeapons.Halo,WeaponRaiseProgress=1};
        state.ControllerState.HaloLightEnabled=true;return state;
    }
}
