using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [Serializable] private sealed class AmbushCase
    {
        public string name;
        public int frames;
        public uint attacks, hits;
        public float health;
        public bool sawWindup, sawStrike, cancelled, passed;
    }
    [Serializable] private sealed class AmbushReport
    {
        public string capturedAt;
        public string evidence = "Edit Mode isolated server World, temporary floor, manual 30 Hz ticks; no transport or Play Mode";
        public List<AmbushCase> cases = new List<AmbushCase>();
        public string error;
    }

    [MenuItem("Tools/Moonkov/Aliens/Check spider ambush contract")]
    public static void RunAmbushContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run the ambush fixtures in stable Edit Mode.");
        var report = new AmbushReport { capturedAt = DateTime.UtcNow.ToString("O") };
        var originalScene = SceneManager.GetActiveScene();
        var fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(fixtureScene);
            RunAmbushCase(report, 0, "stationary target takes exactly one hit");
            RunAmbushCase(report, 1, "lateral dodge after windup avoids committed strike");
            RunAmbushCase(report, 2, "target death during windup cancels strike");
        }
        catch (Exception error) { report.error = error.Message; throw; }
        finally
        {
            SceneManager.SetActiveScene(originalScene);
            EditorSceneManager.CloseScene(fixtureScene, true);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllText("Library/BuildValidation/AlienIntegration/ambush-contract.json", JsonUtility.ToJson(report, true));
        }
        Debug.Log("Spider ambush contract passed: one hit, lateral dodge, target-death cancellation (3 bounded Edit Mode cases).");
    }

    private static void RunAmbushCase(AmbushReport report, int mode, string name)
    {
        var result = new AmbushCase { name = name };
        report.cases.Add(result);
        using var server = new World("Spider ambush fixture " + mode, WorldFlags.GameServer);
        server.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = server.EntityManager;
        var time = manager.CreateEntity(typeof(NetworkTime));
        manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary spider ambush fixture");
        var profile = ScriptableObject.CreateInstance<AlienCreatureProfile>();
        try
        {
            profile.ApplyDefaults(AlienSpecies.Spider);
            profile.Health = 100; profile.Damage = 10; profile.SightRange = 12;
            profile.PatrolRadius = 0; profile.AttackCooldown = 10; profile.Liquid = false;
            profile.SpiderWindup = .4f; profile.SpiderStrikeSeconds = .4f; profile.SpiderRecovery = .35f;
            // One small floor far beyond the authored map keeps the real motor and
            // sight queries active without depending on or modifying map geometry.
            var floorPoint = new Vector3(-8000, 1000, -8000);
            var floor = new GameObject("Temporary ambush floor");
            floor.transform.SetParent(holder.transform, false);
            floor.transform.position = floorPoint - Vector3.up * .25f;
            floor.AddComponent<BoxCollider>().size = new Vector3(16, .5f, 16);
            Physics.SyncTransforms();
            var actor = Alien(holder.transform, server, profile,
                floorPoint + Vector3.up * SurfaceMotor.Height * profile.Scale, MultiplayerRole.Server, -12000, true);
            var target = Humanoid(holder.transform, server, floorPoint + Vector3.forward * 2.25f, false, 42);
            bool changedTarget = false;
            // Sixty frames include the complete strike and recovery, while the long
            // cooldown makes a second attack impossible within this two-second case.
            for (int frame = 0; frame < 60; frame++)
            {
                manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick((uint)frame + 1) });
                Step(actor, 1);
                result.frames++;
                result.sawWindup |= actor.Action == AlienAction.Windup;
                result.sawStrike |= actor.Action == AlienAction.Strike;
                if (!changedTarget && actor.Action == AlienAction.Windup)
                {
                    changedTarget = true;
                    if (mode == 1)
                    {
                        var escaped = target.transform.position + Vector3.right * 3.5f;
                        target.transform.position = escaped;
                        manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(escaped));
                    }
                    else if (mode == 2) SetHealth(manager, target.LinkedEntity, 0);
                }
                else if (mode == 2 && changedTarget && actor.Action == AlienAction.Recover)
                    result.cancelled = true;
            }
            result.attacks = actor.AttackCount; result.hits = actor.DamageCount;
            result.health = manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity).CurrentHealth;
            Require(result.sawWindup && result.attacks == 1, name + ": expected one committed windup.");
            if (mode == 0)
                Require(result.sawStrike && result.hits == 1 && Mathf.Approximately(result.health, 90),
                    name + ": expected one 10-health hit; hits=" + result.hits + ", health=" + result.health);
            else if (mode == 1)
                Require(result.sawStrike && result.hits == 0 && Mathf.Approximately(result.health, 100),
                    name + ": strike followed the dodging target or damaged it.");
            else
                Require(result.cancelled && !result.sawStrike && result.hits == 0 && result.health == 0 && actor.Target == Entity.Null,
                    name + ": attack continued after the target died.");
            result.passed = true;
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(profile); }
    }
}
