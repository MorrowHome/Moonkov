using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.MP_FPS.DollSinger;

/// <summary>Focused regression checks for the actual Cinemachine adapter, not a substitute for visual acceptance.</summary>
public static class MoonkovThirdPersonCameraChecks
{
    [MenuItem("Tools/Moonkov/Check Third Person Camera")]
    public static void Run()
    {
        // Far from the live map; use the default physics scene, as Cinemachine does.
        var holder = new GameObject("Third person camera checks") { hideFlags = HideFlags.DontSave };
        TerrainData terrainData = null;
        Vector3 origin = new Vector3(10000f, 0f, 10000f);
        try
        {
            var owner = Box(holder.transform, "Owner", origin + Vector3.up * 0.85f, new Vector3(0.5f, 1.7f, 0.5f));
            var cameraObject = new GameObject("Fixture camera");
            cameraObject.transform.SetParent(holder.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.3f;
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            var data = camera.GetUniversalAdditionalCameraData();
            bool oldPostProcessing = data.renderPostProcessing;
            int oldMask = data.volumeLayerMask.value;
            var oldTrigger = data.volumeTrigger;
            Vector3 anchor = origin + Vector3.up * 1.25f;
            Physics.SyncTransforms();
            using (var rig = new DollSingerThirdPersonCamera(holder.transform, camera, owner.transform))
            {
                rig.Position(anchor, Quaternion.identity, Vector3.up, 4f, Vector3.zero,
                    0.2f, Physics.DefaultRaycastLayers, 0.35f, -1f);
                Require(Vector3.Distance(camera.transform.position, anchor - Vector3.forward * 4f) < 0.01f,
                    "Own collider prevented an unobstructed orbit.");
                Require(owner.tag == "Untagged", "Cinemachine filtering left the owner retagged.");

                terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(40f, 2f, 40f) };
                var terrain = new GameObject("Fixture terrain");
                terrain.transform.SetParent(holder.transform);
                terrain.transform.position = origin - new Vector3(20f, 0f, 20f);
                terrain.AddComponent<TerrainCollider>().terrainData = terrainData;
                Physics.SyncTransforms();
                foreach (float pitch in new[] { -30f, -60f, -80f })
                {
                    rig.ResetCollision();
                    camera.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
                    rig.Position(anchor, Quaternion.Euler(pitch, 0f, 0f), Vector3.up, 6f, Vector3.zero,
                        0.2f, Physics.DefaultRaycastLayers, 0.35f, 1f / 60f);
                    CheckNearPlane(camera, origin.y, "Upward orbit at pitch " + pitch);
                    Require(Vector3.Distance(camera.transform.position, anchor) < 6f, "Terrain did not shorten the boom.");
                }

                // After collision, rapid zoom/FP transitions must never invert the boom.
                rig.Position(anchor, Quaternion.identity, Vector3.up, 0.2f, Vector3.zero,
                    0.2f, Physics.DefaultRaycastLayers, 0.35f, 1f / 60f);
                Require(camera.transform.position.z <= anchor.z + 0.01f, "Collision recovery inverted a shortened boom.");
                rig.ResetCollision();
                var wall = Box(holder.transform, "Wall", origin + new Vector3(0f, 2f, -2f), new Vector3(8f, 4f, 0.2f));
                Physics.SyncTransforms();
                rig.Position(anchor, Quaternion.identity, Vector3.up, 4f, Vector3.zero,
                    0.2f, Physics.DefaultRaycastLayers, 0.35f, -1f);
                Require(camera.transform.position.z > origin.z - 1.5f, "Camera penetrated the wall.");
                wall.SetActive(false);
                Physics.SyncTransforms();
                float previousDistance = Vector3.Distance(camera.transform.position, anchor);
                rig.Position(anchor, Quaternion.identity, Vector3.up, 4f, Vector3.zero,
                    0.2f, Physics.DefaultRaycastLayers, 0.35f, 1f / 60f);
                Require(Vector3.Distance(camera.transform.position, anchor) > previousDistance
                    && Vector3.Distance(camera.transform.position, anchor) < 4f, "Recovery did not return smoothly.");
                for (int i = 0; i < 90; i++)
                    rig.Position(anchor, Quaternion.identity, Vector3.up, 4f, Vector3.zero,
                        0.2f, Physics.DefaultRaycastLayers, 0.35f, 1f / 60f);
                Require(Vector3.Distance(camera.transform.position, anchor - Vector3.forward * 4f) < 0.01f,
                    "Collision changed the requested zoom permanently.");

                camera.transform.rotation = Quaternion.identity;
                rig.UpdateFocus(owner.GetComponent<Renderer>(), anchor, 1f, 0.35f, 5f, 1f);
                var focusStack = VolumeManager.instance.CreateStack();
                try
                {
                    VolumeManager.instance.Update(focusStack, data.volumeTrigger, data.volumeLayerMask);
                    var focus = focusStack.GetComponent<DepthOfField>();
                    Require(focus.mode.value == DepthOfFieldMode.Gaussian, "Owner camera did not receive background focus.");
                    Require(focus.gaussianEnd.value > focus.gaussianStart.value, "Background blur range is invalid.");
                }
                finally { VolumeManager.instance.DestroyStack(focusStack); }
                // A second camera's ordinary volume mask must not see this private volume.
                var otherStack = VolumeManager.instance.CreateStack();
                try
                {
                    VolumeManager.instance.Update(otherStack, camera.transform, oldMask);
                    Require(otherStack.GetComponent<DepthOfField>().mode.value != DepthOfFieldMode.Gaussian,
                        "Background focus leaked into another camera.");
                }
                finally { VolumeManager.instance.DestroyStack(otherStack); }
                rig.UpdateFocus(null, anchor, 0f, 0.35f, 5f, 1f);
                Require(data.renderPostProcessing == oldPostProcessing, "First person did not restore post-processing state.");
            }
            Require(data.volumeLayerMask.value == oldMask && data.volumeTrigger == oldTrigger
                && data.renderPostProcessing == oldPostProcessing, "Disposal did not restore the camera settings.");
            Debug.Log("Third person camera checks passed: upward terrain orbit, near plane, self filtering, rapid zoom, wall, smooth recovery, private focus volume and cleanup.");
        }
        finally
        {
            holder.SetActive(false);
            if (Application.isPlaying) { UnityEngine.Object.Destroy(holder); if (terrainData) UnityEngine.Object.Destroy(terrainData); }
            else { UnityEngine.Object.DestroyImmediate(holder); if (terrainData) UnityEngine.Object.DestroyImmediate(terrainData); }
            Physics.SyncTransforms();
        }
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name;
        item.transform.SetParent(parent);
        item.transform.position = position;
        item.transform.localScale = size;
        return item;
    }

    private static void CheckNearPlane(Camera camera, float groundHeight, string context)
    {
        var corners = new Vector3[4];
        camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), camera.nearClipPlane,
            Camera.MonoOrStereoscopicEye.Mono, corners);
        foreach (var corner in corners)
            Require(camera.transform.TransformPoint(corner).y >= groundHeight - 0.01f, context + " put the near plane below terrain.");
        Require(camera.transform.position.y >= groundHeight, context + " put the camera below terrain.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
