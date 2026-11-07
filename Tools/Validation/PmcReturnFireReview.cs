using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = Unity.MP_FPS.BodyPart;

// Real decision, motor, sight and weapon entry points. No Play mode or transport.
public static class PmcReturnFireReview
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Regression()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor in Edit Mode.");
        var passed = new List<string>();
        var failures = new List<string>();
        void Check(string name, Action run)
        {
            try { run(); passed.Add(name); }
            catch (Exception error) { failures.Add(name + ": " + (error.GetBaseException().Message)); }
        }
        var holder = new GameObject("Temporary return-fire regression") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
        try
        {
            var map = holder.AddComponent<MoonRaidMap>();
            using var world = new World("PMC return-fire regression", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            typeof(DollSingerEnemySystem).GetField("m_Tuning", PrivateInstance).SetValue(system, tuning);
            void Existing(string name, params object[] args) => Check(name, () =>
                typeof(DollSingerHumanlikeChecks).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args));
            Existing("CheckCoverLifecycle", system);
            Existing("CheckDecisions", system, map, tuning);
            Existing("CheckCombatRecovery", system, map);
            Existing("CheckCombatPressure", system, map);
            Existing("CheckUnknownAttack", system, map);
            Existing("CheckGrenadeDecisions", system, map);
            Existing("CheckFireDecisions", system, map);
            Existing("CheckGrenadeVisibility", system, world);
            Existing("CheckReturnFire", system, map, world);
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(tuning); }
        foreach (var type in new[] { typeof(PmcWeaponTacticsChecks), typeof(PmcUnknownAttackChecks),
            typeof(PmcSprintChecks), typeof(PmcSelfTreatmentChecks), typeof(PmcGrenadeAwarenessChecks),
            typeof(PmcFireSafetyChecks), typeof(PmcReloadFailureChecks), typeof(BountyClashBotAIChecks) })
            Check(type.Name, () => type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, null));
        return new { success = failures.Count == 0, passed, failures };
    }

    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor in Edit Mode.");
        int checks = 0;
        var failures = new List<string>();
        void Check(bool value, string message) { checks++; if (!value) failures.Add(message); }
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var holder = new GameObject("Temporary PMC return-fire review") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        var source = new GameObject("Temporary PMC source");
        var target = new GameObject("Temporary PMC target");
        var wall = new GameObject("Temporary PMC blocker");
        var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
        var instance = typeof(WeaponManager).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
        var oldManager = instance.GetValue(null);
        try
        {
            var manager = holder.AddComponent<WeaponManager>();
            manager.WeaponRegistry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
            instance.SetValue(null, manager);
            var weapon = manager.WeaponRegistry.GetWeaponData(DollSingerWeapons.Halo);
            var map = holder.AddComponent<MoonRaidMap>();
            using var world = new World("PMC return-fire review", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            typeof(DollSingerEnemySystem).GetField("m_Tuning", PrivateInstance).SetValue(system, tuning);
            object Call(string name, params object[] args) => typeof(DollSingerEnemySystem)
                .GetMethod(name, PrivateInstance).Invoke(system, args);
            var self = world.EntityManager.CreateEntity();
            var opponent = world.EntityManager.CreateEntity();
            Vector3 origin = Vector3.one * 9000;
            source.transform.position = origin;
            target.transform.position = origin + Vector3.forward * 12;
            target.layer = LayerMask.NameToLayer("ServerPlayer");
            var capsule = target.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f; capsule.radius = .3f; capsule.center = Vector3.up * .9f;
            var linked = target.AddComponent<GhostGameObject>();
            world.EntityManager.AddComponentObject(opponent, new GhostGameObjectLink { LinkedInstance = linked });
            var actor = source.AddComponent<PlayerGhost>();
            var eye = new GameObject("Temporary shot origin").transform;
            eye.SetParent(source.transform, false); eye.localPosition = Vector3.up * 1.15f;
            typeof(PlayerGhost).GetField("<ShotOrigin>k__BackingField", PrivateInstance).SetValue(actor, eye);
            wall.transform.position = origin + Vector3.forward * 6 + Vector3.up;
            var blocker = wall.AddComponent<BoxCollider>(); blocker.size = new Vector3(2, 3, .3f);
            blocker.enabled = false;
            Physics.SyncTransforms();

            PredictedPlayerGhost Health()
            {
                var health = default(PredictedPlayerGhost);
                RaidHealth.Initialize(ref health);
                health.HaloEquipped = health.HaloEnergyInitialized = true;
                health.HaloEnergy = health.HaloCapacity = 100;
                health.PrimaryWeaponID = health.EquippedWeaponID = DollSingerWeapons.Halo;
                health.EquippedWeaponSlot = 1;
                health.CurrentAmmo = health.PrimaryAmmo = weapon.MagazineSize;
                health.WeaponCooldown = 10;
                return health;
            }
            DollSingerEnemyBrain Brain(PmcAction action = PmcAction.Engage)
            {
                var brain = new DollSingerEnemyBrain
                {
                    Inventory = InventoryGraph.Create(stash: false, starter: false),
                    Random = new Unity.Mathematics.Random(137), Seed = 1, Aggression = .4f, Caution = .8f,
                    Action = action, ActionStarted = 2, Target = opponent, Visible = true, Confidence = 1,
                    LastSeen = target.transform.position, AimOffset = Vector3.up * 1.15f,
                    VisibleSince = 8, LastSeenTime = 10, LastHealth = Health().CurrentHealth,
                    NextDecision = 100, NextSense = 100, NextPath = 100, NextAimError = 100,
                    NextNearbyLootScan = 100, NextShieldSupplyCheck = 100, NextOxygenSupplyCheck = 100,
                    NextTacticalSwitch = 100, Goal = origin - Vector3.forward * 3,
                    TacticalGoal = origin - Vector3.forward * 3, CornerCount = 2, Corner = 1
                };
                brain.Corners[0] = origin; brain.Corners[1] = brain.Goal;
                brain.Contacts.Add(opponent, new PmcContact { Confirmed = true, Visible = true, Confidence = 1 });
                return brain;
            }
            PmcAction Decide(DollSingerEnemyBrain brain, PredictedPlayerGhost health)
            {
                var args = new object[] { brain, origin, health, map, 10d, PmcAction.Scavenge, 0f, null, false };
                Call("EvaluateDecision", args);
                return (PmcAction)args[5];
            }
            PlayerInput Think(DollSingerEnemyBrain brain, ref PredictedPlayerGhost health, double now = 10)
            {
                var args = new object[] { self, brain, actor, LocalTransform.FromPosition(origin), health, map, now, 1f / 60 };
                var input = (PlayerInput)Call("Think", args);
                health = (PredictedPlayerGhost)args[4];
                return input;
            }

            for (int personality = 0; personality < 3; personality++)
            {
                var brain = Brain(PmcAction.Scavenge);
                brain.Aggression = .4f + personality * .2f; brain.Caution = .8f - personality * .2f;
                brain.LastDamage = 9.5; brain.Suppression = .35f;
                Check(Decide(brain, Health()) == PmcAction.Engage, $"personality {personality}: minor damage displaced counterfire");
                brain.Action = PmcAction.Engage; brain.LastDamage = -100; brain.Suppression = 0;
                Check(Decide(brain, Health()) == PmcAction.Engage, $"personality {personality}: a visible target lost priority to routine flanking");
                brain.ActionStarted = 0; brain.BurstUntil = 10.3; brain.NextBurst = 11;
                Check(Decide(brain, Health()) == PmcAction.Engage, $"personality {personality}: flanking interrupted an active burst");
                brain.BurstUntil = 9.8;
                Check(Decide(brain, Health()) == PmcAction.Flank, $"personality {personality}: a later quiet burst pause cannot reposition");
                brain.Action = PmcAction.Flank; brain.ActionStarted = 9; brain.LastDamage = 9.5;
                Check(Decide(brain, Health()) == PmcAction.Engage, $"personality {personality}: fresh attack did not interrupt flanking");
                brain.Action = PmcAction.Engage;
                brain.Suppression = .9f;
                Check(Decide(brain, Health()) == PmcAction.Cover, $"personality {personality}: heavy suppression no longer requests cover");
            }
            var injured = Health(); RaidHealth.Damage(ref injured, BodyPart.Chest, 70, 1);
            Check(Decide(Brain(), injured) == PmcAction.Retreat, "critical injury no longer wins over firing");
            var empty = Health(); empty.HaloEnergy = 0; empty.CurrentAmmo = empty.PrimaryAmmo = 0;
            Check(Decide(Brain(), empty) == PmcAction.Retreat, "no-energy actor chose a firefight");
            var danger = Brain(); danger.HazardDanger = true;
            Check(Decide(danger, Health()) == PmcAction.EvadeHazard, "hazard escape lost priority");
            danger = Brain(); danger.Grenade.Observe(1, origin, 5, 15, 9.5);
            danger.Grenade.Observe(1, origin, 5, 15, 9.7); danger.Grenade.Observe(1, origin, 5, 15, 9.8);
            Check(Decide(danger, Health()) == PmcAction.EvadeGrenade, "grenade escape lost priority");
            var hearing = Brain(); hearing.Visible = false; hearing.LastSeenTime = -100;
            hearing.Contacts[opponent].Confirmed = false;
            Check(Decide(hearing, Health()) == PmcAction.Investigate, "a sound became a confirmed target");

            foreach (var action in new[] { PmcAction.Engage, PmcAction.Retreat, PmcAction.Flank, PmcAction.Cover })
            {
                var brain = Brain(action); var health = Health();
                HaloEnergyRules.SetDefense(ref health, true);
                var input = Think(brain, ref health);
                Check(input.Aim && input.Shoot && !input.HaloDefense && !input.Sprint,
                    action + ": defense displaced a valid shot");
            }
            foreach (PmcCoverPhase phase in Enum.GetValues(typeof(PmcCoverPhase)))
            {
                var brain = Brain(PmcAction.Cover); var health = Health();
                brain.HasCover = true; brain.CoverPhase = phase; brain.CoverPhaseUntil = brain.CoverExposureUntil = 30;
                brain.CoverPosition = origin - Vector3.forward * 4; brain.PeekPosition = origin + Vector3.right * 2;
                var input = Think(brain, ref health);
                Check(input.Shoot && !input.HaloDefense, phase + ": cover state blocked a visible exposed shot");
            }
            var defensive = Brain(PmcAction.Retreat); var shield = Health();
            defensive.VisibleSince = 9.9;
            var waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && waiting.HaloDefense && defensive.NextBurst == 0, "reaction time spent a burst or disabled defense");
            defensive = Brain(PmcAction.Retreat); shield = Health(); defensive.Yaw = 180;
            waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && waiting.HaloDefense && defensive.NextBurst == 0, "turning spent a burst or fired backward");
            defensive = Brain(PmcAction.Retreat); shield = Health(); defensive.NextBurst = 11;
            waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && waiting.HaloDefense && defensive.NextBurst == 11, "burst pause bypassed cadence or defense");
            blocker.enabled = true; Physics.SyncTransforms();
            defensive = Brain(PmcAction.Retreat); shield = Health(); waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && waiting.HaloDefense && defensive.NextBurst == 0, "wall did not block firing");
            blocker.enabled = false; Physics.SyncTransforms();
            defensive = Brain(PmcAction.Retreat); defensive.Visible = false; shield = Health(); waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && defensive.NextBurst == 0, "lost visual contact still started a burst");
            defensive = Brain(PmcAction.Retreat); shield = Health(); shield.WeaponEquipRemaining = .4f;
            waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && defensive.NextBurst == 0, "weapon equip time consumed a burst");
            defensive = Brain(PmcAction.Retreat); shield = Health(); shield.ControllerState.IsReloadingState = true;
            shield.HaloRechargeEnergy = 30;
            waiting = Think(defensive, ref shield);
            Check(!waiting.Shoot && defensive.NextBurst == 0, "charging consumed a burst");
            foreach (var action in new[] { PmcAction.Heal, PmcAction.EvadeHazard, PmcAction.EvadeGrenade })
            {
                var brain = Brain(action); var health = Health();
                if (action == PmcAction.Heal) brain.Interacting = true;
                Check(!Think(brain, ref health).Shoot, action + ": urgent activity allowed firing");
            }

            // Include raising the real halo after defense; input flags alone miss zero-shot bursts.
            var firing = Brain(PmcAction.Retreat); var live = Health();
            HaloEnergyRules.SetDefense(ref live, true);
            int shots = 0, defenseTicks = 0; double firstShot = -1;
            for (int frame = 0; frame < 120; frame++)
            {
                double now = 10 + frame / 60d;
                var input = Think(firing, ref live, now);
                HaloEnergyRules.SetDefense(ref live, input.HaloDefense);
                if (input.HaloDefense) defenseTicks++;
                live.WeaponCooldown += 1f / 60;
                if (!HaloModuleFire.TryFire(ref live, weapon, input, false, 1f / 60)) continue;
                if (firstShot < 0) firstShot = now - 10;
                shots++;
                HaloModuleFire.CommitShot(ref live, weapon, 1f / 60);
                HaloEnergyRules.Spend(ref live, weapon.EnergyPerRound);
            }
            Check(shots > 0 && firstShot < .6, "real weapon failed to return fire promptly after releasing defense");
            Check(defenseTicks > 0, "return-fire cycle discarded defensive pauses");
            Check(live.HaloEnergy < 100, "return fire bypassed real energy consumption");

            // Put a teammate just outside the ideal sight ray but inside the shot lane.
            var allyEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentObject(allyEntity, new GhostGameObjectLink { LinkedInstance = wall.AddComponent<GhostGameObject>() });
            wall.layer = LayerMask.NameToLayer("ServerPlayer");
            wall.transform.position = origin + Vector3.right * .35f + Vector3.forward * 6 + Vector3.up * .9f;
            blocker.size = new Vector3(.6f, 1.8f, .6f); blocker.enabled = true;
            Physics.SyncTransforms();
            var agents = (System.Collections.IList)typeof(DollSingerEnemySystem).GetField("m_ClashAgents", PrivateInstance).GetValue(system);
            var agentType = typeof(DollSingerEnemySystem).GetNestedType("ClashAgent", BindingFlags.NonPublic);
            var ally = Activator.CreateInstance(agentType);
            var allyBrain = Brain(); allyBrain.ClashParticipantId = 2; allyBrain.ClashTeamId = 1;
            agentType.GetField("Entity").SetValue(ally, allyEntity);
            agentType.GetField("Brain").SetValue(ally, allyBrain);
            agentType.GetField("View").SetValue(ally, new ClashBotView { ParticipantId = 2, TeamId = 1, Life = ClashLife.Alive });
            agents.Add(ally);
            var squad = Brain(); squad.ClashParticipantId = 1; squad.ClashTeamId = 1;
            var squadHealth = Health(); squadHealth.ClashEnabled = true; squadHealth.ClashLife = ClashLife.Alive;
            Check((bool)Call("ClearSight", source.transform, eye.position, squad.LastSeen + squad.AimOffset, opponent),
                "teammate fixture blocked the ideal sight ray");
            Check(!Think(squad, ref squadHealth).Shoot && squad.NextBurst == 0, "higher return-fire priority ignored a teammate in the shot lane");
            blocker.enabled = false; Physics.SyncTransforms();
            Check(Think(squad, ref squadHealth).Shoot, "Clash actor did not resume fire after its teammate cleared the lane");
            return new { passed = failures.Count == 0, checks, failures, shots, firstShot, defenseTicks,
                mode = "Edit Mode isolated server World; no Play mode, transport or gameplay acceptance" };
        }
        finally
        {
            instance.SetValue(null, oldManager);
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(holder);
            UnityEngine.Object.DestroyImmediate(tuning);
            Physics.SyncTransforms();
            SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
