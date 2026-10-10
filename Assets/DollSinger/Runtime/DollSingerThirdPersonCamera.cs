using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>
/// Owns a manually evaluated official Cinemachine boom and an owner-camera focus volume.
/// DollSingerView remains the only writer of the real camera and its first-person pose.
/// Runtime objects and camera settings are released on disable and character rebind.
/// </summary>
public sealed class DollSingerThirdPersonCamera : IDisposable
{
    private const string SelfCollisionTag = "CinemachineTarget";
    private static readonly int IgnoredLayers = LayerMask.GetMask("ServerPlayer", "ClientPlayer", "FirstPersonOverlay");
    private readonly Camera camera;
    private readonly GameObject rig;
    private readonly Transform target;
    private readonly CinemachineCamera virtualCamera;
    private readonly CinemachineThirdPersonFollow follow;
    private readonly Vector3[] nearCorners = new Vector3[4];
    private readonly GameObject[] ownerColliderObjects;
    private readonly string[] savedTags;
    private readonly UniversalAdditionalCameraData cameraData;
    private readonly bool originalPostProcessing;
    private readonly LayerMask originalVolumeMask;
    private readonly Transform originalVolumeTrigger;
    private readonly Volume volume;
    private readonly VolumeProfile profile;
    private readonly DepthOfField depthOfField;
    private float previousDistance;

    public DollSingerThirdPersonCamera(Transform parent, Camera camera, Transform owner)
    {
        this.camera = camera;
        rig = new GameObject("DollSinger third person rig (Runtime)") { hideFlags = HideFlags.DontSave };
        rig.transform.SetParent(parent, false);
        target = new GameObject("Torso orbit target").transform;
        target.SetParent(rig.transform, false);
        var virtualCameraObject = new GameObject("Cinemachine boom");
        virtualCameraObject.transform.SetParent(rig.transform, false);
        virtualCamera = virtualCameraObject.AddComponent<CinemachineCamera>();
        // No Brain is installed: the existing network/FP camera owns update order.
        // Disable automatic selection/update and evaluate the pipeline exactly once below.
        virtualCamera.enabled = false;
        virtualCamera.Follow = target;
        follow = virtualCameraObject.AddComponent<CinemachineThirdPersonFollow>();
        follow.Damping = Vector3.zero;
        follow.VerticalArmLength = 0f;
        follow.CameraSide = 1f;

        var objects = new HashSet<GameObject>();
        foreach (var collider in owner.GetComponentsInChildren<Collider>(true)) objects.Add(collider.gameObject);
        ownerColliderObjects = new GameObject[objects.Count];
        objects.CopyTo(ownerColliderObjects);
        savedTags = new string[objects.Count];

        cameraData = camera.GetUniversalAdditionalCameraData();
        originalPostProcessing = cameraData.renderPostProcessing;
        originalVolumeMask = cameraData.volumeLayerMask;
        originalVolumeTrigger = cameraData.volumeTrigger;
        var volumeObject = new GameObject("Third person background focus (Runtime)") { hideFlags = HideFlags.DontSave };
        volumeObject.transform.SetParent(camera.transform, false);
        volumeObject.layer = LayerMask.NameToLayer("FirstPersonOverlay");
        // A tiny local volume with its own camera trigger avoids affecting Scene view,
        // menu portraits, respawn cameras or another local player's view.
        var bounds = volumeObject.AddComponent<BoxCollider>();
        bounds.isTrigger = true;
        bounds.size = Vector3.one * 0.01f;
        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = false;
        volume.priority = 100f;
        volume.blendDistance = 0f;
        volume.weight = 0f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "DollSinger third person focus (Runtime)";
        volume.sharedProfile = profile;
        depthOfField = profile.Add<DepthOfField>(true);
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.highQualitySampling.Override(true);
        cameraData.volumeLayerMask = originalVolumeMask.value | (1 << volumeObject.layer);
        cameraData.volumeTrigger = volumeObject.transform;
    }

    public void Position(Vector3 anchor, Quaternion rotation, Vector3 up, float distance,
        Vector3 shoulderOffset, float minimumRadius, int layers, float recoverySeconds, float deltaTime)
    {
        target.SetPositionAndRotation(anchor, rotation);
        virtualCamera.Lens = LensSettings.FromCamera(camera);
        follow.ShoulderOffset = shoulderOffset;
        // Cinemachine damps an absolute collision correction. Reusing a long-boom
        // correction after zooming in can overshoot the target and invert the boom.
        // Zoom itself is already smoothed by DollSingerView; reseed only on shortening.
        if (distance < previousDistance) virtualCamera.PreviousStateIsValid = false;
        previousDistance = distance;
        follow.CameraDistance = distance;
        camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), camera.nearClipPlane,
            Camera.MonoOrStereoscopicEye.Mono, nearCorners);
        float radius = minimumRadius;
        foreach (var corner in nearCorners) radius = Mathf.Max(radius, corner.magnitude + 0.02f);
        follow.AvoidObstacles = new CinemachineThirdPersonFollow.ObstacleSettings
        {
            Enabled = true,
            CollisionFilter = layers & ~IgnoredLayers,
            IgnoreTag = SelfCollisionTag,
            CameraRadius = radius,
            DampingIntoCollision = 0f,
            DampingFromCollision = Mathf.Max(0f, recoverySeconds)
        };

        // Cinemachine's supported filter is a tag. Mark only this owner's collider objects
        // during the synchronous query, then restore them even if evaluation throws.
        // Do not retag prefabs or leave gameplay tags changed between frames.
        try
        {
            for (int i = 0; i < ownerColliderObjects.Length; i++)
                if (ownerColliderObjects[i])
                {
                    savedTags[i] = ownerColliderObjects[i].tag;
                    ownerColliderObjects[i].tag = SelfCollisionTag;
                }
            virtualCamera.InternalUpdateCameraState(up, deltaTime);
            camera.transform.position = virtualCamera.State.GetFinalPosition();
        }
        finally
        {
            for (int i = 0; i < ownerColliderObjects.Length; i++)
                if (ownerColliderObjects[i] && savedTags[i] != null) ownerColliderObjects[i].tag = savedTags[i];
        }
    }

    public void ResetCollision() => virtualCamera.PreviousStateIsValid = false;

    public void UpdateFocus(Renderer subject, Vector3 fallbackFocus, float weight,
        float padding, float fadeDistance, float blurRadius)
    {
        Vector3 forward = camera.transform.forward;
        float clearDepth = Vector3.Dot(fallbackFocus - camera.transform.position, forward);
        if (subject)
        {
            Bounds bounds = subject.bounds;
            Vector3 extents = bounds.extents;
            // Furthest depth of the entire animated bounds: the skirt/head remain sharp
            // even when the orbit looks up from near the ground or zooms close to the body.
            clearDepth = Vector3.Dot(bounds.center - camera.transform.position, forward)
                + Mathf.Abs(forward.x) * extents.x + Mathf.Abs(forward.y) * extents.y + Mathf.Abs(forward.z) * extents.z;
        }
        depthOfField.gaussianStart.Override(Mathf.Max(camera.nearClipPlane, clearDepth + Mathf.Max(0f, padding)));
        depthOfField.gaussianEnd.Override(depthOfField.gaussianStart.value + Mathf.Max(0.1f, fadeDistance));
        depthOfField.gaussianMaxRadius.Override(blurRadius);
        volume.weight = Mathf.Clamp01(weight);
        cameraData.renderPostProcessing = originalPostProcessing || volume.weight > 0.001f;
    }

    public void Dispose()
    {
        if (cameraData)
        {
            cameraData.renderPostProcessing = originalPostProcessing;
            cameraData.volumeLayerMask = originalVolumeMask;
            cameraData.volumeTrigger = originalVolumeTrigger;
        }
        if (volume) { volume.enabled = false; DestroyRuntime(volume.gameObject); }
        if (profile) { foreach (var component in profile.components) DestroyRuntime(component); DestroyRuntime(profile); }
        if (rig) { rig.SetActive(false); DestroyRuntime(rig); }
    }

    private static void DestroyRuntime(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
}
