using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Automatic suit umbilical area. Only the authoritative survival tick applies its supply.</summary>
    [ExecuteAlways, RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
    public sealed class OxygenRefillZone : MonoBehaviour
    {
        private static readonly List<OxygenRefillZone> s_Zones = new List<OxygenRefillZone>();
        [SerializeField, Min(0)] private float m_RefillPerSecond = 10;
        [SerializeField] private bool m_Powered = true;
        [SerializeField] private bool m_RequiresBreathableAir = true;
        [SerializeField] private bool m_RequiresClearConnection;
        [SerializeField] private Vector3 m_ConnectionPoint = new Vector3(.48f, 1.12f, .38f);
        private BoxCollider m_Box;
        private int m_ConnectionMask;
        public bool RequiresClearConnection => m_RequiresClearConnection;
        public Vector3 ConnectionPoint => transform.TransformPoint(m_ConnectionPoint);
        public bool Powered { get => m_Powered; set => m_Powered = value; }
        public void Configure(float refillPerSecond, bool requiresBreathableAir = true)
        { m_RefillPerSecond = refillPerSecond; m_RequiresBreathableAir = requiresBreathableAir; }
        public void ConfigureConnection(Vector3 localPoint, bool requireClearConnection = true)
        {
            if (!float.IsFinite(localPoint.x) || !float.IsFinite(localPoint.y) || !float.IsFinite(localPoint.z))
                throw new System.ArgumentException("Supply connector must have finite local coordinates.");
            m_ConnectionPoint = localPoint; m_RequiresClearConnection = requireClearConnection;
        }
        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;
        private void OnEnable()
        {
            m_Box = GetComponent<BoxCollider>(); m_ConnectionMask = LayerMask.GetMask("Default", "Ground");
            if (!s_Zones.Contains(this)) s_Zones.Add(this);
        }
        private void OnDisable() => s_Zones.Remove(this);
        // Rebuild once so Enter Play Mode with scene AND domain reload disabled also works.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetZones() => s_Zones.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLoadedZones()
        {
            foreach (var zone in FindObjectsByType<OxygenRefillZone>())
                if (zone.isActiveAndEnabled) zone.OnEnable();
        }
        public static float SampleSupply(Vector3 position, bool breathable)
        {
            float rate = 0;
            foreach (var zone in s_Zones)
            {
                if (!zone || !zone.isActiveAndEnabled || !zone.m_Powered || !zone.m_Box || !zone.m_Box.enabled ||
                    (zone.m_RequiresBreathableAir && !breathable) || !float.IsFinite(zone.m_RefillPerSecond) ||
                    (Application.isPlaying && !Application.IsPlaying(zone.gameObject))) continue;
                var local = zone.transform.InverseTransformPoint(position) - zone.m_Box.center;
                var half = zone.m_Box.size * .5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                {
                    // Only actors inside the small supply volume pay for a physical connection check.
                    if (zone.m_RequiresClearConnection && (!float.IsFinite(zone.m_ConnectionPoint.x) ||
                        !float.IsFinite(zone.m_ConnectionPoint.y) || !float.IsFinite(zone.m_ConnectionPoint.z) ||
                        UnityEngine.Physics.Linecast(position, zone.ConnectionPoint, zone.m_ConnectionMask, QueryTriggerInteraction.Ignore))) continue;
                    rate = Mathf.Max(rate, zone.m_RefillPerSecond);
                }
            }
            return rate;
        }
        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = m_Powered ? Color.cyan : Color.gray;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
