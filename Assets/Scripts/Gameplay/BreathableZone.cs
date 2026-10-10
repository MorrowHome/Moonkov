using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    [ExecuteAlways, RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
    public sealed class BreathableZone : MonoBehaviour
    {
        private static readonly List<BreathableZone> s_Zones = new List<BreathableZone>();
        private BoxCollider m_Box;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetZones() => s_Zones.Clear();
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnEnable() { m_Box = GetComponent<BoxCollider>(); if (!s_Zones.Contains(this)) s_Zones.Add(this); }
        private void OnDisable() => s_Zones.Remove(this);
        public static bool Contains(Vector3 position)
        {
            foreach (var zone in s_Zones)
            {
                if (!zone || !zone.isActiveAndEnabled || !zone.m_Box || !zone.m_Box.enabled ||
                    Application.isPlaying && !Application.IsPlaying(zone.gameObject)) continue;
                var local = zone.transform.InverseTransformPoint(position) - zone.m_Box.center;
                var half = zone.m_Box.size * .5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) return true;
            }
            return false;
        }
    }
}
