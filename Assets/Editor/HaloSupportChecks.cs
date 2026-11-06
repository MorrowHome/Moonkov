using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class HaloSupportChecks
{
    private static int checks;
    internal static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }
    internal static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
    [MenuItem("Tools/Moonkov/Check External Halo Support")]
    public static void Menu() => Debug.Log(Run());
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        checks = 0;
        using var f = new Fixture();
        var a = f.Spawn(1, Vector3.zero); var b = f.Spawn(2, new Vector3(0, 0, 3));
        var guard = f.TargetInput(b, true);
        f.Input(a, f.TargetInput(b, false)); f.Step(.02f);
        Check(!HaloTransferRules.Occupied(f.State(a)), "Full teammate rejects transfer without consuming the guard gesture");
        f.Input(a, guard); f.Step(.3f);
        Check(f.State(a).HaloDeploymentMode == HaloDeploymentMode.AllyGuard && f.State(a).HaloTransferPhase == HaloTransferPhase.Sending, "Guard reaches a fully charged teammate");
        Check(Near(f.State(a).HaloEnergy, 100), "Guarding without impacts costs nothing");
        Check(Near(f.Damage(b, 35), 0) && Near(f.State(a).HaloEnergy, 65) && Near(f.State(b).HaloEnergy, 100), "Bullet damage spends donor pool only");
        Check(f.State(b).ShieldHitSequence == 1 && f.State(a).ShieldHitSequence == 1, "Both actors observe real shield feedback");
        var rifle = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        var state = f.State(a); state.WeaponCooldown = 100; state.WeaponRaiseProgress = 1;
        Check(!HaloModuleFire.TryFire(ref state, rifle, true, true, false), "Guard cannot fire its own halo");
        var wall = f.Cube("Occluding wall", new Vector3(0, 1.5f, 1.5f), new Vector3(3, 3, .2f));
        Check(Near(f.Damage(b, 10), 10) && Near(f.State(a).HaloEnergy, 65), "A wall interrupts guard protection immediately");
        UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();
        state = f.State(a); state.HaloEnergy = 4; f.Set(a, state);
        Check(Near(f.Damage(b, 10), 6) && Near(f.State(a).HaloEnergy, 0), "A partial pool absorbs only its remaining energy");
        f.Input(a, guard); f.Step(.02f);
        Check(f.State(a).HaloTransferPhase == HaloTransferPhase.Returning, "Exhaustion recalls the halo");
        f.Input(a, default); f.Step(.3f);
        state = f.State(a); state.HaloEnergy = 100; f.Set(a, state);
        f.Input(a, guard); f.Step(.3f);
        var victim = f.State(b); victim.OnlineHuman = true; victim.OnlineTeam = 2; f.Set(b, victim);
        Check(Near(f.Damage(b, 8), 8), "Changing team immediately invalidates protection");
        victim.OnlineHuman = false; f.Set(b, victim);
        Check(Near(f.Damage(b, 8, RaidDamageKind.Environment), 8) && Near(f.State(a).HaloEnergy, 100), "Physiological damage never charges external defense");
        f.Input(a, default); f.Step(.3f); f.Step(.3f);

        var ground = f.Cube("Ground", new Vector3(0, -.1f, 4), new Vector3(20, .2f, 20));
        var place = new PlayerInput { AimPoint = new float3(0, 0, 4) };
        place.SetFlag(PlayerInput.InputFlag.HaloAnchor, true);
        f.Input(a, place); f.Step(.02f);
        Check(f.State(a).HaloDeploymentMode == HaloDeploymentMode.Light, "Place begins in lighting mode");
        var anchor = f.State(a).HaloTransferPoint;
        Check(math.distance(anchor, new float3(0, 1.6f, 4)) < .02f, "Server derives floating position from the ground hit");
        var serializer = typeof(PredictedPlayerGhost).Assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_PredictedPlayerGhostGhostComponentSerializer", true);
        object[] to = { new GhostSerializerState { GhostFromEntity = f.System.GetComponentLookup<GhostInstance>(true) },
            Activator.CreateInstance(serializer.GetNestedType("Snapshot", BindingFlags.NonPublic)), f.State(a) };
        serializer.GetMethod("CopyToSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, to);
        using (var map = new NativeParallelHashMap<SpawnedGhost, Entity>(1, Allocator.Temp))
        {
            object[] from = { new GhostDeserializerState { GhostMap = map.AsReadOnly(), SnapshotTick = new NetworkTick(f.Tick) },
                default(PredictedPlayerGhost), 0f, 0f, to[1], to[1] };
            serializer.GetMethod("CopyFromSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, from);
            var observed = (PredictedPlayerGhost)from[1];
            Check(observed.HaloDeploymentMode == HaloDeploymentMode.Light && math.distance(observed.HaloTransferPoint, anchor) < .001f,
                "Generated ghost snapshot preserves deployment mode and stationary position");
        }
        f.Input(a, default); f.Step(.3f);
        Check(f.State(a).HaloTransferPhase == HaloTransferPhase.Sending && Near(f.State(a).HaloEnergy, 100), "Stationary lighting occupies the halo without idle drain");
        f.Cycle(a);
        Check(f.State(a).HaloDeploymentMode == HaloDeploymentMode.Shield, "Lighting switches to area defense");
        f.Move(a, new Vector3(0, 0, 4));
        Check(Near(f.Damage(a, 20), 0) && Near(f.State(a).HaloEnergy, 80), "Owner inside its own field spends the same pool without a stale write");
        Check(Near(f.Damage(b, 15), 0) && Near(f.State(a).HaloEnergy, 65), "Area defense protects a teammate");
        f.Move(b, new Vector3(8, 0, 4));
        Check(Near(f.Damage(b, 5), 5) && Near(f.State(a).HaloEnergy, 65), "Outside area receives no protection");
        f.Move(b, new Vector3(0, 0, 3));
        wall = f.Cube("Field wall", new Vector3(0, 1.5f, 3.5f), new Vector3(3, 3, .2f));
        Check(Near(f.Damage(b, 5), 5), "Area field does not protect through a wall");
        UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();
        var enemy = f.Spawn(0, new Vector3(0, 0, 11));
        f.Cycle(a); f.Input(a, default); f.Step(.3f);
        Check(f.State(a).HaloDeploymentMode == HaloDeploymentMode.Sentry && f.State(a).HaloSentryTarget == enemy, "Sentry selects the enemy, excluding teammates");
        state = f.State(a); state.WeaponCooldown = 10; f.Set(a, state);
        object[] shot = { a, state, rifle, 50u, default(Ray) };
        bool fired = (bool)typeof(ServerPlayerMovementSystem).GetMethod("TrySentryShot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.System, shot);
        Check(fired && Vector3.Distance(((Ray)shot[4]).origin, anchor) < .01f, "Sentry shoots from the actual anchor");
        Check(Vector3.Angle(((Ray)shot[4]).direction, new Vector3(0, -.7f, 7)) <= 9.01f, "Sentry shot remains inside its deliberate spread cone");
        Check(Near(f.Damage(b, 5), 5), "Sentry mode cannot simultaneously shield");
        wall = f.Cube("Sentry wall", new Vector3(0, 1.5f, 8), new Vector3(3, 3, .2f));
        shot = new object[] { a, state, rifle, 51u, default(Ray) };
        Check(!(bool)typeof(ServerPlayerMovementSystem).GetMethod("TrySentryShot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.System, shot), "A newly placed wall prevents shooting before the next acquisition tick");
        f.Move(a, new Vector3(30, 0, 4)); f.Input(a, default); f.Step(.1f);
        Check(f.State(a).HaloTransferPhase == HaloTransferPhase.Returning, "Leaving the deployment leash recalls the halo");
        f.Step(.3f);
        Check(!HaloTransferRules.Occupied(f.State(a)), "Recall releases shared occupancy");
        state = f.State(a); state.HaloEnergy = 100; state.HaloTransferPhase = HaloTransferPhase.Sending; state.HaloDeploymentMode = HaloDeploymentMode.Shield;
        float previous = state.HaloEnergy; HaloEnergyRules.Tick(ref state, 20);
        Check(Near(state.HaloEnergy, previous), "All external modes pause passive recovery");
        float max = 0, mean = 0;
        for (uint i = 1; i <= 200; i++) { float angle = Vector3.Angle(Vector3.forward, HaloSupportRules.SentryDirection(Vector3.forward, i)); max = Mathf.Max(max, angle); mean += angle; }
        Check(max <= 9.01f && mean / 200 > 4, "Sentry has meaningful low accuracy over many shots");
        var rpcType = Array.Find(typeof(ClientSpawnVfxRpc).Assembly.GetTypes(), t => t.IsValueType &&
            typeof(IRpcCommandSerializer<ClientSpawnVfxRpc>).IsAssignableFrom(t));
        var rpcSerializer = (IRpcCommandSerializer<ClientSpawnVfxRpc>)Activator.CreateInstance(rpcType);
        using (var bytes = new NativeArray<byte>(256, Allocator.Temp))
        {
            var writer = new DataStreamWriter(bytes);
            var shotEvent = new ClientSpawnVfxRpc { OwnerNetworkId = 1, WeaponId = DollSingerWeapons.Halo, ShotTick = 88,
                HaloDeployed = true, ShotOrigin = anchor, AimPoint = new float3(0, 1, 12) };
            rpcSerializer.Serialize(ref writer, default, shotEvent);
            var reader = new DataStreamReader(writer.AsNativeArray()); var received = default(ClientSpawnVfxRpc);
            rpcSerializer.Deserialize(ref reader, default, ref received);
            Check(received.HaloDeployed && received.ShotTick == 88 && math.distance(received.ShotOrigin, anchor) < .001f,
                "Shot RPC carries autonomous origin and owner event without relying on local prediction");
        }
        string result = $"External halo support: PASS ({checks} authority/rule checks).";
        Directory.CreateDirectory("LocalData/HaloAbilities"); File.WriteAllText("LocalData/HaloAbilities/support-checks.txt", result); return result;
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly World World = new("Halo abilities isolated checks");
        internal EntityManager Manager => World.EntityManager;
        internal readonly ServerPlayerMovementSystem System;
        internal readonly GameObject Root;
        private readonly Scene scene;
        private readonly Entity maps;
        private int nextGhost;
        internal uint Tick = 100;
        internal Fixture()
        {
            System = World.CreateSystemManaged<ServerPlayerMovementSystem>();
            scene = EditorSceneManager.NewPreviewScene(); Root = new GameObject("Halo abilities fixture"); SceneManager.MoveGameObjectToScene(Root, scene);
            maps = Manager.CreateEntity(); Manager.AddBuffer<ClientsMap>(maps).ResizeUninitialized(4);
        }
        internal Entity Spawn(int owner, Vector3 point, float energy = 100)
        {
            var actor = Manager.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(GhostInstance), typeof(Simulate), typeof(PlayerClientCommandInputLookup), typeof(LocalTransform));
            var state = new PredictedPlayerGhost { CurrentHealth = 440, MaxHealth = 440, HaloEquipped = true, HaloEnergyInitialized = true,
                HaloCapacity = 100, HaloEnergy = energy, EquippedWeaponID = DollSingerWeapons.Halo, InventoryWeapons = true };
            state.ControllerState.Init(point, quaternion.identity); Set(actor, state);
            Manager.SetComponentData(actor, LocalTransform.FromPosition(point));
            Manager.SetComponentData(actor, new GhostOwner { NetworkId = owner });
            Manager.SetComponentData(actor, new GhostInstance { ghostId = ++nextGhost, spawnTick = new NetworkTick(1) });
            var source = Manager.CreateEntity(); Manager.AddBuffer<ClientCommandInput>(source);
            Manager.SetComponentData(actor, new PlayerClientCommandInputLookup { ClientCommandInputEntity = source });
            if (owner > 0)
            {
                var connection = Manager.CreateEntity(typeof(JoinedClient), typeof(RaidSession), typeof(NetworkStreamInGame));
                Manager.SetComponentData(connection, new JoinedClient { PlayerEntity = actor });
                Manager.SetComponentData(connection, new RaidSession { Phase = RaidPhase.Active });
                var clients = Manager.GetBuffer<ClientsMap>(maps);
                clients[owner] = new ClientsMap { ConnectionEntity = connection };
            }
            var go = new GameObject("Actor " + nextGhost); go.transform.SetParent(Root.transform); go.transform.position = point;
            Manager.AddComponentObject(actor, new GhostGameObjectLink { LinkedInstance = go.AddComponent<GhostGameObject>() });
            return actor;
        }
        internal PredictedPlayerGhost State(Entity e) => Manager.GetComponentData<PredictedPlayerGhost>(e);
        internal void Set(Entity e, PredictedPlayerGhost state) => Manager.SetComponentData(e, state);
        internal void Move(Entity e, Vector3 position)
        { var state = State(e); state.ControllerState.CurrentPosition = position; Set(e, state); Manager.SetComponentData(e, LocalTransform.FromPosition(position)); }
        internal PlayerInput TargetInput(Entity target, bool guard)
        {
            var ghost = Manager.GetComponentData<GhostInstance>(target);
            var input = new PlayerInput { HaloTransferGhostId = ghost.ghostId, HaloTransferSpawnTick = ghost.spawnTick.SerializedData };
            input.SetFlag(PlayerInput.InputFlag.HaloTransfer, true); input.SetFlag(PlayerInput.InputFlag.HaloGuard, guard); return input;
        }
        internal void Input(Entity e, PlayerInput input) => Manager.GetBuffer<ClientCommandInput>(Manager.GetComponentData<PlayerClientCommandInputLookup>(e).ClientCommandInputEntity)
            .AddCommandData(new ClientCommandInput { Tick = new NetworkTick(Tick), PlayerInput = input });
        internal void Step(float dt)
        { typeof(ServerPlayerMovementSystem).GetMethod("UpdateHaloTransfers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(System, new object[] { Tick++, dt }); }
        internal void Cycle(Entity e)
        { var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.HaloMode, true); Input(e, input); Step(.02f); Input(e, default); Step(.02f); }
        internal float Damage(Entity e, float amount, RaidDamageKind kind = RaidDamageKind.Bullet)
        { var state = State(e); float lost = RaidHealth.CombatDamage(Manager, e, ref state, BodyPart.Chest, amount, Tick, kind, HaloTransferRules.Anchor(state)); Set(e, state); return lost; }
        internal GameObject Cube(string name, Vector3 position, Vector3 scale)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(Root.transform); go.transform.position = position; go.transform.localScale = scale; Physics.SyncTransforms(); return go; }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(Root); EditorSceneManager.ClosePreviewScene(scene); World.Dispose(); }
    }
}
