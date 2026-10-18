using Unity.Entities;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private bool m_LootDropped;
        private void DropAlienLoot()
        {
            if (m_LootDropped) return;
            m_LootDropped = true;
            var manager = GhostGameObject.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(RaidLootWorld), typeof(RaidLootContainers));
            if (query.IsEmptyIgnoreFilter) return;
            var loot = manager.GetComponentObject<RaidLootContainers>(query.GetSingletonEntity());
            if (!loot.Initialized) return;
            var inventory = AlienHarvest.Create(profile.Boss, loot.LootRandom);
            Vector3 point = transform.position;
            if (!UnityEngine.Physics.Raycast(point + Vector3.up * .1f, Vector3.down, out var floor, 100,
                LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) return;
            loot.Drop(inventory, floor.point, Quaternion.FromToRotation(Vector3.up, floor.normal), -1, World.Time.ElapsedTime);
        }
    }
}
