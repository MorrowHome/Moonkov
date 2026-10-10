using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.DollSinger;

public static class MoonkovCameraCollisionChecks
{
    [MenuItem("Tools/Moonkov/Check Camera Collision (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Enter Play mode before running camera collision checks.");
        var scene = SceneManager.CreateScene("Camera collision checks",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = scene.GetPhysicsScene();
        var holder = new GameObject("Camera collision checks");
        SceneManager.MoveGameObjectToScene(holder, scene);
        TerrainData terrainData = null;
        Mesh slopeMesh = null;
        using (var collision = new DollSingerCameraCollision())
        try
        {
            var owner = new GameObject("Owner");
            owner.transform.SetParent(holder.transform);
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(holder.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.3f;
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            float radius = collision.GetRadius(camera);
            Vector3 anchor = Vector3.up * 1.5f;
            Vector3 desired = Quaternion.Euler(-80f, 0f, 0f) * new Vector3(0f, 2.4f, -4f);
            int layers = ~LayerMask.GetMask("ServerPlayer");

            Require(desired.y < -3f, "Fixture no longer reproduces the reported camera below terrain.");
            RequireClose(collision.Constrain(physics, anchor, desired, camera, owner.transform, layers),
                desired, "An unobstructed camera changed position.");

            // Fill more than the initial query capacity with owner colliders. They must
            // neither shorten the boom nor hide an actual world hit in an unordered query.
            for (int i = 0; i < 40; i++)
                Box(owner.transform, "Owner collider", Vector3.Lerp(anchor, desired, (i + 1f) / 41f), Vector3.one * 0.1f);
            var trigger = Box(holder.transform, "Trigger", Vector3.Lerp(anchor, desired, 0.4f), Vector3.one);
            trigger.isTrigger = true;
            int serverLayer = LayerMask.NameToLayer("ServerPlayer");
            Require(serverLayer >= 0, "ServerPlayer layer is missing.");
            var server = Box(holder.transform, "Server duplicate", Vector3.Lerp(anchor, desired, 0.6f), Vector3.one);
            server.gameObject.layer = serverLayer;
            Physics.SyncTransforms();
            RequireClose(collision.Constrain(physics, anchor, desired, camera, owner.transform, layers),
                desired, "Owner, trigger, or server duplicate blocked the camera.");

            var ground = Box(holder.transform, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            Physics.SyncTransforms();
            CheckAboveGround(collision, physics, camera, owner.transform, layers, anchor, desired, radius);
            // Local controls allow -30 degrees and zoom out to six metres.
            Vector3 localZoom = Quaternion.Euler(-30f, 0f, 0f) * new Vector3(0f, 2.4f, -6f);
            CheckAboveGround(collision, physics, camera, owner.transform, layers, anchor, localZoom, radius);
            // Constrain the blended destination rather than interpolating a corrected
            // third-person position afterwards (which can cross a wall or terrain).
            CheckAboveGround(collision, physics, camera, owner.transform, layers, anchor,
                Vector3.Lerp(desired, anchor, 0.35f), radius);
            Remove(ground.gameObject);

            // A real TerrainCollider exercises the terrain query path as well as boxes.
            terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(32f, 2f, 32f) };
            var terrainObject = new GameObject("Terrain");
            terrainObject.transform.SetParent(holder.transform);
            terrainObject.transform.position = new Vector3(-16f, 0f, -16f);
            terrainObject.AddComponent<TerrainCollider>().terrainData = terrainData;
            Physics.SyncTransforms();
            CheckAboveGround(collision, physics, camera, owner.transform, layers, anchor, desired, radius);
            CheckAboveGround(collision, physics, camera, owner.transform, layers,
                Vector3.up * (radius * 0.5f), desired, radius);
            Remove(terrainObject);

            var wall = Box(holder.transform, "Wall", new Vector3(0f, 1.5f, -2f), new Vector3(8f, 8f, 0.2f));
            Physics.SyncTransforms();
            Vector3 behindWall = new Vector3(0f, 1.5f, -5f);
            Vector3 stopped = collision.Constrain(physics, anchor, behindWall, camera, owner.transform, layers);
            Require(stopped.z >= -1.9f + radius - 0.002f, "Near plane penetrated the wall.");
            // Start partially inside a wall to exercise origin-overlap recovery.
            Vector3 overlappingAnchor = new Vector3(0f, 1.5f, -1.85f);
            stopped = collision.Constrain(physics, overlappingAnchor, behindWall, camera, owner.transform, layers);
            Require(stopped.z >= -1.9f + radius - 0.002f, "Overlapping anchor cast through the wall.");
            var sideWall = Box(holder.transform, "Corner wall", new Vector3(0.6f, 1.5f, 0f), new Vector3(0.2f, 8f, 8f));
            Physics.SyncTransforms();
            stopped = collision.Constrain(physics, overlappingAnchor + Vector3.right * 0.25f,
                behindWall, camera, owner.transform, layers);
            Require(stopped.z >= -1.9f + radius - 0.002f && stopped.x <= 0.5f - radius + 0.002f,
                "Anchor recovery left the camera penetrating a corner.");
            Remove(sideWall.gameObject);
            Remove(wall.gameObject);
            Physics.SyncTransforms();
            RequireClose(collision.Constrain(physics, anchor, behindWall, camera, owner.transform, layers),
                behindWall, "Camera failed to recover the requested zoom after the wall was removed.");

            var slope = new GameObject("Slope");
            slope.transform.SetParent(holder.transform);
            slopeMesh = new Mesh
            {
                vertices = new[] { new Vector3(-10f, 0f, -10f), new Vector3(-10f, 5f, 10f),
                    new Vector3(10f, 0f, -10f), new Vector3(10f, 5f, 10f) },
                triangles = new[] { 0, 1, 2, 2, 1, 3 }
            };
            slope.AddComponent<MeshCollider>().sharedMesh = slopeMesh;
            Physics.SyncTransforms();
            Vector3 slopeAnchor = new Vector3(0f, 4f, 0f);
            stopped = collision.Constrain(physics, slopeAnchor, new Vector3(0f, 0f, -4f),
                camera, owner.transform, layers);
            Vector3 normal = new Vector3(0f, 1f, -0.25f).normalized;
            Require(Vector3.Dot(stopped - new Vector3(0f, 2.5f, 0f), normal) >= radius - 0.002f,
                "Camera near plane penetrated the slope.");

            camera.fieldOfView = 90f;
            camera.aspect = 2.4f;
            camera.nearClipPlane = 0.5f;
            Require(collision.GetRadius(camera) > radius, "Clearance did not follow projection changes.");
        }
        finally
        {
            Remove(holder);
            if (terrainData) UnityEngine.Object.Destroy(terrainData);
            if (slopeMesh) UnityEngine.Object.Destroy(slopeMesh);
            SceneManager.UnloadSceneAsync(scene);
            Physics.SyncTransforms();
        }
        Debug.Log("Camera collision checks passed: network/local pitch, blended position, terrain, slope, wall, " +
            "initial overlap, near-plane clearance, crowded self filtering, trigger/server filtering and boom recovery in a local PhysicsScene.");
    }

    private static void Remove(GameObject target)
    {
        target.SetActive(false);
        UnityEngine.Object.Destroy(target);
    }

    private static BoxCollider Box(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var item = new GameObject(name);
        item.transform.SetParent(parent);
        item.transform.position = position;
        var collider = item.AddComponent<BoxCollider>();
        collider.size = size;
        return collider;
    }

    private static void CheckAboveGround(DollSingerCameraCollision collision, PhysicsScene scene,
        Camera camera, Transform owner, int layers, Vector3 anchor, Vector3 desired, float radius)
    {
        Vector3 result = collision.Constrain(scene, anchor, desired, camera, owner, layers);
        Require(result.y >= radius - 0.002f, "Camera or near plane went below the ground.");
        Require((result - desired).sqrMagnitude > 0.01f, "Fixture did not exercise collision correction.");
    }

    private static void RequireClose(Vector3 actual, Vector3 expected, string message)
        => Require(Vector3.Distance(actual, expected) < 0.002f, message);

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
