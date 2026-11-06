using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CorpseLootInteractionChecks
{
    [MenuItem("Tools/Moonkov/Check Corpse Loot Interaction")]
    public static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Requires idle Edit mode.");
        int checks = 0, eligible = 0, legacyMisses = 0;
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException("Corpse loot: " + label); checks++; }
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject holder = null;
        try
        {
            Check(scene.GetPhysicsScene() == Physics.defaultPhysicsScene, "occlusion fixture uses the queried physics scene");
            SceneManager.SetActiveScene(scene);
            holder = new GameObject("Corpse loot fixture");
            var origin = new Vector3(10000, 200, 10000);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(holder.transform, false);
            floor.transform.position = origin - Vector3.up * .1f; floor.transform.localScale = new Vector3(12, .2f, 12);
            var cameraObject = new GameObject("Aim probe"); cameraObject.transform.SetParent(holder.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            var mapObject = new GameObject("Inactive marker lookup"); mapObject.transform.SetParent(holder.transform, false); mapObject.SetActive(false);
            var map = mapObject.AddComponent<MoonRaidMap>();
            var markers = (Dictionary<int, GameObject>)typeof(MoonRaidMap).GetField("m_DeathBags", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(map);
            var settings = Resources.Load<CorpsePresentationSettings>("Moonkov/CorpsePresentation");
            Check(settings && settings.Death, "actual corpse presentation assets exist");
            Physics.SyncTransforms();
            foreach (int character in new[] { 2, 3, 4 })
            {
                int id = RaidLootContainers.FirstDeathBagId + character;
                var body = CorpseVisual.Spawn(holder.transform, settings, new RaidCorpseRpc {
                    LootId = id, Version = 1, CharacterIndex = character, Position = origin,
                    Age = settings.Death.length + 1,
                    Rotation = Quaternion.Euler(0, (character - 2) * 90, 0) });
                body.SampleAge(settings.Death.length + 1); markers.Add(id, body.gameObject);
                var animator = body.GetComponentInChildren<Animator>();
                var anchor = origin + Vector3.up * .25f;
                foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head })
                {
                    Vector3 target = animator.GetBoneTransform(bone).position;
                    foreach (Vector3 side in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                    {
                        var actor = target + side * 1.2f; actor.y = origin.y;
                        camera.transform.position = actor + Vector3.up * 1.6f; camera.transform.LookAt(target);
                        Check(map.TryGetCorpseAimPoint(id, camera, out var point), "marker resolves its actual body " + character);
                        float score = RaidInteractionAim.Score(camera, actor, point, RaidRules.PickupRange, rangeAnchor: anchor);
                        if (Vector3.Distance(actor, anchor) <= RaidRules.PickupRange)
                        {
                            eligible++;
                            if (RaidInteractionAim.Score(camera, actor, anchor, RaidRules.PickupRange) < 0) legacyMisses++;
                            Check(score > 0, "aimed visible body is selectable " + character + "/" + bone + "/" + side);
                        }
                        else Check(score < 0, "body animation cannot extend authoritative pickup range");
                    }
                }

                var actorNear = origin + Vector3.back;
                camera.transform.position = actorNear + Vector3.up * 1.6f;
                camera.transform.LookAt(animator.GetBoneTransform(HumanBodyBones.Chest).position);
                var selected = body.LootAimPoint(camera);
                Check(RaidInteractionAim.Score(camera, actorNear, selected, RaidRules.PickupRange, rangeAnchor: anchor) > 0, "control pose is selectable");
                Check(RaidInteractionAim.Score(camera, origin + Vector3.back * (RaidRules.PickupRange + 1), selected,
                    RaidRules.PickupRange, rangeAnchor: anchor) < 0, "range still uses the server marker");

                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(holder.transform, false);
                wall.transform.position = Vector3.Lerp(camera.transform.position, selected, .5f);
                wall.transform.localScale = Vector3.one * .2f; Physics.SyncTransforms();
                Check(RaidInteractionAim.Score(camera, actorNear, selected, RaidRules.PickupRange, rangeAnchor: anchor) < 0, "wall blocks the camera's body target");
                wall.transform.position = Vector3.Lerp(actorNear + Vector3.up * 1.4f, anchor, .5f); Physics.SyncTransforms();
                Check(RaidInteractionAim.Visible(camera.transform.position, selected), "access-wall fixture leaves the visible body unobstructed");
                Check(RaidInteractionAim.Score(camera, actorNear, selected, RaidRules.PickupRange, rangeAnchor: anchor) < 0, "wall blocks the server access anchor");
                UnityEngine.Object.DestroyImmediate(wall);

                camera.transform.LookAt(anchor);
                Check(RaidInteractionAim.Score(camera, actorNear, anchor, RaidRules.PickupRange) ==
                    RaidInteractionAim.Score(camera, actorNear, anchor, RaidRules.PickupRange, rangeAnchor: anchor),
                    "ordinary point targets retain their existing range and visibility behavior");

                foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 1000);
                camera.transform.LookAt(camera.transform.position + Vector3.right * 5);
                Check(RaidInteractionAim.Score(camera, actorNear, body.LootAimPoint(camera), RaidRules.PickupRange, rangeAnchor: anchor) < 0,
                    "inflated renderer culling bounds do not become a loot hitbox");
                markers.Remove(id); UnityEngine.Object.DestroyImmediate(body.gameObject);
                Check(!map.TryGetCorpseAimPoint(id, camera, out _), "removed marker cannot resolve a stale corpse");
            }
            Check(eligible >= 24 && legacyMisses >= 12, "real falling poses reproduce the original missing-prompt defect");
            return "PASS " + checks + " corpse interaction checks; " + eligible + " visible pose/approach cases selectable, legacy anchor missed " + legacyMisses +
                ". Three PMC characters, range, both occlusion paths, culling bounds and marker removal. No accounts, Play or live multiplayer.";
        }
        finally
        {
            if (holder) UnityEngine.Object.DestroyImmediate(holder);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
