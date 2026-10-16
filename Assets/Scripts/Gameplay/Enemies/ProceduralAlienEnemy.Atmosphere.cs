using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        // Presentation reads only ghosts already replicated to this client World.
        public static bool TryGetNearbySoundSource(World world, Vector3 listener, float range,
            out Vector3 position)
        {
            position = default;
            float nearest = range * range;
            bool found = false;
            foreach (var alien in s_ClientAliens)
            {
                if (!alien || alien.World != world || alien.m_Body == null ||
                    !alien.GhostGameObject.GhostEntityExists() || !alien.GhostHasComponent<PredictedPlayerGhost>() ||
                    alien.ReadGhostComponentData<PredictedPlayerGhost>().CurrentHealth <= 0) continue;
                // A dormant ceiling ambush stays quiet until it actually wakes.
                if (alien.m_Action == AlienAction.Dead || alien.m_Action == AlienAction.Ambush) continue;
                float squared = (alien.AimPoint - listener).sqrMagnitude;
                if (squared >= nearest) continue;
                nearest = squared;
                position = alien.AimPoint;
                found = true;
            }
            return found;
        }
    }
}
