using System;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class MoonkovGunplayChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .002f;
    private static WeaponRegistry Registry => AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");

    [MenuItem("Tools/Moonkov/Check deliberate gunplay")]
    public static string Run()
    {
        s_Checks = 0;
        var registry = Registry;
        for (uint id = 2; id <= 14; id++)
        {
            var weapon = registry.GetWeaponData(id);
            Check(weapon && weapon.ProjectileGhostPrefab.GhostGuid.IsValid && weapon.WeaponReloadSfx, "Missing weapon setup " + id);
            float close = Ballistics.DamageAtDistance(weapon, 0);
            Check(Near(close, weapon.Damage), "Lost close damage " + id);
            if (weapon.Behavior == ProjectileBehavior.AreaOfEffect)
            { Check(Near(Ballistics.DamageAtDistance(weapon, 1000), close), "Projectile range changed an explosion " + id); continue; }
            Check(weapon.DamageFalloffEnd > weapon.DamageFalloffStart, "Missing direct-fire distance profile " + id);
            float last = close;
            for (int metres = 0; metres <= 400; metres += 10)
            {
                float damage = Ballistics.DamageAtDistance(weapon, metres);
                Check(damage <= last + .001f && damage >= 0, "Damage grew with distance " + id);
                last = damage;
            }
            Check(Near(Ballistics.DamageAtDistance(weapon, 1000), close * weapon.MinimumDamageFraction), "Wrong distance floor " + id);
        }
        var rifle = registry.GetWeaponData(2); var revolver = registry.GetWeaponData(3); var shotgun = registry.GetWeaponData(4);
        Check(ShotsToKill(rifle, BodyPart.Chest, false, 5) == 2, "Close rifle chest needs two aimed hits");
        Check(ShotsToKill(rifle, BodyPart.Head, false, 5) == 1, "Close rifle headshot must be decisive");
        Check(ShotsToKill(rifle, BodyPart.Chest, true, 5) == 5, "Full shield must still protect against rifle fire");
        Check(ShotsToKill(revolver, BodyPart.Chest, false, 5) == 2 && ShotsToKill(revolver, BodyPart.Chest, true, 5) == 3,
            "Revolver body/shield tradeoff");
        Check(ShotsToKill(shotgun, BodyPart.Chest, false, 5, true) == 1 && ShotsToKill(shotgun, BodyPart.Chest, false, 24, true) > 1,
            "Even all pellets must lose one-shot body damage at range");
        Check(ShotsToKill(registry.GetWeaponData(5), BodyPart.Chest, true, 5) == 1, "Sniper retains its close shield penetration");
        Check(Near(rifle.CooldownInMs, .16f) && rifle.Automatic && Near(revolver.CooldownInMs, .85f) && !revolver.Automatic,
            "Cadence/trigger identities");
        return "PASS " + s_Checks + " gunplay and real body/shield damage checks";
    }

    private static int ShotsToKill(WeaponData weapon, BodyPart part, bool shield, float distance, bool allPellets = false)
    {
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state);
        state.ShieldEquipped = shield; state.ShieldCharge = shield ? 100 : 0; state.ShieldEnergy = shield ? 100 : 0;
        int shots = 0;
        while (state.CurrentHealth > 0 && shots < 100)
        {
            shots++;
            for (int i = 0; i < (allPellets ? weapon.PelletCount : 1) && state.CurrentHealth > 0; i++)
                RaidHealth.CombatDamage(ref state, part, Ballistics.DamageAtDistance(weapon, distance), (uint)shots, RaidDamageKind.Bullet);
        }
        return shots;
    }

    public static string CheckReloads()
    {
        s_Checks = 0;
        for (uint id = 2; id <= 14; id++)
        {
            var weapon = Registry.GetWeaponData(id); int capacity = weapon.MagazineSize;
            float full = HaloReload.Duration(weapon, id, 0, capacity);
            float topUp = HaloReload.Duration(weapon, id, capacity - 1, capacity);
            Check(Near(full, weapon.ReloadTime) && topUp > .15f && topUp < full, "Full/one-round timing " + id);
            Check(HaloReload.Duration(weapon, id, 0, 1) > topUp, "Empty priming cost " + id);
            float last = 0;
            for (int added = 1; added <= capacity; added++)
            {
                float duration = HaloReload.Duration(weapon, id, 0, added);
                Check(duration > last, "More paid rounds did not cost more time " + id); last = duration;
            }
            var state = new PredictedPlayerGhost { CurrentHealth = 440, EquippedWeaponID = id, CurrentAmmo = 0 };
            HaloReload.Begin(ref state, weapon, 1);
            float total = state.ReloadDuration;
            Check(state.ControllerState.IsReloadingState && state.CurrentAmmo == 0 && state.ReloadTargetAmmo == 1, "Early/free refill " + id);
            state.ReloadTimer *= .5f; state.BodyHealthInitialized = true; state.LeftArmHealth = state.RightArmHealth = 0;
            Check(Near(HaloReload.Progress(state, weapon), .5f) && Near(HaloReload.Total(state, weapon), total), "Injury changed a running timeline " + id);
            Check(!HaloModuleFire.TryFire(ref state, weapon, true, true, false), "Fired before paid charge completed " + id);
            DollSingerWeapons.CompleteReload(ref state, weapon);
            Check(state.CurrentAmmo == 1 && state.ReloadDuration == 0 && !state.ControllerState.IsReloadingState, "Wrong partial completion " + id);
            DollSingerWeapons.CompleteReload(ref state, weapon);
            Check(state.CurrentAmmo == 1, "Duplicate completion generated ammo " + id);
        }
        var graph = InventoryGraph.Create(false); graph.AddSupply("cells", 1, InventoryCatalog.Pockets, cellCharge: 8);
        var rifle = Registry.GetWeaponData(2);
        Check(graph.TryRecharge(0, rifle.MagazineSize, rifle.EnergyPerRound, out int target) && target == 2 && graph.CellEnergy() == 2,
            "Partial charge must conserve real battery energy");
        var paid = new PredictedPlayerGhost { CurrentHealth = 440, EquippedWeaponID = 2 };
        HaloReload.Begin(ref paid, rifle, target); DollSingerWeapons.CompleteReload(ref paid, rifle);
        Check(paid.CurrentAmmo == 2 && graph.CellEnergy() == 2, "Timeline changed the energy transaction");
        return "PASS " + s_Checks + " charge duration, partial ammo and energy checks";
    }

    public static string CheckReloadSounds()
    {
        s_Checks = 0;
        var observer = new MoonkovReloadAudio(); var weapon = Registry.GetWeaponData(3);
        var state = new PredictedPlayerGhost { CurrentHealth = 440, EquippedWeaponID = 3, CurrentAmmo = 0 };
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Initialization played history");
        HaloReload.Begin(ref state, weapon, 6); state.LastReloadTick = 100;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.Open, "Missing opening mechanism");
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Duplicate opening");
        state.ReloadTimer = state.ReloadDuration * .7f;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.Feed, "Missing transfer pulse");
        state.ReloadTimer = state.ReloadDuration * .8f;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Correction replayed a transfer pulse");
        state.ReloadTimer = state.ReloadDuration * .05f;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.Lock, "Skipped frame missed locking mechanism");
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Duplicate locking mechanism");
        state.LastReloadTick = 99;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Old tick played a mechanism");
        state.LastReloadTick = 100;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Old tick rewound deduplication");
        state.CurrentHealth = 0;
        Check(observer.ObservePhase(state, weapon) == ReloadSoundPhase.None, "Dead actor played a mechanism");
        for (uint id = 2; id <= 14; id++)
        {
            var sound = Registry.GetWeaponData(id).WeaponReloadSfx;
            foreach (var phase in new[] { ReloadSoundPhase.Open, ReloadSoundPhase.Feed, ReloadSoundPhase.Lock })
            {
                var window = MoonkovReloadAudio.Window(sound, phase);
                foreach (var clip in sound.Clips)
                {
                    clip.LoadAudioData(); var samples = new float[clip.samples];
                    Check(clip.GetData(samples, 0), "Unreadable recorded mechanism " + clip.name);
                    int first = Mathf.FloorToInt(window.x * clip.samples);
                    int end = Mathf.Min(clip.samples, first + Mathf.CeilToInt(window.y * clip.frequency));
                    float peak = 0;
                    for (int i = first; i < end; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                    Check(window.y > .04f && peak > .015f && peak < .81f, "Silent/clipped mechanism window " + clip.name + phase);
                }
            }
        }
        return "PASS " + s_Checks + " recorded charge windows and snapshot deduplication checks";
    }

    public static string CheckServerProjectiles()
    {
        s_Checks = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        using var world = new World("Gunplay projectile authority", WorldFlags.GameServer);
        var manager = world.EntityManager;
        world.GetOrCreateSystemManaged<ServerPlayerMovementSystem>();
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            var victim = new GameObject("Range damage target"); SceneManager.MoveGameObjectToScene(victim, scene);
            victim.transform.position = Vector3.right * 20000;
            victim.layer = LayerMask.NameToLayer("ServerPlayer"); victim.AddComponent<SphereCollider>().radius = .5f;
            var target = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(PredictedPlayerGhost));
            var ghost = victim.AddComponent<GhostGameObject>(); ghost.LinkGhost(world, target, MultiplayerRole.Server);
            manager.AddComponentObject(target, new GhostGameObjectLink { LinkedInstance = ghost });
            Physics.SyncTransforms();
            foreach (uint id in new uint[] { 2, 3, 4, 5, 6, 9, 10, 14 })
            foreach (float distance in new[] { 3f, 50f, 190f })
            {
                var weapon = Registry.GetWeaponData(id);
                manager.SetComponentData(target, new PredictedPlayerGhost { MaxHealth = 1000, CurrentHealth = 1000 });
                var root = new GameObject("Actual server projectile"); SceneManager.MoveGameObjectToScene(root, scene);
                var entity = manager.CreateEntity(typeof(GhostGameObjectGuid));
                var projectileGhost = root.AddComponent<GhostGameObject>(); projectileGhost.LinkGhost(world, entity, MultiplayerRole.Server);
                var projectile = root.AddComponent<Projectile>(); projectile.enabled = false; projectile.SetGhostGameObject(projectileGhost);
                var origin = victim.transform.position + Vector3.back * distance;
                // A stationary target with the exact ballistic intercept. The fixture
                // controls aim, then uses the production sweep/impact/damage path.
                float flight = distance / weapon.ProjectileSpeed;
                var velocity = Vector3.forward * weapon.ProjectileSpeed + Vector3.up * (.5f * weapon.ProjectileGravity * flight);
                typeof(Projectile).GetField("m_Weapon", fields).SetValue(projectile, weapon);
                typeof(Projectile).GetField("m_Data", fields).SetValue(projectile, new Projectile.ProjectileData
                    { WeaponID = id, OwnerNetworkId = 1, SpawnTick = 100, Origin = origin, InitialVelocity = velocity });
                typeof(Projectile).GetField("m_HitMask", fields).SetValue(projectile, EnergyGrenadePhysics.HitMask);
                typeof(Projectile).GetMethod("Advance", fields).Invoke(projectile, new object[] { flight + .05f, true });
                Check((bool)typeof(Projectile).GetField("m_Stopped", fields).GetValue(projectile), "Projectile missed fixture " + id);
                float damage = 1000 - manager.GetComponentData<PredictedPlayerGhost>(target).CurrentHealth;
                Check(Near(damage, Ballistics.DamageAtDistance(weapon, Vector3.Distance(origin, root.transform.position))),
                    "Actual server impact bypassed range damage " + id + " at " + distance);
                UnityEngine.Object.DestroyImmediate(root); manager.DestroyEntity(entity);
            }
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return "PASS " + s_Checks + " actual server projectile sweep/impact/distance damage checks";
    }
}
