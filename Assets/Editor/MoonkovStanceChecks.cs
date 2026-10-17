using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MoonkovStanceChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool value, string message, List<string> passed)
    {
        if (!value) throw new InvalidOperationException("Stance check: " + message);
        passed.Add(message);
    }

    [MenuItem("Tools/Moonkov/Check Character Stances")]
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        var passed = new List<string>();
        var scene = EditorSceneManager.NewPreviewScene();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab");
        var player = UnityEngine.Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(player, scene);
        foreach (var b in player.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        player.transform.position = new Vector3(1000, 1000, 1000);
        var motor = player.GetComponent<FirstPersonController>();
        typeof(FirstPersonController).GetMethod("Awake", Private).Invoke(motor, null);
        var capsule = motor.CharacterController;
        float height = capsule.height;
        Vector3 center = capsule.center;
        var roof = new GameObject("Stance clearance roof");
        SceneManager.MoveGameObjectToScene(roof, scene);
        var box = roof.AddComponent<BoxCollider>();
        box.size = new Vector3(3, .1f, 3);
        roof.transform.position = player.transform.position + Vector3.up * 1.3f;
        try
        {
            var constants = player.GetComponent<PredictedPlayerControllerConstsAuthoring>().BuildControllerConsts();
            foreach (DollSingerStance stance in Enum.GetValues(typeof(DollSingerStance)))
            {
                var input = new PlayerInput { MoveInput = new float2(0, 1) };
                input.SetStance(stance);
                input.SetFlag(PlayerInput.InputFlag.Sprint, true);
                Require(input.Sanitized().Stance == stance, "command sanitization preserves " + stance, passed);
                var buffered = default(PlayerInput);
                buffered.UpdateFrom(input);
                Require(buffered.Stance == stance, "command buffering preserves " + stance, passed);
                var state = new FirstPersonController.ControllerState { CurrentPosition = player.transform.position,
                    CurrentRotation = quaternion.identity, MovementType = FirstPersonController.MovementType.Standing,
                    GroundNormal = math.up(), HaloLightEnabled = false, Stance = stance };
                Require(!state.HaloLightEnabled && state.Stance == stance, "stance bits preserve light state " + stance, passed);
                box.enabled = false;
                UnityEngine.Physics.SyncTransforms();
                float3 move = float3.zero;
                FirstPersonController.AccumulateMovement(ref state, ref move, input, constants, .02f, motor);
                float speed = stance == DollSingerStance.Standing ? constants.Sprint.Speed :
                    constants.Walk.Speed * DollSingerStances.SpeedScale(stance);
                Require(Mathf.Abs(state.MovementSpeed - speed) < .001f, "authoritative movement speed " + stance, passed);
                Require(state.Sprinting == (stance == DollSingerStance.Standing), "sprint gating " + stance, passed);
                motor.ApplyStanceCapsule(stance);
                Require(Mathf.Abs(capsule.center.y - capsule.height * .5f - (center.y - height * .5f)) < .001f,
                    "capsule feet remain anchored " + stance, passed);
                if (stance != DollSingerStance.Standing)
                {
                    input.SetFlag(PlayerInput.InputFlag.Jump, true);
                    move = float3.zero;
                    FirstPersonController.AccumulateMovement(ref state, ref move, input, constants, .02f, motor);
                    Require(!state.JumpTriggered, "no jump while posture is held " + stance, passed);
                }
            }
            box.enabled = true;
            UnityEngine.Physics.SyncTransforms();
            var resolved = DollSingerStances.Resolve(capsule, player.transform.position, DollSingerStance.Crouching,
                DollSingerStance.Standing, true, height, center);
            Require(resolved == DollSingerStance.Crouching, "low ceiling blocks standing", passed);
            Require(DollSingerStances.Resolve(capsule, player.transform.position, DollSingerStance.Prone,
                DollSingerStance.Crouching, true, height, center) == DollSingerStance.Crouching, "low ceiling allows intermediate crouch", passed);
            box.enabled = false;
            UnityEngine.Physics.SyncTransforms();
            Require(DollSingerStances.Resolve(capsule, player.transform.position, DollSingerStance.Crouching,
                DollSingerStance.Standing, true, height, center) == DollSingerStance.Standing, "standing resumes after leaving cover", passed);
            Require(DollSingerStances.Resolve(capsule, player.transform.position, DollSingerStance.Standing,
                DollSingerStance.Prone, false, height, center) == DollSingerStance.Standing, "airborne posture entry rejected", passed);
            CheckAnimation(player, passed);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(roof);
            UnityEngine.Object.DestroyImmediate(player);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Physics.SyncTransforms();
        }
        string result = "Passed " + passed.Count + " stance checks:\n" + string.Join("\n", passed);
        System.IO.Directory.CreateDirectory("Temp/StanceValidation");
        System.IO.File.WriteAllText("Temp/StanceValidation/checks.txt", result);
        return result;
    }

    private static void CheckAnimation(GameObject player, List<string> passed)
    {
        var model = player.GetComponentInChildren<DollSingerMovement>(true);
        model.gameObject.SetActive(true);
        var animator = model.GetComponent<Animator>();
        animator.enabled = true;
        player.transform.position = Vector3.zero;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind(); animator.Update(0);
        int layer = animator.GetLayerIndex("Stances");
        Require(layer > 0 && layer < animator.GetLayerIndex("Halo Aim"), "stance animation precedes weapon hand gestures", passed);
        float standHead = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
        foreach (var name in new[] { "Crouch_Idle", "Crouch_Move", "Prone_Idle", "Prone_Crawl", "Tiptoe_Idle", "Tiptoe_Move" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Stances/" + name + ".anim");
            Require(clip && clip.isHumanMotion && AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                "retargetable looping clip " + name, passed);
        }
        animator.SetLayerWeight(layer, 1);
        animator.SetInteger("Stance", 1);
        animator.Play("Crouching", layer, .2f); animator.Update(0);
        Require(animator.GetBoneTransform(HumanBodyBones.Head).position.y < standHead - .3f, "crouch visibly lowers head", passed);
        animator.SetInteger("Stance", 2);
        animator.Play("Prone", layer, .2f); animator.Update(0);
        Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head).position;
        Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
        Require(head.y < .7f && head.z > hips.z + .2f, "prone faces forward and lowers head", passed);
        animator.SetInteger("Stance", 3);
        animator.Play("Tiptoe", layer, .2f); animator.Update(0);
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Vector3 contact = animator.GetBoneTransform(HumanBodyBones.LeftToes).position + model.transform.forward * .08f;
        Vector3 localContact = leftFoot.InverseTransformPoint(contact);
        Vector3 foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
        float headBefore = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
        typeof(DollSingerMovement).GetMethod("Awake", Private).Invoke(model, null);
        typeof(DollSingerMovement).GetField("m_TiptoeBlend", Private).SetValue(model, 1f);
        typeof(DollSingerMovement).GetMethod("ApplyTiptoePose", Private).Invoke(model, null);
        Require(Vector3.Distance(contact, leftFoot.TransformPoint(localContact)) < .025f,
            "tiptoe preserves toe contact", passed);
        Require(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y > foot.y + .025f &&
            animator.GetBoneTransform(HumanBodyBones.Head).position.y > headBefore + .05f, "tiptoe raises heel and eye height", passed);
    }
}
