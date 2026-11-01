using System;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DollSingerAnimationBudgetChecks
{
    [MenuItem("Tools/Moonkov/Performance/Check remote animation budget")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var configure = typeof(DollSingerNetworkPresentation).GetMethod("ConfigureClientAnimationBudget", flags);
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        foreach (var path in new[] { "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab",
            "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab", "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab" })
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                var presentation = root.GetComponent<DollSingerNetworkPresentation>();
                var model = root.GetComponentInChildren<DollSingerMovement>(true);
                var animator = model.GetComponent<Animator>();
                typeof(DollSingerNetworkPresentation).GetField("m_PresentationAnimator", flags).SetValue(presentation, animator);
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var offscreen = renderers.Select(r => r.updateWhenOffscreen).ToArray();
                var meshes = renderers.Select(r => r.sharedMesh).ToArray();
                var bounds = renderers.Select(r => r.localBounds).ToArray();
                var mode = animator.cullingMode;
                var controller = animator.runtimeAnimatorController;
                var avatar = animator.avatar;
                var colliders = root.GetComponentsInChildren<Collider>(true);
                var enabled = colliders.Select(c => c.enabled).ToArray();
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    configure.Invoke(presentation, new object[] { true });
                    Check(animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms && renderers.All(r => !r.updateWhenOffscreen), "Remote avatar still forces offscreen skinning");
                    Check(animator.runtimeAnimatorController == controller && animator.avatar == avatar, "Animation state assets changed");
                    Check(renderers.Select(r => r.sharedMesh).SequenceEqual(meshes) && renderers.Select(r => r.localBounds).SequenceEqual(bounds), "Skin geometry or visibility bounds changed");
                    Check(colliders.Select(c => c.enabled).SequenceEqual(enabled), "Presentation budget changed gameplay colliders");
                    configure.Invoke(presentation, new object[] { false });
                    Check(animator.cullingMode == mode && renderers.Select(r => r.updateWhenOffscreen).SequenceEqual(offscreen), "Owner/original skinning settings were not restored");
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        return $"Remote animation budget: PASS ({checks} checks, 3 avatars, geometry/collider preservation and repeated owner restoration).";
    }
}
