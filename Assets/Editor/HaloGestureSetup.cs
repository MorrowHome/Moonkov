using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Installs editable Humanoid poses in the existing Halo Aim layer. Existing custom poses are preserved.</summary>
public static class HaloGestureSetup
{
    const string Folder = "Assets/DollSinger/Animation/";
    const string ControllerPath = Folder + "DollSingerThirdPersonHalo.controller";
    const string Parameter = "HaloGesture";
    const string PoseTime = "HaloPoseTime";

    [MenuItem("Tools/Doll Singer/Create Missing Weapon Gestures")]
    public static void InstallMissing()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "DollSinger_FingerGunAim.anim");
        if (!controller || !source) throw new InvalidOperationException("Existing DollSinger controller/aim pose required");
        var layer = controller.layers.First(l => l.name == "Halo Aim");
        if (!controller.parameters.Any(p => p.name == Parameter))
            controller.AddParameter(new AnimatorControllerParameter { name = Parameter, type = AnimatorControllerParameterType.Int, defaultInt = 1 });
        if (!controller.parameters.Any(p => p.name == PoseTime)) controller.AddParameter(PoseTime, AnimatorControllerParameterType.Float);
        for (int weapon = 1; weapon <= 4; weapon++)
        {
            string name = weapon == 1 ? "FingerGunAim" : weapon == 2 ? "RevolverGesture" : weapon == 3 ? "ShotgunGesture" : "SniperGesture";
            var state = layer.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
            if (!state)
            {
                state = layer.stateMachine.AddState(name, new Vector3(340, 80 + weapon * 80, 0));
                state.speed = 0; state.writeDefaultValues = false;
            }
            // Sample the first pose with a time parameter while retaining a
            // normal-speed state clock for the short crossfade.
            if (state.speed == 0)
            {
                state.speed = 1; state.timeParameterActive = true; state.timeParameter = PoseTime;
            }
            if (weapon != 1 && !state.motion) state.motion = CreatePose(source, name, weapon);
            if (!layer.stateMachine.anyStateTransitions.Any(t => t.destinationState == state && t.conditions.Any(c => c.parameter == Parameter)))
            {
                var transition = layer.stateMachine.AddAnyStateTransition(state);
                transition.hasExitTime = false; transition.hasFixedDuration = true; transition.duration = .16f;
                transition.canTransitionToSelf = false;
                transition.interruptionSource = TransitionInterruptionSource.None;
                transition.orderedInterruption = true;
                transition.AddCondition(AnimatorConditionMode.Equals, weapon, Parameter);
            }
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        Debug.Log("Editable revolver, shotgun and sniper gestures installed in Halo Aim");
    }

    static AnimationClip CreatePose(AnimationClip source, string name, int weapon)
    {
        string path = Folder + "DollSinger_" + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing) return existing;
        var clip = UnityEngine.Object.Instantiate(source); clip.name = "DollSinger_" + name;
        // Keep the established shoulder/wrist and raised thumb contract. Only
        // finger muscles and the revolver's relaxed support arm differ by weapon.
        if (weapon == 2)
        {
            Finger(clip, "Right", "Middle", -.65f, 0);
            Finger(clip, "Right", "Ring", -.72f, 0);
            Finger(clip, "Right", "Little", -.8f, -.1f);
            Curve(clip, "Left Arm Down-Up", -.55f);
            Curve(clip, "Left Arm Front-Back", .15f);
            Curve(clip, "Left Forearm Stretch", .25f);
        }
        else if (weapon == 3)
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                Finger(clip, side, "Index", .7f, -.12f);
                Finger(clip, side, "Middle", .7f, -.06f);
                Finger(clip, side, "Ring", .65f, .10f);
                Finger(clip, side, "Little", .6f, .15f);
                Finger(clip, side, "Thumb", .85f, .2f);
            }
        }
        else
        {
            Finger(clip, "Right", "Index", .8f, 0);
            Finger(clip, "Right", "Middle", .8f, 0);
            Finger(clip, "Right", "Ring", -.72f, 0);
            Finger(clip, "Right", "Little", -.8f, 0);
            Finger(clip, "Left", "Index", .3f, -.05f);
            Finger(clip, "Left", "Middle", .3f, 0);
            Finger(clip, "Left", "Ring", .2f, 0);
            Finger(clip, "Left", "Little", .1f, 0);
        }
        AssetDatabase.CreateAsset(clip, path); return clip;
    }

    static void Finger(AnimationClip clip, string side, string digit, float stretch, float spread)
    {
        for (int joint = 1; joint <= 3; joint++) Curve(clip, side + "Hand." + digit + "." + joint + " Stretched", stretch);
        Curve(clip, side + "Hand." + digit + ".Spread", spread);
    }
    static void Curve(AnimationClip clip, string muscle, float value) => AnimationUtility.SetEditorCurve(clip,
        EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), AnimationCurve.Constant(0, 1, value));
}
