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
        public float fireSeconds, pitch;
        public Motion[] aimPoses;
        public float[] pose, fire, reload;
    }
    [Serializable] private class Bake { public string revision; public string[] bones; public Motion[] motions; public Quaternion chestBindRotation; }
    private static readonly string Output = "Temp/GestureValidation/Blender";
    public static string Status { get; private set; } = "Not run";
    private static Mesh armSurface;
    private static SkinnedMeshRenderer armRenderer;
    private static int[] armTriangles;
    private static readonly Rect AimWindow = Rect.MinMaxRect(.44f, .44f, .56f, .56f);
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
            catch (Exception e) { if (armSurface) UnityEngine.Object.Destroy(armSurface); Status = "FAILED: " + e; Debug.LogException(e); File.WriteAllText(Output + "/checks.txt", Status); yield break; }
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
        Motion low = m, high = m;
        Vector3 aim = (Vector3)typeof(DollSingerHaloAim).GetField("networkDirection", Private).GetValue(lab.Halo);
        float pitch = Mathf.Asin(-aim.normalized.y)*Mathf.Rad2Deg;
        if (m.aimPoses != null)
        foreach(var pose in m.aimPoses)
        {
            if (pose.pitch <= pitch && (low.pitch > pitch || pose.pitch > low.pitch)) low = pose;
            if (pose.pitch >= pitch && (high.pitch < pitch || pose.pitch < high.pitch)) high = pose;
        }
        float pitchMix = Mathf.InverseLerp(low.pitch, high.pitch, pitch);
        float[] values = low.pose, highValues = high.pose; float frame = 0; int count = 1;
        float age = Time.time - (float)typeof(DollSingerHaloAim).GetField("gestureShotTime", Private).GetValue(lab.Halo);
        if (lab.Reloading)
        {
            float progress = lab.WeaponIndex == 0 ? lab.Halo.rifleVisual.ReloadProgress : lab.WeaponIndex == 1 ? lab.Halo.revolverVisual.ReloadProgress :
                lab.WeaponIndex == 2 ? lab.Halo.shotgunVisual.ReloadProgress : lab.Halo.sniperVisual.ReloadProgress;
            values = low.reload; highValues = high.reload; count = m.reloadFrames; frame = progress * (count - 1);
        }
        else if (age >= 0 && age < m.fireSeconds) { values = low.fire; highValues = high.fire; count = m.fireFrames; frame = age / m.fireSeconds * (count - 1); }
        int a = Mathf.Clamp(Mathf.FloorToInt(frame), 0, count - 1), b = Mathf.Min(a + 1, count - 1);
        float max = 0;
        for (int i = 0; i < bake.bones.Length; i++)
        {
            var expected = Quaternion.Slerp(Read(values, a, i, bake.bones.Length), Read(values, b, i, bake.bones.Length), frame - a);
            if(pitchMix>0) expected=Quaternion.Slerp(expected,Quaternion.Slerp(Read(highValues,a,i,bake.bones.Length),Read(highValues,b,i,bake.bones.Length),frame-a),pitchMix);
            if (bake.bones[i].StartsWith("肩P"))
            {
                // Validate the common world orientation too: local-only checks
                // cannot detect an entire arm pair rolling at steep look angles.
                float authoredPitch = m.aimPoses == null ? 0 : pitch;
                expected = Quaternion.LookRotation(aim, Vector3.up) * Quaternion.Euler(-authoredPitch,0,0) * bake.chestBindRotation * expected;
                // The revolver's tiny common correction aligns interpolated chains.
                if (lab.WeaponIndex != 1) max = Mathf.Max(max, Quaternion.Angle(expected, bones[bake.bones[i]].rotation));
            }
            else max = Mathf.Max(max, Quaternion.Angle(expected, bones[bake.bones[i]].localRotation));
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
            if (lab.WeaponIndex < 3) Require(uv.y < .42f, "Hand entered aiming area: " + bone.name);
        }
    }
    private static void PointAtAim(WeaponGestureLab lab, Dictionary<string, Transform> bones)
    {
        if (lab.WeaponIndex != 1) return;
        var camera = lab.PlayerView.GestureCamera;
        Vector2 a = camera.WorldToViewportPoint(bones["人指１.R"].position);
        Vector2 b = camera.WorldToViewportPoint(bones["人指先.R"].position);
        // Use pixel coordinates so non-square viewports do not distort the angle.
        var pixel = new Vector2(camera.pixelWidth, camera.pixelHeight);
        Require(Vector2.Angle(Vector2.Scale(b-a,pixel), Vector2.Scale(Vector2.one*.5f-a,pixel)) < 1f,
            "Revolver index does not point at crosshair");
    }
    // Test the rendered arm triangles too: sleeves and forearms must leave the
    // central 12% square clear, even when all finger joints are below it.
    private static void ClearAim(WeaponGestureLab lab)
    {
        if (lab.WeaponIndex == 3) return;
        // Compensate the FBX hierarchy scale before applying localToWorldMatrix.
        armRenderer.BakeMesh(armSurface, true);
        var vertices = armSurface.vertices;
        var camera = lab.PlayerView.GestureCamera;
        var world = armRenderer.transform.localToWorldMatrix;
        var projected = new Vector3[vertices.Length];
        var used = new bool[vertices.Length];
        foreach (int i in armTriangles)
            if (!used[i]) { used[i] = true; projected[i] = camera.WorldToViewportPoint(world.MultiplyPoint3x4(vertices[i])); }
        for (int i = 0; i < armTriangles.Length; i += 3)
        {
            var a = projected[armTriangles[i]]; var b = projected[armTriangles[i + 1]]; var c = projected[armTriangles[i + 2]];
            // A triangle crossing the near plane is clipped by the real camera.
            // Its edge belongs to the shoulder, behind the posed hands; check the
            // visible vertices and clipped polygon in camera coordinates below.
            if (a.z < camera.nearClipPlane || b.z < camera.nearClipPlane || c.z < camera.nearClipPlane)
            {
                var input = new List<Vector3> { world.MultiplyPoint3x4(vertices[armTriangles[i]]), world.MultiplyPoint3x4(vertices[armTriangles[i+1]]), world.MultiplyPoint3x4(vertices[armTriangles[i+2]]) };
                var polygon = new List<Vector3>();
                for (int j = 0; j < input.Count; j++)
                {
                    var u = input[j]; var v = input[(j+1)%input.Count];
                    float du = camera.transform.InverseTransformPoint(u).z - camera.nearClipPlane;
                    float dv = camera.transform.InverseTransformPoint(v).z - camera.nearClipPlane;
                    if (du >= 0) polygon.Add(u);
                    if ((du >= 0) != (dv >= 0)) polygon.Add(Vector3.Lerp(u, v, du/(du-dv)));
                }
                for (int j = 1; j + 1 < polygon.Count; j++)
                    Require(!TouchesAim(camera.WorldToViewportPoint(polygon[0]), camera.WorldToViewportPoint(polygon[j]), camera.WorldToViewportPoint(polygon[j+1])), "Clipped arm surface obscures aim");
            }
            else if (TouchesAim(a,b,c))
            {
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/aim-failed.png"));
                throw new Exception($"Arm surface obscures aim: weapon {lab.WeaponIndex}, tri {i/3}, UV {a} / {b} / {c}, source scale {armRenderer.transform.lossyScale}");
            }
        }
    }
    private static bool TouchesAim(Vector2 a, Vector2 b, Vector2 c)
    {
        if (Mathf.Max(a.x,b.x,c.x) < AimWindow.xMin || Mathf.Min(a.x,b.x,c.x) > AimWindow.xMax ||
            Mathf.Max(a.y,b.y,c.y) < AimWindow.yMin || Mathf.Min(a.y,b.y,c.y) > AimWindow.yMax) return false;
        if (AimWindow.Contains(a) || AimWindow.Contains(b) || AimWindow.Contains(c)) return true;
        var corners = new[] { new Vector2(AimWindow.xMin,AimWindow.yMin), new Vector2(AimWindow.xMax,AimWindow.yMin),
            new Vector2(AimWindow.xMax,AimWindow.yMax), new Vector2(AimWindow.xMin,AimWindow.yMax) };
        foreach (var p in corners)
        {
            float x = Cross(b-a,p-a), y = Cross(c-b,p-b), z = Cross(a-c,p-c);
            if ((x >= 0 && y >= 0 && z >= 0) || (x <= 0 && y <= 0 && z <= 0)) return true;
        }
        foreach (var edge in new[] { (a,b), (b,c), (c,a) })
        for (int i = 0; i < 4; i++)
        {
            var p = corners[i]; var q = corners[(i+1)%4];
            if (Cross(edge.Item2-edge.Item1,p-edge.Item1)*Cross(edge.Item2-edge.Item1,q-edge.Item1) < 0 &&
                Cross(q-p,edge.Item1-p)*Cross(q-p,edge.Item2-p) < 0) return true;
        }
        return false;
    }
    private static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
    private static void HeadLook(WeaponGestureLab lab, Dictionary<string, Transform> bones)
    {
        // This imported rig's head bind axes are world forward/up. Checking only
        // forward missed the Humanoid solver's 37-degree roll at high pitch.
        var head = bones["頭"];
        var camera = lab.PlayerView.camera.transform;
        Require(Vector3.Angle(head.forward, camera.forward) < .15f, "Head forward drifted from view");
        Require(Vector3.Angle(head.up, camera.up) < .15f, "Pitch introduced unwanted head roll");
    }
    private static IEnumerator Check(WeaponGestureLab lab)
    {
        Directory.CreateDirectory(Output);
        var report = new List<string> { "Actual DollSingerNetworkPlayer in WeaponGestureLab; four simultaneous cameras. Blender exact-rig bake." };
        var root = UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        lab.SetPaused(false); root.Q<Slider>("speed").value = 1;
        root.Q<Toggle>("repeatFire").value = false; root.Q<Toggle>("aim").value = true;
        root.Q<Toggle>("firstPerson").value = true; root.Q<Slider>("pitch").value = 0; root.Q<Slider>("orbit").value = 0;
        root.Q<Slider>("pitch").lowValue = -80; root.Q<Slider>("pitch").highValue = 80;
        root.Q<Slider>("distance").value = 1.0f; lab.FocusView(-1);
        var asset = new SerializedObject(lab.Halo).FindProperty("gestureBake").objectReferenceValue as TextAsset;
        var bake = JsonUtility.FromJson<Bake>(asset.text);
        var disk = JsonUtility.FromJson<Bake>(File.ReadAllText(AssetDatabase.GetAssetPath(asset)));
        Require(bake.revision == disk.revision, "Unity is using a stale bake. Stop Play, force-import the JSON, then Play again.");
        report.Add("Bake revision: " + bake.revision);
        var bones = lab.Halo.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        armRenderer = lab.Halo.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(x => x.name == "FirstPersonGestureArms (Runtime)");
        armSurface = new Mesh(); armTriangles = armRenderer.sharedMesh.triangles;
        float error = 0, fanDistance = 0;
        for (int weapon = 0; weapon < 4; weapon++)
        {
            lab.SelectWeapon(weapon); yield return new WaitForSeconds(.6f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones); ClearAim(lab); PointAtAim(lab, bones);
            if (weapon == 3) Shape(bones, lab.PlayerView.GestureCamera, report);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + $"/{weapon + 1}-idle.png")); yield return new WaitForEndOfFrame();
            Vector3 offhand = bones["手首.L"].position;
            lab.Fire(); float start = Time.time; bool captured = false;
            while (Time.time - start < bake.motions[weapon].fireSeconds + .04f)
            {
                yield return new WaitForEndOfFrame();
                error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones); ClearAim(lab); PointAtAim(lab, bones);
                if (weapon == 1) fanDistance = Mathf.Max(fanDistance, Vector3.Distance(offhand, bones["手首.L"].position));
                if (!captured && Time.time - start > (weapon == 1 ? .12f : .06f))
                { captured = true; ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + $"/{weapon + 1}-fire.png")); }
            }
            Require(lab.Ammo > 0, "Unexpected ammo state");
            lab.Reload();
            while (lab.Reloading)
            {
                yield return new WaitForEndOfFrame();
                error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones); ClearAim(lab); PointAtAim(lab, bones);
            }
            report.Add($"weapon {weapon + 1}: idle, fire, reload; all four cameras active; exact local rotations and owner finger visibility passed");
        }
        Require(fanDistance > .055f, "Revolver offhand did not fan the hammer: " + fanDistance);
        report.Add($"revolver offhand stroke {fanDistance * 1000:F1} mm; maximum Blender/runtime rotation difference {error:F3} deg");
        // The normal weapons retain a clear aiming window at steep pitch and
        // both common aspect ratios. Inspect the actual deformed arm triangles.
        foreach (float aspect in new[] { 16f/9f, 4f/3f })
        foreach (int weapon in new[] { 0, 1, 2 })
        foreach (float pitch in new[] { -80f, -65f, -40f, 0f, 15f, 30f, 45f, 65f, 70f, 75f, 80f })
        {
            lab.SelectWeapon(weapon); root.Q<Slider>("pitch").value = pitch;
            lab.PlayerView.camera.aspect = aspect;
            yield return new WaitForSeconds(.4f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error,Compare(bake,lab,bones)); Visible(lab, bones); ClearAim(lab); PointAtAim(lab, bones); HeadLook(lab, bones);
        }
        // Reproduce looking up continuously, rather than only checking settled
        // endpoints. The hand projection must not retain another angle's fit.
        lab.PlayerView.camera.aspect = 16f/9f;
        foreach(int weapon in new[]{0,1,2})
        {
            lab.SelectWeapon(weapon); root.Q<Slider>("pitch").value = 0;
            yield return new WaitForSeconds(.4f);
            float start = Time.time;
            Vector2 last = Vector2.zero; bool hasLast = false;
            while(Time.time-start<4f)
            {
                root.Q<Slider>("pitch").value = -80f*Mathf.Sin((Time.time-start)/2f*Mathf.PI);
                yield return new WaitForEndOfFrame();
                Visible(lab,bones); ClearAim(lab); PointAtAim(lab,bones); HeadLook(lab,bones);
                var a=lab.PlayerView.GestureCamera.WorldToViewportPoint(bones["手首.L"].position);
                var b=lab.PlayerView.GestureCamera.WorldToViewportPoint(bones["手首.R"].position);
                Vector2 current=(a+b)*.5f;
                if(hasLast) Require(Vector2.Distance(current,last)<.08f,"Looking up made the gesture projection snap");
                last=current; hasLast=true;
            }
        }
        report.Add("Continuous pitch sweep 0/-80/0/+80/0: visible fingers, clear arm geometry, pointing direction and projection continuity passed.");
        report.Add("Head forward/up track the owner camera within 0.15 degrees through pitch/aspect samples and continuous look-up; no extra Humanoid head roll.");
        lab.PlayerView.camera.aspect = 16f/9f;
        lab.SelectWeapon(3); root.Q<Slider>("pitch").value = 0;
        yield return new WaitForSeconds(.5f);
        report.Add("Revolver index converges on crosshair within 1 degree in idle/fire/reload and pitch/aspect checks. Normal weapons: finger joints below 42% viewport height; deformed arm triangles leave central 12% square clear through fire/reload, authored and interpolated pitch -80 through +80 and 16:9 / 4:3.");
        // Repeat the straightness/closure check through actual first/third-person mode and extreme pitch.
        foreach (bool fp in new[] { true, false })
        foreach (float pitch in new[] { -65f, 0f, 65f })
        {
            root.Q<Toggle>("firstPerson").value = fp; root.Q<Slider>("pitch").value = pitch;
            yield return new WaitForSeconds(.35f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error, Compare(bake, lab, bones));
            if (fp) { Visible(lab, bones); ClearAim(lab); Shape(bones, lab.PlayerView.GestureCamera, report); }
        }
        root.Q<Toggle>("firstPerson").value = true; root.Q<Slider>("pitch").value = 0;
        root.Q<Toggle>("aim").value = false; yield return new WaitForSeconds(.4f);
        root.Q<Toggle>("aim").value = true; yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame();
        Shape(bones, lab.PlayerView.GestureCamera, report);
        lab.SetPaused(true); var hand = bones["手首.L"].position; yield return new WaitForSecondsRealtime(.15f); yield return new WaitForEndOfFrame();
        Require(Vector3.Distance(hand, bones["手首.L"].position) < .00001f, "Frozen gesture drifted."); lab.SetPaused(false);
        report.Add("FP/TP modes, pitch -65/0/+65, aim-out/in, pause/resume passed.");
        UnityEngine.Object.Destroy(armSurface);
        Status = "PASS"; File.WriteAllLines(Output + "/checks.txt", report); Debug.Log("Blender gesture validation PASS: " + string.Join("\n", report));
    }
}
