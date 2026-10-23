using System;
using System.IO;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class AlienFluidAlignmentChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check fluid collision alignment")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var root = new GameObject("Temporary fluid alignment fixture");
        root.transform.position = new Vector3(-9200, 1000, -9200);
        var obstacle = new GameObject("COL_disconnected_mesh_fixture");
        obstacle.transform.position = root.transform.position;
        Mesh mesh = null;
        SkeletonFluidPresentation fluid = null;
        try
        {
            // Two real side walls, with empty walkable space inside their combined AABB.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = cube.GetComponent<MeshFilter>().sharedMesh;
            mesh = new Mesh();
            mesh.CombineMeshes(new[] {
                new CombineInstance { mesh = source, transform = Matrix4x4.TRS(Vector3.left * 2, Quaternion.identity, new Vector3(1, 3, 4)) },
                new CombineInstance { mesh = source, transform = Matrix4x4.TRS(Vector3.right * 2, Quaternion.identity, new Vector3(1, 3, 4)) }
            });
            Object.DestroyImmediate(cube);
            obstacle.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms();
            var seeds = new SkeletonFluidPresentation.Seed[27]; int n = 0;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                seeds[n++] = new SkeletonFluidPresentation.Seed { rest = new Vector4(x * .04f, y * .04f, z * .04f, 1), weights = new Vector4(1, 0, 0, 0) };
            fluid = new SkeletonFluidPresentation(root.transform, 1, seeds, 1, 2.8f, 32, false);
            fluid.Settings.mantleThickness = fluid.Settings.rootThickness = fluid.Settings.kneeThickness = fluid.Settings.toeThickness = 1;
            fluid.Settings.localThicknessVariation = fluid.Settings.legAsymmetry = 0;
            var bones = new[] { Matrix4x4.identity }; var centres = new[] { new Vector4(0, 0, 0, 1) };
            for (int i = 0; i < 30; i++) fluid.Draw(bones, centres, Vector3.zero, 1f / 60);
            var hollow = fluid.SampleParticles();
            Require(hollow.nonFinite == 0 && hollow.maxGuideDistance < .2f, "Mesh AABB expelled fluid from empty space.");
            Object.DestroyImmediate(obstacle.GetComponent<MeshCollider>());
            obstacle.AddComponent<BoxCollider>().size = new Vector3(4, 3, 4);
            Physics.SyncTransforms();
            // Even an invalid guide inside a real obstacle cannot leave the gameplay body.
            fluid.GuideDistanceLimit = .12f;
            for (int i = 0; i < 30; i++) fluid.Draw(bones, centres, Vector3.zero, 1f / 60);
            var trapped = fluid.SampleParticles();
            Require(trapped.nonFinite == 0 && trapped.maxGuideDistance <= .1201f, "Blocked tissue escaped its world-space limit.");
            // A real floor still supports gravity-driven tissue, without the gameplay leash.
            fluid.GuideDistanceLimit = 0;
            obstacle.transform.position = root.transform.position - Vector3.up * .3f;
            obstacle.GetComponent<BoxCollider>().size = new Vector3(4, .2f, 4);
            Physics.SyncTransforms(); fluid.SetVisible(false); fluid.SetVisible(true);
            for (int i = 0; i < 60; i++) fluid.Draw(bones, centres, Vector3.zero, 1f / 60);
            var floor = fluid.SampleParticles();
            Require(floor.nonFinite == 0 && floor.maxGuideDistance < .3f, "Real floor contact lost fluid alignment.");
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllText("Library/BuildValidation/AlienIntegration/fluid-alignment.txt",
                "Hollow mesh: " + JsonUtility.ToJson(hollow) + "\nBlocked: " + JsonUtility.ToJson(trapped) + "\nFloor: " + JsonUtility.ToJson(floor));
            Debug.Log("Fluid alignment checks passed: hollow mesh bounds, blocked guide, real floor.");
        }
        finally { fluid?.Release(); Object.DestroyImmediate(obstacle); Object.DestroyImmediate(root); if (mesh) Object.DestroyImmediate(mesh); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
