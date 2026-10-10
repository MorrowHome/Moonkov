using System;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>Shortens the rendered camera boom without changing orbit or zoom state.</summary>
public sealed class DollSingerCameraCollision : IDisposable
{
    private const float Skin = 0.01f;
    private const int MaxQueryHits = 1024;
    private RaycastHit[] hits = new RaycastHit[16];
    private Collider[] overlaps = new Collider[16];
    private readonly Vector3[] nearCorners = new Vector3[4];
    private SphereCollider probe;

    public float GetRadius(Camera camera)
    {
        // Enclose the whole near plane, not just the camera centre. This also follows
        // aspect/FOV/near-clip changes during ADS and first/third-person transitions.
        camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), camera.nearClipPlane,
            Camera.MonoOrStereoscopicEye.Mono, nearCorners);
        float radius = 0.05f;
        foreach (var corner in nearCorners) radius = Mathf.Max(radius, corner.magnitude);
        return radius;
    }

    public Vector3 Constrain(PhysicsScene scene, Vector3 anchor, Vector3 desired,
        Camera camera, Transform owner, int layers)
    {
        float radius = GetRadius(camera);
        // Sphere casts do not reliably report colliders overlapping their origin.
        // Recover an overlapping shoulder/eye anchor before trying to extend the boom.
        if (!RecoverAnchor(scene, ref anchor, radius, owner, layers)) return anchor;

        Vector3 offset = desired - anchor;
        float distance = offset.magnitude;
        if (distance < 0.0001f) return anchor;
        Vector3 direction = offset / distance;
        int count;
        while (true)
        {
            count = scene.SphereCast(anchor, radius, direction, hits, distance, layers,
                QueryTriggerInteraction.Ignore);
            if (count < hits.Length) break;
            // Non-alloc results are unordered. Never trust a saturated buffer to contain
            // the nearest world hit, especially when the player's own colliders fill it.
            if (hits.Length >= MaxQueryHits) return anchor;
            Array.Resize(ref hits, hits.Length * 2);
        }

        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            if (Ignore(hits[i].collider, owner)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hits[i].distance - Skin));
        }
        return anchor + direction * allowed;
    }

    private bool RecoverAnchor(PhysicsScene scene, ref Vector3 anchor, float radius,
        Transform owner, int layers)
    {
        for (int iteration = 0; iteration < 8; iteration++)
        {
            int count;
            while (true)
            {
                count = scene.OverlapSphere(anchor, radius, overlaps, layers,
                    QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                if (overlaps.Length >= MaxQueryHits) return false;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }

            bool blocked = false;
            bool moved = false;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = overlaps[i];
                if (Ignore(obstacle, owner)) continue;
                blocked = true;
                EnsureProbe(radius);
                if (!Physics.ComputePenetration(probe, anchor, Quaternion.identity,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation,
                    out Vector3 direction, out float distance)) continue;
                anchor += direction * (distance + Skin);
                moved = true;
            }
            if (!blocked) return true;
            if (!moved) return false;
        }
        // In an unresolvable enclosure, collapse at the recovered anchor rather than
        // casting from inside geometry and accidentally extending through its far side.
        return false;
    }

    private static bool Ignore(Collider collider, Transform owner)
        => !collider || (owner && collider.transform.IsChildOf(owner));

    private void EnsureProbe(float radius)
    {
        if (!probe)
        {
            var holder = new GameObject("Camera collision probe") { hideFlags = HideFlags.HideAndDontSave };
            probe = holder.AddComponent<SphereCollider>();
            // Used only as a shape for ComputePenetration, never part of simulation.
            probe.enabled = false;
        }
        probe.radius = radius;
    }

    public void Dispose()
    {
        if (!probe) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(probe.gameObject);
        else UnityEngine.Object.DestroyImmediate(probe.gameObject);
        probe = null;
    }
}
}
