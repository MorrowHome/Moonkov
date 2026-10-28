using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class RaidActionAnimationSetup
{
    public const string Root = "Assets/Resources/Moonkov/Actions/";
    public static readonly string[] Characters = { "DollSinger", "HatsuneMiku", "Shian" };
    private const string Source = "Assets/ThirdParty/Quaternius/Animation/";

    [MenuItem("Tools/Moonkov/Actions/Bake legacy action seed")]
    public static void BakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake outside Play.");
        foreach (string character in Characters)
        {
            string path = Root + character + ".json";
            if (File.Exists(path) && JsonUtility.FromJson<DollSingerActionAnimation.Library>(File.ReadAllText(path)).anatomicalContactVersion >= 2)
                throw new InvalidOperationException("Reviewed Blender actions already exist. Rebuild them with Tools/Blender/export_survival_runtime.py and build_survival_runtime.py. The legacy seed must not overwrite them.");
        }
        QuaterniusMeleeSetup.Import();
        Directory.CreateDirectory(Root);
        foreach (string character in Characters) Bake(character);
        AssetDatabase.Refresh();
    }

    private static void Bake(string character)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var graph = PlayableGraph.Create("Authored survival actions");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{character}/Prefabs/{character}NetworkPlayer.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = true;
            animator.Rebind(); animator.Update(0);
            var hierarchy = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            // Preserve the complete MMD arm chains, including sibling twist helpers.
            // Spine and legs continue to belong to locomotion at runtime.
            var names = hierarchy.Keys.Where(n => n.EndsWith(".L") || n.EndsWith(".R"))
                .Where(n => hierarchy[n].IsChildOf(hierarchy["肩P.L"]) || hierarchy[n].IsChildOf(hierarchy["肩P.R"]))
                .OrderBy(n => Depth(hierarchy[n])).ThenBy(n => n, StringComparer.Ordinal).ToArray();
            var bones = names.Select(n => hierarchy[n]).ToArray();
            var rest = bones.Select(b => b.localRotation).ToArray();
            var positions = bones.Select(b => b.localPosition).ToArray();
            var twist = root.GetComponentInChildren<HumanoidTwistFollowers>(true);
            if (twist) twist.InitializePresentation();
            var output = AnimationPlayableOutput.Create(graph, "Retarget action", animator);
            var motions = new List<DollSingerActionAnimation.Motion>();
            foreach (var entry in new[] { (HandAction.Search, "UAL2_Standard.fbx", "Chest_Open"),
                (HandAction.Eat, "UAL2_Standard.fbx", "Consume"),
                (HandAction.Treat, "UAL1_Standard.fbx", "PickUp_Table") })
            {
                string path = Source + entry.Item2;
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Single(c => !c.name.StartsWith("__") && (c.name == entry.Item3 || c.name.EndsWith("|" + entry.Item3)));
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                output.SetSourcePlayable(playable); graph.Play();
                int frames = Mathf.CeilToInt(clip.length * 60) + 1;
                var values = new float[frames * bones.Length * 4];
                float nearest = float.PositiveInfinity, contact = .5f;
                for (int f = 0; f < frames; f++)
                {
                    for (int b = 0; b < bones.Length; b++) { bones[b].localRotation = rest[b]; bones[b].localPosition = positions[b]; }
                    if (twist) twist.RestoreBeforeAnimation();
                    float phase = (float)f / (frames - 1);
                    playable.SetTime(phase * clip.length); graph.Evaluate(0);
                    if (twist) twist.ApplyPresentation();
                    float distance = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                        animator.GetBoneTransform(HumanBodyBones.Head).position);
                    if (distance < nearest) { nearest = distance; contact = phase; }
                    for (int b = 0; b < bones.Length; b++)
                    {
                        var q = bones[b].localRotation.normalized; int p = (f * bones.Length + b) * 4;
                        values[p] = q.x; values[p + 1] = q.y; values[p + 2] = q.z; values[p + 3] = q.w;
                    }
                }
                motions.Add(new DollSingerActionAnimation.Motion { action = entry.Item1.ToString(), source = path + ":" + entry.Item3,
                    seconds = clip.length, frames = frames, contact = entry.Item1 == HandAction.Eat ? .5f : contact, rotations = values });
                graph.DestroyPlayable(playable);
            }
            string destination = Root + character + ".json";
            if (File.Exists(destination))
            {
                var previous = JsonUtility.FromJson<DollSingerActionAnimation.Library>(File.ReadAllText(destination));
                if (previous?.motions != null) motions.AddRange(previous.motions.Where(m => m.action == "Inject"));
            }
            File.WriteAllText(destination, JsonUtility.ToJson(new DollSingerActionAnimation.Library
            { rig = character, bones = names, motions = motions.ToArray() }));
            Debug.Log($"{character}: baked {motions.Count} authored actions onto {bones.Length} production arm joints.");
        }
        finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static int Depth(Transform node) { int depth = 0; for (; node; node = node.parent) depth++; return depth; }
}
