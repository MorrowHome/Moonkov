using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using UnityEngine.UIElements;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class HealthBodyChecks
{
    [MenuItem("Tools/Moonkov/Check Medical Body Scan")]
    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks++; }
        Check(RaidVitalSignal.BodyTint(60,60,true) == Color.white, "Healthy body preserves the blue-white source art");
        var mild = RaidVitalSignal.BodyTint(45,60,true); var injured = RaidVitalSignal.BodyTint(25,60,true); var critical = RaidVitalSignal.BodyTint(5,60,true);
        Check(mild.g > injured.g && mild.b > injured.b && injured.g > critical.g,
            "Damage maps monotonically from blue-white through amber and orange to red");
        Check(critical.r > critical.g*3 && critical.r > critical.b*3, "Critically injured parts are red");
        var empty = RaidVitalSignal.BodyTint(0,60,true);
        Check(empty.a < .5f && Mathf.Abs(empty.r-empty.b) < .15f, "Zero health leaves a dim neutral silhouette, not a missing limb");
        Check(RaidVitalSignal.BodyTint(15,30,true) == RaidVitalSignal.BodyTint(30,60,true), "Game-mode health caps use the real fraction");
        Check(RaidVitalSignal.BodyTint(60,60,false).a < .3f, "Unknown snapshot cannot look healthy");
        Check(RaidVitalSignal.BodyTint(float.NaN,60,true).a < .3f && RaidVitalSignal.BodyTint(10,0,true).a < .3f,
            "Invalid values cannot produce a fabricated healthy colour");
        Check(RaidVitalSignal.BodyFill(30,60,true) == .5f && RaidVitalSignal.BodyFill(15,30,true) == .5f,
            "Fill area maps to the same fraction under normal and reduced maxima");
        Check(RaidVitalSignal.BodyFill(0,60,true) == 0 && RaidVitalSignal.BodyFill(60,60,true) == 1,
            "Empty and full scan fills are exact");
        Check(RaidVitalSignal.BodyFill(60,60,false) == 0 && RaidVitalSignal.BodyFill(float.NaN,60,true) == 0,
            "Unknown data cannot light the scan mesh");
        var state = default(PredictedPlayerGhost); RaidHealth.Initialize(ref state);
        var body = new RaidBodyDiagram(); body.Present(state);
        var cutsField = typeof(RaidBodyDiagram).GetField("m_FillCuts",BindingFlags.Instance|BindingFlags.NonPublic);
        var cuts = (float[])cutsField.GetValue(body);
        Check(cuts.Length == RaidHealth.PartCount, "Each gameplay part owns one fill cut, not one per visual plate");
        var pieces = (Array)typeof(RaidBodyDiagram).GetField("Pieces",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var scales = (float[])typeof(RaidBodyDiagram).GetField("m_PieceScales",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(body);
        foreach (var core in new[] { BodyPart.Head, BodyPart.Chest, BodyPart.Abdomen })
        {
            var indices = Enumerable.Range(0,pieces.Length).Where(i => (BodyPart)pieces.GetValue(i).GetType().GetField("Part").GetValue(pieces.GetValue(i)) == core).ToArray();
            Check(indices.Length == 1, "Head, merged chest and abdomen each use one complete scan shape");
            foreach (float fraction in new[] { 0f,.25f,1f })
            {
                RaidHealth.Set(ref state,core,RaidHealth.Maximum(state,core)*fraction); body.Present(state);
                float scale = scales[indices[0]];
                Check(Mathf.Abs(scale*scale-fraction) < .0001f && float.IsPositiveInfinity(cuts[(int)core]),
                    "Core scan shrinks from its centre with area proportional to health: "+core);
            }
        }
        var armPlates = pieces.Cast<object>().Where(p => (BodyPart)p.GetType().GetField("Part").GetValue(p) == BodyPart.LeftArm)
            .Select(p => (Vector2[])p.GetType().GetField("Points").GetValue(p)).OrderBy(p => p.Min(v => v.y)).ToArray();
        state.LeftArmHealth = RaidHealth.Maximum(state,BodyPart.LeftArm)*.25f; body.Present(state);
        Check(cuts[(int)BodyPart.LeftArm] < armPlates[1].Min(v => v.y),
            "At quarter arm health the shared fill has not reached the forearm or hand");
        state.LeftArmHealth = RaidHealth.Maximum(state,BodyPart.LeftArm)*.5f; body.Present(state);
        Check(cuts[(int)BodyPart.LeftArm] > armPlates[0].Max(v => v.y),
            "At half arm health the upper arm is fully lit before filling the forearm");
        foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
        {
            if (part == BodyPart.Auto) continue;
            var target = body.Q("healthPart"+part);
            Check(target != null && !(target is Button), "Combat anatomy must not intercept input: "+part);
        }
        Check(body.Query<Label>().ToList().Count == 0 && body.pickingMode == PickingMode.Ignore,
            "The body has no part labels or numerical blood values");
        state.ClashEnabled = true; state.ClashMaxHealthFraction = .5f; state.LeftArmHealth = 15; body.Present(state);
        var arm = body.Q("healthPartLeftArm");
        Check(arm.ClassListContains("health-body-injured"), "Half of the reduced maximum remains an injury");
        state.LeftArmHealth = 30; body.Present(state);
        Check(!arm.ClassListContains("health-body-injured"), "Full health at a reduced maximum is healthy");
        state.BodyHealthInitialized = false; body.Present(state);
        Check(!arm.ClassListContains("health-body-injured"), "Unknown data is separate from injury");
        Check(state.LeftArmHealth == 30 && !state.BodyHealthInitialized, "Scan presentation cannot write player health");
        return $"PASS: {checks} scan colour, whole-part fill, mode maximum, unknown-state and passive-HUD checks.";
    }
}
