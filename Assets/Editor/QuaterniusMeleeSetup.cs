using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.MP_FPS.DollSinger;

// Author-time Humanoid retargeting; the shipped player samples its own skeleton.
public static class QuaterniusMeleeSetup
{
    private const string Source = "Assets/ThirdParty/Quaternius/Animation/UAL2_Standard.fbx";
    private const string BoxingSource = "Assets/ThirdParty/Quaternius/Animation/UAL1_Standard.fbx";
    [Serializable] public sealed class Samples { public float pitch; public int frames; public float[] values; }
    [Serializable] public sealed class Clip
    { public string weapon, action; public float seconds, windup, active, recovery; public Samples[] poses; }
    [Serializable] public sealed class Library
    {
        public int version = 1, frameRate = 60;
        public string revision;
        public string[] bones;
        public Quaternion chestBindRotation, gripRotation;
        public Vector3 gripPosition;
        public Clip[] clips;
    }

    public static void Import()
    {
        ImportOne(Source, new[] { "Melee_Hook", "Melee_Hook_Rec", "Sword_Heavy_Combo", "Sword_Regular_A", "Sword_Regular_B", "Sword_Regular_C", "Sword_Regular_A_Rec" });
        ImportOne(BoxingSource, new[] { "Punch_Cross", "Punch_Jab", "Sword_Idle", "Sword_Attack" });
    }
    private static void ImportOne(string path, string[] takes)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.clipAnimations = importer.defaultClipAnimations.Where(c => takes.Any(t => c.name == t || c.name.EndsWith("|" + t))).ToArray();
        if (importer.clipAnimations.Length != takes.Length) throw new InvalidOperationException("Source takes missing: " + path);
        var mapping = new Dictionary<HumanBodyBones, string>
        {
            {HumanBodyBones.Hips,"pelvis"}, {HumanBodyBones.Spine,"spine_01"},
            {HumanBodyBones.Chest,"spine_02"}, {HumanBodyBones.UpperChest,"spine_03"},
            {HumanBodyBones.Neck,"neck_01"}, {HumanBodyBones.Head,"Head"}
        };
        foreach (bool left in new[] { true, false })
        {
            string side = left ? "Left" : "Right", suffix = left ? "_l" : "_r";
            void Map(string human, string bone) => mapping.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + human), bone + suffix);
            Map("Shoulder","clavicle"); Map("UpperArm","upperarm"); Map("LowerArm","lowerarm"); Map("Hand","hand");
            Map("UpperLeg","thigh"); Map("LowerLeg","calf"); Map("Foot","foot"); Map("Toes","ball");
            foreach (var digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                for (int n = 1; n <= 3; n++)
                    Map(digit + new[] { "Proximal", "Intermediate", "Distal" }[n - 1],
                        (digit == "Little" ? "pinky" : digit.ToLowerInvariant()) + "_0" + n);
        }
        var description = importer.humanDescription;
        description.human = mapping.Select(pair => new HumanBone { boneName = pair.Value,
            humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        importer.humanDescription = description;
        importer.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if (!avatar || !avatar.isHuman || !avatar.isValid) throw new InvalidOperationException("Quaternius Humanoid avatar invalid.");
        Debug.Log("Quaternius source ready: " + path);
    }

    public static void BakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake outside Play mode.");
        Bake("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab", "Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json");
        Bake("Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab", "Assets/HatsuneMiku/Animation/MeleeGestures.json");
        AssetDatabase.Refresh();
    }

    public static void CaptureMelee(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var target = new RenderTexture(480, 480, 24); var sheet = new Texture2D(1440, 1440, TextureFormat.RGB24, false);
        var previous = RenderTexture.active; UnityEngine.Mesh mesh = null;
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab"), scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var p = animator.transform; p; p = p.parent) p.gameObject.SetActive(true);
            animator.runtimeAnimatorController = null; animator.Rebind(); animator.Update(0);
            var bake = new DollSingerMeleeBake(animator, AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HatsuneMiku/Animation/MeleeGestures.json"));
            var source = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).First();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
            var surface = new GameObject("Baked current skeleton"); surface.transform.SetParent(source.transform, false);
            var scale = source.transform.lossyScale;
            surface.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            mesh = new UnityEngine.Mesh(); surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            var node = new GameObject("Melee preview camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(node, scene);
            var camera = node.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .05f, .065f);
            camera.orthographic = true; camera.orthographicSize = .62f; camera.nearClipPlane = .01f;
            var head = animator.GetBoneTransform(HumanBodyBones.Head).position;
            camera.transform.position = head + new Vector3(.9f, -.28f, 2.5f); camera.transform.LookAt(head + Vector3.down * .28f);
            var key = new GameObject("Preview key"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(key, scene);
            var light = key.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(35, -25, 0);
            for (int row = 0; row < 3; row++)
            {
                var weapon = (MeleeWeaponKind)(row + 1); MeleeRules.TryDefinition(weapon, out var definition);
                var timing = definition.Timing(MeleeAttackKind.Light, 0);
                for (int col = 0; col < 3; col++)
                {
                    bake.RestoreBeforeAnimation();
                    float elapsed = col == 0 ? 0 : col == 1 ? timing.Windup * .75f : timing.Windup + timing.Active * .5f;
                    bake.Apply(weapon, col == 0 ? "idle" : "light0", elapsed, 1, timing, Vector3.forward, 0);
                    source.BakeMesh(mesh); mesh.RecalculateBounds();
                    surface.GetComponent<MeshFilter>().sharedMesh = null; surface.GetComponent<MeshFilter>().sharedMesh = mesh;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; sheet.ReadPixels(new Rect(0, 0, 480, 480), col * 480, (2 - row) * 480, false);
                }
            }
            sheet.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, sheet.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; if (mesh) UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(sheet); target.Release(); UnityEngine.Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Bake(string prefab, string destination)
    {
        var library = JsonUtility.FromJson<Library>(File.ReadAllText(destination));
        var source = new[] { Source, BoxingSource }.SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
        AnimationClip Find(string name) => source.Single(c => c.name == name || c.name.EndsWith("|" + name));
        var scene = EditorSceneManager.NewPreviewScene();
        var graph = PlayableGraph.Create("Quaternius exact-rig bake");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = true;
            var hierarchy = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var bones = library.bones.Select(n => hierarchy[n]).ToArray();
            var bind = bones.Select(b => b.localRotation).ToArray();
            var positions = bones.Select(b => b.localPosition).ToArray();
            var left = hierarchy["肩P.L"]; var right = hierarchy["肩P.R"];
            var output = AnimationPlayableOutput.Create(graph, "Retarget", animator);
            AnimationClipPlayable playable = default;
            var replacements = new List<Clip>();
            foreach (var entry in library.clips)
            {
                // The pack's independently authored attacks retain anticipation,
                // contact and follow-through; server timings remain the authority.
                bool fist = entry.weapon == "Fists";
                string action = entry.action;
                string motion = fist ? (action == "recover" ? "Melee_Hook_Rec" : action == "light0" ? "Punch_Jab" : action == "light1" ? "Punch_Cross" : "Melee_Hook") :
                    action == "idle" || action == "equip" || action == "holster" ? "Sword_Idle" :
                    action == "heavy" ? "Sword_Heavy_Combo" : action == "light1" ? "Sword_Regular_B" :
                    action == "light2" ? "Sword_Attack" : action == "recover" ? "Sword_Regular_A_Rec" : "Sword_Regular_A";
                var clip = Find(motion);
                if (playable.IsValid()) graph.DestroyPlayable(playable);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                output.SetSourcePlayable(playable); graph.Play();
                bool attack = action.StartsWith("light") || action == "heavy";
                bool recovery = action == "recover";
                // Only the first committed strike is used from the heavy combo.
                float from = recovery ? 0 : attack ? .04f : .01f;
                float to = recovery ? clip.length : attack ? (action == "heavy" ? Mathf.Min(clip.length, 1.05f) : clip.length) : from;
                int frames = attack || recovery ? Mathf.CeilToInt((to - from) * library.frameRate) + 1 : 1;
                var poses = new List<Samples>();
                var travel = new Vector3[frames];
                var leftTravel = new Vector3[frames];
                foreach (float pitch in new[] { -70f, 0f, 70f })
                {
                    var values = new float[frames * bones.Length * 4];
                    for (int f = 0; f < frames; f++)
                    {
                        for (int b = 0; b < bones.Length; b++) { bones[b].localRotation = bind[b]; bones[b].localPosition = positions[b]; }
                        playable.SetTime(Mathf.Lerp(from, to, frames == 1 ? 0 : (float)f / (frames - 1)));
                        graph.Evaluate(0);
                        if (pitch == 0)
                        {
                            travel[f] = hierarchy["手首.R"].position;
                            leftTravel[f] = hierarchy["手首.L"].position;
                        }
                        var rotation = animator.transform.rotation * Quaternion.Euler(-pitch, 0, 0) * Quaternion.Inverse(animator.transform.rotation);
                        Vector3 pivot = (left.position + right.position) * .5f;
                        foreach (var shoulder in new[] { left, right })
                            shoulder.SetPositionAndRotation(pivot + rotation * (shoulder.position - pivot), rotation * shoulder.rotation);
                        for (int b = 0; b < bones.Length; b++)
                        {
                            var q = bones[b].localRotation.normalized; int i = (f * bones.Length + b) * 4;
                            values[i] = q.x; values[i + 1] = q.y; values[i + 2] = q.z; values[i + 3] = q.w;
                        }
                    }
                    poses.Add(new Samples { pitch = pitch, frames = frames, values = values });
                }
                entry.poses = poses.ToArray();
                if (attack)
                {
                    entry.seconds = to - from;
                    // Align the shared server active segment with the authored
                    // strike's fastest hand travel, rather than an arbitrary clip fraction.
                    int contact = 1; float speed = 0;
                    for (int f = 1; f < frames * .8f; f++)
                    {
                        float step = Mathf.Max((travel[f] - travel[f - 1]).sqrMagnitude, (leftTravel[f] - leftTravel[f - 1]).sqrMagnitude);
                        if (step > speed) { speed = step; contact = f; }
                    }
                    entry.active = Mathf.Min(.14f, entry.seconds * .22f);
                    float center = entry.seconds * contact / (frames - 1);
                    entry.windup = Mathf.Clamp(center - entry.active * .5f, .025f, entry.seconds - entry.active - .025f);
                    entry.recovery = entry.seconds - entry.windup - entry.active;
                }
                replacements.Add(entry);
            }
            library.clips = replacements.ToArray(); library.revision = "Quaternius-UAL1-UAL2-CC0-2026-10-24";
            File.WriteAllText(destination, JsonUtility.ToJson(library));
            Debug.Log("Retargeted " + library.clips.Length + " exact-rig melee tracks: " + destination);
        }
        finally { graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
