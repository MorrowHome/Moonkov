using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Single-Editor character checks; does not start any network world or modify gameplay scenes.</summary>
public static class HatsuneMikuChecks
{
    private const string Root = "Assets/HatsuneMiku";
    private const string Output = "Temp/HatsuneMikuValidation";
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    private sealed class Rest
    {
        public Transform Bone;
        public Vector3 Position;
        public Quaternion Rotation;
        public Rest(Transform bone) { Bone = bone; Position = bone.localPosition; Rotation = bone.localRotation; }
        public void Restore() { Bone.SetLocalPositionAndRotation(Position, Rotation); }
    }

    [MenuItem("Tools/Hatsune Miku/Validate Character")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run character checks in Edit Mode.");
        Directory.CreateDirectory(Output);
        var report = new JObject { ["unity"] = Application.unityVersion, ["networkSessionStarted"] = false };
        var scene = EditorSceneManager.NewPreviewScene();
        Mesh surface = new Mesh();
        GameObject root = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/HatsuneMikuPlayer.prefab");
            Require(prefab, "Miku local prefab is missing.");
            root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var animator = root.GetComponent<Animator>();
            Require(animator && animator.avatar && animator.avatar.isValid && animator.avatar.isHuman, "Miku Humanoid avatar is invalid.");
            animator.runtimeAnimatorController = null;
            animator.Rebind();
            var bones = root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var rest = bones.Values.Select(t => new Rest(t)).ToArray();
            void Restore() { foreach (var value in rest) value.Restore(); }
            foreach (var go in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject) == 0, "Missing script on " + go.name);
            for (int i = (int)HumanBodyBones.LeftThumbProximal; i <= (int)HumanBodyBones.RightLittleDistal; i++)
                Require(animator.GetBoneTransform((HumanBodyBones)i), "Missing Humanoid finger " + (HumanBodyBones)i);
            foreach (string side in new[] { "L", "R" })
                foreach (string digit in new[] { "人指", "中指", "薬指", "小指", "親指" })
                    Require(bones.ContainsKey(digit + "先." + side), "Missing fingertip " + digit + side);
            Require(bones["腕.L"].position.x < bones["腕.R"].position.x, "Character's left/right bones are mirrored.");
            Require(bones["足先EX.L"].position.z > bones["足首.L"].position.z, "Character is not facing +Z.");
            var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            Require(renderer.sharedMesh.isReadable, "First-person mesh needs Read/Write enabled.");
            Require(renderer.sharedMesh.subMeshCount == 3 && renderer.sharedMaterials.Length == 3, "Material slots changed during import.");
            Require(renderer.sharedMesh.blendShapeCount == 26, "Vertex expressions were lost.");
            foreach (var material in renderer.sharedMaterials)
                Require(material && material.shader && material.shader.name != "Hidden/InternalErrorShader", "Missing material or shader.");
            // Compensate the FBX mesh transform's centimetre scale before TransformPoint.
            renderer.BakeMesh(surface, true);
            // FBX conversion can orient the renderer's local up along its Z axis.
            // Character height must be measured after its complete import transform.
            var worldVertices = surface.vertices.Select(renderer.transform.TransformPoint).ToArray();
            var worldBounds = new Bounds(worldVertices[0], Vector3.zero);
            foreach (var vertex in worldVertices) worldBounds.Encapsulate(vertex);
            Require(worldBounds.size.y > 1.5f && worldBounds.size.y < 1.8f, "Unexpected world model height: " + worldBounds.size);
            report["vertices"] = renderer.sharedMesh.vertexCount;
            report["blendShapes"] = renderer.sharedMesh.blendShapeCount;
            report["heightMetres"] = worldBounds.size.y;
            var twistFollowers = root.GetComponent<HumanoidTwistFollowers>();
            Require(twistFollowers, "Miku Humanoid twist follower component is missing.");
            twistFollowers.InitializePresentation();
            Require(twistFollowers.FollowerCount == 12, "Miku must bind all twelve sibling twist helpers.");
            report["humanoidTwistFollowers"] = ValidateTwistFollowers(root, bones, renderer, surface, twistFollowers, Restore);
            var view = root.GetComponentInChildren<DollSingerView>(true);
            var fp = (Mesh)typeof(DollSingerView).GetMethod("BuildFirstPersonMesh", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(view, new object[] { renderer.sharedMesh, renderer.bones, renderer.sharedMaterials });
            try
            {
                Require(fp && fp.triangles.Length > 0 && fp.triangles.Length < renderer.sharedMesh.triangles.Length, "First-person head filtering did not remove triangles.");
                report["firstPersonTriangles"] = fp.triangles.Length / 3;
                report["fullBodyTriangles"] = renderer.sharedMesh.triangles.Length / 3;
            }
            finally { if (fp) Object.DestroyImmediate(fp); }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Animation/WeaponGestures.json");
            var data = JObject.Parse(asset.text);
            Require(((JArray)data["motions"]).Count == 13, "Expected all thirteen firearm gestures.");
            var gun = new DollSingerBakedGestures(animator, asset);
            int gunSamples = 0;
            float maxGap = 0, maxIndexBend = 0, maxRightAngleError = 0;
            for (int weapon = 1; weapon <= 13; weapon++)
            foreach (float pitch in new[] { -70f, -45f, -20f, 0f, 20f, 45f, 70f })
            for (int phase = 0; phase < 3; phase++)
            {
                Restore();
                Vector3 direction = Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
                float age = phase == 1 ? (float)data["motions"][weapon - 1]["fireSeconds"] * .45f : -1;
                gun.Apply(weapon, 1, age, phase == 2, .5f, .01f, animator.GetBoneTransform(HumanBodyBones.Chest), direction, 1);
                renderer.BakeMesh(surface);
                Require(surface.vertices.All(Finite), "Non-finite skinned vertex in weapon " + weapon);
                Require(surface.bounds.size.magnitude < 4, "Exploded skin in weapon " + weapon + ", pitch " + pitch);
                if (weapon == 4 && phase == 0)
                {
                    maxGap = Mathf.Max(maxGap, Vector3.Distance(bones["人指先.L"].position, bones["親指先.R"].position),
                        Vector3.Distance(bones["人指先.R"].position, bones["親指先.L"].position));
                    foreach (var side in new[] { "L", "R" })
                    {
                        Vector3 a = bones["人指１." + side].position, b = bones["人指２." + side].position,
                            c = bones["人指３." + side].position, d = bones["人指先." + side].position;
                        maxIndexBend = Mathf.Max(maxIndexBend, Vector3.Angle(b - a, c - b), Vector3.Angle(c - b, d - c));
                        maxRightAngleError = Mathf.Max(maxRightAngleError, Mathf.Abs(90 - Vector3.Angle(d - b,
                            bones["親指先." + side].position - bones["親指１." + side].position)));
                    }
                }
                if (pitch == 0 && phase == 0 && (weapon == 1 || weapon == 4))
                    Render(root, renderer, scene, weapon == 4 ? "sniper" : "rifle");
                gunSamples++;
            }
            report["firearmPoseSamples"] = gunSamples;
            report["sniperContactGapMm"] = maxGap * 1000;
            report["sniperIndexBendDegrees"] = maxIndexBend;
            report["sniperRightAngleErrorDegrees"] = maxRightAngleError;
            Restore();
            var meleeAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Animation/MeleeGestures.json");
            var meleeData = JObject.Parse(meleeAsset.text);
            var melee = new DollSingerMeleeBake(animator, meleeAsset);
            root.GetComponent<DollSingerMeleePresentation>().Configure(animator, null, null);
            int meleeSamples = 0;
            foreach (JObject clip in meleeData["clips"])
            foreach (float pitch in new[] { -70f, 0f, 70f })
            foreach (float progress in new[] { 0f, .5f, 1f })
            {
                Restore();
                var timing = new MeleeTiming((float)clip["windup"], (float)clip["active"], (float)clip["recovery"]);
                melee.Apply((MeleeWeaponKind)Enum.Parse(typeof(MeleeWeaponKind), (string)clip["weapon"]), (string)clip["action"],
                    (float)clip["seconds"] * progress, 1, timing, Quaternion.Euler(pitch, 0, 0) * Vector3.forward, 0);
                renderer.BakeMesh(surface);
                Require(surface.vertices.All(Finite) && surface.bounds.size.magnitude < 4, "Invalid melee skin: " + clip["weapon"] + "/" + clip["action"]);
                if ((string)clip["action"] == "idle" && pitch == 0 && progress == .5f)
                {
                    var prop = bones["手首.R"].Find("Melee " + (string)clip["weapon"] + " (Runtime)");
                    if (prop)
                    {
                        prop.gameObject.SetActive(true);
                        Render(root, renderer, scene, "melee-" + (string)clip["weapon"]);
                        Render(root, renderer, scene, "grip-" + (string)clip["weapon"], bones["手首.R"]);
                        prop.gameObject.SetActive(false);
                    }
                }
                meleeSamples++;
            }
            report["meleePoseSamples"] = meleeSamples;
            Restore();
            var spring = root.GetComponent<SecondaryBoneSpring>();
            Require(spring && spring.hairRoots.Length >= 2 && spring.skirtRoots.Length >= 8, "Hair/skirt roots are incomplete.");
            Require(root.GetComponent<SkirtHandAvoidance>().outerSkirtRoots.Length == 8, "The outer skirt contour must use its eight panels.");
            Require(spring.hairRoots.All(t => t) && spring.skirtRoots.All(t => t), "Missing secondary bone reference.");
            Require(spring.hairBodyColliders.Length > 0 && spring.skirtBodyColliders.Length > 0 && spring.skirtColliders.Length >= 4, "Missing body/hand collision proxies.");
            spring.InitializePresentation();
            float maximumBend = 0;
            var passive = new HashSet<Transform>(spring.passiveChainBones ?? Array.Empty<Transform>());
            var hairBones = spring.hairRoots.SelectMany(t => t.GetComponentsInChildren<Transform>())
                .Where(t => t.childCount == 1 && !passive.Contains(t)).Distinct().ToArray();
            Require(hairBones.Length == 18, "Miku must simulate its EX hair/tie bones, excluding the short offset nodes.");
            var rootRotations = hairBones.Select(t => t.localRotation).ToArray();
            for (int frame = 0; frame < 240; frame++)
            {
                root.transform.position = new Vector3(Mathf.Sin(frame * .04f) * .2f, frame < 120 ? 0 : -(frame - 120) * .02f, frame * .03f);
                spring.SimulatePresentation(1f / 60);
                for (int i = 0; i < hairBones.Length; i++)
                    maximumBend = Mathf.Max(maximumBend, Quaternion.Angle(rootRotations[i], hairBones[i].localRotation));
                Require(bones.Values.All(t => Finite(t.position)), "Spring simulation produced non-finite bones.");
            }
            Require(maximumBend > .1f, "Hair simulation did not respond to movement/fall.");
            for (int frame = 0; frame < 600; frame++) spring.SimulatePresentation(1f / 60);
            float maximumSettledTailHeight = new[] { "Left_BS_hair_end", "Right_BS_hair_end" }
                .Max(name => root.transform.InverseTransformPoint(bones[name].position).y);
            Require(maximumSettledTailHeight < .65f, "Twin tails curled above the knees after settling: " + maximumSettledTailHeight);
            report["simulatedHairAndTieBones"] = hairBones.Length;
            report["hairSettlingFrames"] = 600;
            report["maximumSettledTailHeightMetres"] = maximumSettledTailHeight;
            root.transform.position += Vector3.one * 10;
            spring.SimulatePresentation(.5f);
            Require(bones.Values.All(t => Finite(t.position)), "Teleport/long-frame reset failed.");
            report["springSimulationFrames"] = 240;
            report["maximumHairRootBendDegrees"] = maximumBend;
            Restore();
            var settings = AssetDatabase.LoadAssetAtPath<MenuCharacterSettings>("Assets/Resources/Moonkov/HatsuneMikuMenuCharacterSettings.asset");
            Require(settings && settings.VisualPrefab && settings.GestureBake == asset, "Menu character settings are incomplete.");
            var graph = PlayableGraph.Create("Miku Humanoid validation");
            try
            {
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                int motions = 0;
                foreach (var clip in settings.Motions.Distinct())
                {
                    Restore();
                    twistFollowers.RestoreBeforeAnimation();
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    output.SetSourcePlayable(playable);
                    playable.SetTime(Mathf.Min(.5f, clip.length * .5f));
                    graph.Evaluate(0);
                    var mainJoints = new[] { "腕.L", "ひじ.L", "手首.L", "腕.R", "ひじ.R", "手首.R" }
                        .Select(name => new Rest(bones[name])).ToArray();
                    twistFollowers.ApplyPresentation();
                    foreach (var joint in mainJoints)
                        Require(Vector3.Distance(joint.Position, joint.Bone.localPosition) < .000001f &&
                            Quaternion.Angle(joint.Rotation, joint.Bone.localRotation) < .1f,
                            "Twist followers changed a Humanoid joint in clip " + clip.name + ": " + joint.Bone.name);
                    Require(bones.Values.All(t => Finite(t.position) && Finite(t.rotation)), "Non-finite Humanoid follower output: " + clip.name);
                    renderer.BakeMesh(surface);
                    Require(surface.vertices.All(Finite) && surface.bounds.size.magnitude < 4, "Invalid retargeted Humanoid clip: " + clip.name);
                    if (motions == 0) Render(root, renderer, scene, "idle");
                    playable.Destroy(); motions++;
                }
                report["humanoidClips"] = motions;
            }
            finally { graph.Destroy(); Restore(); }
            var corpse = Resources.Load<CorpsePresentationSettings>("Moonkov/CorpsePresentation");
            Require(corpse && corpse.CharacterVisuals.Length >= 4 && corpse.CharacterVisuals[3], "Miku corpse is not registered.");
            var network = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/HatsuneMikuNetworkPlayer.prefab");
            Require(network && network.GetComponent<DollSingerNetworkPresentation>(), "Miku network prefab is incomplete.");
            foreach (var propertyName in new[] { "m_Model", "m_Input", "m_View", "m_Halo" })
            {
                var target = (Component)new SerializedObject(network.GetComponent<DollSingerNetworkPresentation>()).FindProperty(propertyName).objectReferenceValue;
                Require(target && target.transform.IsChildOf(network.transform), "Broken network presentation reference: " + propertyName);
            }
            // Preserve the established viewfinder shape contract on the new skeleton.
            Require(maxGap < .001f, "Miku sniper frame contact gap: " + maxGap * 1000 + " mm");
            Require(maxIndexBend < .3f && maxRightAngleError < .3f, "Miku sniper frame fingers are not straight/perpendicular.");
            Render(root, renderer, scene, "bind");
            report["passed"] = true;
            Debug.Log("Hatsune Miku character checks passed: " + gunSamples + " gun / " + meleeSamples + " melee samples; 240 spring frames.");
        }
        catch (Exception exception)
        {
            report["passed"] = false; report["failure"] = exception.ToString();
            throw;
        }
        finally
        {
            File.WriteAllText(Output + "/checks.json", report.ToString());
            Object.DestroyImmediate(surface);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static JObject ValidateTwistFollowers(GameObject root, Dictionary<string, Transform> bones,
        SkinnedMeshRenderer renderer, Mesh surface, HumanoidTwistFollowers followers, Action restore)
    {
        int cases = 0;
        float maximumRotationError = 0, maximumRepeatError = 0, maximumSkinChange = 0;
        var mainNames = new[] { "腕.L", "ひじ.L", "手首.L", "腕.R", "ひじ.R", "手首.R" };
        try
        {
            foreach (var rootRotation in new[] { Quaternion.identity, Quaternion.Euler(17, 41, -13) })
            foreach (string side in new[] { ".L", ".R" })
            foreach (bool upper in new[] { true, false })
            foreach (var pose in new[] { new Vector2(0, 45), new Vector2(60, 0), new Vector2(-80, 35) })
            {
                restore();
                root.transform.rotation = rootRotation;
                followers.RestoreBeforeAnimation();
                var parent = bones[(upper ? "腕" : "ひじ") + side];
                var distal = bones[(upper ? "ひじ" : "手首") + side];
                string prefix = upper ? "腕捩" : "手捩";
                var helpers = Enumerable.Range(1, 3).Select(i => bones[prefix + i + side]).ToArray();
                var helperBind = helpers.Select(t => t.rotation).ToArray();
                var localBind = helpers.Select(t => t.localRotation).ToArray();
                Vector3 axis = (distal.position - parent.position).normalized;
                Vector3 bendAxis = Vector3.Cross(axis, root.transform.up).normalized;
                Require(bendAxis.sqrMagnitude > .5f, "Degenerate validation bend axis.");
                // Inject a known bend/axial roll into the actual imported skeleton.
                // Expected follower angles come from the fixture, not its solver.
                distal.rotation = Quaternion.AngleAxis(pose.y, bendAxis) * Quaternion.AngleAxis(pose.x, axis) * distal.rotation;
                var main = mainNames.Select(name => new Rest(bones[name])).ToArray();
                var mainWorldPositions = main.Select(r => r.Bone.position).ToArray();
                var mainWorldRotations = main.Select(r => r.Bone.rotation).ToArray();
                renderer.BakeMesh(surface);
                var before = surface.vertices;
                // Reinitialization must be idempotent even when bones have moved.
                followers.InitializePresentation();
                Require(followers.FollowerCount == 12, "Follower reinitialization lost bindings.");
                followers.ApplyPresentation();
                for (int i = 0; i < main.Length; i++)
                {
                    Require(Vector3.Distance(main[i].Bone.position, mainWorldPositions[i]) < .000001f &&
                        Quaternion.Angle(main[i].Bone.rotation, mainWorldRotations[i]) < .1f &&
                        Vector3.Distance(main[i].Bone.localPosition, main[i].Position) < .000001f &&
                        Quaternion.Angle(main[i].Bone.localRotation, main[i].Rotation) < .1f,
                        "Follower modified main joint " + main[i].Bone.name);
                }
                for (int i = 0; i < helpers.Length; i++)
                {
                    Require(Finite(helpers[i].rotation) && Finite(helpers[i].position), "Non-finite follower: " + helpers[i].name);
                    Quaternion expected = Quaternion.AngleAxis(pose.x * (i + 1) * .25f, axis) * helperBind[i];
                    float error = Quaternion.Angle(expected, helpers[i].rotation);
                    maximumRotationError = Mathf.Max(maximumRotationError, error);
                    Require(error < .15f, "Incorrect follower fraction/swing isolation: " + helpers[i].name + " / " + error);
                    if (Mathf.Abs(pose.x) > 1)
                        Require(Quaternion.Angle(helperBind[i], helpers[i].rotation) > 10,
                            "Distal Humanoid roll did not move " + helpers[i].name);
                }
                var once = helpers.Select(t => t.rotation).ToArray();
                followers.ApplyPresentation();
                for (int i = 0; i < helpers.Length; i++)
                {
                    float repeat = Quaternion.Angle(once[i], helpers[i].rotation);
                    maximumRepeatError = Mathf.Max(maximumRepeatError, repeat);
                    Require(repeat < .1f, "Follower twist accumulated on repeat: " + helpers[i].name);
                }
                renderer.BakeMesh(surface);
                var after = surface.vertices;
                Require(after.All(Finite), "Follower generated non-finite skinned vertices.");
                float movement = 0;
                for (int i = 0; i < after.Length; i++)
                    // BakeMesh(false) already produces scaled metre-space vertices.
                    movement = Mathf.Max(movement, (after[i] - before[i]).magnitude);
                maximumSkinChange = Mathf.Max(maximumSkinChange, movement);
                if (Mathf.Abs(pose.x) > 1)
                    Require(movement > .000001f, "Follower bones do not deform the real mesh: " + prefix + side);
                followers.RestoreBeforeAnimation();
                for (int i = 0; i < helpers.Length; i++)
                    Require(Quaternion.Angle(localBind[i], helpers[i].localRotation) < .1f,
                        "Follower restore left a previous pose: " + helpers[i].name);
                cases++;
            }
            return new JObject { ["count"] = followers.FollowerCount, ["poseCases"] = cases,
                ["maximumRotationErrorDegrees"] = maximumRotationError,
                ["maximumRepeatErrorDegrees"] = maximumRepeatError,
                ["maximumSkinChangeMetres"] = maximumSkinChange };
        }
        finally { restore(); followers.RestoreBeforeAnimation(); }
    }

    private static void Render(GameObject root, SkinnedMeshRenderer renderer, UnityEngine.SceneManagement.Scene scene, string name, Transform focus = null)
    {
        // Multiple poses are evaluated in one Editor frame. Freeze the current CPU
        // skin for each capture so GPU skinning cannot reuse the first pose's buffer.
        var capturedMesh = new Mesh(); renderer.BakeMesh(capturedMesh, true);
        var capturedObject = new GameObject("Miku captured pose");
        capturedObject.transform.SetParent(renderer.transform, false);
        capturedObject.AddComponent<MeshFilter>().sharedMesh = capturedMesh;
        capturedObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
        bool previousOff = renderer.forceRenderingOff; renderer.forceRenderingOff = true;
        var cameraObject = new GameObject("Miku validation camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.scene = scene; camera.enabled = false;
        camera.transform.position = new Vector3(2.2f, 1.4f, 3.8f);
        camera.transform.LookAt(new Vector3(0, .9f, 0));
        if (focus)
        {
            camera.transform.position = focus.position + new Vector3(.4f, .18f, .6f);
            camera.transform.LookAt(focus.position + Vector3.up * .03f);
        }
        camera.fieldOfView = 30; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.09f, .11f, .14f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var lightObject = new GameObject("Miku validation light");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
        lightObject.transform.rotation = Quaternion.Euler(35, -30, 0);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
        var target = new RenderTexture(900, 1050, 24);
        var pixels = new Texture2D(900, 1050, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 900, 1050), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            renderer.forceRenderingOff = previousOff;
            Object.DestroyImmediate(capturedObject); Object.DestroyImmediate(capturedMesh);
        }
    }
}
