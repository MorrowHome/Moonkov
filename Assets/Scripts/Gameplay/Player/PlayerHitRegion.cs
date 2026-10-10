using UnityEngine;

namespace Unity.MP_FPS
{
    // Query-only extension of the existing body capsule, for arms outside its
    // outline. Health/ownership always live on the linked player ECS entity.
    public sealed class PlayerHitRegion : MonoBehaviour
    {
        public BodyPart Part;
    }
}
