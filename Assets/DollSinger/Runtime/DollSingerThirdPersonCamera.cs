using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>
/// Owns a manually evaluated official Cinemachine third-person boom.
/// DollSingerView remains the only writer of the real camera and its first-person pose.
/// Runtime objects are released on disable and character rebind.
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

    public void Dispose()
    {
        if (rig) { rig.SetActive(false); DestroyRuntime(rig); }
    }

    private static void DestroyRuntime(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
}
