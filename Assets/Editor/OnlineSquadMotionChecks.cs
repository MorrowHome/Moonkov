using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Online;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Exercise the real menu graph and exact-rig sampler in the single open Editor.
public static class OnlineSquadMotionChecks
{
    private const string Folder = "LocalData/SquadMotionReview";
    private static readonly FieldInfo Actors = typeof(OnlineSquadStage).GetField("m_Actors", BindingFlags.Instance | BindingFlags.NonPublic);
    private static int s_Checks;

    [MenuItem("Tools/Moonkov/Check Squad Motions")]
    public static void Run()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Check squad motions in an idle Edit mode Editor.");
        Directory.CreateDirectory(Folder);
        int scenes = EditorSceneManager.previewSceneCount;
        s_Checks = 0;
        foreach (int character in new[] { 2, 3, 4 })
        {
            using (var stage = new OnlineSquadStage(new Image()))
            {
                var members = Enumerable.Range(0, 4).Select(pose => new LobbyMember
                {
                    PlayerId = "motion-" + pose, Name = "Pose " + pose,
                    Appearance = new LobbyAppearance { CharacterIndex = character, PoseIndex = pose, Weapon = "rifle", Melee = "energy_sword" }
                }).ToArray();
                stage.Present(members);
                stage.RenderForEditor(new Vector2(1600, 700), .1f);
                var actors = (MenuCharacterView[])Actors.GetValue(stage);
                Advance(actors, 16);
                var animators = stage.DisplayScene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Animator>(true)).ToArray();
                Require(animators.Length == 4, "expected four shared actors");
                foreach (var animator in animators)
                {
                    Require(animator.runtimeAnimatorController == null && !animator.fireEvents && !animator.applyRootMotion,
                        "squad graph inherited gameplay animation authority");
                    foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                        Require(float.IsFinite(bone.position.x) && float.IsFinite(bone.position.y) && float.IsFinite(bone.position.z), "non-finite bone " + bone.name);
                }
                // Each choice must produce a readable hand silhouette, independent of weapon.
                for (int pose = 0; pose < 4; pose++)
                {
                    var animator = actors[pose].DisplayScene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Animator>(true))
                        .Single(a => a.transform.IsChildOf(Root(actors[pose])));
                    var root = animator.transform;
                    Vector3 left = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
                    Vector3 right = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
                    Vector3 chest = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Chest).position);
                    Vector3 head = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
                    Require(pose != 0 || left.x > right.x, "crossed arms do not cross the torso");
                    Require(pose != 1 || right.y > head.y - .08f, "salute does not reach the temple");
                    Require(pose != 2 || left.y > chest.y && right.y > chest.y && Vector3.Distance(left, right) < .34f, "heart is not visible above the chest");
                    Require(pose != 3 || Vector3.Distance(left, head) < .32f && Vector3.Distance(right, head) < .32f, "coax fists are not beside the cheeks");
                    Require(actors[pose].HaloVisible && actors[pose].HaloAimWeight < .001f, "equipment halo did not remain above the head");
                }
                stage.RenderForEditor(new Vector2(1600, 700), 0);
                Save(stage.Target, Folder + "/character-" + character + ".png");
                // Repeated account snapshots must not restart the performance.
                var before = animators.SelectMany(a => a.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "手首.R").Select(t => t.localRotation).ToArray();
                stage.Present(members); Advance(actors, 40);
                var after = animators.SelectMany(a => a.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "手首.R").Select(t => t.localRotation).ToArray();
                for (int i = 0; i < before.Length; i++) Require(Quaternion.Angle(before[i], after[i]) < .15f, "loop or snapshot continuity broke");
                members[0].Ready = true; stage.Present(members); Advance(actors, 4);
                var salute = animators.Single(a => a.transform.IsChildOf(Root(actors[0])));
                Require(salute.GetBoneTransform(HumanBodyBones.RightHand).position.y > salute.GetBoneTransform(HumanBodyBones.Head).position.y - .1f,
                    "ready acknowledgement did not salute");
                Advance(actors, 20);
                Require(salute.GetBoneTransform(HumanBodyBones.LeftHand).position.x > salute.GetBoneTransform(HumanBodyBones.RightHand).position.x,
                    "ready acknowledgement did not return to selected pose");
                // Switching on the same actor must blend to each target and keep feet planted.
                // Compare each choice at the same frozen idle phase. The normal
                // idle itself shifts weight, independently of the upper body.
                stage.ReducedMotion = true;
                var foot = salute.GetBoneTransform(HumanBodyBones.LeftFoot);
                Vector3 planted = salute.transform.InverseTransformPoint(foot.position);
                foreach (int pose in new[] { 3, 1, 2, 0 })
                {
                    members[0].Appearance.PoseIndex = pose; stage.Present(members);
                    stage.RenderForEditor(new Vector2(1600, 700), 0);
                    Require(Vector3.Distance(planted, salute.transform.InverseTransformPoint(foot.position)) < .01f, "upper body pose moved the planted foot");
                }
                stage.ReducedMotion = true;
                var frozen = salute.GetBoneTransform(HumanBodyBones.Head).localRotation;
                stage.Present(members); stage.RenderForEditor(new Vector2(1600, 700), 0);
                Require(Quaternion.Angle(frozen, salute.GetBoneTransform(HumanBodyBones.Head).localRotation) < .01f, "reduced motion did not freeze");
                stage.Suspend(); Require(stage.ActorCount == 0 && stage.Target == null, "suspend leaked presentation");
                stage.Present(members); stage.RenderForEditor(new Vector2(1600, 700), 0);
                Require(stage.ActorCount == 4, "reopen did not recreate the motion players");
            }
            Require(EditorSceneManager.previewSceneCount == scenes, "motion validation leaked a preview scene");
        }
        File.WriteAllText(Folder + "/result.json", "{\"status\":\"Passed\",\"checks\":" + s_Checks + ",\"characters\":3,\"motions\":4,\"playMode\":false}");
        Debug.Log("Squad motion checks passed: " + s_Checks + ". Three characters, four authored motions; no Play or clients.");
    }

    private static Transform Root(MenuCharacterView actor) => (Transform)typeof(MenuCharacterView)
        .GetProperty("SquadRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);

    [MenuItem("Tools/Moonkov/Capture Squad Motion Preview")]
    public static void CapturePreview()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture in an idle Edit mode Editor.");
        string folder = Folder + "/Frames";
        Directory.CreateDirectory(folder);
        var image = new Image(); image.AddToClassList(TerminalMotion.LiveEditorPreviewClass);
        using (var stage = new OnlineSquadStage(image))
        {
            int[] characters = { 4, 2, 3, 2 };
            stage.Present(Enumerable.Range(0, 4).Select(i => new LobbyMember { PlayerId = "preview-" + i,
                Appearance = new LobbyAppearance { CharacterIndex = characters[i], PoseIndex = 3 - i, Weapon = "rifle" } }).ToArray());
            stage.RenderForEditor(new Vector2(1280, 600), .1f);
            Advance((MenuCharacterView[])Actors.GetValue(stage), 15);
            for (int i = 0; i < 60; i++)
            {
                stage.RenderForEditor(new Vector2(1280, 600), 1f / 15);
                Save(stage.Target, folder + "/" + i.ToString("D3") + ".png");
            }
        }
    }

    private static void Advance(MenuCharacterView[] actors, int frames)
    { for (int f = 0; f < frames; f++) foreach (var actor in actors) actor?.Advance(.1f); }
    private static void Require(bool valid, string message)
    { s_Checks++; if (!valid) throw new InvalidOperationException("Squad motions: " + message); }
    private static void Save(RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
    }
}
