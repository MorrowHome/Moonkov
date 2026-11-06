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
using UnityEngine;

public static class HaloScanChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }
    [MenuItem("Tools/Moonkov/Check Halo Scanning")]
    public static void Menu() => Debug.Log(Run());
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        checks = 0;
        using var f = new HaloSupportChecks.Fixture();
        var a = f.Spawn(1, Vector3.zero); var b = f.Spawn(2, new Vector3(0, 0, 5));
        var enemy = f.Spawn(0, new Vector3(0, 0, 9));
        f.Cube("Scan penetrates building", new Vector3(0, 2, 4), new Vector3(10, 4, .3f));
        var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.HaloScan, true);
        f.Input(a, input); f.Step(.01f);
        void Scan(float dt)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            object[] args = { f.Tick, dt, ecb };
            typeof(ServerPlayerMovementSystem).GetMethod("UpdateHaloScans", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.System, args);
            ecb = (EntityCommandBuffer)args[2]; ecb.Playback(f.Manager); ecb.Dispose();
        }
        using var pulses = f.Manager.CreateEntityQuery(typeof(HaloScanPulseRpc), typeof(SendRpcCommandRequest));
        using var contacts = f.Manager.CreateEntityQuery(typeof(HaloScanContactRpc), typeof(SendRpcCommandRequest));
        Scan(.01f);
        Check(pulses.CalculateEntityCount() == 1 && contacts.IsEmptyIgnoreFilter, "Approved scan emits a pulse before distant contacts");
        Check(HaloSupportChecks.Near(f.State(a).HaloEnergy, 75), "Scan spends 25 shared energy once");
        var pulse = pulses.GetSingleton<HaloScanPulseRpc>();
        var owner = f.Manager.GetComponentData<GhostInstance>(a);
        Check(pulse.OwnerGhostId == owner.ghostId && pulse.OwnerSpawnTick == owner.spawnTick.SerializedData, "Pulse belongs to one spawned owner");
        Scan(.6f);
        Check(contacts.CalculateEntityCount() == 1, "Wave detects the enemy through the building and excludes the teammate");
        var contact = contacts.GetSingleton<HaloScanContactRpc>();
        Check(contact.GhostId == f.Manager.GetComponentData<GhostInstance>(enemy).ghostId, "Contact identifies the actual hostile ghost");
        Check(contacts.GetSingleton<SendRpcCommandRequest>().TargetConnection == pulses.GetSingleton<SendRpcCommandRequest>().TargetConnection, "Contacts are delivered only to the scanner's connection");
        Scan(10);
        Check(pulses.CalculateEntityCount() == 1 && HaloSupportChecks.Near(f.State(a).HaloEnergy, 75), "Holding scan never retriggers or overspends");
        var low = f.State(a); low.HaloScanLatched = false; low.HaloEnergy = 24; f.Set(a, low); Scan(.01f);
        Check(pulses.CalculateEntityCount() == 1, "Insufficient energy rejects scan");
        low.HaloEnergy = 100; low.HaloScanCooldown = 2; low.HaloScanLatched = false; f.Set(a, low); Scan(.01f);
        Check(pulses.CalculateEntityCount() == 1, "Cooldown rejects repeated presses");
        low.HaloScanCooldown = 0; low.HaloScanLatched = false; low.HaloTransferPhase = HaloTransferPhase.Returning; f.Set(a, low); Scan(.01f);
        Check(pulses.CalculateEntityCount() == 1, "A returning halo cannot scan");
        low.HaloTransferPhase = HaloTransferPhase.Sending; low.HaloDeploymentMode = HaloDeploymentMode.Light; low.HaloTransferPoint = new float3(0, 2, 10);
        Check(math.distance(HaloScanRules.Origin(low), new float3(0, 2, 10)) < .001f, "Stationary halo is the scan origin");
        low.HaloTransferPhase = HaloTransferPhase.Idle; low.HaloScanLatched = false; f.Set(a, low);
        for (int i = 0; i < 30; i++) f.Spawn(0, new Vector3(1 + i % 4, 0, 10 + i * .2f));
        f.Input(a, input); Scan(.01f); Scan(2);
        Check(contacts.CalculateEntityCount() == HaloScanRules.MaxContacts + 1, "A dense scan is bounded to 24 contacts");

        var cache = new HaloScanClientState();
        Check(cache.Receive(pulse, 10), "Client accepts an approved pulse");
        Check(!cache.Receive(pulse, 10.1), "Duplicate pulse cannot restart reveal duration");
        Check(cache.Receive(contact, 10.5), "Client accepts authorized contact");
        Check(!cache.Receive(contact, 10.6), "Duplicate contact cannot extend reveal duration");
        Check(cache.Visible(contact.Index, 12) && !cache.Visible(contact.Index, 13.6), "Enemy reveal expires after three seconds");
        contact.Index = 1; contact.OwnerSpawnTick++;
        Check(!cache.Receive(contact, 10.7), "Respawn identity prevents cross-life contacts");
        contact.OwnerSpawnTick--; contact.Position = new float3(float.NaN);
        Check(!cache.Receive(contact, 10.7), "Invalid coordinates cannot reach the render pass");
        cache.Clear(); Check(!cache.Active && !cache.Visible(0, 10.7), "Disconnect/death clears all contacts");
        Check(!cache.Receive(pulse, 14), "A delayed old pulse cannot reopen cleared scan data");

        var serializerType = typeof(ClientCommandInput).Assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_ClientCommandInputSerializer", true);
        var serializer = (ICommandDataSerializer<ClientCommandInput>)Activator.CreateInstance(serializerType);
        foreach (var flag in new[] { PlayerInput.InputFlag.HaloScan, PlayerInput.InputFlag.HaloAnchor, PlayerInput.InputFlag.HaloMode })
        {
            var command = new PlayerInput(); command.SetFlag(flag, true);
            using var bytes = new NativeArray<byte>(512, Allocator.Temp); var writer = new DataStreamWriter(bytes);
            serializer.Serialize(ref writer, default, new ClientCommandInput { PlayerInput = command.Sanitized() });
            var reader = new DataStreamReader(writer.AsNativeArray()); var received = new ClientCommandInput(); serializer.Deserialize(ref reader, default, ref received);
            Check((received.PlayerInput.InputFlags & (uint)flag) != 0, "Generated command serializer preserves " + flag);
            command.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
            Check((command.Sanitized().InputFlags & (uint)flag) == 0, "Menu suppression clears " + flag);
        }
        var guard = f.TargetInput(b, true); var buffered = new PlayerInput(); buffered.SetFlag(PlayerInput.InputFlag.Shoot, true); buffered.UpdateFrom(guard);
        Check(buffered.HaloGuard && !buffered.Shoot, "Guard overrides a buffered weapon shot");
        string result = $"Halo scan: PASS ({checks} authority/client/wire checks).";
        Directory.CreateDirectory("LocalData/HaloAbilities"); File.WriteAllText("LocalData/HaloAbilities/scan-checks.txt", result); return result;
    }
}
