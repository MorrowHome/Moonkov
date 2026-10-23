using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check ranged boss and warning counterplay")]
    public static void RunCounterplayContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var passed = new List<string>();
        try
        {
            foreach (string name in new[] { "Tentacle", "Roots" })
            {
                foreach (string mode in new[] { "hit", "dodge", "wall", "high", "hidden" }) CheckRangedBoss(name, mode, passed);
                CheckCommittedBossPulses(name, false, passed);
                CheckCommittedBossPulses(name, true, passed);
                CheckWarningPresentation(name, passed);
            }
            CheckLostAlienSearch(passed);
            CheckPmcFloorCorners(passed);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/counterplay-contract.txt", passed);
            Debug.Log("Boss counterplay passed: " + string.Join("; ", passed));
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CheckRangedBoss(string name, string mode, List<string> passed)
    {
        using var world = CounterplayWorld(name + mode);
        var manager = world.EntityManager;
        var holder = new GameObject("Temporary ranged boss fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
        p.Liquid = false; p.Damage = 1; p.PatrolRadius = 0; p.AttackCooldown = 60;
        p.BossDisplaySeconds = .6f; p.BossPursuitSpeed = p.BossRepositionSpeed = 0;
        Vector3 origin = new Vector3(-9800, 1000, -9800);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(120, .5f, 120));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -21000);
            Vector3 targetPosition = origin + Vector3.forward * 40 + (mode == "high" ? Vector3.up * 8 : Vector3.zero);
            if (mode == "high") CounterplayBox(holder.transform, "High platform", targetPosition - Vector3.up * .25f, new Vector3(6, .5f, 6));
            var target = Humanoid(holder.transform, world, targetPosition, false, 42);
            if (mode == "hidden") CounterplayBox(holder.transform, "Sight wall", origin + new Vector3(0, 10, 20), new Vector3(30, 30, .4f));
            bool launched = false; float warning = 0; Vector3 previous = actor.transform.position;
            for (int i = 0; i < 360; i++)
            {
                Step(actor, 1);
                Require(Vector3.Distance(previous, actor.transform.position) < .25f, "Boss teleported.");
                previous = actor.transform.position;
                if (actor.Action == AlienAction.Windup)
                {
                    warning += 1f / 30;
                    Require(actor.DamageCount == 0, "Warning caused damage.");
                }
                if (!launched && actor.Action == AlienAction.Strike)
                {
                    launched = true;
                    Require(actor.AttackKind == AlienAttackKind.MucusSpit && warning >= .84f, "Far target had no committed ranged warning.");
                    Require(actor.MucusHits == 0, "Ranged response was hitscan.");
                    if (mode == "wall") CounterplayBox(holder.transform, "Projectile wall", origin + new Vector3(0, 10, 20), new Vector3(30, 30, .4f));
                    if (mode == "dodge")
                    {
                        target.transform.position += Vector3.right * 4;
                        manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                    }
                }
            }
            var health = manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity);
            Require(mode == "hidden" ? actor.AttackCount == 0 : launched, name + " failed ranged acquisition: " + mode);
            if (mode == "hit" || mode == "high") Require(actor.MucusHits == 1 && health.CurrentHealth < 100, name + " failed 40m " + mode);
            else Require(actor.MucusHits == 0 && health.CurrentHealth == 100, name + " ignored " + mode);
            passed.Add(name + " 40m " + mode + ": hits=" + actor.MucusHits + ", warning=" + warning.ToString("F2") + "s");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckCommittedBossPulses(string name, bool hiddenFloor, List<string> passed)
    {
        using var world = CounterplayWorld(name + " pulses");
        var manager = world.EntityManager;
        var holder = new GameObject("Temporary committed boss pulses");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
        p.Liquid = false; p.Damage = 1; p.PatrolRadius = 0; p.AttackCooldown = 60; p.MucusEveryAttacks = 0;
        p.BossDisplaySeconds = .6f; p.BossPursuitSpeed = p.BossRepositionSpeed = 0;
        Vector3 origin = new Vector3(-9850, 1000, -9850);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(70, .5f, 70));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -21001);
            SetHealth(manager, actor.GhostGameObject.LinkedEntity, 40); // Enrage before committing the attack.
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 8, false, 42);
            bool committed = false, enteredStrike = false; uint hits = 0; float warning = 0, previousHitTime = -100;
            var pattern = AlienCombatPattern.For(p, 2, 1);
            for (int i = 0; i < 330; i++)
            {
                Step(actor, 1);
                var state = manager.GetComponentData<ProceduralAlienEnemy.State>(actor.GhostGameObject.LinkedEntity);
                if (state.Action == AlienAction.Windup)
                {
                    warning += 1f / 30;
                    if (!committed && hiddenFloor)
                    {
                        // Target enters an upper floor after the attack was locked below.
                        target.transform.position += Vector3.up * 4;
                        manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                        CounterplayBox(holder.transform, "Separating floor", origin + new Vector3(0, 3.5f, 8), new Vector3(10, .4f, 10));
                    }
                    committed = true;
                }
                if (name == "Roots" && !hiddenFloor && state.Action == AlienAction.Strike)
                {
                    int pulse = Mathf.Max(0, pattern.PulseAt(state.ActionTime + 1f / 30));
                    target.transform.position = pattern.PulsePoint(state.AttackPoint, state.AttackDirection, pulse) - Vector3.up * .04f;
                    manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                }
                if (state.Action == AlienAction.Strike)
                {
                    enteredStrike = true;
                    Require(warning >= 1.14f, "Enraged attack shortened its initial warning.");
                    if (actor.DamageCount > hits)
                    {
                        int pulse = (int)actor.DamageCount - 1;
                        Require(Mathf.Abs(state.ActionTime - pattern.PulseTime(pulse)) < .05f, "Damage and warning pulse clocks diverged.");
                        if (hits > 0) Require(state.ActionTime - previousHitTime >= 1.2f, "Unwarned fast follow-up pulse.");
                        previousHitTime = state.ActionTime; hits = actor.DamageCount;
                    }
                }
            }
            Require(committed && enteredStrike, "Boss did not commit a phase-two attack.");
            Require(hiddenFloor ? hits == 0 : hits == pattern.Pulses, name + " pulse damage count mismatch: " + hits);
            passed.Add(name + (hiddenFloor ? " hidden upper floor: zero damage" : " enrage: all " + hits + " pulses match clock and committed forward direction"));
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckWarningPresentation(string name, List<string> passed)
    {
        var holder = new GameObject("Temporary boss warning presentation");
        var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
        try
        {
            for (uint sequence = 1; sequence <= 2; sequence++)
            {
                using var presentation = new AlienAttackPresentation(holder.transform, p);
                var pattern = AlienCombatPattern.For(p, 2, sequence);
                var state = new ProceduralAlienEnemy.State { Action = AlienAction.Windup, AttackSequence = sequence,
                    Scale = p.Scale, AttackDirection = Vector3.forward, AttackOrigin = Vector3.up * (.56f * p.Scale),
                    AttackPoint = Vector3.forward * 8, ActionTime = pattern.Windup - .1f };
                presentation.Observe(state, pattern, Vector3.up * 3);
                int warnings = 0;
                foreach (var line in holder.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "Boss committed attack warning" && line.enabled) warnings++;
                Require(warnings == (pattern.Kind == AlienAttackKind.RootSpikes ? pattern.Pulses * 3 : 1), "Missing committed warning geometry.");
                Require(holder.GetComponentsInChildren<Collider>().Length == 0, "Client warning created damage colliders.");
                if (pattern.Kind == AlienAttackKind.TentacleSweep && pattern.Pulses > 1)
                {
                    state.Action = AlienAction.Strike; state.ActionTime = pattern.PulseTime(1) - .04f;
                    presentation.Observe(state, pattern, Vector3.up * 3);
                    bool visibleFollowup = false;
                    foreach (var line in holder.GetComponentsInChildren<LineRenderer>())
                        if (line.name.StartsWith("Viscous attack thread") && line.enabled && line.widthMultiplier > .01f)
                            visibleFollowup = true;
                    Require(visibleFollowup, "Enraged follow-up sweep skipped its visible motion.");
                }
                state.Action = AlienAction.Recover; state.ActionTime = 0; presentation.Observe(state, pattern, Vector3.up * 3);
                foreach (var line in holder.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "Boss committed attack warning") Require(!line.enabled, "Warning survived recovery.");
            }
            passed.Add(name + " client: windup geometry exists, recovery hides it, zero colliders");
        }
        finally { Object.DestroyImmediate(holder); }
    }

    private static void CheckLostAlienSearch(List<string> passed)
    {
        using var world = CounterplayWorld("lost clue search");
        var holder = new GameObject("Temporary lost clue search");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Centipede.asset"));
        p.Liquid = false; p.AttackCooldown = 60; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-9900, 1000, -9900);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(80, .5f, 80));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -21002);
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 10, false, 42);
            typeof(ProceduralAlienEnemy).GetField("m_NextAttack", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, 100f);
            Step(actor, 1);
            Require(actor.Target == target.LinkedEntity, "Search fixture never saw target.");
            target.transform.position += Vector3.right * 60;
            world.EntityManager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
            Step(actor, 10);
            Require(actor.Action == AlienAction.Search, "Lost target did not enter search.");
            var field = typeof(ProceduralAlienEnemy).GetField("m_SearchGoal", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 first = (Vector3)field.GetValue(actor); Step(actor, 120);
            Vector3 second = (Vector3)field.GetValue(actor);
            Require(Vector3.Distance(first, second) > 1 && Vector3.Distance(second, origin + Vector3.forward * 10) < 8, "Search tracked hidden transform or froze at clue.");
            Step(actor, 600);
            Require(actor.Action == AlienAction.Roam, "Search outlived target memory.");
            passed.Add("Lost target: bounded clue search, no hidden tracking, expires to patrol");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckPmcFloorCorners(List<string> passed)
    {
        var brain = new DollSingerEnemyBrain { Corner = 0, CornerCount = 2 };
        brain.Corners[0] = Vector3.up * 3; brain.Corners[1] = Vector3.forward * 5 + Vector3.up * 3;
        var follow = typeof(DollSingerEnemySystem).GetMethod("FollowPath", BindingFlags.Static | BindingFlags.NonPublic);
        follow.Invoke(null, new object[] { brain, Vector3.zero });
        Require(brain.Corner == 0, "PMC skipped a path corner on another floor.");
        follow.Invoke(null, new object[] { brain, Vector3.up * 3 });
        Require(brain.Corner == 1, "PMC failed to advance after reaching the actual floor.");
        passed.Add("PMC floor corners: no horizontal-only skipping");
    }

    [MenuItem("Tools/Moonkov/Aliens/Capture boss warning counterplay")]
    public static void CaptureCounterplayWarnings()
    {
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var holder = new GameObject("Temporary warning capture");
        RenderTexture texture = null; Texture2D pixels = null; Material groundMaterial = null;
        try
        {
            Vector3 origin = new Vector3(-9950, 1000, -9950);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.SetParent(holder.transform);
            ground.transform.position = origin - Vector3.up * .25f; ground.transform.localScale = new Vector3(60, .5f, 60);
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(.16f, .18f, .2f)); ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            var lamp = new GameObject("Capture sun"); lamp.transform.SetParent(holder.transform);
            lamp.AddComponent<Light>().type = LightType.Directional; lamp.GetComponent<Light>().intensity = 2;
            lamp.transform.rotation = Quaternion.Euler(55, -25, 0);
            var cameraObject = new GameObject("Capture camera"); cameraObject.transform.SetParent(holder.transform);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .055f, .08f);
            camera.transform.position = origin + new Vector3(19, 23, -24); camera.transform.LookAt(origin + Vector3.forward * 3);
            camera.fieldOfView = 52; camera.farClipPlane = 120;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            texture = new RenderTexture(960, 720, 24); pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            foreach (string name in new[] { "Tentacle", "Roots" })
            {
                var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
                var owner = new GameObject(name + " warning capture"); owner.transform.SetParent(holder.transform);
                owner.transform.position = origin + Vector3.up * .56f * p.Scale;
                try
                {
                    using var presentation = new AlienAttackPresentation(owner.transform, p);
                    var pattern = AlienCombatPattern.For(p, 2, 1);
                    var state = new ProceduralAlienEnemy.State { Action = AlienAction.Windup, AttackSequence = 1,
                        Scale = p.Scale, AttackDirection = Vector3.forward, AttackOrigin = owner.transform.position,
                        AttackPoint = origin + Vector3.forward * 8, ActionTime = pattern.Windup - .1f };
                    Physics.SyncTransforms(); presentation.Observe(state, pattern, owner.transform.position);
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                        new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = texture });
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = texture; pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); pixels.Apply();
                        File.WriteAllBytes("Library/BuildValidation/AlienIntegration/warning-" + name + ".png", pixels.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; }
                }
                finally { Object.DestroyImmediate(owner); }
            }
        }
        finally
        {
            if (texture) Object.DestroyImmediate(texture); if (pixels) Object.DestroyImmediate(pixels);
            if (groundMaterial) Object.DestroyImmediate(groundMaterial); Object.DestroyImmediate(holder);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Moonkov/Aliens/Check boss refinement boundaries")]
    public static void RunRefinementContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var passed = new List<string>();
        try
        {
            CheckAttackEventGates(passed);
            foreach (float distance in new[] { 5f, 8f, 12f })
                foreach (float height in new[] { 0f, 2f, 3f, 8f }) CheckNearHeightChoice(distance, height, passed);
            CheckArcAndImpact(false, passed); CheckArcAndImpact(true, passed); CheckArcAndImpact(false, passed, true);
            CheckContactPresentation(passed);
            CheckGiantRouteBudget(passed);
            CheckBlockedBallisticCorridor(passed);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/refinement-contract.txt", passed);
            Debug.Log("Boss refinement passed: " + string.Join("; ", passed));
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CheckAttackEventGates(List<string> passed)
    {
        var gate = new AlienAttackEvents();
        var state = new ProceduralAlienEnemy.State { Action = AlienAction.Windup, AttackSequence = 1 };
        Require(!gate.Launch(state) && !gate.Impact(state), "Initial snapshot replayed history.");
        state.Action = AlienAction.Strike;
        Require(gate.Launch(state), "Strike at time zero skipped the launch.");
        foreach (float age in new[] { .033f, .067f, .1f, .4f })
        { state.ActionTime = age; Require(!gate.Launch(state), "One attack replayed its launch."); }
        state.ImpactSequence = 1; state.ImpactKind = AlienImpactKind.World;
        Require(gate.Impact(state) && !gate.Impact(state), "Impact did not deduplicate.");
        state.ImpactSequence = 2; state.ImpactKind = AlienImpactKind.Face;
        Require(gate.Impact(state), "Later authoritative impact was lost.");
        state.ImpactSequence = 1; Require(!gate.Impact(state), "Reordered impact replayed.");
        var late = new AlienAttackEvents();
        Require(!late.Launch(state) && !late.Impact(state), "Late join replayed active attack or old impact.");
        state.AttackSequence = 2; state.ActionTime = .2f;
        Require(late.Launch(state) && !late.Launch(state), "Skipped windup lost or duplicated the next launch.");
        passed.Add("Audio/contact gates: time-zero launch, continuous/reordered snapshots, skipped windup, late join, once per sequence");
    }

    private static void CheckNearHeightChoice(float distance, float height, List<string> passed)
    {
        using var world = CounterplayWorld("near height");
        var holder = new GameObject("Temporary near height fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Tentacle.asset"));
        p.Liquid = false; p.BossDisplaySeconds = .6f; p.BossRepositionSpeed = p.BossPursuitSpeed = 0;
        p.AttackCooldown = 60; p.Damage = 1; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-10050, 1000, -10050);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(70, .5f, 70));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * .56f * p.Scale, MultiplayerRole.Server, -22000);
            Humanoid(holder.transform, world, origin + Vector3.forward * distance + Vector3.up * height, false, 42);
            for (int i = 0; i < 120 && actor.AttackCount == 0; i++) Step(actor, 1);
            Require(actor.AttackCount == 1 && actor.AttackKind == (height == 0 ? AlienAttackKind.TentacleSweep : AlienAttackKind.MucusSpit),
                "Near ledge chose an impossible sweep: distance=" + distance + " height=" + height + " kind=" + actor.AttackKind);
            passed.Add("Near " + distance + "m / height " + height + "m selects " + actor.AttackKind);
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckArcAndImpact(bool blocked, List<string> passed, bool body = false)
    {
        using var world = CounterplayWorld("roof arc");
        var manager = world.EntityManager;
        var holder = new GameObject("Temporary roof arc fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Tentacle.asset"));
        p.Liquid = false; p.BossDisplaySeconds = .6f; p.BossPursuitSpeed = p.BossRepositionSpeed = 0;
        p.AttackCooldown = 60; p.Damage = 1; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-10150, 1000, -10150);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(100, .5f, 100));
            CounterplayBox(holder.transform, "Remote canopy", origin + new Vector3(0, 5, 25), new Vector3(20, .3f, 35));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * .56f * p.Scale, MultiplayerRole.Server, -22001);
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 40, false, 42);
            bool launched = false;
            for (int i = 0; i < 240; i++)
            {
                Step(actor, 1);
                var state = manager.GetComponentData<ProceduralAlienEnemy.State>(actor.GhostGameObject.LinkedEntity);
                if (!launched && state.Action == AlienAction.Strike)
                {
                    launched = true; Require(state.MucusLowArc, "Ceiling response did not select the shared low arc.");
                    if (body)
                    {
                        target.transform.position += Vector3.up * .9f;
                        manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                    }
                    if (blocked) CounterplayBox(holder.transform, "New wall after lock", origin + new Vector3(0, 2, 20), new Vector3(20, 4, .3f));
                }
            }
            var impact = manager.GetComponentData<ProceduralAlienEnemy.State>(actor.GhostGameObject.LinkedEntity);
            Require(launched && actor.LastArcQueries <= 50 && impact.ImpactSequence == 1 &&
                impact.ImpactKind == (blocked ? AlienImpactKind.World : body ? AlienImpactKind.Body : AlienImpactKind.Face), "Authoritative contact or arc budget failed.");
            Require(((Vector3)impact.ImpactNormal).sqrMagnitude > .8f &&
                Mathf.Abs(impact.ImpactPoint.z - origin.z - (blocked ? 20 : 40)) < 1, "Contact position/normal did not come from the collider.");
            Require(blocked ? actor.MucusHits == 0 : actor.MucusHits == 1, "Low arc crossed cover or missed the target.");
            passed.Add(body ? "Low arc body contact: no false face classification" : blocked ? "Low arc: post-lock wall contact + no damage + shared impact snapshot" : "Low arc: canopy cleared + face hit + contact snapshot, <=50 preflight queries");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckGiantRouteBudget(List<string> passed)
    {
        using var world = CounterplayWorld("giant navigation");
        var holder = new GameObject("Temporary giant navigation budget");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Tentacle.asset"));
        p.Liquid = false; p.BossRepositionSpeed = 0; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-10250, 1000, -10250);
        UnityEngine.AI.NavMeshData nav = null;
        UnityEngine.AI.NavMeshDataInstance navInstance = default;
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(100, .5f, 100));
            CounterplayBox(holder.transform, "Human opening left", origin + new Vector3(-1.75f, 5, 5), new Vector3(1.5f, 10, .5f));
            CounterplayBox(holder.transform, "Human opening right", origin + new Vector3(1.75f, 5, 5), new Vector3(1.5f, 10, .5f));
            var sources = new List<UnityEngine.AI.NavMeshBuildSource>();
            var bounds = new Bounds(origin, new Vector3(70, 30, 70));
            UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, LayerMask.GetMask("Default", "Ground"),
                UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders, 0, new List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);
            var settings = UnityEngine.AI.NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .35f; settings.agentHeight = 1.8f;
            settings.overrideVoxelSize = true; settings.voxelSize = .2f;
            nav = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            Require(nav, "Temporary human NavMesh failed to build."); navInstance = UnityEngine.AI.NavMesh.AddNavMeshData(nav);
            var humanPath = new UnityEngine.AI.NavMeshPath();
            Require(UnityEngine.AI.NavMesh.CalculatePath(origin, origin + Vector3.forward * 10, UnityEngine.AI.NavMesh.AllAreas, humanPath) &&
                humanPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, "Human-width route was not reachable in the fixture.");
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * .56f * p.Scale, MultiplayerRole.Server, -22002);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var steer = typeof(ProceduralAlienEnemy).GetMethod("UprightWaypoint", flags);
            var clock = typeof(ProceduralAlienEnemy).GetField("m_Clock", flags);
            Vector3 first = (Vector3)steer.Invoke(actor, new object[] { origin + Vector3.forward * 10 });
            Require(actor.LastNavigationQueries <= 144 && Mathf.Abs(first.x - origin.x) > 1, "Narrow route did not select a safe side position: waypoint=" + first + " queries=" + actor.LastNavigationQueries);
            int plans = actor.NavigationPlans;
            for (int i = 1; i < 20; i++)
            {
                clock.SetValue(actor, i * .03f);
                steer.Invoke(actor, new object[] { origin + Vector3.forward * 10 + Vector3.right * i * 3 });
            }
            Require(actor.NavigationPlans == plans, "Moving target bypassed the hard navigation frequency limit.");
            clock.SetValue(actor, .8f); steer.Invoke(actor, new object[] { origin + Vector3.forward * 10 });
            Require(actor.NavigationPlans == plans + 1 && actor.LastNavigationQueries <= 144, "Navigation did not resume within budget.");
            passed.Add("Giant route: rejects actual human NavMesh opening, safe side position, moving target cannot bypass .75s / 144-query budget");
        }
        finally { if (navInstance.valid) navInstance.Remove(); if (nav) Object.DestroyImmediate(nav); Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckBlockedBallisticCorridor(List<string> passed)
    {
        using var world = CounterplayWorld("blocked arcs");
        var holder = new GameObject("Temporary blocked arc corridor");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Tentacle.asset"));
        p.Liquid = false; Vector3 origin = new Vector3(-10350, 1000, -10350);
        try
        {
            CounterplayBox(holder.transform, "Low remote roof", origin + new Vector3(0, 2.1f, 25), new Vector3(12, .1f, 30));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * 2.24f, MultiplayerRole.Server, -22003);
            var probe = typeof(ProceduralAlienEnemy).GetMethod("MucusArcClear", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 from = origin + Vector3.up * 1.8f, to = from + Vector3.forward * 40;
            Require(!Physics.Linecast(from, to, LayerMask.GetMask("Default", "Ground")), "Arc fixture has no straight sightline.");
            Require(!(bool)probe.Invoke(actor, new object[] { from, to, false }) &&
                !(bool)probe.Invoke(actor, new object[] { from, to, true }) && actor.LastArcQueries <= 50,
                "An impossible roof corridor was considered a valid launch.");
            passed.Add("Both arcs rejected under low remote roof despite clear straight sightline, bounded preflight");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckContactPresentation(List<string> passed)
    {
        var holder = new GameObject("Temporary contact presentation");
        var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Tentacle.asset");
        try
        {
            var pattern = AlienCombatPattern.For(p, 1, 1, AlienAttackKind.MucusSpit);
            var state = new ProceduralAlienEnemy.State { Action = AlienAction.Recover, AttackSequence = 1,
                ImpactSequence = 1, ImpactKind = AlienImpactKind.World, ImpactNormal = Vector3.left, ImpactPoint = Vector3.right * 4 };
            using (var presentation = new AlienAttackPresentation(holder.transform, p))
            {
                presentation.Observe(state, pattern);
                var particles = holder.GetComponentInChildren<ParticleSystem>(true);
                Require(particles && particles.particleCount == 0, "First snapshot replayed old contact particles.");
                state.ImpactSequence = 2; presentation.Observe(state, pattern);
                int count = particles.particleCount;
                Require(count > 0 && Vector3.Distance(particles.transform.position, state.ImpactPoint) < .01f &&
                    Vector3.Dot(particles.transform.up, state.ImpactNormal) > .99f, "Contact particles ignored server position/normal.");
                presentation.Observe(state, pattern);
                Require(particles.particleCount == count, "Repeated snapshot duplicated the contact burst.");
            }
            passed.Add("Client contact: actual point/normal burst, duplicate suppression, late first snapshot silent");
        }
        finally { Object.DestroyImmediate(holder); }
    }

    private static World CounterplayWorld(string name)
    {
        var world = new World("Counterplay " + name, WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        manager.SetComponentData(manager.CreateEntity(typeof(NetworkTime)), new NetworkTime { ServerTick = new NetworkTick(1) });
        return world;
    }
    private static void CounterplayBox(Transform holder, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(holder); go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms();
    }
}
