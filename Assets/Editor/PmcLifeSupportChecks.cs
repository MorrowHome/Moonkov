using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class PmcLifeSupportChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .01f;
    private static InventoryItem Supply(string parent, string region, int x = 0, int y = 0) =>
        new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "dust", Parent = parent, Region = region, X = x, Y = y };

    [MenuItem("Moonkov/AI/Check Life Support Rules")]
    public static void RunFromEditor() { Debug.Log(Run()); Debug.Log(RunAuthority()); }

    public static string Run()
    {
        s_Checks = 0;
        var inventory = InventoryGraph.Create(false);
        int initialVersion = inventory.Version;
        Check(!PmcLifeSupport.TryEquip(inventory, true, false) && !PmcLifeSupport.TryEquip(inventory, false, true) &&
            inventory.Version == initialVersion && inventory.Equipped("Helmet") == null,
            "bounty and tutorial loadouts are unchanged");
        Check(PmcLifeSupport.TryEquip(inventory, false, false) && inventory.Version == initialVersion + 1 &&
            inventory.Validate() == InventoryError.None, "ordinary raider receives a real valid life-support item");
        string suitId = inventory.Equipped("Helmet").Id;
        Check(inventory.Find(suitId).FoundInRaid && !PmcLifeSupport.TryEquip(inventory, false, false) &&
            inventory.Equipped("Helmet").Id == suitId, "the equipment slot and item identity are not replaced");
        Check(inventory.AddSupply("oxygen_canister", 1, InventoryCatalog.Pockets, true) == InventoryError.None,
            "accessible oxygen supply fixture");
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        PmcProtectionLoadout.Initialize(ref health, inventory);
        Check(health.HeadDomeEquipped && Near(health.Oxygen, 100), "the player's actual equipment initializes the suit reserve");
        var status = new TacticalServerWorld.Status();
        var settings = new OxygenParameters(600, 5, 8, 3, 1.1f, 1.75f);
        var vacuum = default(AtmosphereSample);
        PmcLifeSupport.Tick(ref health, inventory, status, vacuum, settings, 360, 1, 0, false);
        Check(Near(health.Oxygen, 40) && Near(inventory.Find(suitId).SuitOxygen, 40) && Near(health.CurrentHealth, 440),
            "vacuum drains and writes back the same finite suit oxygen");
        Check(PmcLifeSupport.Low(health, PmcLifeSupport.RefillThreshold) && !PmcLifeSupport.Low(health, PmcLifeSupport.DepartureThreshold) &&
            PmcLifeSupport.HasCarried(inventory), "low reserve is detected before the departure threshold");
        var cylinder = PmcLifeSupport.FindAccessible(inventory); string cylinderId = cylinder.Id;
        int version = inventory.Version;
        Check(PmcLifeSupport.TryRefill(ref health, inventory, cylinder) && Near(health.Oxygen, 80) &&
            inventory.Find(cylinderId) == null && inventory.Version == version + 1,
            "the ordinary oxygen transaction consumes the actual carried canister once");
        Check(!PmcLifeSupport.TryRefill(ref health, inventory, cylinder) && inventory.Version == version + 1,
            "a spent canister cannot be replayed");
        var breathable = new AtmosphereSample("room", 101.3f, .21f, true);
        PmcLifeSupport.Tick(ref health, inventory, status, breathable, settings, 2, 2, 0, false);
        Check(health.BreathableAir && Near(health.Oxygen, 90) && !PmcLifeSupport.Low(health, 100),
            "breathable air refills the real suit and does not trigger an emergency");

        health.Oxygen = 1; health.BreathableAir = false;
        var brain = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        brain.Inventory = inventory; brain.Action = PmcAction.Scavenge; brain.LastDamage = -100;
        brain.LastHealth = health.CurrentHealth; brain.SelfStatus = brain.ObservedSelfStatus = status;
        brain.Action = PmcAction.Retreat;
        Check(!PmcResourceActions.SafeWindow(brain, health, false, true, 10) &&
            PmcResourceActions.SafeWindow(brain, health, false, true, 10, allowCriticalOxygen: true),
            "critical oxygen can be used at a safe withdrawal point");
        brain.Action = PmcAction.Extract; brain.ExtractionProgress = 1;
        Check(PmcResourceActions.SafeWindow(brain, health, false, true, 10, allowCriticalOxygen: true),
            "critical oxygen can be used while extracting without ending the timer");
        brain.Action = PmcAction.Engage; brain.ExtractionProgress = 0;
        Check(!PmcResourceActions.SafeWindow(brain, health, false, true, 10, allowCriticalOxygen: true),
            "a critical reserve does not permit supply use while fighting");
        brain.Action = PmcAction.Scavenge;
        PmcLifeSupport.Tick(ref health, inventory, status, vacuum, settings, 20, 3, 0, false);
        Check(Near(health.Oxygen, 0) && Near(health.CurrentHealth, 422) && Near((float)status.PmcStatusHealthDamage, 18),
            "the normal grace period precedes real hypoxia damage");
        PmcCombatPressure.Update(brain, health, 10, 0);
        Check(brain.LastDamage == -100 && Near(brain.Suppression, 0), "hypoxia does not impersonate an external attacker");
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 2, 4, RaidDamageKind.Bullet);
        PmcCombatPressure.Update(brain, health, 11, 0);
        Check(brain.LastDamage == 11, "a genuine impact during hypoxia still interrupts the raider");
        PmcLifeSupport.Tick(ref health, inventory, status, vacuum, settings, 1000, 5, 0, false);
        Check(health.CurrentHealth == 0 && Near(inventory.Find(suitId).SuitOxygen, 0),
            "unresolved hypoxia can kill and leaves no free oxygen in the suit");
        var corpse = LootInventoryExchange.DropOnDeath(inventory);
        Check(corpse.Find(suitId) != null && Near(corpse.Find(suitId).SuitOxygen, 0) && corpse.Find(cylinderId) == null &&
            inventory.Find(suitId) == null, "the corpse receives the same empty suit and no spent canister");
        var playerInventory = InventoryGraph.Create(false);
        Check(LootInventoryExchange.TryApply(playerInventory, corpse, new InventoryCommand { Operation = InventoryOperation.Move,
            ExpectedVersion = playerInventory.Version, ItemId = suitId, Parent = InventoryCatalog.Equipment, Region = "Helmet" }, corpse.Version) == InventoryError.None,
            "a player can recover the same suit from the corpse");
        var player = new PredictedPlayerGhost(); RaidHealth.Initialize(ref player);
        EnergyShieldEquipment.Synchronize(ref player, playerInventory);
        Check(player.HeadDomeEquipped && Near(player.Oxygen, 0) && playerInventory.Equipped("Helmet").Id == suitId,
            "looting does not refill the enemy's depleted life-support item");

        inventory = InventoryGraph.Create(false);
        Check(PmcLifeSupport.TryEquip(inventory, false, false) &&
            inventory.AddSupply("oxygen_canister", 1, inventory.Equipped("Backpack").Id, true) == InventoryError.None,
            "backpack oxygen fixture");
        health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); PmcProtectionLoadout.Initialize(ref health, inventory);
        health.Oxygen = 20;
        Check(PmcLifeSupport.HasCarried(inventory) && PmcLifeSupport.FindAccessible(inventory) == null,
            "backpack oxygen cannot be used directly");
        version = inventory.Version;
        cylinder = PmcLifeSupport.Prepare(inventory);
        Check(cylinder != null && inventory.ConsumableAccessible(cylinder) && inventory.Version == version + 1 &&
            inventory.Validate() == InventoryError.None, "real inventory movement prepares the same canister");
        Check(PmcLifeSupport.TryRefill(ref health, inventory, cylinder) && Near(health.Oxygen, 60),
            "the prepared backpack canister supplies its configured oxygen");

        inventory = InventoryGraph.Create(false); var pack = inventory.Equipped("Backpack"); var rig = inventory.Equipped("ChestRig");
        Check(inventory.AddSupply("oxygen_canister", 1, pack.Id, true) == InventoryError.None, "blocked access fixture");
        for (int n = 1; n <= 4; n++) inventory.Items.Add(Supply(InventoryCatalog.Pockets, n.ToString()));
        foreach (string region in new[] { "left", "center", "right" })
            for (int y = 0; y < 2; y++) for (int x = 0; x < (region == "center" ? 2 : 1); x++)
                inventory.Items.Add(Supply(rig.Id, region, x, y));
        string snapshot = RaidInventoryTransport.Encode(inventory); version = inventory.Version;
        Check(inventory.Validate() == InventoryError.None && PmcLifeSupport.Prepare(inventory) == null &&
            inventory.Version == version && RaidInventoryTransport.Encode(inventory) == snapshot,
            "full pockets and rig leave every item untouched");
        var failure = new PmcOxygenFailure(); failure.Record(version);
        var blocker = inventory.Items.First(item => item.Parent == InventoryCatalog.Pockets);
        Check(inventory.FindSpace(blocker, pack.Id, out string regionName, out int px, out int py) &&
            inventory.TryApply(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                ItemId = blocker.Id, Parent = pack.Id, Region = regionName, X = px, Y = py }) == InventoryError.None,
            "freeing a pocket uses the real versioned transaction");
        Check(!failure.Matches(inventory.Version) && PmcLifeSupport.Prepare(inventory) != null,
            "an inventory change allows the previously blocked supply to be retried");

        inventory = InventoryGraph.Create(false);
        Check(PmcLifeSupport.TryEquip(inventory, false, false), "movement oxygen fixture");
        health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); PmcProtectionLoadout.Initialize(ref health, inventory);
        health.ControllerState.Sprinting = true;
        PmcLifeSupport.Tick(ref health, inventory, null, vacuum, settings, 100, 6, 0, true);
        Check(Near(health.Oxygen, 100 - 100f / 600 * 100 * 1.75f), "actual sprinting uses the player oxygen multiplier");

        inventory = InventoryGraph.Create(false); Check(PmcLifeSupport.TryEquip(inventory, false, false), "deferred write fixture");
        health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); PmcProtectionLoadout.Initialize(ref health, inventory);
        PmcLifeSupport.Tick(ref health, inventory, null, vacuum, settings, 60, 7, 0, false, flush: false);
        Check(Near(health.Oxygen, 90) && Near(inventory.Equipped("Helmet").SuitOxygen, 100),
            "authoritative oxygen changes without allocating item writes every frame");
        EnergyShieldEquipment.Flush(ref health, inventory);
        Check(Near(inventory.Equipped("Helmet").SuitOxygen, 90), "a transaction or death can flush the current reserve immediately");
        health.Oxygen = 0; health.HypoxiaSeconds = 8; RaidHealth.Set(ref health, BodyPart.Chest, 1);
        PmcLifeSupport.Tick(ref health, inventory, null, vacuum, settings, 1, 8, 0, false, flush: false);
        Check(health.CurrentHealth == 0 && Near(inventory.Equipped("Helmet").SuitOxygen, 0),
            "fatal hypoxia flushes the final reserve even between scheduled writes");

        inventory = InventoryGraph.Create(false);
        Check(inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets, true) == InventoryError.None &&
            inventory.AddSupply("cells", 4, foundInRaid: true) == InventoryError.None &&
            PmcLifeSupport.TryEquip(inventory, false, false) &&
            inventory.AddSupply("oxygen_canister", 1, foundInRaid: true) == InventoryError.None &&
            PmcLifeSupport.FindAccessible(inventory) != null && inventory.Validate() == InventoryError.None,
            "the real standard PMC loadout fits its emergency oxygen in an accessible slot");
        return "PASS " + s_Checks + " PMC life-support and oxygen checks";
    }

    private delegate void SurvivalTick(ref ServerGameSystem system, ref SystemState state);

    // Actual server update and temporary atmosphere in an isolated World. This
    // does not start a raid, open a profile, or replace the authored map scene.
    public static string RunAuthority()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var previous = MoonRaidMap.Active;
        using var world = new World("PMC oxygen authority checks", WorldFlags.GameServer);
        try
        {
            var root = new GameObject("Temporary PMC oxygen map"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene);
            var map = root.AddComponent<MoonRaidMap>();
            typeof(MoonRaidMap).GetField("m_LootPositions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(map, new[] { Vector3.zero });
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, map);
            var manager = world.EntityManager;
            var handle = world.GetOrCreateSystem<ServerGameSystem>();
            manager.SetComponentData(manager.CreateEntity(typeof(NetworkTime)), new NetworkTime { ServerTick = new NetworkTick(1) });
            var update = (SurvivalTick)typeof(ServerGameSystem).GetMethod("UpdateSurvival", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate(typeof(SurvivalTick));
            var inventory = InventoryGraph.Create(false);
            if (!PmcLifeSupport.TryEquip(inventory, false, false)) throw new InvalidOperationException("PMC oxygen suit fixture");
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            PmcProtectionLoadout.Initialize(ref health, inventory);
            health.ControllerState.MovementSpeed = 5; health.ControllerState.Sprinting = true;
            var origin = new float3(20000, 0, 20000);
            var npc = manager.CreateEntity(typeof(DollSingerEnemy), typeof(Simulate), typeof(PredictedPlayerGhost), typeof(LocalTransform));
            manager.SetComponentData(npc, health); manager.SetComponentData(npc, LocalTransform.FromPosition(origin));
            var brain = new DollSingerEnemyBrain { Inventory = inventory }; manager.AddComponentObject(npc, brain);
            var clash = manager.CreateEntity(typeof(DollSingerEnemy), typeof(ClashBot), typeof(Simulate), typeof(PredictedPlayerGhost), typeof(LocalTransform));
            manager.SetComponentData(clash, health); manager.SetComponentData(clash, LocalTransform.FromPosition(origin));
            manager.AddComponentObject(clash, new DollSingerEnemyBrain { Inventory = inventory.Clone(), ClashParticipantId = 1 });
            void Tick(double now)
            {
                world.SetTime(new TimeData(now, 1));
                ref var server = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
                ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
                update(ref server, ref state);
            }
            Tick(1);
            var first = manager.GetComponentData<PredictedPlayerGhost>(npc);
            if (!Near(first.Oxygen, 100 - 100f / 600) || !Near(first.Energy, 100 - 100f / 900) ||
                !Near(first.Hydration, 100 - 100f / 720) || !Near(inventory.Equipped("Helmet").SuitOxygen, first.Oxygen) ||
                !Near(manager.GetComponentData<PredictedPlayerGhost>(clash).Oxygen, 100) ||
                !Near(manager.GetComponentData<PredictedPlayerGhost>(clash).Energy, 100))
                throw new InvalidOperationException("Server idle survival or bounty exclusion failed.");
            manager.SetComponentData(npc, LocalTransform.FromPosition(origin + new float3(1, 0, 0)));
            Tick(2);
            var running = manager.GetComponentData<PredictedPlayerGhost>(npc);
            if (!Near(running.Oxygen, first.Oxygen - 100f / 600 * 1.75f) ||
                !Near(running.Energy, first.Energy - 100f / 900 * 1.75f) ||
                !Near(running.Hydration, first.Hydration - 100f / 720 * 1.4f))
                throw new InvalidOperationException("Server PMC movement did not consume sprint survival resources.");
            var tutorial = manager.CreateEntity(typeof(TutorialRaidMode));
            Tick(3);
            var tutorialHealth = manager.GetComponentData<PredictedPlayerGhost>(npc);
            if (!Near(tutorialHealth.Oxygen, running.Oxygen) || !Near(tutorialHealth.Energy, running.Energy) ||
                !Near(tutorialHealth.Hydration, running.Hydration))
                throw new InvalidOperationException("Tutorial unexpectedly simulated PMC survival.");
            manager.DestroyEntity(tutorial);
            running.Oxygen = 0; running.HypoxiaSeconds = 8;
            EnergyShieldEquipment.Flush(ref running, inventory); manager.SetComponentData(npc, running);
            Tick(4);
            var hypoxic = manager.GetComponentData<PredictedPlayerGhost>(npc);
            using var query = manager.CreateEntityQuery(typeof(TacticalServerWorld));
            var tactical = manager.GetComponentObject<TacticalServerWorld>(query.GetSingletonEntity());
            if (!Near(hypoxic.CurrentHealth, running.CurrentHealth - 3) ||
                !tactical.Statuses.TryGetValue(npc, out var status) || !Near((float)status.PmcStatusHealthDamage, 3))
                throw new InvalidOperationException("Server hypoxia damage or pressure accounting failed.");

            var room = new GameObject("Temporary breathable room"); room.SetActive(false); SceneManager.MoveGameObjectToScene(room, scene);
            room.transform.position = (Vector3)origin + Vector3.right;
            var zone = room.AddComponent<BreathableZone>(); zone.GetComponent<BoxCollider>().size = new Vector3(10, 10, 10);
            zone.Configure("fixture", 101.325f, .21f); room.SetActive(true);
            Tick(5);
            var recovered = manager.GetComponentData<PredictedPlayerGhost>(npc);
            if (!recovered.BreathableAir || !Near(recovered.Oxygen, 5) || !Near(recovered.CurrentHealth, hypoxic.CurrentHealth))
                throw new InvalidOperationException("Server breathable-room recovery failed.");
            recovered.Energy = 0; recovered.StarvationSeconds = 10;
            manager.SetComponentData(npc, recovered); Tick(6);
            var starving = manager.GetComponentData<PredictedPlayerGhost>(npc);
            if (!Near(starving.CurrentHealth, recovered.CurrentHealth - .5f) || !Near((float)status.PmcStatusHealthDamage, 3.5f))
                throw new InvalidOperationException("Server starvation damage or pressure accounting failed.");
            return "PASS server PMC atmosphere, movement, nutrition, hypoxia and mode exclusion checks";
        }
        finally
        {
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, previous);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
