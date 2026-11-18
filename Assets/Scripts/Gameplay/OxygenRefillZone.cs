// 地图中的固定补氧站提供多少氧气，由这个组件查询。它检查供电、环境、站位以及人与接头之间有无遮挡。
// 服务端生存逻辑按查到的补氧速率更新角色。同一位置覆盖多个站时取最快的一个。
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>进入此区域可自动连接氧气接口，只有权威的生存模拟会应用供氧。</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public sealed class OxygenRefillZone : MonoBehaviour {
        private static readonly List<OxygenRefillZone> s_Zones = new();
        internal static IReadOnlyList<OxygenRefillZone> ActiveZones => s_Zones;
        [SerializeField] [Min(0)] private float m_RefillPerSecond = 10;
        [SerializeField] private bool m_Powered = true;
        [SerializeField] private bool m_RequiresBreathableAir = true;
        [SerializeField] private bool m_RequiresClearConnection;
        [SerializeField] private Vector3 m_ConnectionPoint = new(.48f, 1.12f, .38f);
        private BoxCollider m_Box;
        private int m_ConnectionMask;
        public bool RequiresClearConnection => m_RequiresClearConnection;
        public Vector3 ConnectionPoint => transform.TransformPoint(m_ConnectionPoint);
        public bool Powered { get => m_Powered; set => m_Powered = value; }

        public void Configure(float refillPerSecond, bool requiresBreathableAir = true) {
            m_RefillPerSecond = refillPerSecond;
            m_RequiresBreathableAir = requiresBreathableAir;
        }

        // 保存接头的局部位置，所以场景移动设备时接头仍随设备走。
        public void ConfigureConnection(Vector3 localPoint, bool requireClearConnection = true) {
            if (!float.IsFinite(localPoint.x) || !float.IsFinite(localPoint.y) || !float.IsFinite(localPoint.z))
                throw new System.ArgumentException("Supply connector must have finite local coordinates.");
            m_ConnectionPoint = localPoint;
            m_RequiresClearConnection = requireClearConnection;
        }

        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

        private void OnEnable() {
            m_Box = GetComponent<BoxCollider>();
            m_ConnectionMask = LayerMask.GetMask("Default", "Ground");
            if (!s_Zones.Contains(this)) s_Zones.Add(this);
        }

        private void OnDisable() { s_Zones.Remove(this); }

        // 在进入 Play 时同时禁用场景和域重载的情况下，重建一次注册表。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetZones() { s_Zones.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLoadedZones() {
            foreach (var zone in FindObjectsByType<OxygenRefillZone>())
                if (zone.isActiveAndEnabled)
                    zone.OnEnable();
        }

        public static float SampleSupply(Vector3 position, bool breathable) {
            float rate = 0;
            // 略过断电、关闭、环境不满足或速率无效的站。供氧查询不应该从这些站得到补给。
            foreach (var zone in s_Zones) {
                if (!zone || !zone.isActiveAndEnabled || !zone.m_Powered || !zone.m_Box || !zone.m_Box.enabled ||
                    (zone.m_RequiresBreathableAir && !breathable) || !float.IsFinite(zone.m_RefillPerSecond) ||
                    (Application.isPlaying && !Application.IsPlaying(zone.gameObject)))
                    continue;
                var local = zone.transform.InverseTransformPoint(position) - zone.m_Box.center;
                var half = zone.m_Box.size * .5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) {
                    // 角色位于小范围供氧区内时，才做物理连接检查。
                    // 先确认人在触发范围内，再按需要查接头附近有没有墙。这样物理检测只花在可能连接的人身上。
                    if (zone.m_RequiresClearConnection && (!float.IsFinite(zone.m_ConnectionPoint.x) ||
                                                           !float.IsFinite(zone.m_ConnectionPoint.y) ||
                                                           !float.IsFinite(zone.m_ConnectionPoint.z) ||
                                                           UnityEngine.Physics.Linecast(position, zone.ConnectionPoint,
                                                               zone.m_ConnectionMask, QueryTriggerInteraction.Ignore)))
                        continue;
                    // 在重叠的两个补氧站范围里取较快的一个，避免摆放两个盒子就把速度叠加。
                    rate = Mathf.Max(rate, zone.m_RefillPerSecond);
                }
            }
            return rate;
        }

        private void OnDrawGizmosSelected() {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = m_Powered ? Color.cyan : Color.gray;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}