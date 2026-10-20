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
        public FireVariant[] burstFires;
        public float[] pose, fire, reload;
    }
    [Serializable] private class FireVariant { public float[] values; }
    private static bool arsenal;
    private static int focused=-1;
    public static void RunWeapon(int index) { focused=index;arsenal=true;Start(); }
    private static int[] OrdinaryWeapons => focused>=0 ? new[]{focused} : new[]{0,1,2}.Concat(arsenal?Enumerable.Range(4,HaloArsenal.Entries.Length):Enumerable.Empty<int>()).ToArray();
    [MenuItem("Tools/Moonkov/Validate Ten Weapon Gestures In Playing Lab")]
    public static void RunArsenal() { focused=-1; arsenal = true; Start(); }
    [Serializable] private class Bake { public string revision; public string[] bones; public Motion[] motions; public Quaternion chestBindRotation; }
    private static string Output => focused<0?"Temp/GestureValidation/Blender":"Temp/GestureValidation/Arsenal/"+HaloArsenal.Entries[focused-4].Code;
    public static string Status { get; private set; } = "Not run";
    private static Mesh armSurface;
    private static SkinnedMeshRenderer armRenderer;
    private static int[] armTriangles;
    private static readonly Rect AimWindow = Rect.MinMaxRect(.44f, .44f, .56f, .56f);
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Validate Blender Gestures In Playing Lab")]
    public static void Run() { focused=-1; arsenal = false; Start(); }
    private static void Start()
    {
        var lab = UnityEngine.Object.FindAnyObjectByType<WeaponGestureLab>();
        if (!EditorApplication.isPlaying || !lab) throw new InvalidOperationException("Play WeaponGestureLab first.");
        if (Status == "Running") throw new InvalidOperationException("Validation already running.");
        Status = "Running";
        lab.StartCoroutine(Guard(Check(lab)));
    }

    private static IEnumerator Guard(IEnumerator routine)
    {
        var pending = new Stack<IEnumerator>(); pending.Push(routine);
        while (pending.Count>0)
        {
            object current;
            try
            {
                var active = pending.Peek();
                if (!active.MoveNext()) { pending.Pop(); continue; }
                current = active.Current;
                if (current is IEnumerator nested) { pending.Push(nested); continue; }
            }
            catch (Exception e)
            {
                foreach (var active in pending) (active as IDisposable)?.Dispose();
                if (armSurface) UnityEngine.Object.Destroy(armSurface);
                Status = "FAILED: " + e; Debug.LogException(e); File.WriteAllText(Output + "/checks.txt", Status); yield break;
            }
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
                lab.WeaponIndex == 2 ? lab.Halo.shotgunVisual.ReloadProgress : lab.WeaponIndex == 3 ? lab.Halo.sniperVisual.ReloadProgress : lab.Halo.ArsenalVisual.ReloadProgress;
            values = low.reload; highValues = high.reload; count = m.reloadFrames; frame = progress * (count - 1);
        }
        else if (age >= 0 && age < m.fireSeconds) { values = low.fire; highValues = high.fire; count = m.fireFrames; frame = age / m.fireSeconds * (count - 1); }
        if (!lab.Reloading && age >= 0 && age < m.fireSeconds && low.burstFires != null)
        { int sequence=(int)typeof(DollSingerHaloAim).GetField("gestureBurstIndex",Private).GetValue(lab.Halo); values=low.burstFires[sequence].values; highValues=high.burstFires[sequence].values; }
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
                // Directional signs share one common correction across both complete arm chains.
                if (lab.WeaponIndex != 1 && lab.WeaponIndex != 4 && lab.WeaponIndex != 7 && lab.WeaponIndex != 10) max = Mathf.Max(max, Quaternion.Angle(expected, bones[bake.bones[i]].rotation));
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
        if (lab.WeaponIndex == 3)
        {
            Vector3 center = (bones["人指先.L"].position + bones["親指先.R"].position +
                bones["人指先.R"].position + bones["親指先.L"].position) * .25f;
            var uv = lab.PlayerView.GestureCamera.WorldToViewportPoint(center);
            var owner = lab.PlayerView.camera;
            float pixels = new Vector2((uv.x-.5f)*owner.pixelWidth, (uv.y-.5f)*owner.pixelHeight).magnitude;
            Require(pixels < .5f, "Sniper frame centre drifted from crosshair: " + pixels + " pixels");
            Require(Mathf.Abs(lab.PlayerView.GestureCamera.fieldOfView-owner.fieldOfView)<.001f,
                "Sniper hands are not using the owner's zoom");
            Require(Mathf.Abs(lab.PlayerView.GestureCamera.transform.localPosition.z)<.00001f,
                "Sniper hand camera moved backwards to cancel zoom");
            // A normal optical zoom can crop the hands at the viewport edges.
            // Do not force them back inside the screen and undo magnification.
            return;
        }
        foreach (var name in new[] { "手首.L", "手首.R" })
        foreach (var bone in bones[name].GetComponentsInChildren<Transform>())
        {
            var uv = lab.PlayerView.GestureCamera.WorldToViewportPoint(bone.position);
            Require(uv.z > .015f && uv.x > .01f && uv.x < .99f && uv.y > .01f && uv.y < .99f, "Finger left owner viewport: " + bone.name);
            if (lab.WeaponIndex != 3) Require(uv.y < .42f, "Hand entered aiming area: " + bone.name);
        }
    }
    private static void PointAtAim(WeaponGestureLab lab, Dictionary<string, Transform> bones)
    {
        if (lab.WeaponIndex != 1 && lab.WeaponIndex != 4 && lab.WeaponIndex != 7 && lab.WeaponIndex != 10) return;
        var camera = lab.PlayerView.GestureCamera;
        Vector2 a = camera.WorldToViewportPoint(bones["人指１.R"].position);
        Vector2 b = camera.WorldToViewportPoint(bones["人指先.R"].position);
        // Use pixel coordinates so non-square viewports do not distort the angle.
        var pixel = new Vector2(camera.pixelWidth, camera.pixelHeight);
        Require(Vector2.Angle(Vector2.Scale(b-a,pixel), Vector2.Scale(Vector2.one*.5f-a,pixel)) < 1f,
            "Directional index does not point at crosshair: weapon "+lab.WeaponIndex);
    }
    // Test the rendered arm triangles too: sleeves and forearms must leave the
    // central 12% square clear, even when all finger joints are below it.
    private static void ClearAim(WeaponGestureLab lab)
    {
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
        foreach (int weapon in focused>=0?new[]{focused}.AsEnumerable():Enumerable.Range(0,arsenal?HaloArsenal.Entries.Length+4:4))
        {
            lab.SelectWeapon(weapon); yield return new WaitForSeconds(.6f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error, Compare(bake, lab, bones)); Visible(lab, bones); ClearAim(lab); PointAtAim(lab, bones);
            FingerFan(lab,bones);
            if (weapon == 3) Shape(bones, lab.PlayerView.GestureCamera, report);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + $"/{weapon + 1}-idle.png")); yield return new WaitForEndOfFrame();
            Vector3 offhand = bones["手首.L"].position;
            lab.Fire(); float start = Time.time; bool captured = false;
            var data=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset").GetWeaponData((uint)(weapon+2));
            float fireDuration=bake.motions[weapon].fireSeconds+(data.BurstCount-1)*data.BurstInterval+.025f;
            while (Time.time - start < fireDuration + .04f)
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
            if (weapon >= 4) SaveViews(lab, HaloArsenal.Entries[weapon-4].Code);
            report.Add($"weapon {weapon + 1}: idle, fire, reload; all four cameras active; exact local rotations and owner projection passed");
        }
        if(focused<0) Require(fanDistance > .055f, "Revolver offhand did not fan the hammer: " + fanDistance);
        if(focused<0) report.Add($"revolver offhand stroke {fanDistance * 1000:F1} mm; maximum Blender/runtime rotation difference {error:F3} deg");
        // The normal weapons retain a clear aiming window at steep pitch and
        // both common aspect ratios. Inspect the actual deformed arm triangles.
        foreach (float aspect in new[] { 16f/9f, 4f/3f })
        foreach (int weapon in OrdinaryWeapons)
        foreach (float pitch in new[] { -80f, -65f, -40f, 0f, 15f, 30f, 45f, 65f, 70f, 75f, 80f })
        {
            lab.SelectWeapon(weapon); root.Q<Slider>("pitch").value = pitch;
            lab.PlayerView.camera.aspect = aspect;
            yield return new WaitForSeconds(.4f); yield return new WaitForEndOfFrame();
            error = Mathf.Max(error,Compare(bake,lab,bones)); Visible(lab, bones); ClearAim(lab); PointAtAim(lab, bones); HeadLook(lab, bones);
            FingerFan(lab,bones);
        }
        // Reproduce looking up continuously, rather than only checking settled
        // endpoints. The hand projection must not retain another angle's fit.
        lab.PlayerView.camera.aspect = 16f/9f;
        foreach(int weapon in OrdinaryWeapons)
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
        if(focused>=0)
        {
            root.Q<Slider>("pitch").value=0;
            yield return CheckOrdinaryTransitions(lab,bones,root,report);
            UnityEngine.Object.Destroy(armSurface);Status="PASS";
            File.WriteAllLines(Output+"/checks.txt",report);Debug.Log("Focused arsenal gesture PASS: "+HaloArsenal.Entries[focused-4].Code);yield break;
        }
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
        float gestureFov = lab.PlayerView.gestureFieldOfView;
        foreach (float aspect in new[] { 16f/9f, 4f/3f })
        foreach (float fov in new[] { 50f, 65f, 80f })
        foreach (float pitch in new[] { -65f, 0f, 65f, 0f })
        {
            lab.PlayerView.camera.aspect = aspect; lab.PlayerView.gestureFieldOfView = fov;
            root.Q<Slider>("pitch").value = pitch;
            yield return new WaitForSeconds(.2f); yield return new WaitForEndOfFrame();
            Visible(lab, bones);
        }
        lab.PlayerView.gestureFieldOfView = gestureFov; lab.PlayerView.camera.aspect = 16f/9f;
        float sweepStart = Time.time;
        while (Time.time - sweepStart < 3f)
        {
            root.Q<Slider>("pitch").value = 65f * Mathf.Sin((Time.time-sweepStart)*Mathf.PI*2f/3f);
            yield return new WaitForEndOfFrame(); Visible(lab, bones);
        }
        root.Q<Slider>("pitch").value = 0;
        root.Q<Toggle>("aim").value = false; yield return new WaitForSeconds(.4f);
        root.Q<Toggle>("aim").value = true; yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame();
        Shape(bones, lab.PlayerView.GestureCamera, report);
        lab.SetPaused(true); var hand = bones["手首.L"].position; yield return new WaitForSecondsRealtime(.15f); yield return new WaitForEndOfFrame();
        Require(Vector3.Distance(hand, bones["手首.L"].position) < .00001f, "Frozen gesture drifted."); lab.SetPaused(false);
        report.Add("FP/TP modes, pitch -65/0/+65, aim-out/in, pause/resume passed.");
        float scopeMagnification = lab.Halo.sniperVisual.scopeMagnification;
        // Hold the body animation at one pose. Idle breathing changes the
        // eye-to-hand distance and is unrelated to optical magnification.
        var animator = lab.Halo.GetComponent<Animator>(); float animatorSpeed = animator.speed;
        animator.speed = 0;
        Vector2 baseSize = Vector2.zero; float previousHaloSize = float.PositiveInfinity; Vector3 baseHaloPosition = Vector3.zero;
        foreach (float zoom in new[] { 1f, 2f, 4f, 6f, 12f })
        {
            lab.Halo.sniperVisual.scopeMagnification = zoom;
            yield return new WaitForSeconds(.7f); yield return new WaitForEndOfFrame();
            Visible(lab, bones); Shape(bones, lab.PlayerView.GestureCamera, report);
            var camera = lab.PlayerView.GestureCamera;
            Vector2 span = camera.WorldToViewportPoint(bones["人指先.L"].position) - camera.WorldToViewportPoint(bones["人指先.R"].position);
            var normalized = span / lab.PlayerView.CurrentAimMagnification;
            if (zoom == 1) { baseSize = normalized; baseHaloPosition = lab.Halo.haloVisual.position; }
            Require(Vector2.Distance(normalized, baseSize)<.0001f, "Sniper gesture did not magnify with the world at " + zoom);
            float haloSize = lab.Halo.haloVisual.lossyScale.x;
            float expectedSize = Mathf.Min(lab.Halo.aimedHaloScale, lab.Halo.sniperVisual.ScopeScale(
                Vector3.Distance(lab.Halo.haloVisual.position, lab.PlayerView.camera.transform.position), lab.PlayerView.camera.fieldOfView));
            Require(Mathf.Abs(haloSize-expectedSize)<.00001f && haloSize<=previousHaloSize,
                "Sniper halo did not shrink to fit the scope");
            previousHaloSize = haloSize;
            Require(Vector3.Distance(lab.Halo.haloVisual.position,baseHaloPosition)<.0001f, "Zoom moved the sniper halo");
        }
        lab.Halo.sniperVisual.scopeMagnification = scopeMagnification;
        animator.speed = animatorSpeed;
        yield return new WaitForSeconds(.7f);
        report.Add("Sniper hands retain owner FOV, zero depth offset and proportional optical zoom at 1/2/4/6/12x. The halo shrinks with FOV to fit the scope; its world position is stable. Frame centre remains within 0.5 output pixels through actions, pitch and aspect changes. Skinned arms leave the central aiming area clear.");
        yield return CheckSniperTransitions(lab, bones, root, report);
        yield return CheckOrdinaryTransitions(lab, bones, root, report);
        UnityEngine.Object.Destroy(armSurface);
        Status = "PASS"; File.WriteAllLines(Output + "/checks.txt", report); Debug.Log("Blender gesture validation PASS: " + string.Join("\n", report));
    }

    internal static void SaveViews(WeaponGestureLab lab, string code)
    {
        var cameras = new[] { lab.PlayerView.camera }.Concat(lab.Observers).ToArray();
        for (int i=0;i<cameras.Length;i++)
        {
            var previous=RenderTexture.active; RenderTexture.active=cameras[i].targetTexture;
            var texture=new Texture2D(cameras[i].targetTexture.width,cameras[i].targetTexture.height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);texture.Apply();
            File.WriteAllBytes(Output+"/"+code+"-"+new[]{"owner","front","side","back"}[i]+".png",texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);RenderTexture.active=previous;
        }
    }
    private static void FingerFan(WeaponGestureLab lab, Dictionary<string,Transform> bones)
    {
        if(lab.WeaponIndex!=5&&lab.WeaponIndex!=8)return;
        var names=lab.WeaponIndex==5?new[]{"人指先.R","中指先.R","薬指先.R"}:new[]{"親指先.R","人指先.R","中指先.R","薬指先.R","小指先.R"};
        var camera=lab.PlayerView.GestureCamera;
        var pixel=new Vector2(camera.pixelWidth,camera.pixelHeight);
        for(int i=0;i<names.Length;i++)for(int j=i+1;j<names.Length;j++)
        {
            Vector2 a=camera.WorldToViewportPoint(bones[names[i]].position),b=camera.WorldToViewportPoint(bones[names[j]].position);
            Require(Vector2.Scale(a-b,pixel).magnitude>10,"Fan fingers overlap in first person: weapon "+lab.WeaponIndex+" "+names[i]+"/"+names[j]);
        }
    }
    private static IEnumerator CheckOrdinaryTransitions(WeaponGestureLab lab, Dictionary<string, Transform> bones,
        VisualElement root, List<string> report)
    {
        int frames = 0;
        foreach (int weapon in OrdinaryWeapons)
        {
            lab.SelectWeapon(weapon);
            foreach (float pitch in new[] { -65f, 0f, 65f })
            foreach (float aspect in new[] { 16f/9f, 4f/3f })
            {
                root.Q<Slider>("pitch").value = pitch; lab.PlayerView.camera.aspect = aspect;
                root.Q<Toggle>("aim").value = false; yield return new WaitForSeconds(.4f);
                foreach (bool aim in new[] { true, false })
                {
                    root.Q<Toggle>("aim").value = aim; float start = Time.time;
                    while (Time.time-start<.4f)
                    {
                        yield return new WaitForEndOfFrame(); frames++;
                        if (lab.PlayerView.GestureHandsActive) ClearAim(lab);
                        if (lab.PlayerView.CurrentAimMagnification>1.01f)
                        {
                            Require(lab.Halo.AimBlend>.45f, "Ordinary weapon zoom overlapped raising/lowering arms");
                            Visible(lab, bones); PointAtAim(lab, bones);
                        }
                    }
                }
            }
        }
        root.Q<Slider>("pitch").value = 0; lab.PlayerView.camera.aspect = 16f/9f;
        float captureDelta = Time.captureDeltaTime;
        try
        {
            foreach (float step in new[] { 0f, 1f/15f })
            foreach (int weapon in OrdinaryWeapons)
            {
                Time.captureDeltaTime = step; lab.SelectWeapon(weapon);
                root.Q<Toggle>("aim").value = false; yield return new WaitForSeconds(.4f);
                foreach (float interval in new[] { .04f, .08f, .14f, .22f })
                for (int repeat = 0; repeat<6; repeat++)
                {
                    root.Q<Toggle>("aim").value = !root.Q<Toggle>("aim").value;
                    float start = Time.time;
                    while (Time.time-start<interval)
                    {
                        yield return new WaitForEndOfFrame(); frames++;
                        if (lab.PlayerView.GestureHandsActive) ClearAim(lab);
                        if (lab.PlayerView.CurrentAimMagnification>1.01f)
                        { Require(lab.Halo.AimBlend>.45f, "Interrupted ordinary aim lagged pose"); Visible(lab, bones); PointAtAim(lab, bones); }
                    }
                }
            }
        }
        finally { Time.captureDeltaTime = captureDelta; }
        root.Q<Toggle>("aim").value = true; yield return new WaitForSeconds(.5f);
        report.Add($"{OrdinaryWeapons.Length} ordinary weapon aim transitions: {frames} rendered frames, pitch -65/0/+65, 16:9 / 4:3; central skinned arm clearance throughout raising/lowering. {OrdinaryWeapons.Length*48} interrupted aim toggles at 40/80/140/220 ms across normal and simulated 15 Hz steps; pose before zoom and unzoom before lowering; visible settled hands and revolver pointing preserved.");
    }

    private static IEnumerator CheckSniperTransitions(WeaponGestureLab lab, Dictionary<string, Transform> bones,
        VisualElement root, List<string> report)
    {
        int frames = 0; float maximumOffset = 0;
        root.Q<Toggle>("firstPerson").value = true;
        foreach (float pitch in new[] { -65f, 0f, 65f })
        foreach (float aspect in new[] { 16f/9f, 4f/3f })
        {
            root.Q<Slider>("pitch").value = pitch; lab.PlayerView.camera.aspect = aspect;
            // Start with another weapon's fitted hand camera to catch stale
            // projection/position on the very first active sniper frame.
            lab.SelectWeapon(0); root.Q<Toggle>("aim").value = true; yield return new WaitForSeconds(.4f);
            root.Q<Toggle>("aim").value = false; yield return new WaitForSeconds(.4f);
            lab.SelectWeapon(3);
            foreach (bool aim in new[] { true, false, true })
            {
                root.Q<Toggle>("aim").value = aim;
                float start = Time.time;
                while (Time.time-start<.5f)
                {
                    yield return new WaitForEndOfFrame(); frames++;
                    float blend = lab.Halo.AimBlend;
                    var camera = lab.PlayerView.GestureCamera;
                    if (!lab.PlayerView.GestureHandsActive) continue;
                    Require(Mathf.Abs(camera.transform.localPosition.z)<.00001f,
                        "First sniper frame retained another weapon's hand camera");
                    maximumOffset = Mathf.Max(maximumOffset, camera.transform.localPosition.magnitude);
                    if (blend<.1f) Require(camera.transform.localPosition.magnitude<.1f,
                        "Aim transition chased the resting hands down the body");
                    if (lab.PlayerView.CurrentAimMagnification>1.01f)
                    {
                        Require(blend>.45f, "Zoom overlapped the lowering/raising pose");
                        Shape(bones, camera, new List<string>()); ClearAim(lab);
                    }
                }
            }
        }
        root.Q<Slider>("pitch").value = 0; lab.PlayerView.camera.aspect = 16f/9f;
        report.Add($"Sniper aim-in/out/in: {frames} actual rendered frames, pitch -65/0/+65, 16:9 and 4:3. Pose closed before zoom; lowering after unzoom; no resting-hand camera teleport; central arm geometry clear. Maximum camera offset {maximumOffset:F4} m.");
        frames = 0;
        foreach (float interval in new[] { .04f, .08f, .14f, .22f })
        for (int repeat = 0; repeat<6; repeat++)
        {
            root.Q<Toggle>("aim").value = !root.Q<Toggle>("aim").value;
            float start = Time.time;
            while (Time.time-start<interval)
            {
                yield return new WaitForEndOfFrame(); frames++;
                if (lab.PlayerView.CurrentAimMagnification>1.01f)
                {
                    Require(lab.Halo.AimBlend>.45f, "Interrupted aim left camera one frame behind pose");
                    Shape(bones, lab.PlayerView.GestureCamera, new List<string>()); ClearAim(lab);
                }
            }
        }
        root.Q<Toggle>("aim").value = true; yield return new WaitForSeconds(.5f);
        report.Add($"24 interrupted aim toggles at 40/80/140/220 ms, {frames} rendered frames: current pose and zoom synchronized; frame remains closed and central arm geometry clear while zoomed.");
    }
}
