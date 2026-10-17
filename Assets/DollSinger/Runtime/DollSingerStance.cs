using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.DollSinger
{
    public enum DollSingerStance : byte { Standing, Crouching, Prone, Tiptoe }

    /// <summary>Shared stance dimensions and clearance rules for local, predicted and server motors.</summary>
    public static class DollSingerStances
    {
        private static readonly Collider[] overlaps = new Collider[32];
        public static float SpeedScale(DollSingerStance stance) => stance == DollSingerStance.Prone ? .22f :
            stance == DollSingerStance.Crouching ? .5f : stance == DollSingerStance.Tiptoe ? .4f : 1f;
        public static float Height(DollSingerStance stance, float standingHeight, float radius) =>
            Mathf.Max(radius * 2f, stance == DollSingerStance.Prone ? .58f :
                stance == DollSingerStance.Crouching ? standingHeight * .65f :
                stance == DollSingerStance.Tiptoe ? standingHeight + .06f : standingHeight);

        public static DollSingerStance Resolve(CharacterController controller, Vector3 position,
            DollSingerStance current, DollSingerStance requested, bool grounded, float standingHeight, Vector3 standingCenter)
        {
            // Keep a low posture if falling off a ledge; only enter a new posture on the ground.
            if (!grounded && requested != current) return current;
            float height = Height(requested, standingHeight, controller.radius);
            if (height <= Height(current, standingHeight, controller.radius)) return requested;
            float radius = Mathf.Max(.01f, controller.radius - .015f);
            Vector3 bottom = position + standingCenter - Vector3.up * (standingHeight * .5f);
            var physics = controller.gameObject.scene.GetPhysicsScene();
            int count = physics.OverlapCapsule(bottom + Vector3.up * (radius + .02f),
                bottom + Vector3.up * (height - radius), radius, overlaps,
                Physics.DefaultRaycastLayers & ~controller.excludeLayers.value, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return current;
            for (int i = 0; i < count; i++)
                if (overlaps[i] && !overlaps[i].transform.IsChildOf(controller.transform)) return current;
            return requested;
        }

        public static void Apply(CharacterController controller, DollSingerStance stance,
            float standingHeight, Vector3 standingCenter)
        {
            float height = Height(stance, standingHeight, controller.radius);
            Vector3 center = standingCenter + Vector3.up * ((height - standingHeight) * .5f);
            if (!Mathf.Approximately(controller.height, height)) controller.height = height;
            if (controller.center != center) controller.center = center;
        }
    }
}
