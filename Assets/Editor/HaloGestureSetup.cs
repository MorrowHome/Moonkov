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

    [MenuItem("Tools/Doll Singer/Rebuild Expressive Weapon Gestures")]
    public static void RebuildExpressive()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        InstallMissing();
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "DollSinger_FingerGunAim.anim");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var layer = controller.layers.First(l => l.name == "Halo Aim");
        string[] states = { "FingerGunAim", "RevolverGesture", "ShotgunGesture", "SniperGesture" };
        for (int weapon = 1; weapon <= 4; weapon++)
        {
            var state = layer.stateMachine.states.Select(s => s.state).Single(s => s.name == states[weapon - 1]);
            state.motion = CreatePose(source, weapon == 1 ? "RifleGesture" : states[weapon - 1], weapon, true);
            EditorUtility.SetDirty(state);
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static AnimationClip CreatePose(AnimationClip source, string name, int weapon, bool overwrite = false)
    {
        string path = Folder + "DollSinger_" + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing && !overwrite) return existing;
        var clip = UnityEngine.Object.Instantiate(source); clip.name = "DollSinger_" + name;
        // Whole arm silhouettes are authored alongside both hands; camera-space placement
        // only presents these same Humanoid fingers to the owner, without changing aim.
        Curve(clip, "Right Arm Down-Up", -.1f);
        Curve(clip, "Right Arm Front-Back", .65f);
        Curve(clip, "Right Forearm Stretch", .25f);
        Curve(clip, "Left Arm Down-Up", -.15f);
        Curve(clip, "Left Arm Front-Back", .65f);
        Curve(clip, "Left Forearm Stretch", .1f);
        if (weapon == 1) // Thunder rail: sword fingers and a raised command seal.
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                Finger(clip, side, "Index", .9f, -.12f);
                Finger(clip, side, "Middle", .9f, -.12f);
                Finger(clip, side, "Ring", -.85f, 0);
                Finger(clip, side, "Little", -.85f, 0);
                Finger(clip, side, "Thumb", .35f, .25f);
            }
            Curve(clip, "Left Forearm Stretch", -.15f);
        }
        else if (weapon == 2) // Judgment: cocked finger gun, offhand victory sigil.
        {
            Finger(clip, "Right", "Index", .95f, .08f);
            Finger(clip, "Right", "Middle", -.85f, 0);
            Finger(clip, "Right", "Ring", -.9f, 0);
            Finger(clip, "Right", "Little", -.9f, 0);
            Finger(clip, "Right", "Thumb", .95f, .55f);
            Finger(clip, "Left", "Index", .9f, -.6f);
            Finger(clip, "Left", "Middle", .9f, .6f);
            Finger(clip, "Left", "Ring", -.85f, 0);
            Finger(clip, "Left", "Little", -.85f, 0);
            Finger(clip, "Left", "Thumb", .6f, .3f);
            Curve(clip, "Left Arm Down-Up", .1f);
        }
        else if (weapon == 3) // Rupture: two cupped, fanned palms around the energy shell.
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                Finger(clip, side, "Index", .55f, -.65f);
                Finger(clip, side, "Middle", .6f, -.2f);
                Finger(clip, side, "Ring", .45f, .35f);
                Finger(clip, side, "Little", .35f, .7f);
                Finger(clip, side, "Thumb", .8f, .65f);
            }
            Curve(clip, "Right Forearm Stretch", -.05f);
            Curve(clip, "Left Forearm Stretch", -.05f);
        }
        else // Viewfinder: opposite thumb/index corners form a photographer's frame.
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                Finger(clip, side, "Index", .95f, 0);
                Finger(clip, side, "Thumb", .95f, .9f);
                Finger(clip, side, "Middle", -.9f, 0);
                Finger(clip, side, "Ring", -.9f, 0);
                Finger(clip, side, "Little", -.9f, 0);
            }
            Curve(clip, "Left Forearm Stretch", -.2f);
        }
        if (existing)
        {
            EditorUtility.CopySerialized(clip, existing);
            UnityEngine.Object.DestroyImmediate(clip);
            EditorUtility.SetDirty(existing);
            return existing;
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
