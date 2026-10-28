using System;
using System.Linq;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OxygenHandActionChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Oxygen/Check Authored Refill Hands")]
    public static string Run()
    {
        s_Checks = 0;
        foreach (string code in new[] { "oxygen_canister", "oxygen_reserve" })
            Check(RaidHandActionSystem.Kind(code, true) == HandAction.RefillOxygen, code + " must not use treatment or drinking");
        Check(RaidHandActionSystem.Kind("water", true) == HandAction.Drink &&
            RaidHandActionSystem.Kind("medkit", false) == HandAction.Inject, "Existing action routing remains intact");
        Authority();
        foreach (string rig in RaidActionAnimationSetup.Characters) Rig(rig);
        return $"PASS: {s_Checks} authored oxygen action, live server state, rig sampling and cancellation checks.";
    }

    private static void Authority()
    {
        using var world = new World("Isolated oxygen hand publication");
        var em = world.EntityManager;
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        health.Oxygen = 20; health.ProtectionInitialized = health.HeadDomeEquipped = true;
        var actor = em.CreateEntity(typeof(PredictedPlayerGhost)); em.SetComponentData(actor, health);
        var connection = em.CreateEntity(typeof(JoinedClient)); em.SetComponentData(connection, new JoinedClient { PlayerEntity = actor });
        var inventory = new RaidInventoryState { Graph = InventoryGraph.Create(false, false) };
        Check(inventory.Graph.AddSupply("oxygen_canister", 1) == InventoryError.None, "Oxygen fixture fits pockets");
        var item = inventory.Graph.Items.Find(i => i.Code == "oxygen_canister");
        Check(inventory.BeginConsumable(health, item.Id, inventory.Graph.Version, float3.zero, 0) == InventoryError.None, "Timed refill starts");
        em.AddComponentObject(connection, inventory);
        var publisher = world.GetOrCreateSystemManaged<RaidHandActionSystem>();
        world.SetTime(new TimeData(.25, .25f)); publisher.Update();
        var snapshot = em.GetComponentData<PredictedPlayerGhost>(actor);
        Check((HandAction)snapshot.HandAction == HandAction.RefillOxygen && snapshot.HandActionItem.ToString() == item.Code,
            "Real server publisher selects oxygen take and actual item");
        Check(Mathf.Abs(snapshot.HandActionDuration - 2) < .001f && Mathf.Abs(snapshot.HandActionRemaining - 1.75f) < .001f,
            "Authoritative action uses two-second timeline");
        uint sequence = snapshot.HandActionSequence;
        world.SetTime(new TimeData(.5, .25f)); publisher.Update();
        Check(em.GetComponentData<PredictedPlayerGhost>(actor).HandActionSequence == sequence, "Snapshot refresh cannot restart hand animation");
        inventory.CancelActions(); publisher.Update(); snapshot = em.GetComponentData<PredictedPlayerGhost>(actor);
        Check(snapshot.HandAction == 0 && snapshot.HandActionItem.IsEmpty && inventory.Graph.Find(item.Id) != null && snapshot.Oxygen == 20,
            "Cancellation clears hand intent and preserves canister and oxygen");
    }

    private static void Rig(string rig)
    {
        var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
        try
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"), scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var parent = animator.transform; parent; parent = parent.parent) parent.gameObject.SetActive(true);
            animator.runtimeAnimatorController = null; animator.Rebind(); animator.Update(0); animator.enabled = false;
            var asset = Resources.Load<TextAsset>("Moonkov/Actions/" + rig);
            var playback = new DollSingerActionAnimation(animator, asset);
            Check(playback.Has(HandAction.RefillOxygen), rig + " has dedicated oxygen data");
            foreach (string code in new[] { "oxygen_canister", "oxygen_reserve" })
            {
                var motion = playback.GetMotion(HandAction.RefillOxygen, code);
                Check(motion != null && motion.items.Contains(code) && motion.frames == 121 && Mathf.Abs(motion.seconds - 2) < .001f,
                    rig + " / " + code + " resolves its own source take");
                Check(motion.rotations.All(float.IsFinite) && motion.root.positions.All(float.IsFinite), rig + " has finite authored samples");
                float radius = code == "oxygen_reserve" ? .051f : .037f;
                foreach (float phase in new[] { 0f, .2f, .5f, .8f, 1f })
                {
                    playback.RestoreBeforeAnimation(); playback.Sample(HandAction.RefillOxygen, phase, 1, code);
                    motion.root.Sample(phase, motion.frames, out var offset, out _);
                    Vector3 center = playback.RightHand.position + playback.RightHand.rotation * offset;
                    Vector3 index = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal).position;
                    var radial = index - center;
                    Check(Mathf.Sqrt(radial.x * radial.x + radial.z * radial.z) >= radius - .001f,
                        rig + " finger chain must wrap outside the actual canister");
                }
            }
            var presentation = animator.gameObject.AddComponent<RaidActionPresentation>(); presentation.Configure(animator, null, null, rig);
            var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state);
            state.HandAction = (byte)HandAction.RefillOxygen; state.HandActionItem = "oxygen_canister";
            state.HandActionSequence = 1; state.HandActionRemaining = state.HandActionDuration = 2;
            presentation.Present(state, true, true, false); presentation.SamplePreview(.5f);
            Check(presentation.Action == HandAction.RefillOxygen && presentation.OwnsHands, rig + " actual presentation owns oxygen hands");
            presentation.ResetPresentation();
            Check(!presentation.OwnsHands, rig + " interrupted action releases hands");
        }
        finally { if (root) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static void RunBatch()
    {
        try
        {
            Debug.Log(Run()); Debug.Log(OxygenFeedbackChecks.Run());
            System.IO.File.WriteAllText("Library/BuildValidation/OxygenHands-result.txt", "PASS");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
