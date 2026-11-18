// 异形死亡时生成采集容器。只处理一次，使用本局战利品池挑物品，再向下找地面落点。
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class ProceduralAlienEnemy {
        private bool m_LootDropped;

        private void DropAlienLoot() {
            // 一次死亡只尝试掉落一次。死亡状态可能持续多拍，避免按帧生成多个相同容器。
            if (m_LootDropped) return;
            m_LootDropped = true;
            var manager = GhostGameObject.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(RaidLootWorld), typeof(RaidLootContainers));
            if (query.IsEmptyIgnoreFilter) return;
            var loot = manager.GetComponentObject<RaidLootContainers>(query.GetSingletonEntity());
            if (!loot.Initialized) return;
            // 物种和是否 Boss 决定可采集种类，由本局随机源生成真实库存。
            var inventory = AlienHarvest.Create(profile.Boss, loot.LootRandom, profile.Species.ToString());
            var point = transform.position;
            // 墙上和头顶死亡也把残骸放到脚下找到的地面，并让外观朝向地面法线。
            if (!UnityEngine.Physics.Raycast(point + Vector3.up * .1f, Vector3.down, out var floor, 100,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                return;
            loot.DropHarvest(inventory, floor.point, Quaternion.FromToRotation(Vector3.up, floor.normal),
                World.Time.ElapsedTime);
        }
    }
}