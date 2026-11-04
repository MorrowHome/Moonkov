using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HaloTransferChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int s_Checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); s_Checks++; }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .003f;
    private static PredictedPlayerGhost Actor(float energy = 100, float x = 0, float z = 0)
    {
        var actor = new PredictedPlayerGhost { CurrentHealth = 440, MaxHealth = 440, HaloEquipped = true,
            HaloEnergyInitialized = true, HaloCapacity = 100, HaloEnergy = energy,
            EquippedWeaponID = DollSingerWeapons.Halo, InventoryWeapons = true };
        actor.ControllerState.Init(new float3(x, 0, z), quaternion.identity);
        return actor;
    }

    [MenuItem("Tools/Moonkov/Check Teammate Halo Transfer")]
    public static void Menu() => Debug.Log(Run());
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        s_Checks = 0;
        Rules(); Server();
        string result = $"Halo transfer: PASS ({s_Checks} rule/input/server checks).";
        Directory.CreateDirectory("LocalData/HaloTransfer");
        File.WriteAllText("LocalData/HaloTransfer/checks.txt", result);
        return result;
    }

    private static void Rules()
    {
        var target = new Entity { Index = 7, Version = 1 };
        var rifle = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var a = Actor(); var b = Actor(0);
            a.HaloPendingShot = true; a.BurstRemaining = 2;
            HaloTransferRules.Begin(ref a, target, default);
            Check(!a.HaloPendingShot && a.BurstRemaining == 0, "Deploy must clear buffered and burst fire");
            for (int frame = 0; frame < fps * 2; frame++) HaloTransferRules.Transfer(ref a, ref b, 1f / fps);
            Check(Near(a.HaloEnergy, 65) && Near(b.HaloEnergy, 26.25f), "Frame-rate invariant travel time, cost and receipt");
            Check(Near(100 - a.HaloEnergy, b.HaloEnergy / HaloTransferRules.Efficiency), "Conserve transfer energy");
        }
        var donor = Actor(50); var receiver = Actor(99);
        HaloTransferRules.Begin(ref donor, target, default);
        HaloTransferRules.Transfer(ref donor, ref receiver, 1);
        Check(Near(receiver.HaloEnergy, 100) && Near(donor.HaloEnergy, 50 - 1 / .75f), "Partial gap spends only the delivered amount");
        Check(donor.HaloTransferPhase == HaloTransferPhase.Returning, "Full recipient recalls");
        Check(RaidHealth.HandsBusy(donor) && !HaloEnergyRules.CanDefend(donor), "Recall still occupies attack and defense");
        donor.WeaponCooldown = 100; donor.WeaponRaiseProgress = 1;
        Check(!HaloModuleFire.TryFire(ref donor, rifle, true, true, false), "Recall blocks firing");
        float previous = donor.HaloEnergy;
        HaloEnergyRules.Tick(ref donor, 20);
        Check(Near(donor.HaloEnergy, previous), "Transfer and recall pause natural regeneration");
        HaloTransferRules.TickReturn(ref donor, .2f);
        Check(!HaloTransferRules.Occupied(donor) && HaloEnergyRules.CanDefend(donor), "Recall restores normal control");
        HaloEnergyRules.Tick(ref donor, 3);
        Check(Near(donor.HaloEnergy, previous), "Recovery delay follows recall");
        HaloEnergyRules.Tick(ref donor, .5f);
        Check(Near(donor.HaloEnergy, previous + 5), "Normal recovery resumes");
        donor = Actor(1); receiver = Actor(0);
        HaloTransferRules.Begin(ref donor, target, default);
        HaloTransferRules.Transfer(ref donor, ref receiver, 5);
        Check(Near(donor.HaloEnergy, 0) && Near(receiver.HaloEnergy, .75f) && donor.HaloTransferPhase == HaloTransferPhase.Returning, "Exhaustion cannot overspend");
        donor = Actor(); receiver = Actor(0);
        HaloTransferRules.Begin(ref donor, target, default);
        foreach (float dt in new[] { float.NaN, float.PositiveInfinity, -1f, 0f }) HaloTransferRules.Transfer(ref donor, ref receiver, dt);
        Check(Near(donor.HaloEnergy, 100) && Near(receiver.HaloEnergy, 0), "Invalid elapsed time is inert");
        receiver.ClashEnabled = true; receiver.ClashLife = ClashLife.Downed;
        Check(!HaloTransferRules.CanReceive(receiver), "Downed actors cannot receive");
        donor = Actor(); receiver = Actor(0);
        Check(HaloTransferRules.Teammates(donor, receiver, true, true) && !HaloTransferRules.Teammates(donor, receiver, true, false), "Cooperative humans, never arbitrary NPCs");
        donor.OnlineHuman = receiver.OnlineHuman = true; donor.OnlineTeam = 1; receiver.OnlineTeam = 2;
        Check(!HaloTransferRules.Teammates(donor, receiver, true, true), "Online enemies rejected");
        receiver.OnlineTeam = 1;
        Check(HaloTransferRules.Teammates(donor, receiver, true, true), "Online teammates accepted");
        donor.ClashEnabled = receiver.ClashEnabled = true; donor.ClashTeam = receiver.ClashTeam = 2;
        Check(HaloTransferRules.Teammates(donor, receiver, true, false), "Same-team Clash bot can receive");
        receiver.ClashTeam = 1;
        Check(!HaloTransferRules.Teammates(donor, receiver, true, true), "Clash team overrides online team");
        Check(!HaloTransferRules.InRange(default, new float3(float.NaN, 0, 0)) && !HaloTransferRules.InRange(default, new float3(0, 0, 6.01f)), "Invalid or excessive range rejected");

        var old = new PlayerInput { AimPoint = new float3(99), LookYawPitchDegrees = new float2(80, 0) };
        old.SetFlag(PlayerInput.InputFlag.Shoot, true);
        var transfer = new PlayerInput { HaloTransferGhostId = 27, HaloTransferSpawnTick = 9, LookYawPitchDegrees = new float2(5, 0) };
        transfer.SetFlag(PlayerInput.InputFlag.HaloTransfer, true);
        transfer.SetFlag(PlayerInput.InputFlag.HaloDefense, true);
        transfer.SetFlag(PlayerInput.InputFlag.Reload, true);
        old.UpdateFrom(transfer);
        Check(old.HaloTransfer && !old.Shoot && !old.HaloDefense && !old.Reload && old.HaloTransferGhostId == 27 && Near(old.LookYawPitchDegrees.x, 5), "Transfer clears buffered shot and preserves fresh target intent");
        var lateShot = new PlayerInput(); lateShot.SetFlag(PlayerInput.InputFlag.Shoot, true);
        old.UpdateFrom(lateShot, false);
        Check(old.HaloTransfer && !old.Shoot && old.HaloTransferGhostId == 27, "Older input cannot restore a queued shot");
        old.UpdateFrom(default(PlayerInput));
        Check(!old.HaloTransfer && old.HaloTransferGhostId == 0, "Release clears recipient identity");
        transfer.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
        Check(!transfer.Sanitized().HaloTransfer && transfer.Sanitized().HaloTransferGhostId == 0, "Menus suppress transfer");
        transfer.SetFlag(PlayerInput.InputFlag.FireSuppressed, false);
        var serializerType = typeof(ClientCommandInput).Assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_ClientCommandInputSerializer", true);
        var serializer = (ICommandDataSerializer<ClientCommandInput>)Activator.CreateInstance(serializerType);
        using var bytes = new NativeArray<byte>(512, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        serializer.Serialize(ref writer, default, new ClientCommandInput { PlayerInput = transfer.Sanitized() });
        var reader = new DataStreamReader(writer.AsNativeArray());
        var received = new ClientCommandInput(); serializer.Deserialize(ref reader, default, ref received);
        Check(received.PlayerInput.HaloTransfer && received.PlayerInput.HaloTransferGhostId == 27 && received.PlayerInput.HaloTransferSpawnTick == 9,
            "Generated wire format preserves transfer intent and spawn identity");
    }

    private static void Server()
    {
        using var world = new World("Halo transfer isolated checks");
        var em = world.EntityManager;
        var system = world.CreateSystemManaged<ServerPlayerMovementSystem>();
        var update = typeof(ServerPlayerMovementSystem).GetMethod("UpdateHaloTransfers", Private);
        var scene = EditorSceneManager.NewPreviewScene();
        var maps = em.CreateEntity(); em.AddBuffer<ClientsMap>(maps).ResizeUninitialized(4);
        var root = new GameObject("Transfer fixture"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            Entity Spawn(int owner, float energy, float x, float z)
            {
                var actor = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(GhostInstance), typeof(Simulate), typeof(PlayerClientCommandInputLookup));
                em.SetComponentData(actor, Actor(energy, x, z));
                em.SetComponentData(actor, new GhostOwner { NetworkId = owner });
                em.SetComponentData(actor, new GhostInstance { ghostId = owner, spawnTick = new NetworkTick(1) });
                var commands = em.CreateEntity(); em.AddBuffer<ClientCommandInput>(commands);
                em.SetComponentData(actor, new PlayerClientCommandInputLookup { ClientCommandInputEntity = commands });
                var connection = em.CreateEntity(typeof(JoinedClient), typeof(RaidSession), typeof(NetworkStreamInGame));
                em.SetComponentData(connection, new JoinedClient { PlayerEntity = actor });
                em.SetComponentData(connection, new RaidSession { Phase = RaidPhase.Active });
                var clients = em.GetBuffer<ClientsMap>(maps);
                clients[owner] = new ClientsMap { ConnectionEntity = connection };
                var go = new GameObject("Actor " + owner); go.transform.SetParent(root.transform);
                em.AddComponentObject(actor, new GhostGameObjectLink { LinkedInstance = go.AddComponent<GhostGameObject>() });
                return actor;
            }
            var a = Spawn(1, 100, 0, 0); var b = Spawn(2, 0, 0, 3); var c = Spawn(3, 100, 1, 0);
            var snapshotType = typeof(PredictedPlayerGhost).Assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_PredictedPlayerGhostGhostComponentSerializer", true);
            var sent = Actor(64); sent.HaloTransferPhase = HaloTransferPhase.Extending; sent.HaloTransferRemaining = .12f;
            sent.HaloTransferTarget = b; sent.HaloTransferPoint = new float3(1, 2, 3);
            object[] to = { new GhostSerializerState { GhostFromEntity = system.GetComponentLookup<GhostInstance>(true) },
                Activator.CreateInstance(snapshotType.GetNestedType("Snapshot", BindingFlags.NonPublic)), sent };
            snapshotType.GetMethod("CopyToSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, to);
            using (var ghostMap = new NativeParallelHashMap<SpawnedGhost, Entity>(1, Allocator.Temp))
            {
                ghostMap.Add(em.GetComponentData<GhostInstance>(b), c);
                object[] from = { new GhostDeserializerState { GhostMap = ghostMap.AsReadOnly(), SnapshotTick = new NetworkTick(10) },
                    new PredictedPlayerGhost(), 0f, 0f, to[1], to[1] };
                snapshotType.GetMethod("CopyFromSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, from);
                var observed = (PredictedPlayerGhost)from[1];
                Check(observed.HaloTransferTarget == c && observed.HaloTransferPhase == sent.HaloTransferPhase &&
                    Near(observed.HaloTransferRemaining, .12f) && math.distancesq(observed.HaloTransferPoint, sent.HaloTransferPoint) < .00001f,
                    "Generated snapshot maps target identity and preserves late-join deployment state");
            }
            uint tick = 10;
            void Intent(Entity actor, bool held, Entity target = default, uint? spawn = null)
            {
                var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.HaloTransfer, held);
                if (target != Entity.Null)
                {
                    var ghost = em.GetComponentData<GhostInstance>(target);
                    input.HaloTransferGhostId = ghost.ghostId; input.HaloTransferSpawnTick = spawn ?? ghost.spawnTick.SerializedData;
                }
                var entity = em.GetComponentData<PlayerClientCommandInputLookup>(actor).ClientCommandInputEntity;
                em.GetBuffer<ClientCommandInput>(entity).AddCommandData(new ClientCommandInput { Tick = new NetworkTick(tick), PlayerInput = input });
            }
            void Step(float dt = .5f) { UnityEngine.Physics.SyncTransforms(); update.Invoke(system, new object[] { tick, dt }); tick++; }
            void Reset()
            {
                em.SetComponentData(a, Actor()); em.SetComponentData(b, Actor(0, 0, 3)); em.SetComponentData(c, Actor(100, 1, 0));
                Intent(a, false); Intent(c, false); Step();
            }
            Intent(a, true, b); Step();
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(a).HaloEnergy, 95) && Near(em.GetComponentData<PredictedPlayerGhost>(b).HaloEnergy, 3.75f), "Real server accepts a live teammate and spends after deployment");
            Intent(a, false); Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning && Near(em.GetComponentData<PredictedPlayerGhost>(b).HaloEnergy, 3.75f), "Release stops delivery immediately");
            Step(); Check(!HaloTransferRules.Occupied(em.GetComponentData<PredictedPlayerGhost>(a)), "Server completes recall");
            Reset(); Intent(a, true, b, 999); Step();
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(a).HaloEnergy, 100), "Recycled ghost id cannot authorize a recipient");
            Reset(); Intent(a, true, a); Step();
            Check(!HaloTransferRules.Occupied(em.GetComponentData<PredictedPlayerGhost>(a)), "Self transfer rejected");
            Reset(); var rival = em.GetComponentData<PredictedPlayerGhost>(b); rival.OnlineHuman = true; rival.OnlineTeam = 2; em.SetComponentData(b, rival);
            Intent(a, true, b); Step();
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(b).HaloEnergy, 0), "Server rejects hostile target");
            Reset(); Intent(a, true, b); Step();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(root.transform); wall.transform.position = new Vector3(0, 1.25f, 1.5f); wall.transform.localScale = new Vector3(3, 3, .3f);
            float delivered = em.GetComponentData<PredictedPlayerGhost>(b).HaloEnergy;
            Intent(a, true, b); Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning && Near(em.GetComponentData<PredictedPlayerGhost>(b).HaloEnergy, delivered), "New obstruction stops the actual server transfer");
            UnityEngine.Object.DestroyImmediate(wall); Intent(a, true, b); Step(); Intent(a, true, b); Step();
            Check(!HaloTransferRules.Occupied(em.GetComponentData<PredictedPlayerGhost>(a)), "Held key cannot restart after automatic interruption");
            Intent(a, false); Step(); Intent(a, true, b); Step();
            Check(HaloTransferRules.Occupied(em.GetComponentData<PredictedPlayerGhost>(a)), "Release then press reacquires");
            tick += 13; Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning, "Stale command times out");
            Reset(); Intent(a, true, b); Intent(c, true, b); Step();
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(b).HaloEnergy, 7.5f), "Multiple donors use fresh receiver state");
            var far = em.GetComponentData<PredictedPlayerGhost>(b); far.ControllerState.CurrentPosition.z = 8; em.SetComponentData(b, far);
            Intent(a, true, b); Intent(c, true, b); Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning, "Leaving range recalls");
            Reset(); Intent(a, true, b); Step();
            var connectionB = em.GetBuffer<ClientsMap>(maps)[2].ConnectionEntity;
            em.AddComponent<NetworkStreamRequestDisconnect>(connectionB); Intent(a, true, b); Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning, "Disconnect recalls");
            em.RemoveComponent<NetworkStreamRequestDisconnect>(connectionB);
            Reset(); Intent(a, true, b); Step();
            var noReceiver = em.GetComponentData<PredictedPlayerGhost>(b); noReceiver.CurrentHealth = 0; em.SetComponentData(b, noReceiver); Intent(a, true, b); Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning, "Recipient death recalls");
            Reset(); Intent(a, true, b); Step();
            var switched = em.GetComponentData<PredictedPlayerGhost>(a); switched.EquippedWeaponID = DollSingerWeapons.Shotgun; em.SetComponentData(a, switched); Intent(a, true, b); Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(a).HaloTransferPhase == HaloTransferPhase.Returning, "Inventory weapon replacement cancels the original deployment");
            Reset(); Intent(a, true, b); Step();
            var dead = em.GetComponentData<PredictedPlayerGhost>(a); dead.CurrentHealth = 0; em.SetComponentData(a, dead); Intent(a, true, b); Step();
            Check(!HaloTransferRules.Occupied(em.GetComponentData<PredictedPlayerGhost>(a)), "Death clears deployed halo");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
