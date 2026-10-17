using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

/// <summary>Play-only validation on the real network player in the four-camera lab.</summary>
public static class BlenderGestureValidation
{
    [Serializable] private class Motion
    {
        public int fireFrames, reloadFrames;
        public float fireSeconds;
        public float[] pose, fire, reload;
    }
    [Serializable] private class Bake { public string[] bones; public Motion[] motions; }
    private static readonly string Output = "Temp/GestureValidation/Blender";
    public static string Status { get; private set; } = "Not run";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Validate Blender Gestures In Playing Lab")]
    public static void Run()
    {
        var lab = UnityEngine.Object.FindAnyObjectByType<WeaponGestureLab>();
        if (!EditorApplication.isPlaying || !lab) throw new InvalidOperationException("Play WeaponGestureLab first.");
        if (Status == "Running") throw new InvalidOperationException("Validation already running.");
        Status = "Running";
        lab.StartCoroutine(Guard(Check(lab)));
    }

    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            object current;
            try { if (!routine.MoveNext()) break; current = routine.Current; }
            catch (Exception e) { Status = "FAILED: " + e; Debug.LogException(e); File.WriteAllText(Output + "/checks.txt", Status); yield break; }
            yield return current;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Quaternion Read(float[] values, int frame, int bone, int count)
    {
        int i = (frame * count + bone) * 4;
        return new Quaternion(values[i], values[i + 1], values[i + 2], values[i + 3]);
    }
    private static float Compare(Bake bake, WeaponGestureLab lab, Dictionary<string, Transform> bones)
    {
        var m = bake.motions[lab.WeaponIndex];
        float[] values = m.pose; float frame = 0; int count = 1;
        float age = Time.time - (float)typeof(DollSingerHaloAim).GetField("gestureShotTime", Private).GetValue(lab.Halo);
        if (lab.Reloading)
        {
            float progress = lab.WeaponIndex == 0 ? lab.Halo.rifleVisual.ReloadProgress : lab.WeaponIndex == 1 ? lab.Halo.revolverVisual.ReloadProgress :
                lab.WeaponIndex == 2 ? lab.Halo.shotgunVisual.ReloadProgress : lab.Halo.sniperVisual.ReloadProgress;
            values = m.reload; count = m.reloadFrames; frame = progress * (count - 1);
        }
        else if (age >= 0 && age < m.fireSeconds) { values = m.fire; count = m.fireFrames; frame = age / m.fireSeconds * (count - 1); }
        int a = Mathf.Clamp(Mathf.FloorToInt(frame), 0, count - 1), b = Mathf.Min(a + 1, count - 1);
        float max = 0;
        for (int i = 0; i < bake.bones.Length; i++)
        {
            if (bake.bones[i].StartsWith("肩P")) continue; // The authored pair receives a common look rotation here.
            var expected = Quaternion.Slerp(Read(values, a, i, bake.bones.Length), Read(values, b, i, bake.bones.Length), frame - a);
            max = Mathf.Max(max, Quaternion.Angle(expected, bones[bake.bones[i]].localRotation));
        }
        Require(max < .12f, "Blender rotations overwritten at runtime: " + max + " degrees");
        return max;
    }
    private static void Shape(Dictionary<string, Transform> b, Camera camera, List<string> report)
    {
        float worstStraight = 0, worstRightAngle = 0;
        foreach (var side in new[] { "L", "R" })
        {
            var a = b["人指１." + side].position; var c = b["人指２." + side].position;
            var d = b["人指３." + side].position; var e = b["人指先." + side].position;
            worstStraight = Mathf.Max(worstStraight, Vector3.Angle(c - a, d - c), Vector3.Angle(d - c, e - d));
            var thumb = b["親指先." + side].position - b["親指１." + side].position;
            worstRightAngle = Mathf.Max(worstRightAngle, Mathf.Abs(90 - Vector3.Angle(e - c, thumb)));
            var indexUV = camera.WorldToScreenPoint(e) - camera.WorldToScreenPoint(c);
            var thumbUV = camera.WorldToScreenPoint(b["親指先." + side].position) - camera.WorldToScreenPoint(b["親指１." + side].position);
            Require(Mathf.Abs(90 - Vector2.Angle(indexUV, thumbUV)) < 4, "Projected frame corner lost its right angle.");
        }
        float gap = Mathf.Max(Vector3.Distance(b["人指先.L"].position, b["親指先.R"].position), Vector3.Distance(b["人指先.R"].position, b["親指先.L"].position));
        Require(worstStraight < .3f, "Index joints bent: " + worstStraight);
        Require(worstRightAngle < .3f, "Thumb/index not perpendicular: " + worstRightAngle);
        Require(gap < .001f, "Viewfinder did not close: " + gap);
        report.Add($"sniper: index bend {worstStraight:F3} deg, thumb/index right-angle error {worstRightAngle:F3} deg, maximum contact gap {gap * 1000:F3} mm");
    }
    private static void Visible(WeaponGestureLab lab, Dictionary<string, Transform> bones)
    {
        foreach (var name in new[] { "手首.L", "手首.R" })
        foreach (var bone in bones[name].GetComponentsInChildren<Transform>())
        {
            var uv = lab.PlayerView.GestureCamera.WorldToViewportPoint(bone.position);
            Require(uv.z > .015f && uv.x > .01f && uv.x < .99f && uv.y > .01f && uv.y < .99f, "Finger left owner viewport: " + bone.name);
        }
    }
    private static IEnumerator Check(WeaponGestureLab lab)
    {
        Directory.CreateDirectory(Output);
        var report = new List<string> { "Actual DollSingerNetworkPlayer in WeaponGestureLab; four simultaneous cameras. Blender exact-rig bake." };
        var root = UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        lab.SetPaused(false); root.Q<Slider>("speed").value = 1;
        root.Q<Toggle>("repeatFire").value = false; root.Q<Toggle>("aim").value = true;
        root.Q<Toggle>("firstPerson").value = true; root.Q<Slider>("pitch").value = 0; root.Q<Slider>("orbit").value = 0;
        root.Q<Slider>("distance").value = 1.0f; lab.FocusView(-1);
        var asset = new SerializedObject(lab.Halo).FindProperty("gestureBake").objectReferenceValue as TextAsset;
        var bake = JsonUtility.FromJson<Bake>(asset.text);
        var bones = lab.Halo.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        float error = 0, fanDistance = 0;
        for (int weapon = 0; weapon < 4; weapon++)
        {
            lab.SelectWeapon(weapon); yield return new WaitForSeconds(.6f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones);
            if (weapon == 3) Shape(bones, lab.PlayerView.GestureCamera, report);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + $"/{weapon + 1}-idle.png")); yield return new WaitForEndOfFrame();
            Vector3 offhand = bones["手首.L"].position;
            lab.Fire(); float start = Time.time; bool captured = false;
            while (Time.time - start < bake.motions[weapon].fireSeconds + .04f)
            {
                yield return new WaitForEndOfFrame();
                error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones);
                if (weapon == 1) fanDistance = Mathf.Max(fanDistance, Vector3.Distance(offhand, bones["手首.L"].position));
                if (!captured && Time.time - start > (weapon == 1 ? .12f : .06f))
                { captured = true; ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + $"/{weapon + 1}-fire.png")); }
            }
            Require(lab.Ammo > 0, "Unexpected ammo state");
            lab.Reload();
            while (lab.Reloading)
            {
                yield return new WaitForEndOfFrame();
                error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones);
            }
            report.Add($"weapon {weapon + 1}: idle, fire, reload; all four cameras active; exact local rotations and owner finger visibility passed");
        }
        Require(fanDistance > .055f, "Revolver offhand did not fan the hammer: " + fanDistance);
        report.Add($"revolver offhand stroke {fanDistance * 1000:F1} mm; maximum Blender/runtime rotation difference {error:F3} deg");
        // Repeat the straightness/closure check through actual first/third-person mode and extreme pitch.
        foreach (bool fp in new[] { true, false })
        foreach (float pitch in new[] { -65f, 0f, 65f })
        {
            root.Q<Toggle>("firstPerson").value = fp; root.Q<Slider>("pitch").value = pitch;
            yield return new WaitForSeconds(.35f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error, Compare(bake, lab, bones));
            if (fp) { Visible(lab, bones); Shape(bones, lab.PlayerView.GestureCamera, report); }
        }
        root.Q<Toggle>("firstPerson").value = true; root.Q<Slider>("pitch").value = 0;
        root.Q<Toggle>("aim").value = false; yield return new WaitForSeconds(.4f);
        root.Q<Toggle>("aim").value = true; yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame();
        Shape(bones, lab.PlayerView.GestureCamera, report);
        lab.SetPaused(true); var hand = bones["手首.L"].position; yield return new WaitForSecondsRealtime(.15f); yield return new WaitForEndOfFrame();
        Require(Vector3.Distance(hand, bones["手首.L"].position) < .00001f, "Frozen gesture drifted."); lab.SetPaused(false);
        report.Add("FP/TP modes, pitch -65/0/+65, aim-out/in, pause/resume passed.");
        Status = "PASS"; File.WriteAllLines(Output + "/checks.txt", report); Debug.Log("Blender gesture validation PASS: " + string.Join("\n", report));
    }
}
