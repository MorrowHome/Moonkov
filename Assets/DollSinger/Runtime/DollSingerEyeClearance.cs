using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Constrains the animated eye to the owner's physics scene, including the near plane.</summary>
    public static class DollSingerEyeClearance
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static Vector3 Constrain(PhysicsScene scene, Vector3 origin, Vector3 eye, Transform owner,
            float nearClip, float fieldOfView, float aspect, int layers)
        {
            Vector3 offset = eye - origin;
            float distance = offset.magnitude;
            if (!scene.IsValid() || distance < .0001f) return eye;
            float height = nearClip * Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad);
            float radius = Mathf.Max(.045f, Mathf.Sqrt(nearClip * nearClip + height * height * (1f + aspect * aspect)) + .005f);
            int count = scene.SphereCast(origin, radius, offset / distance, Hits, distance,
                layers, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return origin;
            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (!hit.collider || owner && hit.collider.transform.IsChildOf(owner)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - .005f));
            }
            return origin + offset * (allowed / distance);
        }
    }
}
