using System;
using System.Collections;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class BountyClashMapChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int s_Passed;
    private static void Check(bool value,string name)
    {if(!value)throw new InvalidOperationException("Bounty Clash map: "+name);s_Passed++;}
    [MenuItem("Tools/Moonkov/Bounty Clash/Check map configuration and mode isolation")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use idle EditMode.");
        const int fixtureOwner=int.MinValue+729;
        var retired=(System.Collections.Generic.HashSet<int>)typeof(Gameplay.Leaderboard.LeaderboardManager).GetField("s_RetiredRaidActors",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        bool retiredBefore=retired.Contains(fixtureOwner);
        s_Passed=0;var root=new GameObject("Clash map contract / isolated fixture");root.SetActive(false);
        try
        {
            var settings=root.AddComponent<BountyClashArenaSettings>();var definition=settings.Definition;
            Check(definition.Validate()==null && definition.Spawns.Length==12 && definition.Exits.Length==2,"complete shared arena contract");
            var first=definition.Spawns[0];definition.Spawns[0]=new ClashPoint(0,0,0);definition.Center=new ClashPoint(0,0,0);
            Check(ClashPoint.DistanceSquared(settings.Definition.Spawns[0],first)<.0001 && settings.Definition.Center.X==1620,"callers mutate live spawn configuration");
            definition=settings.Definition;
            Check(settings.Contains(BountyClashArenaSettings.Position(definition.Target)) &&
                !settings.Contains(BountyClashArenaSettings.Position(definition.Center)+Vector3.right*(definition.Radius+1)),"XZ boundary and target containment");
            for(int team=0;team<4;team++)for(int slot=0;slot<3;slot++)
                Check(ClashPoint.HorizontalSquared(definition.Spawns[team*3+slot],definition.Center)<definition.Radius*definition.Radius,"spawn inside boundary");
            Check(ClashPoint.HorizontalSquared(definition.Exits[0],definition.Exits[1])>100*100,"independent extraction zones");
            Check(Math.Sqrt(ClashPoint.HorizontalSquared(definition.Exits[0],definition.Center))+definition.BlockRadius<definition.Radius &&
                Math.Sqrt(ClashPoint.HorizontalSquared(definition.Exits[1],definition.Center))+definition.BlockRadius<definition.Radius,"exit blocking regions cross timeout boundary");
            using(var world=new World("Clash map isolation checks",WorldFlags.GameServer))
            {
                var em=world.EntityManager;var mode=em.CreateEntity(typeof(RaidModeState));
                em.SetComponentData(mode,new RaidModeState {Mode=RaidGameMode.BountyClash,RulesVersion=BountyClashRules.Version});
                var aliens=world.GetOrCreateSystemManaged<AlienEnemySpawnSystem>();
                var stateType=typeof(AlienEnemySpawnSystem).GetNestedType("EncounterState",BindingFlags.NonPublic);
                var encounter=Activator.CreateInstance(stateType,true);var alien=em.CreateEntity();
                stateType.GetField("Entity").SetValue(encounter,alien);
                ((IList)typeof(AlienEnemySpawnSystem).GetField("m_Encounters",Private).GetValue(aliens)).Add(encounter);
                typeof(AlienEnemySpawnSystem).GetMethod("OnUpdate",Private).Invoke(aliens,null);
                Check(!em.Exists(alien) && aliens.SpawnedCount==0 && aliens.TotalSpawnedCount==0,"legacy aliens remain in clash");
                var pmcs=world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
                var input=em.CreateEntity();var pmc=em.CreateEntity(typeof(DollSingerEnemy),typeof(GhostOwner));
                em.SetComponentData(pmc,new DollSingerEnemy {InputEntity=input});em.SetComponentData(pmc,new GhostOwner {NetworkId=fixtureOwner});
                em.AddComponentObject(pmc,new DollSingerEnemyBrain());
                var map=root.AddComponent<MoonRaidMap>();
                typeof(DollSingerEnemySystem).GetField("m_Map",Private).SetValue(pmcs,map);
                typeof(DollSingerEnemySystem).GetMethod("OnUpdate",Private).Invoke(pmcs,null);
                Check(!em.Exists(pmc) && !em.Exists(input),"legacy PMC or its input survives mode switch");
                em.SetComponentData(mode,new RaidModeState {Mode=RaidGameMode.Expedition});
                typeof(AlienEnemySpawnSystem).GetMethod("OnUpdate",Private).Invoke(aliens,null);
                Check(!(bool)typeof(AlienEnemySpawnSystem).GetField("m_ClashSuppressed",Private).GetValue(aliens),"original encounter mode cannot resume");
            }
            var presentationType=typeof(BountyClashArenaSettings).Assembly.GetType("Unity.MP_FPS.BountyClashMapPresentation");
            var view=Activator.CreateInstance(presentationType,new object[]{root.transform,definition});
            try
            {
                var method=presentationType.GetMethod("PresentObjective");
                var core=(GameObject)presentationType.GetField("m_BountyCore",Private).GetValue(view);
                var signals=(IList)presentationType.GetField("m_TargetSignals",Private).GetValue(view);
                method.Invoke(view,new object[]{1f,true});
                Check(core.activeSelf && ((GameObject)signals[0]).activeSelf,"ground bounty has no visible signal");
                var dropped=BountyClashArenaSettings.Position(definition.Target)+Vector3.right*12;
                presentationType.GetMethod("SetBountyPosition").Invoke(view,new object[]{dropped});
                method.Invoke(view,new object[]{1f,true});
                Check(Vector3.Distance(core.transform.position,dropped+Vector3.up*.15f)<.01f && !((GameObject)signals[0]).activeSelf,"dropped bounty signal remains at old boss site");
                method.Invoke(view,new object[]{1f,false});
                Check(!core.activeSelf && !((GameObject)signals[0]).activeSelf,"carried/extracted bounty leaves ghost ground signal");
                method.Invoke(view,new object[]{.5f,false});
                Check(!core.activeSelf && ((GameObject)signals[0]).activeSelf,"banishment site disappears before completion");
            }
            finally {presentationType.GetMethod("Dispose").Invoke(view,null);}
            Check(root.transform.childCount==0,"mode presentation leaked objects");
            Debug.Log("Bounty Clash map checks passed: "+s_Passed+"; config copies, 12 spawn anchors, dual exits, actual AI retirement, objective presentation cleanup. Geometry routes have separate formal-map evidence.");
        }
        finally {if(!retiredBefore)retired.Remove(fixtureOwner);Object.DestroyImmediate(root);}
    }
}
