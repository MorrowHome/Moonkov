using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.MP_FPS.DollSinger;
using UnityEditor;

// Run through Pipeline run_script outside Play. Uses the production-prefab fixture.
public static class GestureReadabilityChecks
{
    private const string Output = "LocalData/GestureReadability";
    private static bool TouchesReticle(Vector3 a, Vector3 b, Vector3 c)
    {
        if (a.z <= .015f || b.z <= .015f || c.z <= .015f) return false;
        if (Mathf.Min(a.x, b.x, c.x) > .505f || Mathf.Max(a.x, b.x, c.x) < .495f ||
            Mathf.Min(a.y, b.y, c.y) > .505f || Mathf.Max(a.y, b.y, c.y) < .495f) return false;
        // The tiny centre window is sampled at its centre and four corners.
        foreach (var p in ReticlePoints)
        {
            float x = Cross(b - a, p - (Vector2)a), y = Cross(c - b, p - (Vector2)b), z = Cross(a - c, p - (Vector2)c);
            if (x >= 0 && y >= 0 && z >= 0 || x <= 0 && y <= 0 && z <= 0) return true;
        }
        return false;
    }
    private static readonly Vector2[] ReticlePoints = { new Vector2(.495f,.495f), new Vector2(.505f,.495f),
        new Vector2(.495f,.505f), new Vector2(.505f,.505f), new Vector2(.5f,.5f) };
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    public static object Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play.");
        var rows = new List<string>(); var failures = new List<string>();
        int samples = 0;
        var type = typeof(WeaponFramingChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
        foreach (var rig in new[] { "DollSinger", "HatsuneMiku", "Shian" })
        using (var fixture = (IDisposable)Activator.CreateInstance(type, new object[] { rig }))
        {
            T Get<T>(string name) => (T)type.GetField(name).GetValue(fixture);
            var view = Get<DollSingerView>("View"); var gestures = Get<DollSingerBakedGestures>("Gestures");
            var bones = Get<Dictionary<string, Transform>>("Bones");
            var body = Get<SkinnedMeshRenderer>("Body"); var animator = Get<Animator>("Animator");
            var hands = new[] { animator.GetBoneTransform(HumanBodyBones.LeftHand), animator.GetBoneTransform(HumanBodyBones.RightHand) };
            var arms = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), animator.GetBoneTransform(HumanBodyBones.RightUpperArm) };
            var weights = body.sharedMesh.boneWeights; var palette = body.bones;
            bool Under(int i, Transform[] roots) => roots.Any(h => palette[i] && palette[i].IsChildOf(h));
            float Weight(BoneWeight w, Transform[] roots) => (Under(w.boneIndex0, roots) ? w.weight0 : 0) +
                (Under(w.boneIndex1, roots) ? w.weight1 : 0) + (Under(w.boneIndex2, roots) ? w.weight2 : 0) + (Under(w.boneIndex3, roots) ? w.weight3 : 0);
            var handVertices = Enumerable.Range(0, weights.Length).Where(i => Weight(weights[i], hands) > .5f).ToArray();
            var armVertices = new HashSet<int>(Enumerable.Range(0, weights.Length).Where(i => Weight(weights[i], arms) > .5f));
            var source = body.sharedMesh.triangles; var triangles = new List<int>();
            for (int i = 0; i < source.Length; i += 3)
                if (armVertices.Contains(source[i]) || armVertices.Contains(source[i + 1]) || armVertices.Contains(source[i + 2]))
                { triangles.Add(source[i]); triangles.Add(source[i + 1]); triangles.Add(source[i + 2]); }
            var used = triangles.Distinct().ToArray();
            var mesh = new Mesh();
            try
            {
                foreach (float pitch in new[] { -80f, -65f, 0f, 65f, 80f })
                for (int weapon = 1; weapon <= 13; weapon++)
                foreach (int sample in Enumerable.Range(0, 18))
                {
                    type.GetMethod("Pose").Invoke(fixture, new object[] { pitch });
                    var world = view.camera; world.enabled = true;
                    Vector3 eye = world.transform.position; Quaternion look = world.transform.rotation;
                    Matrix4x4 optics = world.projectionMatrix;
                    gestures.Apply(weapon, 1, sample == 0 ? 10f : (sample - 1) * .04f,
                        sample >= 7, (sample - 7) * .1f, .01f, bones["上半身2"], world.transform.forward, 1, eye: world);
                    if (weapon == 4) gestures.AlignViewfinder(world, 1);
                    if (world.transform.position != eye || world.transform.rotation != look || world.projectionMatrix != optics)
                        throw new Exception("Gesture moved world optics");
                    body.BakeMesh(mesh, true); var points = mesh.vertices; var projected = new Vector3[points.Length];
                    foreach (float aspect in new[] { 4f / 3, 16f / 9 })
                    {
                        world.aspect = aspect; view.PresentGesture(1);
                        var camera = view.GestureProjectionCamera;
                        if (!view.GestureProjectionActive || camera.transform.position != eye || camera.transform.rotation != look ||
                            camera.GetUniversalAdditionalCameraData().clearDepth || camera.nearClipPlane != world.nearClipPlane || camera.farClipPlane != world.farClipPlane)
                            throw new Exception("Gesture projection broke eye/depth contract");
                        var min = Vector3.one * float.PositiveInfinity; var max = Vector3.one * float.NegativeInfinity;
                        foreach (int i in used) projected[i] = camera.WorldToViewportPoint(body.transform.TransformPoint(points[i]));
                        foreach (int i in handVertices) { min = Vector3.Min(min, projected[i]); max = Vector3.Max(max, projected[i]); }
                        bool cropped = min.x < .01f || max.x > .99f || min.y < .01f || max.y > .99f || min.z < camera.nearClipPlane;
                        bool blocked = false;
                        for (int j = 0; j < triangles.Count; j += 3)
                            if (TouchesReticle(projected[triangles[j]], projected[triangles[j + 1]], projected[triangles[j + 2]])) { blocked = true; break; }
                        string row = $"{rig} weapon={weapon} pitch={pitch} sample={sample} aspect={aspect:F3} min={min:F3} max={max:F3} cropped={cropped} blocked={blocked}";
                        rows.Add(row); samples++;
                        if (cropped || blocked) failures.Add(row);
                        if (sample == 0)
                        {
                            if (weapon == 1 && pitch == 0 && Mathf.Abs((min.x + max.x) * .5f - .5f) > .04f)
                                throw new Exception("Rifle gesture moved away from horizontal centre");
                            // Optical zoom can change the world, never the settled hand projection.
                            Matrix4x4 gesture = camera.projectionMatrix;
                            foreach (float zoom in new[] { 1f, 6f, 12f })
                            {
                                world.fieldOfView = 2 * Mathf.Atan(Mathf.Tan(75 * .5f * Mathf.Deg2Rad) / zoom) * Mathf.Rad2Deg;
                                view.PresentGesture(1);
                                if (camera.projectionMatrix != gesture) throw new Exception("Zoom resized the gesture");
                            }
                        }
                    }
                }
                view.PresentGesture(0);
                if (view.GestureProjectionActive || view.camera.GetUniversalAdditionalCameraData().cameraStack.Contains(view.GestureProjectionCamera))
                    throw new Exception("Gesture projection survived aim-out");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        Directory.CreateDirectory(Output);
        File.WriteAllLines(Output + "/geometry.txt", rows); File.WriteAllLines(Output + "/failures.txt", failures);
        if (failures.Count > 0) throw new Exception($"{failures.Count}/{samples} gesture samples failed; see {Output}/failures.txt");
        return new { samples, failures = failures.Count, examples = failures.Take(16).ToArray() };
    }
}
