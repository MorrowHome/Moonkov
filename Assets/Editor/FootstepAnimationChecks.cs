using System;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.DollSinger.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FootstepAnimationChecks
{
    [MenuItem("Tools/Moonkov/Check Footstep Animation Events")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run in an isolated Play scene so Unity dispatches animation events.");
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); }
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DollSingerLocomotionTools.ControllerPath);
        foreach (var clip in controller.animationClips.Distinct())
        {
            string path = AssetDatabase.GetAssetPath(clip);
            bool locomotion = path.StartsWith(DollSingerLocomotionTools.ClipRoot + "/", StringComparison.Ordinal) ||
                path.StartsWith(DollSingerStanceTools.Root + "/", StringComparison.Ordinal);
            if (!locomotion) continue;
            var steps = clip.events.Where(e => e.functionName == "OnFootstep").ToArray();
            if (clip.name.EndsWith("_Idle", StringComparison.Ordinal))
                Check(steps.Length == 0, "Idle clip emits footsteps: " + clip.name);
            else
            {
                Check(steps.Length >= 2, "Moving clip has no pair of contacts: " + clip.name);
                Check(steps.All(e => e.time >= 0 && e.time < clip.length) &&
                    steps.Select(e => e.intParameter).Distinct().Count() == 2, "Invalid left/right contacts: " + clip.name);
            }
        }
        var scene = SceneManager.CreateScene("Footstep animation checks");
        GameObject root = null;
        try
        {
            root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab"));
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var model = root.GetComponentInChildren<DollSingerMovement>(true);
            typeof(DollSingerMovement).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(model, null);
            model.enabled = true;
            var animator = model.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = true;
            var probe = model.gameObject.AddComponent<FootstepEventProbe>();
            int count = 0; model.Footstep += () => count++;
            foreach (float aim in new[] { 0f, .5f, 1f })
            foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                new Vector3(1, 0, 1).normalized })
            foreach (float speed in new[] { .65f, 1.3f, 2f })
            {
                animator.Rebind(); animator.SetFloat("AimBlend", aim);
                animator.Play("Base Layer.Idle Walk Run Blend", 0, 0);
                model.ApplyNetworkPresentation(speed, 0, true, false, false, .016f, direction * speed);
                animator.Update(0); count = 0;
                for (int frame = 0; frame < 180; frame++)
                {
                    model.ApplyNetworkPresentation(speed, 0, true, false, false, 1f / 60, direction * speed);
                    animator.Update(1f / 60);
                }
                Check(count >= 2 && count <= 14, $"Missing/duplicated gait events at aim={aim}, speed={speed}, direction={direction}: {count}, raw={probe.Count}");
            }
            foreach (var stance in new[] { DollSingerStance.Crouching, DollSingerStance.Prone, DollSingerStance.Tiptoe })
            {
                animator.Rebind();
                float speed = stance == DollSingerStance.Crouching ? 1 : stance == DollSingerStance.Prone ? .44f : .8f;
                for (int frame = 0; frame < 30; frame++)
                { model.ApplyNetworkPresentation(speed, 0, true, false, false, 1f / 60, Vector3.forward * speed, stance: stance); animator.Update(1f / 60); }
                count = 0;
                for (int frame = 0; frame < 240; frame++)
                { model.ApplyNetworkPresentation(speed, 0, true, false, false, 1f / 60, Vector3.forward * speed, stance: stance); animator.Update(1f / 60); }
                Check(count >= 2 && count <= 12, $"Missing/duplicated stance contacts: {stance}: {count}");
            }
            foreach (bool grounded in new[] { true, false })
            {
                animator.Rebind(); count = 0;
                for (int frame = 0; frame < 180; frame++)
                { model.ApplyNetworkPresentation(grounded ? 0 : 2, 0, grounded, false, false, 1f / 60, Vector3.forward * 2); animator.Update(1f / 60); }
                Check(count == 0, grounded ? "Idle emits footsteps." : "Airborne animation emits footsteps.");
            }
        }
        finally
        {
            if (root) UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
        return $"PASS {checks} footstep clip and real Animator event checks";
    }
}

public sealed class FootstepEventProbe : MonoBehaviour
{
    public int Count;
    private void OnFootstep(AnimationEvent evt) { Count++; }
}
