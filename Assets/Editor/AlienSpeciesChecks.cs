using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    public static void RunRoofAmbushContract()
    {
        var settings = Object.FindAnyObjectByType<AlienEncounterSettings>();
        AlienEncounterSettings.Spawn slot = null;
        for (int i = 0; settings && i < settings.Count; i++) if (settings[i].Ambush) { slot = settings[i]; break; }
        if (slot == null) throw new InvalidOperationException("Formal roof encounter missing");
        using var world = new World("MC00 roof ambush fixture", WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager; var time = manager.CreateEntity(typeof(NetworkTime));
        manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary real roof encounter");
        var profile = Object.Instantiate(slot.Profile); profile.Liquid = false; profile.AttackCooldown = 30;
        try
        {
            if (!Physics.Raycast(slot.Position - Vector3.up * .6f, Vector3.down, out var floor, 10, LayerMask.GetMask("Default", "Ground")))
                throw new InvalidOperationException("No floor beneath perch");
            Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0, slot.Yaw, 0) * Vector3.forward, slot.SurfaceNormal).normalized;
            var actor = Alien(holder.transform, world, profile, slot.Position + slot.SurfaceNormal * .56f * profile.Scale,
                MultiplayerRole.Server, -15000, true, Quaternion.LookRotation(forward, slot.SurfaceNormal));
            var target = Humanoid(holder.transform, world, floor.point, false, 42);
            bool windup = false, struck = false, returning = false, roof = false;
            for (int frame = 0; frame < 540; frame++)
            {
                Step(actor, 1);
                windup |= actor.Action == AlienAction.Windup;
                struck |= actor.Action == AlienAction.Strike;
                returning |= actor.Action == AlienAction.Reposition;
                if (returning && actor.Body.Motor.Current.Up.y < -.8f) { roof = true; break; }
            }
            Require(windup && struck && actor.DamageCount == 1, "Roof spider did not pounce once at the player beneath it");
            Require(roof, "Roof spider failed to return to a ceiling after pouncing: action=" + actor.Action +
                " position=" + actor.transform.position + " up=" + actor.Body.Motor.Current.Up +
                " detached=" + actor.Body.Motor.Detached + " transitions=" + actor.Body.Motor.Transitions +
                " goal=" + actor.RepositionGoal + " navigation=" + actor.NavigationFailure);
            Debug.Log("Actual MC00 roof ambush passed: ceiling hide, downward pounce, one hit, ceiling return.");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(profile); }
    }
    public static void RunBossAndLootContract()
    {
        using var world = new World("Boss phase and loot fixtures", WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        var time = manager.CreateEntity(typeof(NetworkTime)); manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary boss and loot fixture");
        var profile = ScriptableObject.CreateInstance<AlienCreatureProfile>();
        try
        {
            profile.ApplyDefaults(AlienSpecies.Tentacle); profile.Boss = true; profile.Damage = 1;
            profile.AttackCooldown = .3f; profile.PatrolRadius = 0; profile.Liquid = false;
            Vector3 origin = new Vector3(-9500, 1000, -9500);
            var floor = new GameObject("Temporary loot floor"); floor.transform.SetParent(holder.transform);
            floor.transform.position = origin - Vector3.up * .25f; floor.AddComponent<BoxCollider>().size = new Vector3(30, .5f, 30);
            Physics.SyncTransforms();
            var actor = Alien(holder.transform, world, profile, origin + Vector3.up * .56f * profile.Scale, MultiplayerRole.Server, -14000);
            Humanoid(holder.transform, world, origin + Vector3.forward * 2.25f, false, 42);
            bool sweep = false, slam = false, enraged = false, changedHealth = false;
            for (int i = 0; i < 240; i++)
            {
                Step(actor, 1);
                sweep |= actor.Action == AlienAction.Strike && actor.AttackKind == AlienAttackKind.TentacleSweep;
                slam |= actor.Action == AlienAction.Strike && actor.AttackKind == AlienAttackKind.TentacleSlam;
                enraged |= actor.BossPhase == 2;
                if (sweep && !changedHealth && actor.Action == AlienAction.Recover)
                { SetHealth(manager, actor.GhostGameObject.LinkedEntity, 40); changedHealth = true; }
            }
            Require(sweep && slam && enraged, "Boss did not alternate sweep/slam across its health phase");
            var lootEntity = manager.CreateEntity(typeof(RaidLootWorld)); var loot = new RaidLootContainers(); loot.Initialize(1);
            manager.AddComponentObject(lootEntity, loot);
            SetHealth(manager, actor.GhostGameObject.LinkedEntity, 0); Step(actor, 2);
            Require(loot.DeathBags.Count == 1, "Boss loot did not drop exactly once");
            foreach (var bag in loot.DeathBags)
            {
                var graph = loot.Containers[bag.Key];
                Require(bag.Value.CharacterIndex == -1 && graph.Validate() == InventoryError.None, "Alien remains bag is invalid");
                Require(graph.Count("alien_tissue") == 5 && graph.Count("alien_core") == 1, "Boss reward contents missing");
                var decoded = RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(graph));
                Require(decoded.Validate() == InventoryError.None && decoded.Count("alien_core") == 1, "Reward transport lost items");
                var client = new RaidDeathBagClientState(); client.Receive(new RaidCorpseRpc { LootId = bag.Key, Version = graph.Version,
                    CharacterIndex = -1, Position = bag.Value.Position, Rotation = bag.Value.Rotation });
                Require(client.Bags.Count == 1, "Client rejected alien remains identity");
            }
            Debug.Log("Boss phase/alternating attacks and one-time searchable reward transport passed.");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(profile); }
    }
    [Serializable] private sealed class SpeciesReport { public List<string> passed = new List<string>(); public string error; }
    [MenuItem("Tools/Moonkov/Aliens/Check species attacks bosses and face attachment")]
    public static void RunSpeciesContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var report = new SpeciesReport();
        try
        {
            foreach (var species in new[] { AlienSpecies.Centipede, AlienSpecies.Caterpillar, AlienSpecies.Leech, AlienSpecies.Tentacle, AlienSpecies.Roots, AlienSpecies.Slime, AlienSpecies.Snake })
                CheckSpecies(species, report);
        }
        catch (Exception error) { report.error = error.Message; throw; }
        finally { Directory.CreateDirectory("Library/BuildValidation/AlienIntegration"); File.WriteAllText("Library/BuildValidation/AlienIntegration/species-contract.json", JsonUtility.ToJson(report, true)); }
        Debug.Log("Species contracts passed: " + string.Join("; ", report.passed));
    }
    private static void CheckSpecies(AlienSpecies species, SpeciesReport report)
    {
        using var world = new World("Alien species fixture " + species, WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        var time = manager.CreateEntity(typeof(NetworkTime)); manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary species fixture");
        var profile = ScriptableObject.CreateInstance<AlienCreatureProfile>();
        try
        {
            profile.ApplyDefaults(species); profile.Scale = .65f; profile.Damage = 12; profile.Liquid = false;
            profile.AttackCooldown = 20; profile.PatrolRadius = 0; profile.Boss = species == AlienSpecies.Tentacle || species == AlienSpecies.Roots;
            Vector3 origin = new Vector3(-9000, 1000, -9000);
            var floor = new GameObject("Temporary physical floor"); floor.transform.SetParent(holder.transform);
            floor.transform.position = origin - Vector3.up * .25f; floor.AddComponent<BoxCollider>().size = new Vector3(40, .5f, 40);
            Physics.SyncTransforms();
            var actor = Alien(holder.transform, world, profile, origin + Vector3.up * .56f * profile.Scale, MultiplayerRole.Server, -13000);
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 2.25f, false, 42);
            GhostGameObject extra = null;
            if (species == AlienSpecies.Centipede) extra = Humanoid(holder.transform, world, origin + Vector3.forward * 5, false, 43);
            if (profile.Boss) SetHealth(manager, actor.GhostGameObject.LinkedEntity, 40);
            bool windup = false, attached = false, followed = false, released = false;
            Vector3 start = actor.transform.position;
            for (int frame = 0; frame < 150; frame++)
            {
                manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick((uint)frame + 1) });
                Step(actor, 1);
                if (!windup && actor.Action == AlienAction.Windup)
                {
                    windup = true;
                    Require(manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity).CurrentHealth == 100, species + " damaged during warning");
                    if (species == AlienSpecies.Roots)
                    {
                        Vector3 escape = origin + Vector3.forward * 8;
                        target.transform.position = escape; manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(escape));
                    }
                }
                if (species == AlienSpecies.Leech && actor.Action == AlienAction.Attached)
                {
                    if (!attached)
                    {
                        attached = true;
                        Vector3 moved = target.transform.position + Vector3.right;
                        target.transform.position = moved; manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(moved));
                    }
                    else
                    {
                        followed |= Vector3.Distance(actor.transform.position, target.transform.position + Vector3.up * 1.6f) < .35f;
                        if (actor.DamageCount > 0) SetHealth(manager, target.LinkedEntity, 0);
                    }
                }
                if (attached && actor.AttachedTarget == Entity.Null) released = true;
            }
            Require(windup, species + " never attacked");
            if (species == AlienSpecies.Leech) Require(attached && followed && released, "Face attachment did not follow/release its victim");
            else if (species == AlienSpecies.Roots)
            {
                Require(actor.DamageCount == 0, "Root spikes tracked the dodging target");
                Require(Vector3.Distance(start, actor.transform.position) < .05f, "Root boss left its defended area");
            }
            else Require(actor.DamageCount > 0, species + " attack failed to hit stationary target");
            if (extra) Require(manager.GetComponentData<PredictedPlayerGhost>(extra.LinkedEntity).CurrentHealth < 100, "Charge failed to sweep second target");
            if (profile.Boss) Require(actor.BossPhase == 2, "Boss did not enter its low-health phase");
            if (species == AlienSpecies.Caterpillar || species == AlienSpecies.Slime) Require(actor.DamageCount == 3, species + " did not produce exactly three area pulses");
            report.passed.Add(species + ": " + actor.AttackKind + ", hits=" + actor.DamageCount + (profile.Boss ? ", phase 2" : ""));
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(profile); }
    }
}
