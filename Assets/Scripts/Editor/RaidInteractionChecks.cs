using System;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Moon;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class RaidInteractionChecks
{
    private static int s_Passed;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Interaction: " + name);
        s_Passed++;
    }
    private static LightSwitchButton Button(Transform parent, Vector3 position, string id, LightCircuitControls controls)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = id;
        go.transform.SetParent(parent); go.transform.position = position; go.transform.localScale = Vector3.one * .12f;
        var button = go.AddComponent<LightSwitchButton>();
        button.Configure(id, id, controls, new[] { "test.light" }, LightCommand.Toggle);
        return button;
    }
    [MenuItem("Tools/Moonkov/Interaction/Run focused checks")]
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        s_Passed = 0;
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var root = new GameObject("Interaction check fixture");
            var origin = new Vector3(20000, 0, 20000);
            var viewer = new GameObject("viewer").AddComponent<Camera>(); viewer.transform.SetParent(root.transform);
            viewer.transform.position = origin + Vector3.up * 1.5f;
            var fixture = new GameObject("light").AddComponent<Light>(); fixture.transform.SetParent(root.transform); fixture.enabled = true;
            var budget = root.AddComponent<LocalLightBudget>();
            var serialized = new SerializedObject(budget); var fixtures = serialized.FindProperty("fixtures");
            fixtures.arraySize = 1; fixtures.GetArrayElementAtIndex(0).objectReferenceValue = fixture; serialized.ApplyModifiedPropertiesWithoutUndo();
            var controls = root.AddComponent<LightCircuitControls>();
            controls.Configure(budget, new[] { new LightCircuitControls.Binding { lightId = "test.light", fixture = fixture } });
            var a = Button(root.transform, origin + new Vector3(0, 1.5f, 2), "test.A", controls);
            var b = Button(root.transform, origin + new Vector3(0, 1.7f, 2), "test.B", controls);
            var owner = new GameObject("airlock").AddComponent<AirlockController>(); owner.transform.SetParent(root.transform);
            var airObject = GameObject.CreatePrimitive(PrimitiveType.Cube); airObject.transform.SetParent(root.transform);
            airObject.transform.position = origin + new Vector3(.16f, 1.5f, 2); airObject.transform.localScale = Vector3.one * .12f;
            var air = airObject.AddComponent<AirlockButton>(); air.Configure("test.air.open", "OPEN OUTER", owner, AirlockCommand.OpenOuter);
            // Runtime-only MonoBehaviours don't receive OnEnable in an EditMode fixture.
            air.SendMessage("OnEnable");
            UnityEngine.Physics.SyncTransforms();
            var selection = new RaidInteractionSelection();
            selection.Refresh(viewer, origin, true);
            Check(selection.Selected.Light == a && selection.Choices.Count == 2, "cross-type focus and light panel grouping");
            selection.Cycle(1); Check(selection.Selected.Light == b, "cycle light panel");
            selection.Refresh(viewer, origin, true); Check(selection.Selected.Light == b, "retain chosen panel command");
            selection.Cycle(-1); Check(selection.Selected.Light == a, "reverse cycle");
            selection.Refresh(viewer, origin, false); Check(!selection.Selected.IsValid, "UI/input gate clears selection");
            selection.Refresh(viewer, origin + Vector3.back * 8, true); Check(!selection.Selected.IsValid, "out-of-range target excluded");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(root.transform);
            wall.transform.position = origin + new Vector3(0, 1.5f, 1); wall.transform.localScale = new Vector3(2, 2, .1f);
            UnityEngine.Physics.SyncTransforms(); selection.Refresh(viewer, origin, true);
            Check(!selection.Selected.IsValid, "solid wall blocks both kinds");
            wall.SetActive(false); a.GetComponent<Collider>().enabled = false; b.GetComponent<Collider>().enabled = false;
            UnityEngine.Physics.SyncTransforms(); selection.Refresh(viewer, origin, true);
            Check(selection.Selected.Airlock == air, "disabled light colliders yield to airlock");
            a.GetComponent<Collider>().enabled = true; b.GetComponent<Collider>().enabled = true;
            var duplicate = Button(root.transform, origin + Vector3.right * 8, "test.A", controls);
            Check(!LightSwitchButton.Find("test.A"), "ambiguous network ID rejected"); duplicate.gameObject.SetActive(false);
            Check(LightSwitchButton.Find("test.A") == a, "disabled endpoint unregistered");
            Check(!LightSwitchNetworkContext.ValidId(new string('x', 121)), "oversized RPC ID rejected");
            UnityEngine.Physics.SyncTransforms();
            Check(LightSwitchNetworkContext.Press(null, a) && a.State == LightGroupState.AllOff, "standalone executes existing lighting owner");

            using (var world = new World("Interaction checks", WorldFlags.GameServer))
            {
                var em = world.EntityManager;
                var player = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(LocalTransform));
                em.SetComponentData(player, new PredictedPlayerGhost { CurrentHealth = 100 });
                em.SetComponentData(player, LocalTransform.FromPosition(origin));
                var connection = em.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame), typeof(JoinedClient), typeof(RaidSession));
                em.SetComponentData(connection, new JoinedClient { PlayerEntity = player });
                em.SetComponentData(connection, new RaidSession { Phase = RaidPhase.Active });
                Check(ServerLightSwitchSystem.ValidateAndPress(em, connection, a) == LightSwitchFeedback.Accepted && a.State == LightGroupState.AllOn, "server executes live player's in-range command");
                em.SetComponentData(player, new PredictedPlayerGhost { CurrentHealth = 0 });
                Check(ServerLightSwitchSystem.ValidateAndPress(em, connection, a) == LightSwitchFeedback.Unavailable, "dead player rejected");
                em.SetComponentData(player, new PredictedPlayerGhost { CurrentHealth = 100 });
                em.SetComponentData(connection, new RaidSession { Phase = RaidPhase.Extracted });
                Check(ServerLightSwitchSystem.ValidateAndPress(em, connection, a) == LightSwitchFeedback.Unavailable, "settled raid rejected");
                em.SetComponentData(connection, new RaidSession { Phase = RaidPhase.Active });
                em.SetComponentData(player, LocalTransform.FromPosition(origin + Vector3.back * 8));
                Check(ServerLightSwitchSystem.ValidateAndPress(em, connection, a) == LightSwitchFeedback.OutOfRange, "server distance guard");
                em.SetComponentData(player, LocalTransform.FromPosition(origin)); wall.SetActive(true); UnityEngine.Physics.SyncTransforms();
                Check(ServerLightSwitchSystem.ValidateAndPress(em, connection, a) == LightSwitchFeedback.Obstructed && a.State == LightGroupState.AllOn, "server wall guard leaves state intact");
                wall.SetActive(false); UnityEngine.Physics.SyncTransforms();
                em.CreateEntity(typeof(NetworkTime));
                var server = world.GetOrCreateSystemManaged<ServerLightSwitchSystem>();
                world.SetTime(new TimeData(1, .02f)); server.Update();
                using var snapshots = em.CreateEntityQuery(typeof(LightSwitchStateRpc));
                Check(snapshots.CalculateEntityCount() == 1, "deduplicated initial per-light snapshot");
                var initial = snapshots.GetSingleton<LightSwitchStateRpc>();
                Check(initial.Powered && initial.Session != 0, "initial snapshot carries authoritative state and session");
                em.DestroyEntity(snapshots);
                Request(em, connection, initial.Session, 1); server.Update();
                Check(a.State == LightGroupState.AllOff, "RPC executes once");
                Request(em, connection, initial.Session, 1); world.SetTime(new TimeData(1.3, .02f)); server.Update();
                Check(a.State == LightGroupState.AllOff, "duplicate request never toggles twice");
                Request(em, connection, initial.Session + 1, 2); server.Update();
                Check(a.State == LightGroupState.AllOff, "stale server session rejected");
                em.DestroyEntity(snapshots);
                var late = em.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame));
                world.SetTime(new TimeData(1.6, .02f)); server.Update();
                Check(snapshots.CalculateEntityCount() == 1 && !snapshots.GetSingleton<LightSwitchStateRpc>().Powered, "late connection receives current power without a new press");
                em.DestroyEntity(late);
            }
            using (var world = new World("Interaction client checks", WorldFlags.GameClient))
            {
                var em = world.EntityManager;
                var client = world.GetOrCreateSystemManaged<ClientLightSwitchSystem>();
                var connection = em.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame));
                Snapshot(em, connection, 9, 1, true); client.Update();
                Check(a.State == LightGroupState.AllOn, "client applies authoritative power snapshot");
                Snapshot(em, connection, 9, 1, false); client.Update();
                Check(a.State == LightGroupState.AllOn, "client ignores stale revision");
                Snapshot(em, connection, 9, 2, false); client.Update();
                Check(a.State == LightGroupState.AllOff, "client applies newer revision without toggling");
                var clientState = LightSwitchNetworkContext.State(world);
                Check(clientState.Session == 9, "client adopts active server session");
                Check(LightSwitchNetworkContext.Press(world, a) && a.State == LightGroupState.AllOff, "client request does not predict world light power");
                Check(!LightSwitchNetworkContext.Press(world, a), "pending request suppresses repeated input");
                var pending = clientState.PendingRequest;
                var ack = em.CreateEntity(); em.AddComponentData(ack, new LightSwitchAckRpc { Session = 9, RequestId = pending, Feedback = LightSwitchFeedback.Accepted });
                em.AddComponentData(ack, new ReceiveRpcCommandRequest { SourceConnection = connection }); client.Update();
                Check(clientState.PendingRequest == 0 && clientState.Feedback == LightSwitchFeedback.Accepted, "matching server acknowledgment releases pending input");
                em.DestroyEntity(connection); client.Update();
                Check(clientState.Session == 0 && clientState.Snapshots.Count == 0, "disconnect clears session and cached snapshots");
            }
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/AirlockPrompt").CloneTree();
            Check(tree.Q<Label>("interactionCaption") != null && tree.Q<Label>("interactionRange") != null && tree.Q("airlockMarker") != null, "HUD resource imports and exposes required fields");
            string report = "{\"passed\":" + s_Passed + ",\"scope\":\"selection,physics,authority,RPC replay,late connection,HUD import\"}";
            System.IO.Directory.CreateDirectory("Temp/RaidInteraction");
            System.IO.File.WriteAllText("Temp/RaidInteraction/checks.json", report);
            return report;
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<LightSwitchButton>(true)) button.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
                foreach (var button in root.GetComponentsInChildren<AirlockButton>(true)) button.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
            }
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
    private static void Request(EntityManager em, Entity connection, ulong session, uint sequence)
    {
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new LightSwitchRequestRpc { ButtonId = new FixedString128Bytes("test.A"), Session = session, RequestId = sequence });
        em.AddComponentData(rpc, new ReceiveRpcCommandRequest { SourceConnection = connection });
    }
    private static void Snapshot(EntityManager em, Entity connection, ulong session, uint revision, bool powered)
    {
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new LightSwitchStateRpc { ButtonId = new FixedString128Bytes("test.A"), LightId = new FixedString128Bytes("test.light"),
            Session = session, Revision = revision, Powered = powered });
        em.AddComponentData(rpc, new ReceiveRpcCommandRequest { SourceConnection = connection });
    }
}
