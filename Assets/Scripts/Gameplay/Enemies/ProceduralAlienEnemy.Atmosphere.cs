// 给客户端气氛音效找附近的异形声源。只查这个 World 已经收到的异形，死亡和静伏时不发这一类声音。
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class ProceduralAlienEnemy {
        // Presentation reads only ghosts already replicated to this client World.
        public static bool TryGetNearbySoundSource(World world, Vector3 listener, float range, out Vector3 position) {
            position = default;
            // 用平方距离选范围内最近的可用异形，扫描每个候选时避免开平方。
            float nearest = range * range;
            bool found = false;
            foreach (var alien in s_ClientAliens) {
                if (!alien || alien.World != world || alien.m_Body == null ||
                    !alien.GhostGameObject.GhostEntityExists() || !alien.GhostHasComponent<PredictedPlayerGhost>() ||
                    alien.ReadGhostComponentData<PredictedPlayerGhost>().CurrentHealth <= 0)
                    continue;
                // A dormant ceiling ambush stays quiet until it actually wakes.
                // 已死亡或仍在无声伏击的对象不参加附近声音提示。血量和距离外的淘汰也由这一轮筛选完成。
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