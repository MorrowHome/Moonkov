using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    [ExecuteAlways, RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
    public sealed class BreathableZone : MonoBehaviour
    {
        private static readonly List<BreathableZone> s_Zones = new List<BreathableZone>();
        private BoxCollider m_Box;
        [SerializeField] private string m_AirspaceId;
        [SerializeField] private int m_Priority;
        [SerializeField, Min(0)] private float m_PressureKPa = 101.325f;
        [SerializeField, Range(0, 1)] private float m_OxygenFraction = .21f;
        [SerializeField, Min(0)] private float m_MinPressureKPa = 60;
        [SerializeField, Min(0)] private float m_MinOxygenPartialPressureKPa = 15;
        [SerializeField] private AirlockController m_Chamber;
        [Tooltip("Breathable air always prevents hypoxia. Disable this when suit tanks require a separate refill station.")]
        [SerializeField] private bool m_AllowsSuitRefill = true;
        public bool AllowsSuitRefill { get => m_AllowsSuitRefill; set => m_AllowsSuitRefill = value; }
        public string AirspaceId => m_AirspaceId;
        public float PressureKPa => m_Chamber ? m_Chamber.State.PressureKPa : m_PressureKPa;
        public void Configure(string id, float pressureKPa, float oxygenFraction, int priority = 0, AirlockController chamber = null)
        {
            m_AirspaceId = id; m_PressureKPa = Mathf.Max(0, pressureKPa);
            m_OxygenFraction = Mathf.Clamp01(oxygenFraction); m_Priority = priority; m_Chamber = chamber;
            m_Box = GetComponent<BoxCollider>();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetZones() => s_Zones.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLoadedZones()
        {
            foreach (var zone in FindObjectsByType<BreathableZone>())
                if (zone.isActiveAndEnabled) zone.OnEnable();
        }
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnEnable() { m_Box = GetComponent<BoxCollider>(); if (!s_Zones.Contains(this)) s_Zones.Add(this); }
        private void OnDisable() => s_Zones.Remove(this);
        public static bool Contains(Vector3 position) => TrySample(position, out var sample) && sample.Breathable;
        public static bool TrySample(Vector3 position, out AtmosphereSample sample)
        {
            BreathableZone selected = null;
            foreach (var zone in s_Zones)
            {
                if (!zone || !zone.isActiveAndEnabled || !zone.m_Box || !zone.m_Box.enabled ||
                    Application.isPlaying && !Application.IsPlaying(zone.gameObject)) continue;
                var local = zone.transform.InverseTransformPoint(position) - zone.m_Box.center;
                var half = zone.m_Box.size * .5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z &&
                    (!selected || zone.m_Priority > selected.m_Priority)) selected = zone;
            }
            if (!selected) { sample = default; return false; }
            float pressure = selected.PressureKPa;
            float fraction = selected.m_Chamber ? selected.m_Chamber.Settings.SupplyOxygenFraction : selected.m_OxygenFraction;
            float minimum = selected.m_Chamber ? selected.m_Chamber.Settings.BreathablePressureKPa : selected.m_MinPressureKPa;
            float oxygenMinimum = selected.m_Chamber ? selected.m_Chamber.Settings.BreathableOxygenPartialPressureKPa : selected.m_MinOxygenPartialPressureKPa;
            bool valid = AirlockSettings.Finite(pressure) && AirlockSettings.Finite(fraction) &&
                AirlockSettings.Finite(minimum) && AirlockSettings.Finite(oxygenMinimum) &&
                pressure >= 0 && fraction >= 0 && fraction <= 1 && minimum >= 0 && oxygenMinimum >= 0;
            sample = new AtmosphereSample(selected.m_AirspaceId, valid ? pressure : 0, valid ? fraction : 0,
                valid && pressure >= minimum && pressure * fraction >= oxygenMinimum, selected.m_AllowsSuitRefill);
            return true;
        }
        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = new Color(.3f, 1, .5f, .8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
    public readonly struct AtmosphereSample
    {
        public readonly string AirspaceId;
        public readonly float PressureKPa, OxygenFraction;
        public readonly bool Breathable, AllowsSuitRefill;
        public AtmosphereSample(string id, float pressure, float oxygen, bool breathable, bool allowsSuitRefill = true)
        { AirspaceId = id; PressureKPa = pressure; OxygenFraction = oxygen; Breathable = breathable; AllowsSuitRefill = allowsSuitRefill; }
    }
}
