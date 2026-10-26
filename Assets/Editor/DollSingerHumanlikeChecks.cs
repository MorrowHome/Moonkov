using System;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            CheckCombatRecovery(system, map);
            CheckCombatPressure(system);
            CheckBleedTreatment(system, map);
            CheckReturnFire(system, map, world);
            CheckReloadAvailability(system, map);
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

    private static void CheckCombatPressure(DollSingerEnemySystem system)
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
    }

    private static void CheckBleedTreatment(DollSingerEnemySystem system, MoonRaidMap map)
    {
        var brain = Brain(); var health = Health();
        brain.SelfStatus = new TacticalServerWorld.Status { Bleed = 40 };
        brain.BleedOnlyDamage = true; brain.LastHealth = health.CurrentHealth;
        RaidHealth.Damage(ref health, BodyPart.LeftArm, 1, 1);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 10d, .1f });
        Require(brain.LastDamage < 0 && brain.Suppression == 0,
            "Ongoing blood loss was mistaken for a new attack.");
        var decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Heal && !brain.Leaving && brain.Inventory.Count("medkit") == 0,
            "An unarmed wound did not prompt safe self-treatment without medicine.");
        brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 10);
        Require(brain.HealBleeding && brain.HealUntil == 13 && brain.SelfStatus.Bleed == 40,
            "Stopping a bleed changed status before its timer completed.");
        RaidHealth.Damage(ref health, BodyPart.LeftArm, 1, 2);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 12.9d, .1f });
        Heal(system, brain, ref health, 12.9);
        Require(brain.HealBleeding && brain.SelfStatus.Bleed == 40,
            "Own blood loss interrupted a safe stop-bleeding action.");
        Heal(system, brain, ref health, 13);
        Require(!brain.HealBleeding && brain.HealUntil == 0 && brain.SelfStatus.Bleed == 0,
            "The PMC did not finish stopping the bleed once.");

        brain.SelfStatus.Bleed = 60; brain.NextHeal = 0; brain.LastDamage = -100; brain.Action = PmcAction.Heal;
        Heal(system, brain, ref health, 20);
        RaidHealth.CombatDamage(ref health, BodyPart.RightArm, 10, 3, RaidDamageKind.Bullet);
        Call(system, "UpdateCombatPressure", new object[] { brain, health, 21d, .1f });
        Heal(system, brain, ref health, 21);
        Require(brain.LastDamage == 21 && brain.HealUntil == 0 && brain.SelfStatus.Bleed == 60,
            "A fresh bullet did not interrupt the PMC's stop-bleeding action.");
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

            PlayerInput Input(PmcAction action, bool visible = true, bool heal = false, int ammo = 15)
            {
                var brain = Brain(); var health = Health(); health.CurrentAmmo = ammo;
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
