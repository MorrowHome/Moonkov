using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    /// <summary>Publishes existing server action state. Animation never completes or consumes an action.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ServerGameSystem))]
    public partial class RaidHandActionSystem : SystemBase
    {
        private EntityQuery connections, players, tactical, loot;
        private readonly Dictionary<Entity, RaidInventoryState> inventories = new Dictionary<Entity, RaidInventoryState>();
        protected override void OnCreate()
        {
            connections = GetEntityQuery(typeof(RaidInventoryState), typeof(JoinedClient));
            players = GetEntityQuery(typeof(PredictedPlayerGhost));
            tactical = GetEntityQuery(typeof(TacticalServerWorld));
            loot = GetEntityQuery(typeof(RaidLootContainers));
        }
        protected override void OnUpdate()
        {
            inventories.Clear();
            using (var entities = connections.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    inventories[EntityManager.GetComponentData<JoinedClient>(entity).PlayerEntity] =
                        EntityManager.GetComponentObject<RaidInventoryState>(entity);
            var world = tactical.IsEmptyIgnoreFilter ? null : EntityManager.GetComponentObject<TacticalServerWorld>(tactical.GetSingletonEntity());
            var caches = loot.IsEmptyIgnoreFilter ? null : EntityManager.GetComponentObject<RaidLootContainers>(loot.GetSingletonEntity());
            double now = SystemAPI.Time.ElapsedTime;
            using var actors = players.ToEntityArray(Allocator.Temp);
            foreach (var actor in actors)
            {
                var health = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                HandAction action = HandAction.None; string code = null;
                Unity.Mathematics.float3 target = default;
                float remaining = 0, duration = 0;
                bool alive = health.CurrentHealth > 0 && (!health.ClashEnabled || health.ClashLife == ClashLife.Alive);
                if (alive && inventories.TryGetValue(actor, out var inventory))
                {
                    if (inventory.MedicalItemId != null)
                    {
                        code = inventory.Graph.Find(inventory.MedicalItemId)?.Code;
                        action = Kind(code, inventory.MedicalIsConsumable);
                        remaining = (float)System.Math.Max(0, inventory.MedicalUntil - now);
                        duration = inventory.MedicalIsConsumable ? UseSeconds(code) : MoonRaidMap.Active ? MoonRaidMap.Active.MedicalSeconds : 3;
                    }
                    else if (inventory.DissectingLootId >= 0)
                    {
                        action = HandAction.Search; remaining = inventory.SearchItemRemaining(now);
                        duration = inventory.SearchSecondsPerItem;
                        int id = inventory.DissectingLootId; var map = MoonRaidMap.Active;
                        if (map && id >= 0 && id < map.LootPositions.Length) target = map.LootPositions[id];
                        else if (caches != null && caches.FurnitureCaches.TryGetValue(id, out var furniture)) target = furniture;
                        else if (caches != null && caches.MissionCaches.TryGetValue(id, out var mission)) target = mission;
                        else if (caches != null && caches.DeathBags.TryGetValue(id, out var corpse)) target = corpse.Position + new Unity.Mathematics.float3(0, .25f, 0);
                    }
                }
                if (alive && EntityManager.HasComponent<DollSingerEnemyBrain>(actor))
                {
                    var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(actor);
                    if (brain.NutritionUntil > now)
                    {
                        code = brain.Inventory.Find(brain.NutritionItemId)?.Code; action = Kind(code, true);
                        remaining = (float)(brain.NutritionUntil - now); duration = UseSeconds(code);
                    }
                    else if (brain.HealUntil > now)
                    {
                        code = brain.Inventory.Find(brain.HealItemId)?.Code; action = Kind(code, false);
                        remaining = (float)(brain.HealUntil - now); duration = 3;
                    }
                    else if (brain.LootUntil > now)
                    { action = HandAction.Search; remaining = (float)(brain.LootUntil - now); duration = 2.5f; target = brain.LootLookPoint; }
                }
                if (alive && action == HandAction.None && world != null && world.Uses.TryGetValue(actor, out var use) && use.Until > now &&
                    (use.Definition?.Kind == TacticalItemKind.Vitality || use.Definition?.Kind == TacticalItemKind.Stamina || use.Definition?.Kind == TacticalItemKind.Antidote))
                {
                    action = HandAction.Inject; code = use.Definition.Code;
                    remaining = (float)(use.Until - now); duration = use.Definition.UseSeconds;
                }
                Publish(ref health, action, code, remaining, duration);
                health.HandActionTarget = target;
                EntityManager.SetComponentData(actor, health);
            }
        }
        public static HandAction Kind(string code, bool supply)
        {
            if (supply)
            {
                var definition = ConsumableCatalog.Get(code);
                if (definition?.Oxygen > 0) return HandAction.RefillOxygen;
                return definition?.Food == true ? HandAction.Eat : definition?.Hydration > 0 ? HandAction.Drink : HandAction.Treat;
            }
            return code == "medkit" || code == "vitality_shot" || code == "stamina_shot" || code == "antidote_shot" ? HandAction.Inject : HandAction.Treat;
        }
        private static float UseSeconds(string code) => ConsumableCatalog.Get(code)?.Oxygen > 0 ? RaidOxygenConsumables.UseSeconds :
            ConsumableCatalog.Get(code)?.Food == true ? RaidNutrition.FoodUseSeconds : RaidNutrition.DrinkUseSeconds;
        public static void Publish(ref PredictedPlayerGhost health, HandAction action, string code, float remaining, float duration)
        {
            if (remaining <= 0 || duration <= 0) action = HandAction.None;
            var item = action == HandAction.None ? default : new FixedString64Bytes(code ?? "");
            if (action != HandAction.None && ((byte)action != health.HandAction || !item.Equals(health.HandActionItem) || action!=HandAction.Search&&remaining > health.HandActionRemaining + .15f))
                health.HandActionSequence = health.HandActionSequence == uint.MaxValue ? 1 : health.HandActionSequence + 1;
            health.HandAction = (byte)action; health.HandActionItem = item;
            health.HandActionRemaining = action == HandAction.None ? 0 : remaining;
            health.HandActionDuration = action == HandAction.None ? 0 : duration;
        }
    }
}
