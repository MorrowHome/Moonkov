using System;
using System.Collections;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Check Footstep Perception")]
    public static string RunFootstepContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var holder = new GameObject("Temporary footstep checks");
        var profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Snake.asset"));
        profile.Liquid = false; profile.PatrolRadius = 0; profile.Speed = 0; profile.SightRange = 0; profile.HearingRange = 16;
        World world = null;
        int checks = 0, index = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            float sprint = RaidFootsteps.HearingRange(12, DollSingerStance.Standing, true, 0, false);
            float walk = RaidFootsteps.HearingRange(12, DollSingerStance.Standing, false, 0, false);
            float crouch = RaidFootsteps.HearingRange(12, DollSingerStance.Crouching, false, 0, false);
            float tiptoe = RaidFootsteps.HearingRange(12, DollSingerStance.Tiptoe, false, 0, false);
            float prone = RaidFootsteps.HearingRange(12, DollSingerStance.Prone, false, 0, false);
            Check(sprint > walk && walk > crouch && crouch > prone && prone > tiptoe,
                "Posture and speed do not produce distinct hearing ranges.");
            Check(RaidFootsteps.HearingRange(12, DollSingerStance.Standing, false, 45, false) > walk &&
                Mathf.Approximately(RaidFootsteps.HearingRange(12, DollSingerStance.Standing, false, 45, true), walk),
                "Carried load or bounty-mode isolation is wrong.");
            Check(RaidFootsteps.Audible(7, sprint, true) && !RaidFootsteps.Audible(9, sprint, true) &&
                !RaidFootsteps.Audible(20, sprint, false), "Sound range or obstruction limit is wrong.");
            Check(RaidFootsteps.HearingRange(16 * RaidFootsteps.AlienMovementRangeScale, DollSingerStance.Standing, true, 45, false) < 16,
                "A heavy sprint carries farther to an alien than its gunshot hearing range.");

            world = CounterplayWorld("footstep perception");
            var manager = world.EntityManager;
            void AlienCase(DollSingerStance stance, bool running, bool wall, float distance, float weight, bool airborne, bool teleport, bool expected)
            {
                Vector3 origin = new Vector3(-9000 + index++ * 100, 1000, -9000);
                CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(50, .5f, 50));
                var alien = Alien(holder.transform, world, profile, origin + Vector3.up * (.56f * profile.Scale), MultiplayerRole.Server, -29000 - index);
                Step(alien, 7);
                Vector3 listener = alien.AimPoint;
                Vector3 targetOrigin = new Vector3(listener.x, origin.y, listener.z);
                if (wall) CounterplayBox(holder.transform, "Sound wall", targetOrigin + new Vector3(0, 2, distance / 2), new Vector3(15, 4, .5f));
                var human = Humanoid(holder.transform, world, targetOrigin + new Vector3(-1.25f, 0, distance), false, 40 + index);
                var entity = human.LinkedEntity;
                var state = manager.GetComponentData<PredictedPlayerGhost>(entity);
                state.ControllerState.Stance = stance; state.ControllerState.Sprinting = running;
                state.ControllerState.MovementType = airborne ? FirstPersonController.MovementType.Falling : FirstPersonController.MovementType.Standing;
                state.CarriedWeight = weight; manager.SetComponentData(entity, state);
                Step(alien, 7);
                Check(alien.Target == Entity.Null, "A stationary target made a footstep.");
                int samples = teleport ? 1 : 6;
                for (int i = 1; i <= samples; i++)
                {
                    Vector3 position = targetOrigin + new Vector3(-1.25f + (teleport ? 3 : i * .5f), 0, distance);
                    human.transform.position = position;
                    manager.SetComponentData(entity, LocalTransform.FromPosition(position));
                    Step(alien, 7);
                }
                bool heard = alien.Target == entity;
                var tracks = (IDictionary)typeof(ProceduralAlienEnemy).GetField("m_Footsteps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(alien);
                var track = tracks[entity];
                var trackDistance = track == null ? -1 : (float)track.GetType().GetField("Distance").GetValue(track);
                bool actualBlocked = Physics.Linecast(alien.AimPoint, human.transform.position + Vector3.up,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
                Check(heard == expected, $"Alien hearing mismatch: {stance}, distance={distance}, wall={wall}, weight={weight}, airborne={airborne}, teleport={teleport}, heard={heard}, expected={expected}, action={alien.Action}, tracked={trackDistance}, separation={Vector3.Distance(alien.AimPoint,human.transform.position+Vector3.up)}, blocked={actualBlocked}");
                if (expected)
                {
                    var clue = (Vector3)typeof(ProceduralAlienEnemy).GetField("m_LastSeen", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(alien);
                    Check(alien.Action == AlienAction.Search && alien.AttackCount == 0 &&
                        Vector3.Distance(clue, human.transform.position + Vector3.up) > 1,
                        "An unseen footstep became an exact firing target.");
                }
                if (airborne)
                {
                    state.ControllerState.MovementType = FirstPersonController.MovementType.Standing;
                    state.LastLandTick = 1; manager.SetComponentData(entity, state);
                    Step(alien, 7);
                    Check(alien.Target == entity && alien.Action == AlienAction.Search && alien.AttackCount == 0,
                        "Landing after airborne movement did not produce an uncertain sound.");
                }
            }
            AlienCase(DollSingerStance.Crouching, false, true, 6, 0, false, false, false);
            AlienCase(DollSingerStance.Standing, true, true, 6, 0, false, false, true);
            AlienCase(DollSingerStance.Standing, false, false, 5, 0, false, false, true);
            AlienCase(DollSingerStance.Tiptoe, false, false, 5, 0, false, false, false);
            AlienCase(DollSingerStance.Standing, false, false, 7, 0, false, false, false);
            AlienCase(DollSingerStance.Standing, false, false, 7, 45, false, false, true);
            AlienCase(DollSingerStance.Standing, true, false, 5, 0, true, false, false);
            AlienCase(DollSingerStance.Standing, true, false, 5, 0, false, true, false);

            Vector3 pmcOrigin = new Vector3(-8000, 1000, -9000);
            var humanTarget = Humanoid(holder.transform, world, pmcOrigin + new Vector3(-1.25f, 0, 6), false, 100);
            manager.AddComponent<Simulate>(humanTarget.LinkedEntity);
            var observer = new GameObject("Temporary PMC observer"); observer.transform.SetParent(holder.transform); observer.transform.position = pmcOrigin;
            var ai = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
            try
            {
                typeof(DollSingerEnemySystem).GetField("m_Tuning", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ai, tuning);
                var collect = typeof(DollSingerEnemySystem).GetMethod("CollectTargets", BindingFlags.Instance | BindingFlags.NonPublic);
                var sense = typeof(DollSingerEnemySystem).GetMethod("Sense", BindingFlags.Instance | BindingFlags.NonPublic);
                var eventField = typeof(DollSingerEnemySystem).GetField("m_Events", BindingFlags.Instance | BindingFlags.NonPublic);
                void SetStance(DollSingerStance stance, bool running = false)
                {
                    var h = manager.GetComponentData<PredictedPlayerGhost>(humanTarget.LinkedEntity);
                    h.ControllerState.Stance = stance; h.ControllerState.Sprinting = running; manager.SetComponentData(humanTarget.LinkedEntity, h);
                }
                void MoveBatch(bool reverse, double start)
                {
                    for (int i = 0; i <= 5; i++)
                    {
                        var pos = pmcOrigin + new Vector3(reverse ? 1.25f - i * .5f : -1.25f + i * .5f, 0, 6);
                        humanTarget.transform.position = pos; manager.SetComponentData(humanTarget.LinkedEntity, LocalTransform.FromPosition(pos));
                        collect.Invoke(ai, new object[] { start + i * .1 });
                    }
                }
                DollSingerEnemyBrain Brain() => new DollSingerEnemyBrain { Inventory = InventoryGraph.Create(false, false),
                    Random = new Unity.Mathematics.Random(137), Seed = 1 };
                SetStance(DollSingerStance.Crouching); MoveBatch(false, 10);
                Check(((IList)eventField.GetValue(ai)).Count > 0, "PMC did not collect the actual movement event.");
                var quiet = Brain(); sense.Invoke(ai, new object[] { quiet, observer.transform, pmcOrigin + Vector3.up * 1.4f, 0f, 10.5d });
                Check(quiet.Target == Entity.Null, "PMC heard crouching movement outside its reduced range.");
                SetStance(DollSingerStance.Standing, true); MoveBatch(true, 11);
                var collected = (IList)eventField.GetValue(ai);
                var stepEvent = collected[0];
                float actualRange = (float)stepEvent.GetType().GetField("Range").GetValue(stepEvent);
                Check(Mathf.Approximately(actualRange, sprint), "PMC sprint hearing did not use the actual sprint state.");
                var loud = Brain(); sense.Invoke(ai, new object[] { loud, observer.transform, pmcOrigin + Vector3.up * 1.4f, 0f, 11.5d });
                Check(loud.Target == humanTarget.LinkedEntity && !loud.Visible && !loud.Contacts[humanTarget.LinkedEntity].Confirmed,
                    "PMC did not investigate a loud footstep as uncertain hearing.");
                var falling = manager.GetComponentData<PredictedPlayerGhost>(humanTarget.LinkedEntity);
                falling.ControllerState.MovementType = FirstPersonController.MovementType.Falling;
                manager.SetComponentData(humanTarget.LinkedEntity, falling); MoveBatch(false, 12);
                Check(((IList)eventField.GetValue(ai)).Count == 0, "Airborne movement generated a PMC footstep.");
                falling.ControllerState.MovementType = FirstPersonController.MovementType.Standing; falling.LastLandTick = 1;
                manager.SetComponentData(humanTarget.LinkedEntity, falling); collect.Invoke(ai, new object[] { 12.6d });
                var landing = Brain(); sense.Invoke(ai, new object[] { landing, observer.transform, pmcOrigin + Vector3.up * 1.4f, 0f, 12.6d });
                Check(landing.Target == humanTarget.LinkedEntity && !landing.Visible, "PMC ignored the landing sound.");
                var teleport = humanTarget.transform.position + Vector3.right * 10;
                humanTarget.transform.position = teleport; manager.SetComponentData(humanTarget.LinkedEntity, LocalTransform.FromPosition(teleport));
                collect.Invoke(ai, new object[] { 12.7d });
                Check(((IList)eventField.GetValue(ai)).Count == 0, "A teleport generated a PMC footstep.");
                teleport += Vector3.right * 10;
                humanTarget.transform.position = teleport; manager.SetComponentData(humanTarget.LinkedEntity, LocalTransform.FromPosition(teleport));
                collect.Invoke(ai, new object[] { 20d });
                Check(((IList)eventField.GetValue(ai)).Count == 0, "A stale PMC sample generated a footstep.");
            }
            finally { Object.DestroyImmediate(tuning); }
            return $"PASS {checks} footstep checks: actual PMC perception, alien hearing, posture, weight, walls and unseen targets.";
        }
        finally
        {
            Object.DestroyImmediate(holder); Object.DestroyImmediate(profile);
            if (world != null && world.IsCreated) world.Dispose();
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
