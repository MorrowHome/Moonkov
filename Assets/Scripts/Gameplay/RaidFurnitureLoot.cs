using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Scene-authored furniture targets; inventories belong to the server map session.</summary>
    public sealed class RaidFurnitureLoot : MonoBehaviour
    {
        // Separate from ordinary cache masks, corpse IDs and mission output epochs.
        public const int FirstId = 1500000000;
        private int m_WorldMask;
        [Serializable] public sealed class Drawer
        {
            public Transform Target;
            public Vector3 LocalPoint;
            public string Label;
            public RaidLootProfile Profile = RaidLootProfile.Industrial;
            public Vector3 Position => Target ? Target.TransformPoint(LocalPoint) : default;
        }

        [SerializeField] private Drawer[] m_Drawers = Array.Empty<Drawer>();
        public static RaidFurnitureLoot Active { get; private set; }
        public Drawer[] Drawers => m_Drawers;
        public static bool IsFurniture(int id) => id >= FirstId;
        public static string NameFor(int id)
        {
            int index = id - FirstId;
            return Active && index >= 0 && index < Active.m_Drawers.Length
                ? Active.m_Drawers[index].Label : "抽屉";
        }

        private void OnEnable() { m_WorldMask = LayerMask.GetMask("Default", "Ground"); Active = this; }
        private void OnDisable() { if (Active == this) Active = null; }

        public void Initialize(RaidLootContainers caches)
        {
            for (int i = 0; i < m_Drawers.Length; i++)
            {
                var drawer = m_Drawers[i];
                if (drawer == null || !drawer.Target) continue;
                int id = FirstId + i;
                if (caches.Containers.ContainsKey(id)) continue;
                caches.Containers.Add(id, RaidLootTable.CreateDrawer(drawer.Profile, caches.LootRandom));
                caches.FurnitureCaches.Add(id, drawer.Position);
            }
        }

        public int Select(Camera camera, Vector3 actor, out float distance)
        {
            distance = RaidRules.PickupRange;
            if (!camera) return -1;
            if (m_WorldMask == 0) m_WorldMask = LayerMask.GetMask("Default", "Ground");
            int selected = -1;
            float bestAim = .97f;
            for (int i = 0; i < m_Drawers.Length; i++)
            {
                var drawer = m_Drawers[i];
                if (drawer == null || !drawer.Target || !drawer.Target.gameObject.activeInHierarchy) continue;
                Vector3 target = drawer.Position;
                float range = Vector3.Distance(actor, target);
                if (range > RaidRules.PickupRange) continue;
                float aim = Vector3.Dot(camera.transform.forward, (target - camera.transform.position).normalized);
                if (aim <= bestAim || UnityEngine.Physics.Linecast(actor + Vector3.up * 1.4f, target,
                        m_WorldMask, QueryTriggerInteraction.Ignore)) continue;
                bestAim = aim;
                distance = range;
                selected = FirstId + i;
            }
            return selected;
        }
    }
}
