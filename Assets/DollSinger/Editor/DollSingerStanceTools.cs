using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger.Editor
{
    public static class DollSingerStanceTools
    {
        public const string Root = "Assets/DollSinger/Animation/Stances";
        private const string Quaternius = Root + "/Source/UAL1_Standard.fbx";
        private const string KayKit = Root + "/Source/Rig_Medium_MovementAdvanced.fbx";

        [MenuItem("Tools/Doll Singer/Set Up Stances")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            ConfigureImport(Quaternius);
            ConfigureImport(KayKit);
            var crouchIdle = Copy(Quaternius, "Crouch_Idle_Loop", "Crouch_Idle");
            var crouchMove = Copy(Quaternius, "Crouch_Fwd_Loop", "Crouch_Move");
            var crawl = Copy(KayKit, "Crawling", "Prone_Crawl");
            var proneIdle = Copy(KayKit, "Crawling", "Prone_Idle", true);
            var toeIdle = Copy("Assets/DollSinger/Animation/Locomotion/Unarmed_Idle.anim", "Unarmed_Idle", "Tiptoe_Idle");
            var toeMove = Copy(KayKit, "Sneaking", "Tiptoe_Move");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(DollSingerLocomotionTools.ControllerPath);
            if (!controller.parameters.Any(p => p.name == "Stance"))
                controller.AddParameter("Stance", AnimatorControllerParameterType.Int);
            if (!controller.layers.Any(l => l.name == "Stances")) controller.AddLayer("Stances");
            var layers = controller.layers.ToList();
            var layer = layers.Single(l => l.name == "Stances");
            // Aim's existing mask contains arms/fingers only, so it can follow the new whole-body poses.
            layers.Remove(layer);
            layers.Insert(layers.FindIndex(l => l.name == "Halo Aim"), layer);
            layer.defaultWeight = 0;
            layer.iKPass = true;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            var sm = layer.stateMachine;
            var stand = State(sm, "Standing", AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Tiptoe_Idle.anim"));
            var crouch = State(sm, "Crouching", Tree(controller, "Crouch Movement", crouchIdle, crouchMove, 1f));
            var prone = State(sm, "Prone", Tree(controller, "Prone Movement", proneIdle, crawl, .44f));
            var toe = State(sm, "Tiptoe", Tree(controller, "Tiptoe Movement", toeIdle, toeMove, .8f));
            var states = new[] { stand, crouch, prone, toe };
            sm.defaultState = stand;
            for (int i = 0; i < states.Length; i++)
            {
                foreach (var transition in states[i].transitions) states[i].RemoveTransition(transition);
                for (int j = 0; j < states.Length; j++)
                {
                    if (i == j) continue;
                    var transition = states[i].AddTransition(states[j]);
                    transition.hasExitTime = false;
                    transition.duration = .22f;
                    transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
                    transition.AddCondition(AnimatorConditionMode.Equals, j, "Stance");
                }
            }
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureImport(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.bakeAxisConversion = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimationClip Copy(string path, string sourceName, string name, bool freeze = false)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Single(c => c.name == sourceName || c.name == "Armature|" + sourceName);
            if (!source.isHumanMotion) throw new InvalidOperationException("Not Humanoid: " + sourceName);
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = name;
            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            {
                var curve = AnimationUtility.GetEditorCurve(copy, binding);
                if (freeze) curve = AnimationCurve.Constant(0, 1, curve.Evaluate(0));
                else if (binding.type == typeof(Animator) && (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z"))
                {
                    var keys = curve.keys;
                    float start = curve.Evaluate(0), slope = (curve.Evaluate(source.length) - start) / source.length;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].value -= start + slope * keys[i].time;
                        keys[i].inTangent -= slope;
                        keys[i].outTangent -= slope;
                    }
                    curve.keys = keys;
                }
                AnimationUtility.SetEditorCurve(copy, binding, curve);
            }
            if (path == KayKit) FaceForward(copy);
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            string destination = Root + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (!existing) { AssetDatabase.CreateAsset(copy, destination); return copy; }
            EditorUtility.CopySerialized(copy, existing);
            UnityEngine.Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void FaceForward(AnimationClip clip)
        {
            // KayKit's FBX body faces -Z; preserve the crawl's pitch while matching our +Z motor.
            string[] axes = { "x", "y", "z", "w" };
            string[] sourceAxes = { "z", "w", "x", "y" };
            var rootRotation = sourceAxes.Select(axis => AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ." + axis))).ToArray();
            for (int i = 0; i < 4; i++)
            {
                var curve = rootRotation[i];
                var keys = curve.keys;
                if (i >= 2) for (int k = 0; k < keys.Length; k++)
                { keys[k].value = -keys[k].value; keys[k].inTangent = -keys[k].inTangent; keys[k].outTangent = -keys[k].outTangent; }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ." + axes[i]), curve);
            }
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = false;
            EditorUtility.SetDirty(state);
            return state;
        }

        private static BlendTree Tree(AnimatorController controller, string name, Motion idle, Motion move, float speed)
        {
            var tree = AssetDatabase.LoadAllAssetsAtPath(DollSingerLocomotionTools.ControllerPath)
                .OfType<BlendTree>().FirstOrDefault(t => t.name == name);
            if (!tree) { tree = new BlendTree { name = name }; AssetDatabase.AddObjectToAsset(tree, controller); }
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = idle, threshold = 0, timeScale = 1 },
                new ChildMotion { motion = move, threshold = speed, timeScale = 1 }
            };
            EditorUtility.SetDirty(tree);
            return tree;
        }
    }
}
