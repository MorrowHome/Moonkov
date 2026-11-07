using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Online;
using Unity.NetCode;
using Unity.Services.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

// Run in the existing disconnected BountyClashUIFixture. Synthetic ECS counters and session
// descriptors exercise the real HUD without opening a socket, account, server or second client.
public static class RaidTelemetryReview
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Folder = "LocalData/RaidTelemetryReview";
    static readonly List<string> Checks = new List<string>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Checks.Add(message); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    static void Property(Type type, object target, string name, object value) => type.GetProperty(name, Hidden).SetValue(target, value);

    static async Task Frames(int count = 6)
    {
        int start = Time.frameCount;
        double deadline = EditorApplication.timeSinceStartup + 15;
        while (Time.frameCount < start + count)
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Fixture is not advancing frames.");
            await Task.Delay(15);
        }
    }
    static void Capture(string name)
    {
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(image); }
    }
    static string[] Read(RaidHUD hud)
    {
        var args = new object[3]; Call(hud, "ReadTelemetryNetwork", args);
        return args.Cast<string>().ToArray();
    }
    static void Refresh(RaidHUD hud)
    {
        Set(hud, "m_TelemetryVisible", false);
        Call(hud, "UpdateTelemetry", true);
    }
    static void Rebind(RaidHUD hud) { Call(hud, "DisposeTelemetry"); Refresh(hud); }

    public static async Task<string> Run()
    {
        if (!EditorApplication.isPlaying || !BountyClashUIFixture.Hud) throw new Exception("Start BountyClashUIFixture first.");
        Directory.CreateDirectory(Folder); Checks.Clear();
        var hud = BountyClashUIFixture.Hud;
        var em = BountyClashUIFixture.ClientWorld.EntityManager;
        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        var label = root.Q<Label>("raidTelemetry");
        var oldGame = GameManager.GameConnection;
        var oldSnapshot = OnlineLobbyClient.Snapshot;
        var oldInstance = OnlineLobbyClient.InstanceId;
        var accountField = typeof(OnlineLobbyClient).GetField("s_Account", Hidden);
        var oldAccount = accountField.GetValue(null);
        var oldLocale = LocalizationSettings.SelectedLocale;
        float oldTimeScale = Time.timeScale;
        Entity connection = Entity.Null, mode = Entity.Null;
        var game = new GameConnection();
        var ice = (EmbeddedIceClientSession)Activator.CreateInstance(typeof(EmbeddedIceClientSession), Hidden, null, new object[] { "telemetry-review" }, null);
        var editor = typeof(EditorWindow).Assembly;
        var gameType = editor.GetType("UnityEditor.GameView");
        var gameView = (EditorWindow)Resources.FindObjectsOfTypeAll(gameType)[0];
        var size = gameType.GetProperty("currentGameViewSize", Hidden).GetValue(gameView);
        var width = size.GetType().GetProperty("width", Hidden);
        var height = size.GetType().GetProperty("height", Hidden);
        int oldWidth = (int)width.GetValue(size), oldHeight = (int)height.GetValue(size);
        var sizesType = editor.GetType("UnityEditor.GameViewSizes");
        var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance", Hidden).GetValue(null);
        void Resize(int w, int h) { width.SetValue(size, w); height.SetValue(size, h); sizesType.GetMethod("Changed", Hidden).Invoke(sizes, null); gameView.Repaint(); }
        void State(ConnectionState.State value) => em.SetComponentData(connection, new NetworkStreamConnection { CurrentState = value });
        NetworkSnapshotAck Ack(float rtt = 42.4f, ulong received = 95, ulong dropped = 5) => new NetworkSnapshotAck
        {
            EstimatedRTT = rtt, SnapshotPacketLoss = new SnapshotPacketLossStatistics
            { NumPacketsReceived = received, NumPacketsDroppedNeverArrived = dropped, NumPacketsCulledOutOfOrder = 30, NumPacketsCulledAsArrivedOnSameFrame = 70 }
        };
        void Sample(NetworkSnapshotAck value) => em.SetComponentData(connection, value);
        void RemoveConnection()
        {
            if (connection == Entity.Null || !em.Exists(connection)) return;
            em.RemoveComponent<NetworkStreamConnection>(connection); em.DestroyEntity(connection); connection = Entity.Null;
        }
        try
        {
            Property(typeof(GameManager), null, "GameConnection", game);
            Property(typeof(GameConnection), game, "SessionConnectionType", NetworkType.Direct);
            Property(typeof(OnlineLobbyClient), null, "InstanceId", null);
            Property(typeof(OnlineLobbyClient), null, "Snapshot", null);
            BountyClashUIFixture.State("active"); BountyClashUIFixture.Inventory(false); gameView.Focus(); await Frames();
            Refresh(hud);
            Check(Read(hud).All(x => x == "No connection"), "Missing connection is explicit, not zero latency/loss.");
            connection = em.CreateEntity(typeof(NetworkStreamConnection));
            State(ConnectionState.State.Connecting);
            Check(Read(hud)[0] == "Connecting...", "Connecting does not display a stale RTT.");
            State(ConnectionState.State.Connected);
            Check(Read(hud).Take(2).All(x => x == "Measuring"), "Missing ack waits for measurements.");
            em.AddComponentData(connection, Ack(received: 0, dropped: 0));
            Check(Read(hud).Take(2).All(x => x == "Measuring"), "No snapshots does not claim zero packet loss.");
            Sample(Ack()); var values = Read(hud);
            Check(values[0] == "42 ms", "Latency uses measured NetCode RTT in milliseconds.");
            Check(values[1] == "5.0%", "Loss uses received plus missing packets; same-frame and late packets are excluded.");
            Check(values[2] == "Direct", "Ordinary direct connection is identified.");
            Sample(Ack(0)); Check(Read(hud)[0] == "0 ms", "A measured zero RTT remains valid.");
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            { Sample(Ack(invalid)); Check(Read(hud)[0] == "Measuring", "Invalid RTT is not a fabricated number: " + invalid); }
            Sample(Ack(received: 999, dropped: 1)); Check(Read(hud)[1] == "0.1%", "Small packet loss retains one decimal.");
            Sample(Ack(received: 1000, dropped: 0)); Check(Read(hud)[1] == "0.0%", "Loss correction can return to zero.");
            Set(hud, "m_TelemetryLastPacket", Time.realtimeSinceStartupAsDouble - 3);
            Check(Read(hud).Take(2).All(x => x == "Waiting for data"), "Stalled snapshots expire both metrics.");
            Sample(Ack(received: 1001, dropped: 0)); Check(Read(hud)[0] == "42 ms", "Fresh data restores measurements.");
            State(ConnectionState.State.Disconnected); Check(Read(hud).All(x => x == "No connection"), "Disconnect clears all old network values.");
            State(ConnectionState.State.Connected); Sample(Ack(received: 0, dropped: 0));
            Check(Read(hud)[1] == "Measuring", "Reconnection starts without invented loss data.");
            Sample(Ack()); Check(Read(hud)[1] == "5.0%", "Reconnection reads the new snapshot counters.");
            Set(hud, "m_TelemetryLastPacket", Time.realtimeSinceStartupAsDouble - 3);
            RemoveConnection(); connection = em.CreateEntity(typeof(NetworkStreamConnection), typeof(NetworkSnapshotAck));
            State(ConnectionState.State.Connected); Sample(Ack());
            Check(Read(hud)[0] == "42 ms", "Replacing the connection entity resets freshness even with identical counters.");

            Property(typeof(GameConnection), game, "SessionConnectionType", NetworkType.Relay);
            Check(Read(hud)[2] == "Relay", "Relay uses the active session type.");
            Property(typeof(GameConnection), game, "SessionConnectionType", NetworkType.Direct);
            Property(typeof(GameConnection), game, "IceSession", ice);
            Check(Read(hud)[2] == "P2P direct", "ICE session identifies P2P without opening transport.");
            Property(typeof(GameConnection), game, "IceSession", null);
            Property(typeof(OnlineLobbyClient), null, "InstanceId", "telemetry-review");
            accountField.SetValue(null, PlayerProfileClient.LocalPreferenceScope);
            Property(typeof(OnlineLobbyClient), null, "Snapshot", new LobbySnapshot { InstanceId = "telemetry-review", ConnectionPath = OnlineNetworkProtocol.Vps });
            Check(Read(hud)[2] == "VPS relay", "Current online instance identifies VPS relay.");
            Property(typeof(OnlineLobbyClient), null, "Snapshot", new LobbySnapshot { InstanceId = "stale-instance", ConnectionPath = OnlineNetworkProtocol.Vps });
            Check(Read(hud)[2] == "Direct", "Old lobby state cannot relabel the current connection.");
            Property(typeof(GameManager), null, "GameConnection", null);
            Check(Read(hud)[2] == "Unknown connection", "Missing route descriptor remains unknown.");
            Property(typeof(GameManager), null, "GameConnection", game);
            mode = em.CreateEntity(typeof(LanRaidMode)); Rebind(hud);
            Check(Read(hud)[2] == "LAN", "Client-world LAN marker identifies local network play.");
            em.DestroyEntity(mode); mode = em.CreateEntity(typeof(OfflineRaidMode)); Rebind(hud);
            Check(Read(hud).SequenceEqual(new[] { "Local", "Not applicable", "Single player" }), "Single player shows local and not-applicable network values.");
            em.DestroyEntity(mode); mode = Entity.Null; Rebind(hud);
            Property(typeof(GameConnection), game, "IceSession", ice);

            foreach (string language in new[] { "zh-Hans", "en" })
            foreach (var dimension in new[] { new Vector2Int(1280, 720), new Vector2Int(800, 600) })
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(language);
                await LocalizationSettings.StringDatabase.GetTableAsync(MoonkovLocalization.Strings).Task;
                Resize(dimension.x, dimension.y); await Frames(8); Sample(Ack(received: (ulong)Time.frameCount, dropped: 5)); Refresh(hud); await Frames();
                float scale = root.worldBound.width / Screen.width;
                Check(Screen.width == dimension.x && Screen.height == dimension.y, "Real viewport " + language + " " + dimension);
                Check(label.text.Contains("FPS") && label.text.Contains("42 ms") && label.text.Contains("%") && label.text.Contains("P2P"), "All four metrics are rendered: " + language + " " + dimension);
                Check(label.text.Contains(language == "zh-Hans" ? "丢包率" : "Loss") && label.text.Contains(language == "zh-Hans" ? "连接" : "Connection"), "Localized labels: " + language + " " + dimension);
                Check(label.resolvedStyle.fontSize / scale >= 11.9f && label.worldBound.height / scale >= 32, "Readable two-line text: " + language + " " + dimension);
                Check(label.worldBound.xMin >= 0 && label.worldBound.yMin >= 0 && label.worldBound.xMax / scale < dimension.x - 295, "Readout stays on-screen and clear of right task panel: " + language + " " + dimension);
                Capture(language + "-" + dimension.x);
            }
            Time.timeScale = 0; await Frames(40);
            Check(Regex.IsMatch(label.text, @"[1-9][0-9]* FPS"), "Rendered FPS continues while timeScale is zero.");
            Call(hud, "UpdateTelemetry", false);
            Check(label.style.display.value == DisplayStyle.None, "Leaving the raid hides telemetry.");
            Check(typeof(RaidHUD).GetField("m_TelemetryWorld", Hidden).GetValue(hud) == null, "Hiding disposes the cached ECS query.");
            Refresh(hud); Check(Read(hud)[0] == "42 ms", "Showing again rebuilds the query and reads current data.");
            File.WriteAllLines(Folder + "/checks.txt", Checks);
            return "PASS " + Checks.Count + " native telemetry, route, lifecycle and viewport checks. No networking started.";
        }
        finally
        {
            Time.timeScale = oldTimeScale; RemoveConnection();
            if (mode != Entity.Null && em.Exists(mode)) em.DestroyEntity(mode);
            Property(typeof(GameManager), null, "GameConnection", oldGame);
            Property(typeof(OnlineLobbyClient), null, "Snapshot", oldSnapshot);
            Property(typeof(OnlineLobbyClient), null, "InstanceId", oldInstance);
            accountField.SetValue(null, oldAccount);
            Property(typeof(GameConnection), game, "IceSession", null); ice.Dispose();
            LocalizationSettings.SelectedLocale = oldLocale;
            Resize(oldWidth, oldHeight); Rebind(hud); await Frames();
            File.WriteAllLines(Folder + "/progress.txt", Checks);
        }
    }
}
