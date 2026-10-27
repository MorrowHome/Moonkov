using System;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using BodyPart = Unity.MP_FPS.BodyPart;

/// <summary>Bounded, scene-independent checks of PMC decisions and action lifecycles.
/// These exercise production methods without spawning raid actors, baking navigation,
/// opening a connection, touching a profile, or claiming gameplay acceptance.</summary>
public static class DollSingerHumanlikeChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Moonkov/AI/Check Humanlike Decisions")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run PMC decision checks in Edit Mode.");
        var holder = new GameObject("Temporary PMC decision checks") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false); // Do not register a second active raid map.
        var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
        tuning.name = "Temporary PMC check tuning";
        try
        {
            var map = holder.AddComponent<MoonRaidMap>();
            using var world = new World("PMC decision checks", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            typeof(DollSingerEnemySystem).GetField("m_Tuning", PrivateInstance).SetValue(system, tuning);
            CheckCoverLifecycle(system);
            CheckMedicalLifecycle(system);
            CheckDecisions(system, map, tuning);
            CheckGrenadeDecisions(system, map);
            CheckFireDecisions(system, map);
            CheckGrenadeVisibility(system, world);
            CheckCombatRecovery(system, map);
            CheckCombatPressure(system, map);
            CheckUnknownAttack(system, map);
            CheckBleedTreatment(system, map);
            CheckFireTreatment(system, map);
            CheckReturnFire(system, map, world);
            CheckReloadAvailability(system, map);
            CheckBackpackMedical(system, map);
        }
        finally
        {
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(tuning);
        }
        Debug.Log("PMC checks passed: cover/healing lifecycles, temporary injury recovery, bounded pressure, physical sightline return fire while retreating/without cover, and sprint/empty/heal safety. Gameplay and network acceptance remain separate.");
    }

    private static DollSingerEnemyBrain Brain() => new DollSingerEnemyBrain
    {
        Inventory = InventoryGraph.Create(stash: false, starter: false),
        Random = new Unity.Mathematics.Random(137),
        Seed = 1,
        Caution = .5f,
        Aggression = .4f,
        ActionStarted = 10,
        TacticalGoal = Vector3.zero
    };

    private static void CheckReloadAvailability(DollSingerEnemySystem system,MoonRaidMap map)
    {
        var property=typeof(WeaponManager).GetProperty("Instance",BindingFlags.Public|BindingFlags.Static);
        var old=property.GetValue(null);
        var root=new GameObject("Temporary PMC reload registry") {hideFlags=HideFlags.HideAndDontSave};root.SetActive(false);
        try
        {
            var manager=root.AddComponent<WeaponManager>();
            manager.WeaponRegistry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
            Require(manager.WeaponRegistry!=null,"Weapon registry missing from PMC reload check.");
            property.SetValue(null,manager);
            var health=Health();health.CurrentAmmo=health.PrimaryAmmo=health.SecondaryAmmo=health.PistolAmmo=0;
            var brain=Brain();brain.Inventory=InventoryGraph.Create(false);
            string pack=brain.Inventory.Equipped("Backpack").Id;
            Require(brain.Inventory.AddSupply("cells",1,pack)==InventoryError.None,"PMC reload fixture battery.");
            int cost=manager.WeaponRegistry.GetWeaponData(health.EquippedWeaponID,health.EquippedModules).EnergyPerRound;
            brain.ReloadFailure.Record(brain.Inventory.Version,health.EquippedWeaponID,cost);
            var decision=Decide(system,brain,health,map,10);
            Require(brain.Leaving && decision.action==PmcAction.Extract,"PMC kept attempting a known inaccessible reload instead of leaving.");
            brain=Brain();brain.Inventory=InventoryGraph.Create(false);
            Require(brain.Inventory.AddSupply("cells",1,InventoryCatalog.Pockets)==InventoryError.None,"Accessible PMC battery fixture.");
            decision=Decide(system,brain,health,map,10);
            Require(!brain.Leaving && decision.action==PmcAction.Reload,"Accessible battery failed to request a reload.");
            brain=Brain();brain.Inventory=InventoryGraph.Create(false);
            Require(brain.Inventory.AddSupply("cells",1,InventoryCatalog.Pockets,cellCharge:4)==InventoryError.None,"Modified PMC battery fixture.");
            health.EquippedModules=(ushort)HaloModule.ChargeBridge;
            Require(manager.WeaponRegistry.GetWeaponData(health.EquippedWeaponID,health.EquippedModules).EnergyPerRound>4,
                "Modified weapon must cost more than the fixture's remaining energy.");
            decision=Decide(system,brain,health,map,10);
            Require(brain.Leaving && decision.action==PmcAction.Extract,"PMC ignored the actual module's reload cost.");
        }
        finally {property.SetValue(null,old);Object.DestroyImmediate(root);}
    }

    private static void CheckBackpackMedical(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); brain.Inventory = InventoryGraph.Create(false);
        string pack = brain.Inventory.Equipped("Backpack").Id;
        Require(brain.Inventory.AddSupply("medkit", 1, pack) == InventoryError.None, "Backpack medical fixture.");
        string itemId = brain.Inventory.Items.First(item => item.Code == "medkit").Id;
        var health = Health(); RaidHealth.Damage(ref health, BodyPart.Chest, 65, 1);
        var decision = Decide(system, brain, health, map, 10);
        Require(!brain.Leaving && decision.action == PmcAction.Heal && brain.Inventory.MedicalAccessible(brain.Inventory.Find(itemId)),
            "A safe injured PMC did not move its own backpack medicine to an accessible slot.");
        brain.Action = PmcAction.Heal; brain.TacticalGoal = Vector3.zero;
        Heal(system, brain, ref health, 10); double complete = brain.HealUntil;
        Require(complete > 10 && brain.Inventory.Find(itemId) != null, "PMC treatment consumed backpack medicine early.");
        Heal(system, brain, ref health, complete);
        Require(brain.Inventory.Find(itemId) == null && health.ChestHealth > 20,
            "PMC failed to complete treatment using its own prepared medicine.");

        brain = Brain(); brain.Inventory = InventoryGraph.Create(false); pack = brain.Inventory.Equipped("Backpack").Id;
        Require(brain.Inventory.AddSupply("medkit", 1, pack) == InventoryError.None, "Unsafe backpack medical fixture.");
        itemId = brain.Inventory.Items.First(item => item.Code == "medkit").Id;
        health = Health(); RaidHealth.Damage(ref health, BodyPart.Chest, 65, 2); brain.LastDamage = 10;
        decision = Decide(system, brain, health, map, 10);
        Require(!brain.Leaving && decision.action == PmcAction.Retreat && brain.Inventory.Find(itemId).Parent == pack,
            "A recently hit PMC abandoned its medicine or prepared it under immediate threat.");
        decision = Decide(system, brain, health, map, 20);
        Require(!brain.Leaving && decision.action == PmcAction.Heal && brain.Inventory.MedicalAccessible(brain.Inventory.Find(itemId)),
            "A PMC did not prepare its medicine after reaching a safe opening.");
    }

    private static PredictedPlayerGhost Health()
    {
        var health = default(PredictedPlayerGhost);
        RaidHealth.Initialize(ref health);
        health.CurrentAmmo = 15;
        health.PrimaryAmmo = 15;
        health.EquippedWeaponID = DollSingerWeapons.Halo;
        health.EquippedWeaponSlot = 1;
        return health;
    }

    private static void CheckCoverLifecycle(DollSingerEnemySystem system)
    {
        var brain = Brain();
        brain.Action = PmcAction.Cover;
        brain.HasCover = true;
        brain.CoverPosition = Vector3.zero;
        brain.PeekPosition = Vector3.right * 2;
        brain.CoverPhase = PmcCoverPhase.Approaching;
        brain.CoverPhaseUntil = 30;
        Cover(system, brain, Vector3.left * 5, 10);
        Require(brain.HasCover && brain.CoverPhase == PmcCoverPhase.Approaching && !brain.IsPeeking &&
            brain.TacticalGoal == brain.CoverPosition, "A travelling PMC started peeking before reaching cover.");

        Cover(system, brain, brain.CoverPosition, 10.1);
        double ready = brain.CoverPhaseUntil;
        Require(brain.CoverPhase == PmcCoverPhase.Settling && ready > 10.1 && !brain.IsPeeking,
            "Cover arrival did not start a concealed settling interval.");
        Cover(system, brain, brain.CoverPosition, ready - .01);
        Require(brain.CoverPhase == PmcCoverPhase.Settling && !brain.IsPeeking,
            "The PMC exposed itself before finishing the settling interval.");
        Cover(system, brain, brain.CoverPosition, ready + .01);
        Require(brain.CoverPhase == PmcCoverPhase.Advancing && brain.TacticalGoal == brain.PeekPosition,
            "The PMC did not travel to its peek position after settling.");
        Cover(system, brain, brain.PeekPosition + Vector3.right * .72f, ready + .1);
        Require(brain.CoverPhase == PmcCoverPhase.Holding && brain.IsPeeking,
            "Peek arrival did not start the bounded holding phase.");

        brain.LastDamage = ready + .15;
        brain.BurstUntil = ready + 10;
        Cover(system, brain, brain.PeekPosition, ready + .2);
        Require(brain.CoverPhase == PmcCoverPhase.Returning && !brain.IsPeeking &&
            brain.TacticalGoal == brain.CoverPosition && brain.BurstUntil <= ready + .2,
            "Taking damage did not stop the burst and return the PMC to cover.");
    }

    private static void CheckMedicalLifecycle(DollSingerEnemySystem system)
    {
        var brain = Brain();
        brain.Action = PmcAction.Heal;
        brain.TacticalGoal = Vector3.right * .72f; // Must accept the motor's stopping radius.
        Require(brain.Inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets) == InventoryError.None,
            "Could not prepare accessible medical supplies.");
        var health = Health();
        RaidHealth.Damage(ref health, BodyPart.Chest, 50, 1);
        float injured = health.CurrentHealth;
        int version = brain.Inventory.Version;
        Heal(system, brain, ref health, 10);
        double completedAt = brain.HealUntil;
        Require(completedAt > 10 && health.CurrentHealth == injured && brain.Inventory.Count("medkit") == 2 &&
            brain.Inventory.Version == version, "Starting treatment changed health or consumed medicine immediately.");
        Heal(system, brain, ref health, completedAt - .01);
        Require(health.CurrentHealth == injured && brain.Inventory.Count("medkit") == 2,
            "Treatment completed before its action timer.");
        Heal(system, brain, ref health, completedAt);
        Heal(system, brain, ref health, completedAt);
        Require(Mathf.Approximately(health.CurrentHealth, injured + 40) && brain.Inventory.Count("medkit") == 1 &&
            brain.Inventory.Version == version + 1 && brain.HealUntil == 0 && brain.NextHeal > completedAt,
            "Treatment failed to heal once and atomically consume one carried injector.");

        brain = Brain();
        brain.Action = PmcAction.Heal;
        Require(brain.Inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets) == InventoryError.None,
            "Could not prepare interrupted-treatment supplies.");
        health = Health();
        RaidHealth.Damage(ref health, BodyPart.Chest, 50, 1);
        injured = health.CurrentHealth;
        version = brain.Inventory.Version;
        Heal(system, brain, ref health, 10);
        brain.LastDamage = 10.2;
        Heal(system, brain, ref health, 10.2);
        Require(brain.HealUntil == 0 && brain.HealItemId == null && health.CurrentHealth == injured &&
            brain.Inventory.Count("medkit") == 2 && brain.Inventory.Version == version,
            "A damaged PMC completed treatment or consumed medicine instead of interrupting it.");
    }

    private static void CheckDecisions(DollSingerEnemySystem system, MoonRaidMap map, DollSingerAITuning tuning)
    {
        var brain = Brain();
        var health = Health();
        brain.Action = PmcAction.Scavenge;
        brain.CommitUntil = 40;
        brain.Target = new Entity { Index = 1, Version = 1 };
        brain.LastSeen = Vector3.forward * 5;
        brain.LastSeenTime = brain.VisibleSince = 10;
        brain.Visible = true;
        brain.Confidence = 1;
        brain.Contacts.Add(brain.Target, new PmcContact { Confirmed = true, Visible = true, Confidence = 1 });
        var decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Engage && decision.emergency && brain.CommitUntil > 10,
            "A newly confirmed close opponent failed to interrupt a looting commitment.");

        brain.Action = PmcAction.Extract;
        brain.Leaving = true;
        brain.Visible = false;
        brain.LastSeenTime = brain.LastHeardTime = 3;
        brain.Confidence = .6f;
        decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Extract,
            "A departing PMC pursued a stale remembered contact instead of extracting.");

        brain.Leaving = false;
        brain.Action = PmcAction.Engage;
        brain.LastSeenTime = 10;
        decision = Decide(system, brain, health, map, 10.1);
        Require(decision.action == PmcAction.HoldAngle, "Losing sight skipped the short last-exit hold.");
        brain.Action = PmcAction.HoldAngle;
        decision = Decide(system, brain, health, map, 10 + tuning.HoldAngleSeconds + .1);
        Require(decision.action == PmcAction.Search && decision.emergency,
            "An expired angle hold prevented deliberate search from starting.");
    }

    private static void Cover(DollSingerEnemySystem system, DollSingerEnemyBrain brain, Vector3 position, double now)
        => Call(system, "AdvanceCover", new object[] { brain, position, now });

    private static void CheckGrenadeDecisions(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health();
        brain.Action = PmcAction.Scavenge; brain.CommitUntil = 40;
        var point = Vector3.forward * 4;
        brain.Grenade.Observe(1, point, 8, 14, 10);
        brain.Grenade.Observe(1, point, 8, 14, 10.1);
        brain.Grenade.Observe(1, point, 8, 14, 10.2);
        Require(Decide(system, brain, health, map, 10.2).action != PmcAction.EvadeGrenade,
            "A first glimpse instantly interrupted the PMC.");
        brain.Grenade.Observe(1, point, 8, 14, 10.3);
        var decision = Decide(system, brain, health, map, 10.3);
        Require(decision.action == PmcAction.EvadeGrenade && decision.emergency && brain.CommitUntil > 10.3,
            "A recognized explosive failed to interrupt a committed search.");
        brain.Action = PmcAction.EvadeGrenade; brain.HealUntil = 15; brain.HealItemId = "test";
        Heal(system, brain, ref health, 10.3);
        Require(brain.HealUntil == 0 && brain.HealItemId == null, "Treatment continued while escaping an explosive.");
        health.CurrentAmmo = 0;
        Require(!(bool)Call(system, "ShouldReload", new object[] { brain, health, Vector3.zero, 10.3d }),
            "The PMC started a reload while escaping an explosive.");
        decision = Decide(system, brain, health, map, 14);
        Require(decision.action != PmcAction.EvadeGrenade && decision.emergency,
            "The PMC stayed in explosive evasion after the threat expired.");
        brain.Action = PmcAction.Scavenge; brain.ClashParticipantId = 1;
        Require(Decide(system, brain, health, map, 10.4).action != PmcAction.EvadeGrenade,
            "The raid-only explosive rule changed the other mode.");
    }

    private static void CheckGrenadeVisibility(DollSingerEnemySystem system, World world)
    {
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var actor = new GameObject("Temporary PMC grenade observer");
        var wall = new GameObject("Temporary grenade sight blocker");
        var field = typeof(DollSingerEnemySystem).GetField("m_TacticalWorld", PrivateInstance);
        object previous = field.GetValue(system);
        try
        {
            Vector3 origin = new Vector3(9000, 9000, 9000);
            actor.transform.position = origin;
            wall.transform.position = origin + Vector3.forward * 2 + Vector3.up;
            var blocker = wall.AddComponent<BoxCollider>(); blocker.size = new Vector3(2, 3, .3f); blocker.enabled = false;
            var tactical = new TacticalServerWorld { Now = 1 };
            var explosive = new TacticalServerWorld.Object { Id = 1, Definition = TacticalItemRules.Find("frag_bomb"),
                Phase = TacticalWorldPhase.Flying, Scene = scene.GetPhysicsScene(), Position = origin + Vector3.forward * 4, Until = 10 };
            tactical.Objects.Add(explosive); field.SetValue(system, tactical);
            Vector3 eye = origin + Vector3.up * 1.15f;
            void Sight(DollSingerEnemyBrain brain, double now) => Call(system, "ObserveGrenades", new object[] { brain, actor.transform, eye, now });
            void Watch(DollSingerEnemyBrain brain)
            { Sight(brain, 1); Sight(brain, 1.1); Sight(brain, 1.2); Sight(brain, 1.3); }
            Physics.SyncTransforms();
            var seen = Brain(); Watch(seen);
            Require(seen.Grenade.Active(1.3), "A visible explosive was not recognized.");
            blocker.enabled = true; Physics.SyncTransforms();
            var hidden = Brain(); Watch(hidden);
            Require(!hidden.Grenade.Active(1.3), "The PMC saw an explosive through a wall.");
            Vector3 lastSeen = seen.Grenade.Position;
            explosive.Position += Vector3.right;
            Sight(seen, 1.4);
            Require(seen.Grenade.Active(1.4) && seen.Grenade.Position == lastSeen,
                "The PMC tracked an explosive after it moved behind a wall.");
            blocker.enabled = false; Physics.SyncTransforms();
            explosive.Position = origin - Vector3.forward * 4;
            var behind = Brain(); Watch(behind);
            Require(!behind.Grenade.Active(1.3), "The PMC saw an explosive behind its field of view.");
            explosive.Position = origin + Vector3.forward * 4;
            tactical.Objects.Add(new TacticalServerWorld.Object { Id = 2, Definition = TacticalItemRules.Find("smoke_grenade"),
                Phase = TacticalWorldPhase.Smoke, Scene = scene.GetPhysicsScene(), Position = origin + Vector3.forward * 2, Until = 20 });
            var smoke = Brain(); Watch(smoke);
            Require(!smoke.Grenade.Active(1.3), "The PMC saw an explosive through dense smoke.");

            tactical.Objects.Clear();
            var energy = world.GetOrCreateSystemManaged<EnergyGrenadeServerSystem>();
            var grenadeType = typeof(EnergyGrenadeServerSystem).GetNestedType("Grenade", BindingFlags.NonPublic);
            var energyObject = Activator.CreateInstance(grenadeType, true);
            void SetGrenade(string name, object value) => grenadeType.GetField(name).SetValue(energyObject, value);
            SetGrenade("Id", 1u); SetGrenade("Scene", scene.GetPhysicsScene());
            SetGrenade("Position", origin + Vector3.forward * 3); SetGrenade("Age", 0f);
            var energyObjects = (System.Collections.IList)typeof(EnergyGrenadeServerSystem).GetField("m_Active", PrivateInstance).GetValue(energy);
            energyObjects.Add(energyObject);
            Require(!energy.TryGetLiveThreat(-1, out _, out _, out _, out _) &&
                energy.TryGetLiveThreat(0, out uint energyId, out _, out _, out float remaining) && energyId == 1 && remaining == EnergyGrenadeRules.Fuse,
                "Live energy grenade exposure is not bounded or accurate.");
            var energySeen = Brain(); Watch(energySeen);
            Require(energySeen.Grenade.Active(1.3) && energySeen.Grenade.Radius == EnergyGrenadeRules.Radius,
                "The PMC ignored a visible energy grenade.");
            blocker.enabled = true; Physics.SyncTransforms();
            var energyHidden = Brain(); Watch(energyHidden);
            Require(!energyHidden.Grenade.Active(1.3), "The PMC saw an energy grenade through a wall.");
            blocker.enabled = false; Physics.SyncTransforms();
            var energyClash = Brain(); energyClash.ClashParticipantId = 1; Watch(energyClash);
            Require(!energyClash.Grenade.Active(1.3), "Energy grenade evasion changed bounty mode.");
            SetGrenade("Age", EnergyGrenadeRules.Fuse);
            var expired = Brain(); Watch(expired);
            Require(!expired.Grenade.Active(1.3) && !energy.TryGetLiveThreat(0, out _, out _, out _, out _),
                "A detonated energy grenade remained an active threat.");
            energyObjects.Clear();

            tactical.Objects.Clear();
            var fire = new TacticalServerWorld.Object { Id = 3, Definition = TacticalItemRules.Find("fire_bomb"),
                Phase = TacticalWorldPhase.Fire, Scene = scene.GetPhysicsScene(), Position = origin + Vector3.forward * 2, Until = 10 };
            tactical.Objects.Add(fire);
            var burning = Brain(); burning.SelfStatus = new TacticalServerWorld.Status { Fire = 5 };
            void FireSight(DollSingerEnemyBrain brain) => Call(system, "ObserveHazardArea", new object[] { brain, actor.transform, 1d });
            blocker.enabled = false; Physics.SyncTransforms(); FireSight(burning);
            Require(burning.HazardDanger && burning.HazardId == 3 && burning.HazardPosition == fire.Position && burning.HazardRadius == 3,
                "A burning PMC ignored the active fire covering its position.");
            wall.transform.position = origin + Vector3.forward + Vector3.up;
            blocker.enabled = true; Physics.SyncTransforms(); FireSight(burning);
            Require(!burning.HazardDanger, "A wall-separated fire was treated as contact with the actor.");
            blocker.enabled = false; Physics.SyncTransforms();
            var unburned = Brain(); unburned.SelfStatus = new TacticalServerWorld.Status(); FireSight(unburned);
            Require(!unburned.HazardDanger, "The PMC inferred fire damage before the server applied it.");
            var fireClash = Brain(); fireClash.ClashParticipantId = 1; fireClash.SelfStatus = new TacticalServerWorld.Status { Fire = 5 };
            FireSight(fireClash); Require(!fireClash.HazardDanger, "Fire evasion changed bounty mode.");
            fire.Phase = TacticalWorldPhase.Removed; FireSight(burning);
            Require(!burning.HazardDanger, "An expired fire remained an active hazard.");
            fire.Position = origin + Vector3.forward * .5f;
            fire.Definition = TacticalItemRules.Find("poison_trip_mine"); fire.Phase = TacticalWorldPhase.Poison;
            burning.SelfStatus.Fire = 0; burning.SelfStatus.Poison = 5; FireSight(burning);
            Require(burning.HazardDanger && burning.HazardId == 3, "An affected PMC ignored its active poison area.");
            burning.SelfStatus.Antidote = 6; FireSight(burning);
            Require(!burning.HazardDanger, "An antidote-protected PMC unnecessarily fled a poison area.");
            burning.SelfStatus.Antidote = 0; burning.SelfStatus.Poison = 0; burning.SelfStatus.Wire = 5;
            fire.Definition = TacticalItemRules.Find("concertina_trip_mine"); fire.Phase = TacticalWorldPhase.Wire;
            FireSight(burning);
            Require(burning.HazardDanger, "A PMC affected by wire did not try to leave the trap.");
            fire.Phase = TacticalWorldPhase.Armed; FireSight(burning);
            Require(!burning.HazardDanger, "An untriggered wire trap was treated as an active area.");
        }
        finally
        {
            field.SetValue(system, previous);
            Object.DestroyImmediate(actor); Object.DestroyImmediate(wall); Physics.SyncTransforms();
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CheckFireDecisions(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health();
        brain.Action = PmcAction.Scavenge; brain.CommitUntil = 40; brain.HazardDanger = true;
        brain.SelfStatus = new TacticalServerWorld.Status { Wire = 30, Bleed = 30 };
        var decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.EvadeHazard && decision.emergency,
            "A wire hazard did not interrupt a committed search or prioritize escape over stopping the bleed.");
        brain.Action = PmcAction.EvadeHazard; brain.HealUntil = 15; brain.HealItemId = "test";
        Heal(system, brain, ref health, 10);
        Require(brain.HealUntil == 0 && brain.HealItemId == null &&
            !(bool)Call(system, "ShouldReload", new object[] { brain, health, Vector3.zero, 10d }),
            "Treatment or reload continued while escaping fire.");
        brain.Grenade.Observe(1, Vector3.forward, 8, 14, 10);
        brain.Grenade.Observe(1, Vector3.forward, 8, 14, 10.1);
        brain.Grenade.Observe(1, Vector3.forward, 8, 14, 10.2);
        brain.Grenade.Observe(1, Vector3.forward, 8, 14, 10.3);
        Require(Decide(system, brain, health, map, 10.3).action == PmcAction.EvadeGrenade,
            "An immediate explosive did not take priority over fire.");
        brain.HazardDanger = false; brain.Action = PmcAction.EvadeHazard;
        decision = Decide(system, brain, health, map, 14);
        Require(decision.action != PmcAction.EvadeHazard && decision.emergency,
            "The PMC remained in fire evasion after leaving the area.");
        brain.HazardDanger = true; brain.ClashParticipantId = 1;
        Require(Decide(system, brain, health, map, 14).action != PmcAction.EvadeHazard,
            "The raid fire response changed bounty mode.");
    }

    private static void CheckCombatRecovery(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain();
        var health = Health();
        brain.Target = new Entity { Index = 1, Version = 1 };
        brain.Visible = true; brain.Confidence = 1; brain.LastSeenTime = brain.VisibleSince = 10;
        brain.LastSeen = Vector3.forward * 12;
        brain.Action = PmcAction.Engage; brain.LastDamage = 10;
        brain.Contacts.Add(brain.Target, new PmcContact { Confirmed = true, Visible = true, Confidence = 1 });
        Require(brain.Inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets) == InventoryError.None,
            "Could not prepare a recoverable wounded PMC.");
        RaidHealth.Damage(ref health, BodyPart.Chest, 64, 1);
        var decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Retreat, "A critically wounded PMC did not seek safety.");
        Require(!brain.Leaving, "A treatable wound permanently latched extraction despite carried medicine.");
        var medical = brain.Inventory.Items.Find(i => i.Code == "medkit");
        Require(RaidHealth.UseMedical(ref health, brain.Inventory, medical.Id, brain.Inventory.Version, BodyPart.Chest) == InventoryError.None,
            "The shared medical transaction rejected recovery.");
        brain.Action = PmcAction.Retreat; brain.Suppression = .1f;
        decision = Decide(system, brain, health, map, 12);
        Require(!brain.Leaving && decision.action != PmcAction.Retreat && decision.action != PmcAction.Extract,
            "A treated PMC kept fleeing instead of contesting the visible opponent.");

        brain.LootedCaches = 8;
        decision = Decide(system, brain, health, map, 12);
        Require(brain.Leaving && decision.action == PmcAction.Retreat,
            "Finishing the raid objective no longer allows deliberate extraction under threat.");

        brain = Brain(); health = Health(); brain.LastDamage = 10;
        RaidHealth.Damage(ref health, BodyPart.Chest, 64, 1);
        Decide(system, brain, health, map, 10);
        Require(brain.Leaving, "A critically wounded PMC without medicine abandoned its extraction plan.");
    }

    private static void CheckCombatPressure(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health(); brain.LastHealth = health.CurrentHealth;
        for (int i = 0; i < 2; i++)
        {
            RaidHealth.Damage(ref health, BodyPart.LeftArm, 10, (uint)(i + 1));
            Call(system, "UpdateCombatPressure", new object[] { brain, health, 10d + i * .1, 1f / 60 });
        }
        Require(brain.Suppression > 0 && brain.Suppression < .35f,
            "Two minor wounds saturated pressure and prevented a return peek.");
        brain.Suppression = 1;
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 12.1d, 2f });
        Require(brain.Suppression < .4f, "Pressure remained above the peek threshold after two quiet seconds.");
        brain.Suppression = 0; brain.NextDecision = 100;
        health.ShieldHitSequence++; health.ShieldHitStrength = .8f;
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 13d, 0f });
        float pressure = brain.Suppression;
        Require(pressure > 0 && brain.NextDecision == 0 && brain.LastDamage == 13,
            "A real shield impact failed to interrupt passive behavior.");
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 13d, 0f });
        Require(brain.Suppression == pressure, "An unchanged shield event repeatedly added pressure.");

        brain = Brain(); health = Health();
        RaidHealth.Damage(ref health, BodyPart.LeftArm, 20, 1);
        brain.LastHealth = health.CurrentHealth; brain.NextDecision = 100; brain.BleedOnlyDamage = true;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 2, RaidDamageKind.Bullet);
        RaidHealth.Set(ref health, BodyPart.LeftArm, health.LeftArmHealth + 5);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 15d, 0f });
        Require(brain.LastDamage == 15 && brain.NextDecision == 0,
            "Healing concealed a new bullet impact from the PMC's decision.");
        brain.Action = PmcAction.Scavenge; brain.LootUntil = 30;
        var decision = Decide(system, brain, health, map, 15.1);
        Require(decision.action == PmcAction.Retreat && decision.emergency,
            "A masked impact failed to trigger unseen-attack evasion.");
    }

    private static void CheckUnknownAttack(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health(); brain.Action = PmcAction.Scavenge;
        brain.LastDamage = 10; brain.LootUntil = 20;
        var decision = Decide(system, brain, health, map, 10.1);
        Require(decision.action == PmcAction.Retreat && decision.emergency && !brain.Leaving && brain.Target == Entity.Null,
            "An unseen hit did not interrupt looting for temporary evasion without inventing a target.");
        var origin = new Vector3(900000, 900000, 900000); brain.RaidExit = origin + Vector3.forward * 20;
        Call(system, "Decide", new object[] { brain, origin, health, map, 10.1d });
        Require(brain.Action == PmcAction.Retreat && brain.LootUntil == 0 && brain.Target == Entity.Null &&
            brain.NextUnknownEvasion > 10.1, "Unknown-fire evasion did not cancel the search and bound path retries.");
        double retry = brain.NextUnknownEvasion; var goal = brain.TacticalGoal;
        Call(system, "Decide", new object[] { brain, origin, health, map, 10.2d });
        Require(brain.NextUnknownEvasion == retry && brain.TacticalGoal == goal && brain.Target == Entity.Null,
            "Unseen danger replanned continuously or fabricated an attacker.");
        decision = Decide(system, brain, health, map, 13);
        Require(decision.action != PmcAction.Retreat, "Unseen-fire evasion did not expire after the danger interval.");

        brain = Brain(); brain.Action = PmcAction.Scavenge; brain.LastDamage = 10;
        brain.HasCover = brain.IsPeeking = true; brain.CoverPosition = origin + Vector3.right * 4;
        brain.PeekPosition = origin + Vector3.left * 4;
        Call(system, "Decide", new object[] { brain, origin, health, map, 10.1d });
        Cover(system, brain, origin, 10.2);
        Require(brain.HasCover && !brain.IsPeeking && brain.TacticalGoal == brain.CoverPosition,
            "Unseen-fire evasion did not return to the available cover.");

        brain = Brain(); brain.Action = PmcAction.Scavenge; brain.LastDamage = 10;
        brain.HasCover = brain.PathFailed = true; brain.CoverPosition = origin + Vector3.right * 4;
        brain.RaidExit = origin + Vector3.forward * 20;
        Call(system, "Decide", new object[] { brain, origin, health, map, 10.1d });
        goal = brain.TacticalGoal;
        Cover(system, brain, origin, 10.2);
        Require(!brain.HasCover && brain.TacticalGoal == goal && brain.NextUnknownEvasion > 10.1,
            "Failed cover overrode the fallback evasion path.");
    }

    private static void CheckBleedTreatment(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health();
        brain.SelfStatus = new TacticalServerWorld.Status { Bleed = 40 };
        brain.BleedOnlyDamage = true; brain.LastHealth = health.CurrentHealth;
        var beforeStatus = health;
        RaidHealth.Damage(ref health, BodyPart.LeftArm, 1, 1);
        PmcStatusDamage.Record(brain.SelfStatus, beforeStatus, health);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 10d, .1f });
        Require(brain.LastDamage < 0 && brain.Suppression == 0,
            "Ongoing blood loss was mistaken for a new attack.");
        var decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Heal && !brain.Leaving && brain.Inventory.Count("medkit") == 0,
            "An unarmed wound did not prompt safe self-treatment without medicine.");
        brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 10);
        Require(brain.HealStatus && brain.HealUntil == 13 && brain.SelfStatus.Bleed == 40,
            "Stopping a bleed changed status before its timer completed.");
        beforeStatus = health;
        RaidHealth.Damage(ref health, BodyPart.LeftArm, 1, 2);
        PmcStatusDamage.Record(brain.SelfStatus, beforeStatus, health);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 12.9d, .1f });
        Heal(system, brain, ref health, 12.9);
        Require(brain.HealStatus && brain.SelfStatus.Bleed == 40,
            "Own blood loss interrupted a safe stop-bleeding action.");
        Heal(system, brain, ref health, 13);
        Require(!brain.HealStatus && brain.HealUntil == 0 && brain.SelfStatus.Bleed == 0,
            "The PMC did not finish stopping the bleed once.");

        brain.SelfStatus.Bleed = 60; brain.NextHeal = 0; brain.LastDamage = -100; brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 20);
        RaidHealth.CombatDamage(ref health, BodyPart.RightArm, 10, 3, RaidDamageKind.Bullet);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 21d, .1f });
        Heal(system, brain, ref health, 21);
        Require(brain.LastDamage == 21 && brain.HealUntil == 0 && brain.SelfStatus.Bleed == 60,
            "A fresh bullet did not interrupt the PMC's stop-bleeding action.");
    }

    private static void CheckFireTreatment(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health();
        brain.SelfStatus = new TacticalServerWorld.Status { Fire = 30, Bleed = 30 };
        brain.LastHealth = health.CurrentHealth;
        var before = health;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 1, RaidDamageKind.Fire);
        PmcStatusDamage.Record(brain.SelfStatus, before, health);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 10d, .1f });
        Require(brain.LastDamage < 0 && Decide(system, brain, health, map, 10).action == PmcAction.Heal,
            "A safe burning PMC could not choose self-treatment.");
        brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 10);
        Require(brain.HealStatus && brain.HealUntil == 13 && brain.SelfStatus.Fire == 30 && brain.SelfStatus.Bleed == 30,
            "Self-treatment removed fire or bleeding before the timer finished.");
        before = health;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 2, RaidDamageKind.Fire);
        PmcStatusDamage.Record(brain.SelfStatus, before, health);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 12d, .1f });
        Heal(system, brain, ref health, 12);
        Require(brain.HealStatus && brain.SelfStatus.Fire == 30 && brain.SelfStatus.Bleed == 30,
            "Own continuing fire stopped self-treatment.");
        Heal(system, brain, ref health, 13);
        Require(!brain.HealStatus && brain.SelfStatus.Fire == 0 && brain.SelfStatus.Bleed == 0,
            "Completed self-treatment did not clear both statuses.");

        brain.SelfStatus.Fire = 50; brain.NextHeal = 0; brain.LastDamage = -100; brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 20);
        before = health;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 3, RaidDamageKind.Fire);
        PmcStatusDamage.Record(brain.SelfStatus, before, health);
        RaidHealth.CombatDamage(ref health, BodyPart.RightArm, 5, 3, RaidDamageKind.Bullet);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 21d, .1f });
        Heal(system, brain, ref health, 21);
        Require(brain.HealUntil == 0 && brain.SelfStatus.Fire == 50 && brain.LastDamage == 21,
            "A same-tick bullet did not interrupt fire self-treatment.");
        brain.HazardDanger = true; brain.LastDamage = -100; brain.NextHeal = 0; brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 22);
        Require(brain.HealUntil == 0, "The PMC tried to extinguish itself inside active fire.");
    }

    private static void CheckReturnFire(DollSingerEnemySystem system, MoonRaidMap map, World world)
    {
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var source = new GameObject("Temporary PMC return-fire source") { hideFlags = HideFlags.HideInHierarchy };
        var target = new GameObject("Temporary PMC sight target") { hideFlags = HideFlags.HideInHierarchy };
        var wall = new GameObject("Temporary PMC sight blocker") { hideFlags = HideFlags.HideInHierarchy };
        try
        {
            // No player prefab, nav bake or connection: exercise the real motor and sight query.
            Vector3 origin = new Vector3(9000, 9000, 9000);
            source.transform.position = origin; target.transform.position = origin + Vector3.forward * 12;
            target.layer = LayerMask.NameToLayer("ServerPlayer");
            var capsule = target.AddComponent<CapsuleCollider>(); capsule.height = 1.8f; capsule.radius = .3f; capsule.center = Vector3.up * .9f;
            var linked = target.AddComponent<GhostGameObject>();
            var entity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = linked });
            var actor = source.AddComponent<PlayerGhost>();
            var eye = new GameObject("Temporary shot origin").transform; eye.SetParent(source.transform, false); eye.localPosition = Vector3.up * 1.15f;
            typeof(PlayerGhost).GetField("<ShotOrigin>k__BackingField", PrivateInstance).SetValue(actor, eye);
            wall.transform.position = origin + Vector3.forward * 6 + Vector3.up;
            var blocker = wall.AddComponent<BoxCollider>(); blocker.size = new Vector3(2, 3, .3f); blocker.enabled = false;
            UnityEngine.Physics.SyncTransforms();

            PlayerInput Input(PmcAction action, bool visible = true, bool heal = false, int ammo = 15, bool exhausted = false)
            {
                var brain = Brain(); var health = Health(); health.CurrentAmmo = ammo;
                health.TacticalSprintExhausted = exhausted;
                brain.Action = action; brain.Target = entity; brain.Visible = visible; brain.Confidence = 1;
                brain.LastSeen = target.transform.position; brain.VisibleSince = 8; brain.LastSeenTime = 10;
                brain.LastHealth = health.CurrentHealth;
                brain.NextDecision = brain.NextSense = brain.NextPath = brain.NextAimError = 100;
                brain.Goal = brain.TacticalGoal = origin - Vector3.forward * 3;
                brain.CornerCount = 2; brain.Corner = 1; brain.Corners[0] = origin; brain.Corners[1] = brain.Goal;
                if (heal) { brain.HealUntil = 15; brain.Interacting = true; }
                return (PlayerInput)Call(system, "Think", new object[] { brain, actor,
                    Unity.Transforms.LocalTransform.FromPosition(origin), health, map, 10d, 1f / 60 });
            }
            var retreat = Input(PmcAction.Retreat);
            Require(retreat.Aim && retreat.Shoot && !retreat.Sprint && retreat.MoveInput.y < 0,
                "A withdrawing PMC failed to walk backward and return fire at a visible opponent.");
            var noCover = Input(PmcAction.Cover);
            Require(noCover.Aim && noCover.Shoot, "A failed cover search left the PMC unable to fight in the open.");
            Require(!Input(PmcAction.Retreat, visible: false).Shoot, "Withdrawal shot at a contact that was no longer visible.");
            blocker.enabled = true; UnityEngine.Physics.SyncTransforms();
            Require(!Input(PmcAction.Retreat).Shoot, "Return fire bypassed physical cover.");
            blocker.enabled = false; UnityEngine.Physics.SyncTransforms();
            Require(!Input(PmcAction.Heal, heal: true).Shoot, "Treatment/interaction allowed simultaneous fire.");
            var empty = Input(PmcAction.Retreat, ammo: 0);
            Require(!empty.Shoot && empty.Sprint, "An empty withdrawing PMC could not sprint to safety.");
            var grenade = Input(PmcAction.EvadeGrenade);
            Require(!grenade.Shoot && grenade.Sprint, "Explosive evasion did not sprint without firing.");
            var fire = Input(PmcAction.EvadeHazard, exhausted: true);
            Require(!fire.Shoot && fire.Sprint, "Fire evasion did not attempt to sprint without firing.");
            Require(!Input(PmcAction.Retreat, visible: false, exhausted: true).Sprint,
                "An exhausted PMC kept sprinting during an ordinary retreat.");
            Require(Input(PmcAction.EvadeGrenade, exhausted: true).Sprint,
                "An exhausted PMC could not attempt to escape a grenade.");

            var looting = Brain(); var lootingHealth = Health();
            looting.Action = PmcAction.Scavenge; looting.Cache = -1; looting.LootUntil = 15;
            looting.LootLookPoint = origin + Vector3.right * 3; looting.LastHealth = lootingHealth.CurrentHealth;
            looting.NextDecision = looting.NextSense = looting.NextPath = looting.NextAimError = looting.NextNearbyLootScan = 100;
            looting.Goal = looting.TacticalGoal = origin;
            var lootInput = (PlayerInput)Call(system, "Think", new object[] { looting, actor,
                Unity.Transforms.LocalTransform.FromPosition(origin), lootingHealth, map, 10d, 1f / 60 });
            Require(lootInput.LookYawPitchDegrees.x > 0 && lootInput.LookYawPitchDegrees.y > 0 &&
                lootInput.MoveInput.x == 0 && lootInput.MoveInput.y == 0 && !lootInput.Shoot,
                "A searching PMC failed to look toward its loot while remaining stationary.");

            var property = typeof(WeaponManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var previous = property.GetValue(null);
            var managerObject = new GameObject("Temporary PMC tactical weapon registry") { hideFlags = HideFlags.HideAndDontSave };
            managerObject.SetActive(false);
            try
            {
                var manager = managerObject.AddComponent<WeaponManager>();
                manager.WeaponRegistry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
                Require(manager.WeaponRegistry != null, "Tactical weapon registry is missing.");
                property.SetValue(null, manager);
                target.transform.position = origin + Vector3.forward * 8; UnityEngine.Physics.SyncTransforms();
                var brain = Brain(); var health = Health(); health.InventoryWeapons = true;
                health.PrimaryWeaponID = DollSingerWeapons.Halo; health.PrimaryAmmo = health.CurrentAmmo = 30;
                health.SecondaryWeaponID = DollSingerWeapons.Shotgun; health.SecondaryAmmo = 4;
                brain.Action = PmcAction.Engage; brain.Target = entity; brain.Visible = true; brain.Confidence = 1;
                brain.LastSeen = target.transform.position; brain.VisibleSince = 8; brain.LastSeenTime = 10;
                brain.LastHealth = health.CurrentHealth; brain.NextDecision = brain.NextSense = brain.NextPath = brain.NextAimError = 100;
                brain.Goal = brain.TacticalGoal = origin;
                PlayerInput Think(double now) => (PlayerInput)Call(system, "Think", new object[] { brain, actor,
                    Unity.Transforms.LocalTransform.FromPosition(origin), health, map, now, 1f / 60 });
                var close = Think(10);
                Require(close.EquipRevolver && !close.Shoot && !close.Reload && brain.NextTacticalSwitch == 13 &&
                    DollSingerWeapons.TryEquip(ref health, close, manager.WeaponRegistry) &&
                    health.EquippedWeaponSlot == 2 && health.EquippedWeaponID == DollSingerWeapons.Shotgun && health.CurrentAmmo == 4,
                    "Close combat did not request a real loaded shotgun switch without shooting or charging.");
                health.WeaponEquipRemaining = 0; // The first switch completed before the next observation.
                target.transform.position = origin + Vector3.forward * 25; brain.LastSeen = target.transform.position;
                UnityEngine.Physics.SyncTransforms();
                Require(!Think(11).EquipHalo, "A distance change bypassed the tactical switch interval.");
                var distant = Think(13.1);
                Require(distant.EquipHalo && !distant.Shoot && !distant.Reload &&
                    DollSingerWeapons.TryEquip(ref health, distant, manager.WeaponRegistry) &&
                    health.EquippedWeaponSlot == 1 && health.EquippedWeaponID == DollSingerWeapons.Halo && health.CurrentAmmo == 30,
                    "Distant combat did not return to the real loaded rifle after the interval.");
            }
            finally { property.SetValue(null, previous); Object.DestroyImmediate(managerObject); }
        }
        finally
        {
            Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(wall);
            UnityEngine.Physics.SyncTransforms();
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Heal(DollSingerEnemySystem system, DollSingerEnemyBrain brain, ref PredictedPlayerGhost health, double now)
    {
        var args = new object[] { brain, health, Vector3.zero, now };
        Call(system, "AdvanceHealing", args);
        health = (PredictedPlayerGhost)args[1];
    }

    private static (PmcAction action, bool emergency) Decide(DollSingerEnemySystem system, DollSingerEnemyBrain brain,
        PredictedPlayerGhost health, MoonRaidMap map, double now)
    {
        var args = new object[] { brain, Vector3.zero, health, map, now, PmcAction.Scavenge, 0f, null, false };
        Call(system, "EvaluateDecision", args);
        return ((PmcAction)args[5], (bool)args[8]);
    }

    private static object Call(DollSingerEnemySystem system, string name, object[] args)
    {
        var method = typeof(DollSingerEnemySystem).GetMethod(name, PrivateInstance | BindingFlags.Static);
        Require(method != null, "Missing production action entry: " + name);
        try { return method.Invoke(system, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { throw new InvalidOperationException("PMC action check failed in " + name, error.InnerException); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("PMC check: " + message); }
}
