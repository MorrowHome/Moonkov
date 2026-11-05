using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LocomotionCadenceChecks
{
    [MenuItem("Tools/Moonkov/Check Natural Locomotion Cadence")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); }
        var scene = EditorSceneManager.NewPreviewScene();
        var samples = new List<string>();
        try
        {
            foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab",
                "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab", "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab" })
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var model = root.GetComponentInChildren<DollSingerMovement>(true);
                root.SetActive(true); model.gameObject.SetActive(true);
                typeof(DollSingerMovement).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(model, null);
                var animator = model.GetComponent<Animator>();
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.fireEvents = false;
                float Cycle(float speed, Vector3 direction, float aim, DollSingerStance stance = DollSingerStance.Standing)
                {
                    animator.Rebind(); animator.SetFloat("AimBlend", aim);
                    animator.Play("Base Layer.Idle Walk Run Blend", 0, 0);
                    int layer = stance == DollSingerStance.Standing ? 0 : animator.GetLayerIndex("Stances");
                    for (int i = 0; i < 90; i++)
                    {
                        model.ApplyNetworkPresentation(speed, 0, true, speed > 2, false, 1f / 60, direction * speed, stance: stance);
                        animator.Update(1f / 60);
                    }
                    float before = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
                    for (int i = 0; i < 60; i++)
                    {
                        model.ApplyNetworkPresentation(speed, 0, true, speed > 2, false, 1f / 60, direction * speed, stance: stance);
                        animator.Update(1f / 60);
                    }
                    return 1f / (animator.GetCurrentAnimatorStateInfo(layer).normalizedTime - before);
                }
                foreach (float aim in new[] { 0f, .5f, 1f })
                foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                    new Vector3(1, 0, 1).normalized })
                {
                    float healthy = Cycle(2, direction, aim);
                    float injured = Cycle(1.3f, direction, aim);
                    float burdened = Cycle(.65f, direction, aim);
                    Check(healthy < 1.2f && Mathf.Abs(healthy - injured) < .025f && Mathf.Abs(healthy - burdened) < .025f,
                        $"Slow motion remains on {root.name}, aim={aim}, direction={direction}: {healthy:F3}/{injured:F3}/{burdened:F3}s");
                    Check(animator.speed == 1, "Global Animator speed changed, affecting hands and actions.");
                    if (aim == 0 && direction == Vector3.forward)
                        samples.Add($"{root.name}: walk {healthy:F3}s, injured {injured:F3}s, burdened {burdened:F3}s");
                }
                foreach (var stance in new[] { DollSingerStance.Crouching, DollSingerStance.Prone, DollSingerStance.Tiptoe })
                {
                    float speed = stance == DollSingerStance.Crouching ? 1 : stance == DollSingerStance.Prone ? .44f : .8f;
                    float healthy = Cycle(speed, Vector3.forward, 0, stance);
                    float injured = Cycle(speed * .65f, Vector3.forward, 0, stance);
                    Check(Mathf.Abs(healthy - injured) < .025f, "Injured stance slows the entire animation: " + stance);
                }
                Cycle(0, Vector3.forward, 0);
                Check(animator.GetFloat("MotionSpeed") == 1 && animator.GetFloat("StanceMotionSpeed") == 1,
                    "Idle retained the walking rate correction.");
                model.ApplyNetworkPresentation(1.3f, -1, false, false, false, .016f, Vector3.forward);
                Check(animator.GetFloat("MotionSpeed") == 1 && animator.GetFloat("StanceMotionSpeed") == 1,
                    "Airborne state retained a ground rate correction.");
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        return $"PASS {checks} cadence checks; " + string.Join("; ", samples);
    }
}
