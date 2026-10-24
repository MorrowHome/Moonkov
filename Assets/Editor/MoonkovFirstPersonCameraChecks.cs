using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MP_FPS.DollSinger;

/// <summary>Offline skin/near-plane checks; does not start Play or alter the open scene.</summary>
public static class MoonkovFirstPersonCameraChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Check First Person Camera")]
    public static void Run() => Debug.Log(RunReport());

    public static string RunReport()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run camera checks in Edit Mode.");
        int cases = 0, oldIntersections = 0;
        float minimumGap = float.PositiveInfinity;
        foreach (var path in new[] { "Assets/HatsuneMiku/Prefabs/HatsuneMikuPlayer.prefab",
            "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab" })
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var baked = new Mesh();
            GameObject root = null;
            try
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                var animator = root.GetComponentInChildren<Animator>();
                var clips = animator.runtimeAnimatorController.animationClips.Distinct()
                    .Where(c => new[] { "run", "jump", "land", "fall", "crouch", "prone" }
                        .Any(s => c.name.ToLowerInvariant().Contains(s))).ToArray();
                animator.runtimeAnimatorController = null;
                animator.Rebind();
                var transforms = root.GetComponentsInChildren<Transform>(true);
                var positions = transforms.Select(t => t.localPosition).ToArray();
                var rotations = transforms.Select(t => t.localRotation).ToArray();
                var scales = transforms.Select(t => t.localScale).ToArray();
                var view = root.GetComponentInChildren<DollSingerView>(true);
                var type = typeof(DollSingerView);
                type.GetMethod("ResolveEyeAnchor", Private).Invoke(view, new object[] { root });
                var renderer = (SkinnedMeshRenderer)type.GetMethod("FindBodyRenderer", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { root });
                var safetyField = type.GetField("torsoClearance", Private);
                var safety = Activator.CreateInstance(safetyField.FieldType, renderer, animator);
                var solve = type.GetMethod("GetFirstPersonEyePosition", Private);
                var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                Quaternion headBind = Quaternion.Inverse(root.transform.rotation) * head.rotation;
                var torso = new[] { animator.GetBoneTransform(HumanBodyBones.Chest),
                    animator.GetBoneTransform(HumanBodyBones.UpperChest), neck }.Where(t => t).ToArray();
                var bones = renderer.bones;
                bool Torso(int index) => torso.Contains(bones[index]);
                var indices = renderer.sharedMesh.boneWeights.Select((w, i) => new {
                    Index = i, Weight = (Torso(w.boneIndex0) ? w.weight0 : 0)
                        + (Torso(w.boneIndex1) ? w.weight1 : 0) + (Torso(w.boneIndex2) ? w.weight2 : 0)
                        + (Torso(w.boneIndex3) ? w.weight3 : 0) }).Where(w => w.Weight >= .5f).Select(w => w.Index).ToArray();
                if (indices.Length == 0 || clips.Length == 0) throw new Exception("Missing torso or movement clips: " + path);
                view.camera.aspect = 16f / 9f;
                foreach (var clip in clips)
                foreach (float time in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                        transforms[i].localScale = scales[i];
                    }
                    clip.SampleAnimation(root, clip.length * time);
                    foreach (float pitch in new[] { -60f, 0f, 45f, 85f })
                    foreach (float yaw in new[] { -70f, 0f, 70f })
                    {
                        view.transform.rotation = root.transform.rotation * Quaternion.Euler(pitch, yaw, 0);
                        // Movement.StabilizeHeadLook writes the head before View.LateUpdate.
                        head.rotation = view.transform.rotation * headBind;
                        // Independent Unity skinning oracle, including imported FBX scales.
                        renderer.BakeMesh(baked, true);
                        var vertices = baked.vertices;
                        float top = indices.Max(i => Vector3.Dot(renderer.transform.TransformPoint(vertices[i]), root.transform.up));
                        safetyField.SetValue(view, null);
                        var oldEye = (Vector3)solve.Invoke(view, null);
                        safetyField.SetValue(view, safety);
                        var eye = (Vector3)solve.Invoke(view, null);
                        float halfHeight = view.firstPersonNearClip * Mathf.Tan(view.firstPersonFieldOfView * .5f * Mathf.Deg2Rad);
                        foreach (float roll in new[] { -15f, 0f, 15f })
                        {
                            Quaternion look = view.transform.rotation * Quaternion.Euler(0, 0, roll);
                            foreach (float x in new[] { -1f, 1f })
                            foreach (float y in new[] { -1f, 1f })
                            {
                                var corner = look * new Vector3(x * halfHeight * view.camera.aspect, y * halfHeight, view.firstPersonNearClip);
                                float gap = Vector3.Dot(eye + corner, root.transform.up) - top;
                                minimumGap = Mathf.Min(minimumGap, gap);
                                if (Vector3.Dot(oldEye + corner, root.transform.up) < top) oldIntersections++;
                                if (gap < .014f) throw new Exception(path + " " + clip.name + " t=" + time + " pitch=" + pitch + " gap=" + gap);
                            }
                        }
                        cases++;
                    }
                }
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(baked);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        return "First person camera checks passed: " + cases + " animated look poses; minimum near-plane/torso height gap="
            + minimumGap.ToString("F4") + "m; previous unsafe near-plane corners=" + oldIntersections
            + ". No Play or network session started; visual framing still needs player acceptance.";
    }
}
