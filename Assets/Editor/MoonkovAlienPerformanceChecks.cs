using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the cosmetic budget, transformed mesh equivalence and real GPU voxel atomics.</summary>
public static class MoonkovAlienPerformanceChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct Particle
    {
        public Vector3 position; public float padding0;
        public Vector3 velocity; public float padding1;
    }

    private static int assertions;

    [MenuItem("Tools/Moonkov/Performance/Check alien presentation and GPU splats")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit Mode for the isolated fixtures.");
        assertions = 0;
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            CheckDistanceBudget();
            CheckMeshTransforms();
            CheckSpringCapsules();
            CheckGpuSplats();
            return $"Alien performance: PASS ({assertions} assertions; near/scope/opt-out, transformed mesh equivalence, GPU accumulation/clear).";
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CheckDistanceBudget()
    {
        var camera = new GameObject("Budget fixture camera").AddComponent<Camera>();
        camera.fieldOfView = 45;
        var method = typeof(ProceduralAlienEnemy).GetMethod("PresentationInterval", BindingFlags.Static | BindingFlags.NonPublic);
        float Interval(float distance, float budget = 60, Camera viewer = null) =>
            (float)method.Invoke(null, new object[] { viewer ? viewer : camera, Vector3.forward * distance, budget });
        Require(Interval(60) == 0, "Near rigs must update every frame.");
        Require(Mathf.Approximately(Interval(61), 1f / 30), "Middle rigs must use 30 Hz.");
        Require(Mathf.Approximately(Interval(121), 1f / 15), "Distant rigs must use 15 Hz.");
        Require(Interval(1000, 0) == 0, "Zero budget must disable throttling.");
        camera.fieldOfView = 15;
        Require(Interval(180) == 0, "A magnified distant rig must regain full rate.");
        camera.orthographic = true;
        Require(Interval(1000) == 0, "Orthographic viewers must fail open.");
        Require((float)method.Invoke(null, new object[] { null, Vector3.forward * 1000, 60f }) == 0,
            "A missing camera must fail open.");
        Object.DestroyImmediate(camera.gameObject);
    }

    private static void CheckMeshTransforms()
    {
        var owner = new GameObject("Transformed presentation fixture").transform;
        owner.SetPositionAndRotation(new Vector3(1800, 215, 2200), Quaternion.Euler(13, 47, -9));
        owner.localScale = new Vector3(.7f, 1.3f, .9f);
        var motor = new SurfaceMotor(owner.position, owner.rotation, 0);
        var shape = new CreatureChainSettings();
        var chain = new CreatureChain(0);
        chain.Reset(motor.Current, shape.length);
        chain.Tick(motor.Current, shape, true, .5f, 1f / 60, 0, false);
        var view = new CreatureChainPresentation(owner, 0);
        var branchShape = BranchCreatureSettings.RootsDefaults();
        var branch = new BranchCreatureRig(owner, 0, branchShape, ProceduralMenagerieExperiment.Species.Roots);
        try
        {
            view.Draw(chain, shape, true, .37f, 1f / 60);
            var root = Field<Transform>(view, "m_Root");
            var vertices = Field<Vector3[]>(view, "m_Vertices");
            var normals = Field<Vector3[]>(view, "m_Normals");
            float maxPosition = 0, maxNormal = 0;
            for (int ring = 0; ring < CreatureChain.Rings; ring++)
            {
                Vector3 up = chain.Ups[ring], tangent = chain.Tangents[ring];
                Vector3 right = Vector3.Cross(up, tangent).normalized;
                if (right.sqrMagnitude < .1f) right = root.right;
                up = Vector3.Cross(tangent, right).normalized;
                for (int side = 0; side <= 16; side++)
                {
                    float angle = side / 16f * Mathf.PI * 2;
                    Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle) * shape.verticalScale;
                    Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle) / shape.verticalScale;
                    int index = ring * 17 + side;
                    maxPosition = Mathf.Max(maxPosition, Vector3.Distance(vertices[index], root.InverseTransformPoint(chain.Centres[ring] + radial * chain.Radii[ring])));
                    maxNormal = Mathf.Max(maxNormal, Vector3.Distance(normals[index], root.InverseTransformDirection(normal.normalized)));
                }
            }
            Require(maxPosition < .001f && maxNormal < .00001f, $"Chain transform regression: {maxPosition}, {maxNormal}.");
            const float clock = .37f;
            branch.Tick(motor, branchShape, clock, .5f, .3f, 1f / 60);
            root = Field<Transform>(branch, "m_Root");
            vertices = Field<Vector3[]>(branch, "m_Vertices"); normals = Field<Vector3[]>(branch, "m_Normals");
            maxPosition = maxNormal = 0;
            for (int arm = 0; arm < branch.Branches; arm++) for (int point = 0; point < BranchCreatureRig.Samples; point++)
            {
                int bone = BranchCreatureRig.Index(arm, point);
                for (int side = 0; side <= 12; side++)
                {
                    float angle = side / 12f * Mathf.PI * 2;
                    Vector3 radial = branch.Rotations[bone] * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                    float ripple = 1 + branchShape.ripple * Mathf.Sin(point * .9f + clock * 3 + arm * 1.3f);
                    int vertex = (arm * BranchCreatureRig.Samples + point) * 13 + side;
                    maxPosition = Mathf.Max(maxPosition, Vector3.Distance(vertices[vertex], root.InverseTransformPoint(branch.Points[bone] + radial * branch.Radii[bone] * ripple)));
                    maxNormal = Mathf.Max(maxNormal, Vector3.Distance(normals[vertex], root.InverseTransformDirection(radial.normalized)));
                }
            }
            Require(maxPosition < .001f && maxNormal < .00001f, $"Branch transform regression: {maxPosition}, {maxNormal}.");
        }
        finally { view.Release(); branch.Release(); Object.DestroyImmediate(owner.gameObject); }
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);

    private static void CheckSpringCapsules()
    {
        var owner = new GameObject("Capsule snapshot fixture");
        var spring = owner.AddComponent<SecondaryBoneSpring>();
        var start = new GameObject("Independent body start").transform;
        var end = new GameObject("Independent body end").transform;
        start.SetParent(owner.transform, false); end.SetParent(owner.transform, false);
        start.position = new Vector3(.3f, -1, 0); end.position = new Vector3(.3f, 1, 0);
        var source = new[] { new SecondaryBoneSpring.BoneCapsule { start = start, end = end, radius = .2f } };
        var capture = typeof(SecondaryBoneSpring).GetMethod("CaptureCapsules", BindingFlags.NonPublic | BindingFlags.Instance);
        var resolve = typeof(SecondaryBoneSpring).GetMethod("ResolveCapsuleCollisions", BindingFlags.NonPublic | BindingFlags.Static);
        Vector3 initial = new Vector3(.28f, 0, .1f).normalized * .3f;
        try
        {
            object[] arguments = { source, null }; capture.Invoke(spring, arguments);
            var samples = (Array)arguments[1];
            Require(!(bool)samples.GetValue(0).GetType().GetField("liveQuery").GetValue(samples.GetValue(0)), "Independent body proxies should use a snapshot.");
            Vector3 direction = (Vector3)resolve.Invoke(null, new object[] { Vector3.zero, initial, .3f, samples });
            Require(Mathf.Abs(direction.magnitude - .3f) < .00001f && Vector3.Distance(direction, initial) > .01f, "Cached capsule failed to correct a penetrating tip.");
            start.position += Vector3.right * 2; end.position += Vector3.right * 2;
            capture.Invoke(spring, arguments);
            direction = (Vector3)resolve.Invoke(null, new object[] { Vector3.zero, initial, .3f, arguments[1] });
            Require(direction == initial, "A new frame must refresh a moving body proxy.");
            var bound = Field<System.Collections.Generic.HashSet<Transform>>(spring, "m_BoundBones"); bound.Add(start);
            // Reinitialization rebuilds dependency ownership for a changed skeleton.
            arguments[1] = null; capture.Invoke(spring, arguments); samples = (Array)arguments[1];
            Require((bool)samples.GetValue(0).GetType().GetField("liveQuery").GetValue(samples.GetValue(0)), "A simulated proxy must retain live queries.");
            start.position = new Vector3(.3f, -1, 0); end.position = new Vector3(.3f, 1, 0);
            direction = (Vector3)resolve.Invoke(null, new object[] { Vector3.zero, initial, .3f, samples });
            Require(Vector3.Distance(direction, initial) > .01f, "A simulated proxy must observe writes after the snapshot.");
        }
        finally { Object.DestroyImmediate(owner); }
    }

    private static void CheckGpuSplats()
    {
        const int side = 8, count = 1024;
        var solver = Object.Instantiate(Resources.Load<ComputeShader>("Umbra08Fluid"));
        using var accumulation = new ComputeBuffer(side * side * side, sizeof(uint));
        using var particles = new ComputeBuffer(count, 32);
        using var seeds = new ComputeBuffer(count, 48);
        var descriptor = new RenderTextureDescriptor(side, side)
        { dimension = TextureDimension.Tex3D, volumeDepth = side, graphicsFormat = GraphicsFormat.R32_SFloat, enableRandomWrite = true };
        var a = new RenderTexture(descriptor); var b = new RenderTexture(descriptor);
        try
        {
            Require(a.Create() && b.Create(), "GPU fixture volume allocation failed.");
            foreach (string name in new[] { "Initialize", "Transport", "ClearGrid", "BuildGrid", "Density", "Integrate", "ClearVolume", "Deposit", "ResolveX", "BlurY", "BlurZ" })
                Require(solver.IsSupported(solver.FindKernel(name)), "Unsupported fluid kernel: " + name);
            particles.SetData(new Particle[count]); seeds.SetData(new SkeletonFluidPresentation.Seed[count]);
            solver.SetInt("_VolumeSide", side); solver.SetInt("_ParticleCount", count);
            solver.SetFloat("_HalfVolume", 2.8f); solver.SetFloat("_Predatory", 0);
            int clear = solver.FindKernel("ClearVolume"), deposit = solver.FindKernel("Deposit"), resolve = solver.FindKernel("ResolveX"), y = solver.FindKernel("BlurY"), z = solver.FindKernel("BlurZ");
            foreach (int kernel in new[] { clear, deposit, resolve }) solver.SetBuffer(kernel, "_Accum", accumulation);
            solver.SetBuffer(deposit, "_Input", particles); solver.SetBuffer(deposit, "_Seeds", seeds);
            solver.SetTexture(resolve, "_WriteVolume", a);
            solver.SetTexture(y, "_ReadVolume", a); solver.SetTexture(y, "_WriteVolume", b);
            solver.SetTexture(z, "_ReadVolume", b); solver.SetTexture(z, "_WriteVolume", a);
            solver.Dispatch(clear, 2, 2, 2); solver.Dispatch(deposit, count / 64, 1, 1);
            var raw = new uint[side * side * side]; accumulation.GetData(raw);
            double rawMass = 0; foreach (uint value in raw) rawMass += value / 65536.0;
            Require(Math.Abs(rawMass - count) < .001, "Atomic voxel accumulation lost mass: " + rawMass);
            Resolve();
            var read = AsyncGPUReadback.Request(a); read.WaitForCompletion();
            Require(!read.hasError, "GPU density readback failed.");
            float total = 0;
            for (int layer = 0; layer < read.layerCount; layer++)
                foreach (float value in read.GetData<float>(layer))
                { Require(float.IsFinite(value) && value >= 0, "Non-finite/negative density."); total += value; }
            Require(Mathf.Abs(total - count) < .001f, "Atomic splats lost mass: " + total);
            Require(Mathf.Abs(read.GetData<float>(3)[3 * side + 3] - count * .052734375f) < .0001f, "Voxel layout/blur differs from the reference.");
            solver.Dispatch(clear, 2, 2, 2); Resolve();
            read = AsyncGPUReadback.Request(a); read.WaitForCompletion();
            Require(!read.hasError, "GPU clear readback failed.");
            for (int layer = 0; layer < read.layerCount; layer++)
                foreach (float value in read.GetData<float>(layer)) Require(value == 0, "ClearVolume retained a previous splat.");
            void Resolve() { solver.Dispatch(resolve, 2, 2, 2); solver.Dispatch(y, 2, 2, 2); solver.Dispatch(z, 2, 2, 2); }
        }
        finally { a.Release(); b.Release(); Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(solver); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
