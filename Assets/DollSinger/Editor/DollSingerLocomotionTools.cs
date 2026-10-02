using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger.Editor
{
    /// <summary>Builds editable in-place locomotion without changing imported Mixamo FBX assets.</summary>
    public static class DollSingerLocomotionTools
    {
        public const string ControllerPath = "Assets/DollSinger/Animation/DollSingerThirdPersonHalo.controller";
        public const string ClipRoot = "Assets/DollSinger/Animation/Locomotion";
        private const string Loco = "Assets/Mixamo/Locomotion Pack/";
        private const string Pistol = "Assets/Mixamo/Pistol_Handgun Locomotion Pack/";

        [MenuItem("Tools/Doll Singer/Set Up Mixamo Locomotion")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller) throw new InvalidOperationException("DollSinger controller missing.");
            var state = controller.layers[0].stateMachine.states.Select(s => s.state)
                .Single(s => s.name == "Idle Walk Run Blend");
            if (!AssetDatabase.IsValidFolder(ClipRoot)) AssetDatabase.CreateFolder("Assets/DollSinger/Animation", "Locomotion");
            foreach (string parameter in new[] { "MoveX", "MoveZ", "AimBlend" })
            {
                var existing = controller.parameters.FirstOrDefault(p => p.name == parameter);
                if (existing == null) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
                else if (existing.type != AnimatorControllerParameterType.Float)
                    throw new InvalidOperationException(parameter + " must be a Float parameter.");
            }

            // These two existing assets intentionally stay exactly as authored.
            var forwardWalk = LoadClip("Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/Locomotion--Walk_N.anim.fbx");
            var forwardRun = LoadClip("Assets/DollSinger/Animation/DollSinger_Run_N_RelaxedFingers.anim");
            var free = DirectionTree(controller, "Unarmed Directional Locomotion",
                CopyClip(Loco + "idle.fbx", "Unarmed_Idle"), forwardWalk, forwardRun,
                CopyClip(Loco + "walking.fbx", "Unarmed_Walk_Back", true),
                CopyClip(Loco + "running.fbx", "Unarmed_Run_Back", true),
                CopyClip(Loco + "left strafe walking.fbx", "Unarmed_Walk_Left"),
                CopyClip(Loco + "right strafe walking.fbx", "Unarmed_Walk_Right"),
                CopyClip(Loco + "left strafe.fbx", "Unarmed_Run_Left"),
                CopyClip(Loco + "right strafe.fbx", "Unarmed_Run_Right"));

            var pistolLeft = CopyClip(Pistol + "pistol strafe.fbx", "Pistol_Strafe_Left");
            var pistolRight = CopyClip(Pistol + "pistol strafe (2).fbx", "Pistol_Strafe_Right");
            var aim = DirectionTree(controller, "Pistol Directional Locomotion",
                CopyClip(Pistol + "pistol idle.fbx", "Pistol_Idle"),
                CopyClip(Pistol + "pistol walk.fbx", "Pistol_Walk_Forward"),
                CopyClip(Pistol + "pistol run.fbx", "Pistol_Run_Forward"),
                CopyClip(Pistol + "pistol walk backward.fbx", "Pistol_Walk_Back"),
                CopyClip(Pistol + "pistol run backward.fbx", "Pistol_Run_Back"),
                pistolLeft, pistolRight, pistolLeft, pistolRight);
            var aimChildren = aim.children;
            aimChildren[7].timeScale = 1.5f;
            aimChildren[8].timeScale = 1.5f;
            aim.children = aimChildren;

            var root = state.motion as BlendTree;
            if (!root) root = GetTree(controller, "Grounded Locomotion");
            root.name = "Grounded Locomotion";
            root.blendType = BlendTreeType.Simple1D;
            root.blendParameter = "AimBlend";
            root.useAutomaticThresholds = false;
            root.children = new[]
            {
                new ChildMotion { motion = free, threshold = 0, timeScale = 1 },
                new ChildMotion { motion = aim, threshold = 1, timeScale = 1 }
            };
            state.motion = root;
            EditorUtility.SetDirty(free);
            EditorUtility.SetDirty(aim);
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(controller);
            SetUpTurnInPlace();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Doll Singer/Set Up Turn In Place")]
        public static void SetUpTurnInPlace()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller) throw new InvalidOperationException("DollSinger controller missing.");
            if (!AssetDatabase.IsValidFolder(ClipRoot)) AssetDatabase.CreateFolder("Assets/DollSinger/Animation", "Locomotion");
            foreach (string parameter in new[] { "TurnDirection", "TurnPlayback" })
            {
                var existing = controller.parameters.FirstOrDefault(p => p.name == parameter);
                if (existing == null) controller.AddParameter(new AnimatorControllerParameter
                {
                    name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f
                });
                else if (existing.type != AnimatorControllerParameterType.Float)
                    throw new InvalidOperationException(parameter + " must be a Float parameter.");
            }

            const string maskPath = "Assets/DollSinger/Animation/DollSinger_TurnLegs.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (!mask)
            {
                mask = new AvatarMask { name = "DollSinger_TurnLegs" };
                AssetDatabase.CreateAsset(mask, maskPath);
            }
            // Ignore authored root/body turning: the controller owns world rotation.
            // Include the clip's humanoid foot goals as well as leg muscles; masking
            // those goals out leaves idle targets pinning the feet in place.
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,
                    i == (int)AvatarMaskBodyPart.LeftLeg || i == (int)AvatarMaskBodyPart.RightLeg ||
                    i == (int)AvatarMaskBodyPart.LeftFootIK || i == (int)AvatarMaskBodyPart.RightFootIK);

            var tree = GetTree(controller, "Turn In Place Direction");
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "TurnDirection";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = CopyClip(Loco + "left turn 90.fbx", "Turn_Left"), threshold = -1f, timeScale = 1f },
                new ChildMotion { motion = CopyClip(Loco + "right turn 90.fbx", "Turn_Right"), threshold = 1f, timeScale = 1f }
            };
            if (!controller.layers.Any(l => l.name == "Turn In Place")) controller.AddLayer("Turn In Place");
            var layers = controller.layers;
            var layer = layers.Single(l => l.name == "Turn In Place");
            layer.avatarMask = mask;
            layer.defaultWeight = 0f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            var state = layer.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Turn Steps")
                        ?? layer.stateMachine.AddState("Turn Steps");
            layer.stateMachine.defaultState = state;
            state.motion = tree;
            state.speedParameter = "TurnPlayback";
            state.speedParameterActive = true;
            state.writeDefaultValues = false;
            controller.layers = layers;
            EditorUtility.SetDirty(mask);
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(layer.stateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static BlendTree DirectionTree(AnimatorController controller, string name,
            Motion idle, Motion forwardWalk, Motion forwardRun, Motion backWalk, Motion backRun,
            Motion leftWalk, Motion rightWalk, Motion leftRun, Motion rightRun)
        {
            var tree = GetTree(controller, name);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                Child(idle, 0, 0), Child(forwardWalk, 0, 2), Child(forwardRun, 0, 6),
                Child(backWalk, 0, -2), Child(backRun, 0, -6),
                Child(leftWalk, -2, 0), Child(rightWalk, 2, 0),
                Child(leftRun, -6, 0), Child(rightRun, 6, 0)
            };
            return tree;
        }

        private static ChildMotion Child(Motion motion, float x, float z) =>
            new ChildMotion { motion = motion, position = new Vector2(x, z), timeScale = 1 };

        private static BlendTree GetTree(AnimatorController controller, string name)
        {
            var tree = AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().FirstOrDefault(t => t.name == name);
            if (tree) return tree;
            tree = new BlendTree { name = name };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        private static AnimationClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (!clip || !clip.isHumanMotion) throw new InvalidOperationException("Humanoid animation missing: " + path);
            return clip;
        }

        private static AnimationClip CopyClip(string sourcePath, string name, bool backwards = false)
        {
            var source = LoadClip(sourcePath);
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = name;
            float duration = source.length;
            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            {
                var curve = AnimationUtility.GetEditorCurve(copy, binding);
                var keys = curve.keys;
                // Remove net horizontal hip travel, rather than baking metres of travel
                // into the body when the network controller already owns displacement.
                if (binding.type == typeof(Animator) &&
                    (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z"))
                {
                    float start = curve.Evaluate(0);
                    float slope = (curve.Evaluate(duration) - start) / duration;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].value -= start + slope * keys[i].time;
                        keys[i].inTangent -= slope;
                        keys[i].outTangent -= slope;
                    }
                }
                if (backwards)
                {
                    Array.Reverse(keys);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        var key = keys[i];
                        float tangent = key.inTangent, weight = key.inWeight;
                        key.time = duration - key.time;
                        key.inTangent = -key.outTangent;
                        key.outTangent = -tangent;
                        key.inWeight = key.outWeight;
                        key.outWeight = weight;
                        key.weightedMode = key.weightedMode == WeightedMode.In ? WeightedMode.Out :
                            key.weightedMode == WeightedMode.Out ? WeightedMode.In : key.weightedMode;
                        keys[i] = key;
                    }
                }
                curve.keys = keys;
                curve.preWrapMode = curve.postWrapMode = WrapMode.Loop;
                AnimationUtility.SetEditorCurve(copy, binding, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            string path = ClipRoot + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(copy, existing);
                UnityEngine.Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
    }
}
