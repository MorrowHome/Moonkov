// 角色或异形的擦墙声从这里找接触面和位移。探测结果会短时复用，射线也有每帧共用的额度，避免许多角色一起擦墙时突然增加开销。
using UnityEngine;

namespace Unity.MP_FPS {
    // Cosmetic contact sampling shared by suit and creature foley. No collider or movement authority.
    public sealed class MoonkovSurfaceFoley {
        private static readonly RaycastHit[] s_Hits = new RaycastHit[16];
        private static readonly int s_Mask = LayerMask.GetMask("Default", "Ground");
        private static int s_Frame = -1, s_Rays;
        private float m_SampleIn, m_Slip, m_NextSound, m_ContactAge;
        private bool m_Touched;
        private RaycastHit m_Contact;

        public void Reset() {
            m_Contact = default;
            m_Slip = 0;
            m_Touched = false;
            m_SampleIn = 0;
            m_ContactAge = 0;
        }

        public bool TrySlide(
            Transform owner,
            Vector3 center,
            Vector3 displacement,
            float reach,
            float dt,
            out RaycastHit contact) {
            contact = default;
            float distance = displacement.magnitude;
            // 抖动和瞬移可能看起来像一下穿过很长的墙。用位移、耗时与速度一起筛掉这些样本。
            if (!owner || dt <= 0 || dt > .25f || distance > 2 || distance / dt < .15f) {
                Reset();
                return false;
            }
            m_ContactAge += dt;
            m_SampleIn -= dt;
            // 一段时间没重新采样才查附近的墙。预算不足时短暂复用旧接触，离开墙面或接触过期就重新判断。
            if (m_SampleIn <= 0 && TakeRays(4)) {
                m_SampleIn = .14f + (owner.GetEntityId().GetHashCode() & 15) * .004f;
                if (!TryFindWall(owner, center, displacement, reach, out m_Contact)) {
                    m_Touched = false;
                    m_Slip = 0;
                }
                m_ContactAge = 0;
            }
            if (!m_Contact.collider) return false;
            // A denied ray budget or movement beyond the sampled wall must not sustain phantom friction.
            if (m_ContactAge > .25f || !ContactWithinReach(center, reach + .025f) ||
                Vector3.ProjectOnPlane(displacement, m_Contact.normal).sqrMagnitude <
                displacement.sqrMagnitude * .16f) {
                Reset();
                return false;
            }
            m_Slip += Vector3.ProjectOnPlane(displacement, m_Contact.normal).magnitude;
            // 沿墙滑了足够距离且超过冷却后才播。第一次接触阈值更短，贴墙一直移动时避免太密。
            if (m_Slip < (m_Touched ? .32f : .065f) || Time.time < m_NextSound) return false;
            contact = m_Contact;
            m_Touched = true;
            m_Slip = 0;
            m_NextSound = Time.time + .48f;
            return true;
        }

        private bool ContactWithinReach(Vector3 center, float reach) {
            var collider = m_Contact.collider;
            if (collider is not MeshCollider { convex: false } && collider is not TerrainCollider)
                return (collider.ClosestPoint(center) - center).sqrMagnitude <= reach * reach;
            // ClosestPoint does not support concave geometry. Confirm the cached
            // face directly; its bounds alone would keep scraping across openings.
            return TakeRays(1) && collider.Raycast(new Ray(center, -m_Contact.normal), out _, reach);
        }

        private static bool TakeRays(int count) {
            if (s_Frame != Time.frameCount) {
                s_Frame = Time.frameCount;
                s_Rays = 0;
            }
            // 同一帧限额由所有角色共享。主角与异形都刷擦墙声时，可以限制物理射线的峰值。
            if (s_Rays + count > 12) return false;
            s_Rays += count;
            return true;
        }

        public static bool TrySupportContact(
            Transform owner,
            Vector3 center,
            Vector3 normal,
            float reach,
            out RaycastHit contact) {
            contact = default;
            if (!owner || !TakeRays(1)) return false;
            int count = owner.gameObject.scene.GetPhysicsScene().Raycast(center, -normal, s_Hits,
                reach, s_Mask, QueryTriggerInteraction.Ignore);
            float nearest = reach;
            for (int i = 0; i < count; i++) {
                var hit = s_Hits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(owner) || hit.distance > nearest ||
                    Vector3.Dot(hit.normal, normal) < .65f)
                    continue;
                nearest = hit.distance;
                contact = hit;
            }
            return contact.collider;
        }

        // Excludes floors/ceilings, triggers, own children and motion directly into a wall.
        public static bool TryFindWall(
            Transform owner,
            Vector3 center,
            Vector3 displacement,
            float reach,
            out RaycastHit contact) {
            contact = default;
            if (!owner || displacement.sqrMagnitude < .000001f) return false;
            float nearest = reach;
            var physics = owner.gameObject.scene.GetPhysicsScene();
            // 按移动方向的侧面和前后查附近表面，挑符合朝向和距离约束的最近碰撞。代表角色自己的碰撞体会被跳过。
            for (int direction = 0; direction < 4; direction++) {
                var axis = direction < 2 ? Vector3.right : Vector3.forward;
                if ((direction & 1) != 0) axis = -axis;
                int count = physics.Raycast(center, axis, s_Hits, reach, s_Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++) {
                    var hit = s_Hits[i];
                    if (!hit.collider || hit.collider.transform.IsChildOf(owner) || hit.distance > nearest ||
                        Mathf.Abs(hit.normal.y) > .7f || Vector3.ProjectOnPlane(displacement, hit.normal).sqrMagnitude <
                        displacement.sqrMagnitude * .16f)
                        continue;
                    nearest = hit.distance;
                    contact = hit;
                }
            }
            return contact.collider;
        }
    }
}