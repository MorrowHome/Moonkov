// 战术道具的目录和共用规则。用来判断物品是否可取用，计算游戏中的伤害和闪光效果，也用来准备临时玩法的补给。
#nullable disable
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public enum TacticalItemKind : byte {
        Vitality,
        Stamina,
        Antidote,
        Dynamite,
        Frag,
        Fire,
        Choke,
        AlertTrap,
        WireTrap,
        PoisonTrap,
        AmmoBox,
        Smoke,
        Flash,
        PoisonBomb,
        Regeneration
    }

    public enum TacticalItemAction : byte {
        Use,
        Cancel,
        Disarm,
        Resupply,
        Extinguish
    }

    public enum TacticalItemFeedback : byte {
        None,
        Started,
        Cancelled,
        Interrupted,
        Completed,
        NeedGround,
        Limit,
        Poisoned,
        NoWound,
        NoTarget,
        NoRoom,
        Stale,
        Unavailable
    }

    // 服务端用阶段表示装置在飞、已布置、正生效或已移除。表现按阶段读同一对象 ID。
    public enum TacticalWorldPhase : byte {
        Flying,
        Armed,
        Fire,
        Choke,
        Wire,
        Poison,
        Alarm,
        Supply,
        Explosion,
        Removed,
        Smoke
    }

    public sealed class TacticalItemDefinition {
        public bool Injectable => TacticalItemRules.IsInjection(Kind);
        public readonly string Code;
        public readonly TacticalItemKind Kind;
        public readonly float UseSeconds, Fuse, Radius, Lifetime;

        // 类型决定道具有无投掷、布置或注射动作。规则表还提供读条、半径、引信和存续时间。
        public bool Throwable =>
            (Kind >= TacticalItemKind.Dynamite && Kind <= TacticalItemKind.Choke) || Kind == TacticalItemKind.Smoke ||
            Kind == TacticalItemKind.Flash || Kind == TacticalItemKind.PoisonBomb;

        public bool Trap => Kind >= TacticalItemKind.AlertTrap && Kind <= TacticalItemKind.PoisonTrap;
        public bool Deployable => Trap || Kind == TacticalItemKind.AmmoBox;

        public TacticalItemDefinition(
            string code,
            TacticalItemKind kind,
            float use,
            float fuse = 0,
            float radius = 0,
            float lifetime = 0) {
            Code = code;
            Kind = kind;
            UseSeconds = use;
            Fuse = fuse;
            Radius = radius;
            Lifetime = lifetime;
        }
    }

    // Explicit Moonkov adaptation; these are not claimed as Hunt's numerical balance.
    public static class TacticalItemRules {
        public const int RulesVersion = 1, MaxWorldObjects = 64, MaxOwnerTraps = 8;
        public const float TrapVerticalTolerance = .65f, InteractionRange = 2.2f;
        public const float StaminaDuration = 420, AntidoteDuration = 600, BleedDuration = 8, BurningDuration = 4;
        public const float RegenerationDuration = 120, RegenerationPerSecond = 2;
        public const float BulletWoundThreshold = 20, BulletWoundDuration = 30, StopBleedSeconds = 3;
        public const int SupplyEnergy = 400, EnergyPerUse = 100;

        public static readonly TacticalItemDefinition[] Definitions = {
            new("vitality_shot", TacticalItemKind.Vitality, 3), new("stamina_shot", TacticalItemKind.Stamina, 3),
            new("antidote_shot", TacticalItemKind.Antidote, 3),
            new("regeneration_shot", TacticalItemKind.Regeneration, 3),
            new("dynamite_bundle", TacticalItemKind.Dynamite, 1, 4, 8),
            new("frag_bomb", TacticalItemKind.Frag, 1, 4, 8),
            new("fire_bomb", TacticalItemKind.Fire, 1, 6, 3, 30),
            new("choke_bomb", TacticalItemKind.Choke, 1, 3, 4, 20),
            new("alert_trip_mine", TacticalItemKind.AlertTrap, 3, 0, 1, 180),
            new("concertina_trip_mine", TacticalItemKind.WireTrap, 3, 0, 1, 180),
            new("poison_trip_mine", TacticalItemKind.PoisonTrap, 3, 0, 1, 180),
            new("ammo_box", TacticalItemKind.AmmoBox, 3, 0, 0, 120),
            new("smoke_grenade", TacticalItemKind.Smoke, 1, 3, 5, 20),
            new("flash_grenade", TacticalItemKind.Flash, 1, 2, 12, 4),
            new("poison_bomb", TacticalItemKind.PoisonBomb, 1, 3, 3, 15)
        };

        public static TacticalItemDefinition Find(string code) {
            foreach (var d in Definitions)
                if (d.Code == code)
                    return d;
            return null;
        }

        public static bool IsInjection(TacticalItemKind kind) {
            return kind == TacticalItemKind.Vitality || kind == TacticalItemKind.Stamina ||
                   kind == TacticalItemKind.Antidote || kind == TacticalItemKind.Regeneration;
        }

        // 只有已携带并放在口袋或胸挂直下的战术物品可以直接拿。箱子和其他不可从手上取到的位置拒绝。
        public static bool Accessible(InventoryGraph graph, InventoryItem item) {
            return graph != null && item != null && item.Quantity > 0 && Find(item.Code) != null &&
                   graph.Carried(item) && (item.Parent == InventoryCatalog.Pockets ||
                                           item.Parent == graph.Equipped("ChestRig")?.Id);
        }

        // 开始与完成使用都能用同一套库存校验。读条期间移动过物品，旧请求不会把新版本里的东西扣掉。
        public static InventoryError Validate(
            InventoryGraph graph,
            string id,
            int version,
            out TacticalItemDefinition definition) {
            definition = null;
            if (graph == null) return InventoryError.Invalid;
            if (graph.Version != version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            definition = Find(item.Code);
            return Accessible(graph, item) ? InventoryError.None : InventoryError.Inaccessible;
        }

        public static InventoryError Take(InventoryGraph graph, string id, int version) {
            // 真正生效时才扣一件，数量到零就删堆并提高版本。无效请求保留旧图。
            var error = Validate(graph, id, version, out _);
            if (error != InventoryError.None) return error;
            var item = graph.Find(id);
            if (--item.Quantity == 0) graph.Items.Remove(item);
            graph.Version++;
            return InventoryError.None;
        }

        // 只按虚拟道具种类与距离算基础伤害；遮挡与同队规则交给服务端玩法层。
        public static float BlastDamage(TacticalItemKind kind, float distance) {
            if (!float.IsFinite(distance) || distance < 0 || distance >= 8) return 0;
            return (kind == TacticalItemKind.Dynamite ? 650 : kind == TacticalItemKind.Frag ? 320 : 0) *
                   Math.Min(1, (8 - distance) / 6);
        }

        // 靠近且朝向闪光才产生最强效果，距离与朝向各自贡献衰减。这是游戏里对画面和 AI 的同一基础强度。
        public static float FlashStrength(float distance, float facing) {
            if (!float.IsFinite(distance) || !float.IsFinite(facing) || distance < 0 || distance >= 12) return 0;
            return Math.Clamp((facing + .2f) / 1.05f, 0, 1) * Math.Clamp((12 - distance) / 8, 0, 1);
        }

        // Works on a copy so inventory capacity failures never spend the box or alter cells.
        // Recharges an existing legal carried cell before adding one real cell to the rig/pockets.
        public static InventoryError PrepareResupply(
            InventoryGraph graph,
            int version,
            int available,
            out InventoryGraph result,
            out int supplied) {
            result = null;
            supplied = 0;
            if (graph == null || available < 1) return InventoryError.Invalid;
            if (graph.Version != version) return InventoryError.Stale;
            var copy = graph.Clone();
            int budget = Math.Min(EnergyPerUse, available);
            // 补给箱先给外层可访问、未充满的现有电池加电。每次使用的极限也受箱中剩余能源限制。
            foreach (var item in copy.Items) {
                if (!BatteryEnergy.IsCell(item) || !copy.Carried(item) || (item.Parent != InventoryCatalog.Pockets &&
                                                                           item.Parent != copy.Equipped("ChestRig")
                                                                               ?.Id))
                    continue;
                int missing = BatteryEnergy.Capacity - BatteryEnergy.Charge(item);
                int add = Math.Min(missing, budget);
                if (add <= 0) continue;
                item.CellCharge = BatteryEnergy.Charge(item) + add;
                supplied = add;
                break;
            }
            // 没有能补的电池才尝试装一节新电池。箱子余量不到一整节，或口袋与胸挂放不下，都不会改原箱。
            if (supplied == 0) {
                // AddSupply enforces real grid capacity/weight; grant only whole lawful cells.
                if (budget < BatteryEnergy.Capacity) return InventoryError.Full;
                var error = copy.AddSupply("cells", 1, InventoryCatalog.Pockets);
                if (error != InventoryError.None && copy.Equipped("ChestRig") != null) {
                    var rigError = copy.AddSupply("cells", 1, copy.Equipped("ChestRig").Id);
                    if (error != InventoryError.Overweight || rigError == InventoryError.None) error = rigError;
                }
                if (error != InventoryError.None) return error;
                supplied = BatteryEnergy.Capacity;
            }
            var validation = copy.Validate();
            if (validation != InventoryError.None) {
                supplied = 0;
                return validation;
            }
            // 返回试算成功的背包和实际应扣能量。调用方同时更新这两侧，任何一侧失败都不能消耗箱子。
            copy.Version = graph.Version + 1;
            result = copy;
            return InventoryError.None;
        }
    }
}