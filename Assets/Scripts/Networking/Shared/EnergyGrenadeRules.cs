// 能量手雷的库存和范围伤害规则。服务端通过这里判断能否从可访问的物品槽取出，并按距离算游戏中的伤害。
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 共享库存规则无需引用 Unity，也会在持久化检查中运行。
    public static class EnergyGrenadeRules {
        public const string Code = "energy_grenade";
        public const float Fuse = 3f, Radius = 6f, InnerRadius = 1.5f, Damage = 480f;
        public const float Speed = 14f, Gravity = 7.5f, BodyRadius = .13f, Cooldown = .8f;

        // 检查正好是携带中的能量手雷，且在角色可从手上取用的容器。
        public static bool Accessible(InventoryGraph graph, InventoryItem item) {
            return graph != null && item != null && item.Code == Code && item.Quantity > 0 &&
                   (item.Parent == InventoryCatalog.Pockets || item.Parent == graph.Equipped("ChestRig")?.Id);
        }

        // 核对版本、是否仍有此物品以及可达，再通过消费事务写入新库存。
        public static InventoryError Take(InventoryGraph graph, string id, int version) {
            if (graph == null) return InventoryError.Invalid;
            if (graph.Version != version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            if (!Accessible(graph, item)) return InventoryError.Inaccessible;
            if (--item.Quantity == 0) graph.Items.Remove(item);
            graph.Version++;
            return InventoryError.None;
        }

        // 按游戏伤害衰减计算范围内基础值。是否被墙挡住、队友保护或光环吸收由调用此方法的服务端再处理。
        public static float DamageAt(float distance) {
            if (!float.IsFinite(distance) || distance < 0 || distance >= Radius) return 0;
            return Damage * Math.Min(1f, (Radius - distance) / (Radius - InnerRadius));
        }
    }
}