using System;
using System.Collections;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    public static string RunLootPlayback()
    {
        if (!EditorApplication.isPlaying || !GameManager.Instance) throw new InvalidOperationException("Use initialized Play mode.");
        var listener = new GameObject("Loot playback listener");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Loot playback wall";
        var origin = new Vector3(15000, 1000, 15000);
        listener.transform.position = origin;
        wall.transform.position = origin + Vector3.forward * 2.5f;
        wall.transform.localScale = new Vector3(3, 3, .3f); Physics.SyncTransforms();
        var pool = new SoundGameObjectPool("Loot playback pool", 4);
        var system = new SoundSystem();
        try
        {
            var sound = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary").LootSearchWorld;
            system.Init(listener.transform, 2, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            var emitted = system.CreateEmitter(sound, origin + Vector3.forward * 5);
            if (emitted == null || !emitted.Handle.IsValid()) throw new InvalidOperationException("Search sound did not allocate an emitter.");
            var source = emitted.Handle.Emitter.ActiveSoundGameObjects[0];
            if (source.m_AudioSource.clip != sound.Clips[0] || source.m_AudioSource.spatialBlend < .99f ||
                !Mathf.Approximately(source.m_AudioSource.maxDistance, 12) ||
                !(bool)typeof(SoundGameObject).GetField("m_Occluded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source))
                throw new InvalidOperationException("Search sound lost its clip, position, range or wall occlusion.");
            system.Kill(emitted);
            if (system.CreateEmitter(sound, origin + Vector3.forward * 13) != null || pool.AvailableSoundObjectCount != 4)
                throw new InvalidOperationException("Out-of-range search sound consumed a voice.");
            return "PASS real pooled search playback: mono clip, spatial source, wall occlusion, range and cleanup.";
        }
        finally { system.KillAll(); pool.Dispose(); Object.Destroy(listener); Object.Destroy(wall); }
    }

    [MenuItem("Tools/Moonkov/Check Loot Acoustics")]
    public static string RunLootAcousticsContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var holder = new GameObject("Temporary loot acoustics checks");
        var profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Snake.asset"));
        profile.Liquid = false; profile.PatrolRadius = 0; profile.Speed = 0; profile.SightRange = 0; profile.HearingRange = 16;
        World world = null;
        int checks = 0, index = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            var library = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
            var sound = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/LootSearchWorld.asset");
            Check(library && sound && library.LootSearchWorld == sound && sound.Clips.Count == 1 && sound.Clips[0] && sound.Clips[0].channels == 1,
                "The world search cue is missing or is not mono.");
            Check(sound.DistanceInfo.SpatialBlend == 1 && sound.DistanceInfo.VolumeDistMax >= RaidLootAcoustics.HearingRange &&
                sound.Occlusion && sound.LowPassFilter.EnableComponent && sound.MaxVoices > 0,
                "The world search cue is not positional, bounded and occluded.");
            uint last = 7;
            Check(!MoonkovPlayerAudio.NewTick(7, ref last) && MoonkovPlayerAudio.NewTick(8, ref last) &&
                !MoonkovPlayerAudio.NewTick(8, ref last), "Late joins or repeated snapshots replay search audio.");
            var wrapping = new PredictedPlayerGhost { LootSearchSequence = uint.MaxValue };
            RaidLootAcoustics.Mark(ref wrapping);
            uint beforeWrap = uint.MaxValue;
            Check(wrapping.LootSearchSequence == 1 && MoonkovPlayerAudio.NewTick(wrapping.LootSearchSequence, ref beforeWrap),
                "Search sequence rollover lost the cue.");

            world = CounterplayWorld("loot acoustics");
            var manager = world.EntityManager;
            void AlienCase(float distance, bool wall, bool expected)
            {
                Vector3 origin = new Vector3(-12000 + index++ * 100, 1000, -12000);
                CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(50, .5f, 50));
                var alien = Alien(holder.transform, world, profile, origin + Vector3.up * (.56f * profile.Scale), MultiplayerRole.Server, -39000 - index);
                Step(alien, 7);
                Vector3 listener = alien.AimPoint;
                Vector3 targetOrigin = new Vector3(listener.x, origin.y, listener.z);
                if (wall) CounterplayBox(holder.transform, "Loot sound wall", targetOrigin + new Vector3(0, 2, distance / 2), new Vector3(15, 4, .5f));
                var human = Humanoid(holder.transform, world, targetOrigin + new Vector3(0, 0, distance), false, 40 + index);
                var health = manager.GetComponentData<PredictedPlayerGhost>(human.LinkedEntity);
                health.LootSearchSequence = 7; manager.SetComponentData(human.LinkedEntity, health);
                Step(alien, 7);
                Check(alien.Target == Entity.Null, "An old search was replayed when an alien began observing.");
                RaidLootAcoustics.Mark(ref health); manager.SetComponentData(human.LinkedEntity, health);
                Step(alien, 7);
                Check((alien.Target == human.LinkedEntity) == expected, $"Alien loot hearing mismatch: distance={distance}, wall={wall}.");
                if (expected)
                {
                    var clue = (Vector3)typeof(ProceduralAlienEnemy).GetField("m_LastSeen", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(alien);
                    Check(alien.Action == AlienAction.Search && alien.AttackCount == 0 &&
                        Vector3.Distance(clue, human.transform.position + Vector3.up) > 1,
                        "Unseen looting gave an alien exact sight or an immediate attack.");
                }
            }
            AlienCase(6, false, true);
            AlienCase(12, false, false);
            AlienCase(6, true, false);
            AlienCase(4, true, true);

            Vector3 pmcOrigin = new Vector3(-11000, 1000, -12000);
            var humanTarget = Humanoid(holder.transform, world, pmcOrigin + new Vector3(0, 0, 6), false, 150);
            manager.AddComponent<Simulate>(humanTarget.LinkedEntity);
            var observer = new GameObject("Temporary PMC loot observer"); observer.transform.SetParent(holder.transform); observer.transform.position = pmcOrigin;
            var ai = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
            try
            {
                typeof(DollSingerEnemySystem).GetField("m_Tuning", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ai, tuning);
                var collect = typeof(DollSingerEnemySystem).GetMethod("CollectTargets", BindingFlags.Instance | BindingFlags.NonPublic);
                var sense = typeof(DollSingerEnemySystem).GetMethod("Sense", BindingFlags.Instance | BindingFlags.NonPublic);
                var events = typeof(DollSingerEnemySystem).GetField("m_Events", BindingFlags.Instance | BindingFlags.NonPublic);
                DollSingerEnemyBrain Brain() => new DollSingerEnemyBrain { Inventory = InventoryGraph.Create(false, false),
                    Random = new Unity.Mathematics.Random(137), Seed = 1 };
                collect.Invoke(ai, new object[] { 10d });
                var health = manager.GetComponentData<PredictedPlayerGhost>(humanTarget.LinkedEntity);
                RaidLootAcoustics.Mark(ref health); manager.SetComponentData(humanTarget.LinkedEntity, health);
                collect.Invoke(ai, new object[] { 10.1d });
                var emitted = (IList)events.GetValue(ai);
                Check(emitted.Count == 1 && Mathf.Approximately((float)emitted[0].GetType().GetField("Range").GetValue(emitted[0]), RaidLootAcoustics.HearingRange),
                    "PMC did not collect the server's search event at its bounded range.");
                var listener = Brain(); sense.Invoke(ai, new object[] { listener, observer.transform, pmcOrigin + Vector3.up * 1.4f, 0f, 10.1d });
                Check(listener.Target == humanTarget.LinkedEntity && !listener.Visible && !listener.Contacts[humanTarget.LinkedEntity].Confirmed,
                    "PMC failed to investigate unseen looting as an uncertain clue.");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "PMC loot wall";
                wall.transform.SetParent(holder.transform); wall.transform.position = pmcOrigin + new Vector3(0, 2, 3);
                wall.transform.localScale = new Vector3(10, 4, .5f);
                Physics.SyncTransforms();
                var blocked = Brain(); sense.Invoke(ai, new object[] { blocked, observer.transform, pmcOrigin + Vector3.up * 1.4f, 0f, 10.1d });
                Check(blocked.Target == Entity.Null, "A wall did not reduce PMC loot hearing range.");
                Object.DestroyImmediate(wall); Physics.SyncTransforms();
                collect.Invoke(ai, new object[] { 10.2d });
                Check(((IList)events.GetValue(ai)).Count == 0, "An unchanged search sequence produced repeated events.");

                var mapObject = new GameObject("Temporary loot objective map"); mapObject.transform.SetParent(holder.transform); mapObject.SetActive(false);
                var map = mapObject.AddComponent<MoonRaidMap>();
                var serialized = new SerializedObject(map); var positions = serialized.FindProperty("m_LootPositions");
                positions.arraySize = 1; positions.GetArrayElementAtIndex(0).vector3Value = pmcOrigin + Vector3.forward;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var objective = typeof(DollSingerEnemySystem).GetMethod("AdvanceObjective", BindingFlags.Instance | BindingFlags.NonPublic);
                var scavenge = Brain(); scavenge.Action = PmcAction.Scavenge; scavenge.Cache = 0;
                var args = new object[] { scavenge, default(PredictedPlayerGhost), pmcOrigin, pmcOrigin + Vector3.up * 1.4f, map, 20d, .1f };
                objective.Invoke(ai, args);
                Check(((PredictedPlayerGhost)args[1]).LootSearchSequence == 1 && scavenge.LootUntil > 20,
                    "A real PMC search did not emit its own opening sound.");
                args[5] = 20.1d; objective.Invoke(ai, args);
                Check(((PredictedPlayerGhost)args[1]).LootSearchSequence == 1, "A continuing PMC search emitted sound every frame.");
                scavenge.LootUntil = 0; args[2] = pmcOrigin + Vector3.right * 20; args[3] = (Vector3)args[2] + Vector3.up * 1.4f;
                objective.Invoke(ai, args);
                Check(((PredictedPlayerGhost)args[1]).LootSearchSequence == 1, "An out-of-range PMC produced a search sound.");

                var lootEntity = manager.CreateEntity(typeof(RaidLootWorld));
                var containers = new RaidLootContainers(); containers.Initialize(1); manager.AddComponentObject(lootEntity, containers);
                var sharedCache = containers.Containers[0];
                sharedCache.Items.RemoveAll(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root); sharedCache.Version++;
                Check(sharedCache.AddSupply("data_drive", 1, LootInventoryExchange.Root, true) == InventoryError.None,
                    "PMC loot fixture could not create its real valuable item.");
                var valuableId = sharedCache.Items.Find(item => item.Code == "data_drive").Id;
                args[2] = pmcOrigin; args[3] = pmcOrigin + Vector3.up * 1.4f; args[5] = 21d; objective.Invoke(ai, args);
                double finishedAt = scavenge.LootUntil;
                args[5] = finishedAt - .01; objective.Invoke(ai, args);
                Check(sharedCache.Find(valuableId) != null && scavenge.Inventory.Find(valuableId) == null,
                    "PMC took the real item before its search finished.");
                args[5] = finishedAt; objective.Invoke(ai, args);
                Check(sharedCache.Find(valuableId) == null && scavenge.Inventory.Find(valuableId) != null &&
                    scavenge.VisitedCaches.Contains(0) && scavenge.LootedCaches == 1,
                    "A completed PMC search did not transfer the real valuable item and finish the objective.");
            }
            finally { Object.DestroyImmediate(tuning); }
            return $"PASS {checks} loot acoustics checks: world audio, server sequence, real PMC scavenging and alien/PMC hearing.";
        }
        finally
        {
            Object.DestroyImmediate(holder); Object.DestroyImmediate(profile);
            if (world != null && world.IsCreated) world.Dispose();
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
