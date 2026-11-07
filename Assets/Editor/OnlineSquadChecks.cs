using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Online;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Native UI and rendering checks with sample snapshots. No login, gameplay World,
// server process, Play mode, or player inventory writes.
public sealed class OnlineSquadChecks : EditorWindow
{
    private const string Folder = "LocalData/OnlineSquadReview";
    private OnlineSquadPanel m_View;
    private LobbySnapshot m_Snapshot;
    private int m_Checks, m_SceneCount, m_ChangedCharacter, m_ChangedPose;
    private string m_LastAction;
    private bool m_Retried;
    [Serializable] private sealed class Result { public string Status, Message; public int Checks; }

    [MenuItem("Tools/Moonkov/Check Online Squad Presentation")]
    public static void Begin()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/result.json", JsonUtility.ToJson(new Result { Status = "Running" }));
        var window = OpenWindow("Online squad check - sample data", 960, 540);
        window.rootVisualElement.schedule.Execute(window.Check).StartingIn(600);
    }

    [MenuItem("Moonkov/Networking/Online Lobby UI Preview")]
    public static void OpenPreview() => OpenWindow("Online squad preview - sample data", 1280, 720);

    [MenuItem("Tools/Moonkov/Check Squad Entry Recovery")]
    public static void BeginEntryRecovery()
    {
        Directory.CreateDirectory(Folder);
        var window = OpenWindow("Squad entry recovery - sample data", 960, 540);
        window.m_Snapshot.Phase = "Ready";
        window.m_Snapshot.SelfId = "b";
        window.m_Snapshot.CanRetry = window.m_Snapshot.CanLeave = true;
        window.m_Snapshot.Members.Single(m => m.PlayerId == "b").Returned = true;
        window.Present(error: "P2P 直连失败。");
        window.rootVisualElement.schedule.Execute(window.CheckEntryRecovery).StartingIn(600);
    }

    private void CheckEntryRecovery()
    {
        try
        {
            var root = m_View.Element;
            var leave = root.Q<Button>("onlineLeave");
            var retry = root.Q<Button>("onlineRetry");
            foreach (var button in new[] { leave, retry })
                Require(button.enabledInHierarchy && button.resolvedStyle.display == DisplayStyle.Flex &&
                    root.panel.Pick(button.worldBound.center) == button, "failed entry action is not visible or reachable: " + button.name);
            foreach (var button in new[] { leave, retry })
            {
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center }))
                    button.SendEvent(down);
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = button.worldBound.center }))
                    button.SendEvent(up);
            }
            Require(m_LastAction == "leave" && m_Retried, "recovery controls did not emit the real action callbacks");
            Present(busy: true);
            Require(!leave.enabledInHierarchy && !retry.enabledInHierarchy, "pending recovery actions accept duplicate input");
            m_Snapshot.CanRetry = false; Present();
            Require(retry.style.display.value == DisplayStyle.None && leave.enabledInHierarchy &&
                root.Q("onlineActions").style.display.value == DisplayStyle.Flex, "settled member cannot leave while teammates remain in the raid");
            m_Snapshot.CanLeave = false; Present();
            Require(!leave.enabledInHierarchy, "unsettled equipment allows departure");
            File.WriteAllText(Folder + "/entry-recovery.json", JsonUtility.ToJson(new Result { Status = "Passed", Checks = m_Checks }, true));
            Debug.Log("Squad entry recovery: " + m_Checks + " checks passed; sample snapshots and callbacks only.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Folder + "/entry-recovery.json", JsonUtility.ToJson(new Result { Status = "Failed", Checks = m_Checks, Message = ex.ToString() }, true));
            Debug.LogException(ex);
        }
        finally { Close(); }
    }

    private static OnlineSquadChecks OpenWindow(string title, int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use the squad preview in idle Edit mode.");
        var window = CreateInstance<OnlineSquadChecks>();
        window.titleContent = new GUIContent(title);
        window.position = new Rect(40, 60, width, height);
        window.minSize = new Vector2(640, 480);
        window.ShowUtility(); window.Build();
        return window;
    }

    private static LobbyAppearance Appearance(int character, int pose, string weapon, string head, string backpack)
    {
        var graph = InventoryGraph.Create(false, false);
        foreach (var item in new[] { (weapon, "Primary"), (head, "Helmet"), (backpack, "Backpack"), ("energy_sword", "Melee") })
            if (!string.IsNullOrEmpty(item.Item1)) graph.Items.Add(new InventoryItem { Id = "fixture-" + item.Item2,
                Code = item.Item1, Parent = InventoryCatalog.Equipment, Region = item.Item2 });
        return LobbyAppearanceInventory.FromInventory(graph, character, pose);
    }
    private void Build()
    {
        m_SceneCount = EditorSceneManager.previewSceneCount;
        m_View = new OnlineSquadPanel(action => m_LastAction = action.Action, () => { }, () => m_Retried = true, (character, pose) =>
        { m_ChangedCharacter = character; m_ChangedPose = pose; });
        rootVisualElement.Add(m_View.Element);
        m_View.SetReducedMotion(true);
        m_View.Element.Q<Toggle>("onlineReduceMotion").SetValueWithoutNotify(true);
        m_View.SetVisible(true);
        var menu = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/MainMenu.uxml").CloneTree();
        var advancedHost = m_View.Element.Q("onlineAdvancedHost");
        advancedHost.Add(menu.Q("AdvancedConnection"));
        advancedHost.style.display = DisplayStyle.Flex;
        m_View.Element.Q("onlineReconnectHost").Add(menu.Q("ReconnectPanel"));
        m_Snapshot = new LobbySnapshot { SelfId = "a", LeaderId = "a", Code = "A1B2C3D4", Phase = "Lobby", Mode = "Coop", Members = new[] {
            new LobbyMember { PlayerId = "c", Name = "SHIAN", Ready = true, Appearance = Appearance(4, 0, "sniper", "helmet", "backpack") },
            new LobbyMember { PlayerId = "a", Name = "DOLLSINGER", Ready = true, Appearance = Appearance(2, 1, "rifle", "energy_head_dome", "sling_pack") },
            new LobbyMember { PlayerId = "b", Name = "HATSUNE MIKU", Ready = true, Appearance = Appearance(3, 2, "shotgun", "helmet", "cargo_pack") },
            new LobbyMember { PlayerId = "d", Name = "MIKU / 待命", Ready = true, Appearance = Appearance(3, 3, "compact", "energy_head_dome", "small_pack") }
        }};
        Present();
    }
    private void Present(bool busy = false, string error = null) => m_View.Present(m_Snapshot,
        m_Snapshot.Members.Single(m => m.PlayerId == m_Snapshot.SelfId).Appearance, "Sample operator", error, busy, false);
    private void Require(bool valid, string message)
    { m_Checks++; if (!valid) throw new InvalidOperationException("Online squad: " + message); }

    private void Check()
    {
        try
        {
            var root = m_View.Element;
            Require(root.Q<DropdownField>("onlineCharacter").choices.Count == 3 && root.Q<DropdownField>("onlinePose").choices.Count == 5, "selector assets did not import");
            // Batch Editor utility windows may be clamped by the desktop work area.
            // Check the actual host geometry, rather than assuming a 720px window.
            Require(root.worldBound.width > 900 && root.worldBound.height > 300 &&
                Math.Abs(root.worldBound.height - rootVisualElement.worldBound.height) < 2,
                "screen does not fill its native host: " + root.worldBound + " / " + rootVisualElement.worldBound);
            var image = root.Q<Image>("onlineStage");
            var dock = root.Q("onlineActions").worldBound;
            Require(image.worldBound == root.worldBound, "the stage does not fill the screen");
            Require(dock.yMax <= root.worldBound.yMax &&
                root.Query<VisualElement>(className: "online-member").ToList().All(card => !card.worldBound.Overlaps(dock)),
                "member badges overlap the departure controls");
            var frost = root.Q<Button>("onlineCopy").parent.Q<Image>(className: "squad-glass-image").image as RenderTexture;
            Require(frost != null && frost.IsCreated() && Mathf.Max(frost.width, frost.height) <= 512,
                "the room card is missing its shared frosted backdrop");
            var start = root.Q<Button>("onlineStart");
            Require(start.enabledInHierarchy && root.panel.Pick(start.worldBound.center) == start,
                "ready leader cannot reach the departure button");
            Require(root.Q<VisualElement>("onlineMember1").Q<Label>(className: "online-member-name").text == "DOLLSINGER", "leader is not at the central stand");
            root.Q<DropdownField>("onlineCharacter").value = "SHIAN";
            Require(m_ChangedCharacter == 4, "character selector did not emit intent");
            root.Q<DropdownField>("onlinePose").value = "比心";
            Require(m_ChangedPose == 2, "pose selector did not emit intent");
            Present();
            m_Snapshot.Members[0].Connected = false; m_Snapshot.Members[0].ReconnectSeconds = 40; Present();
            Require(!root.Q<Button>("onlineStart").enabledInHierarchy &&
                root.Query<VisualElement>(className: "online-member").ToList().Any(card => card.ClassListContains("is-offline") &&
                    card.Q<Label>(className: "online-member-name").text == "SHIAN"), "offline member did not block departure");
            m_Snapshot.Members[0].Connected = true; Present(busy: true);
            Require(!root.Q<Button>("onlineReady").enabledInHierarchy, "busy action can submit twice");
            m_Snapshot.SelfId = "b"; Present();
            Require(root.Q<Button>("onlineStart").style.display.value == DisplayStyle.None &&
                root.Q("onlineManage").style.display.value == DisplayStyle.None, "member received leader controls");
            m_Snapshot.SelfId = "a"; m_Snapshot.Mode = "Versus"; Present();
            Require(!root.Q<Button>("onlineStart").enabledInHierarchy && root.Q<Label>("onlineStatus").text.Contains("A、B"), "private versus accepted one team");
            m_Snapshot.Members[2].Team = 1; Present();
            Require(root.Q<Button>("onlineStart").enabledInHierarchy, "valid two-team roster cannot start");
            m_Snapshot.Phase = "Queued"; Present();
            Require(root.Q<Button>("onlineCancel").style.display.value == DisplayStyle.Flex &&
                root.Q("onlineActions").style.display.value == DisplayStyle.Flex, "leader cannot cancel the queue");
            m_Snapshot.Phase = "Starting"; Present();
            Require(!root.Q<DropdownField>("onlineCharacter").enabledInHierarchy && !root.Q<Button>("onlineReady").enabledInHierarchy, "loading allows roster edits");
            m_Snapshot.Phase = "Ready"; m_Snapshot.Members[1].Returned = true; Present();
            Require(root.Q<Button>("onlineRetry").style.display.value == DisplayStyle.None && root.Q<Button>("onlineReturn").style.display.value == DisplayStyle.None,
                "returned player can rejoin the completed raid");
            m_Snapshot.Members[1].Returned = false;
            m_View.Present(null, m_Snapshot.Members[1].Appearance, "Sample operator", null, false, false);
            Require(root.Q("onlineEntry").style.display.value == DisplayStyle.Flex &&
                root.Q("onlineActions").style.display.value == DisplayStyle.None, "solo operator lost the create/join dock");
            m_Snapshot.Phase = "Lobby"; m_Snapshot.Mode = "Coop"; Present();
            m_View.Stage.RenderForEditor(image.contentRect.size);
            var scene = m_View.Stage.DisplayScene;
            var objects = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true)).ToArray();
            Require(m_View.Stage.ActorCount == 4 && EditorSceneManager.previewSceneCount == m_SceneCount + 1, "actors do not share one scene");
            Require(objects.Count(t => t.GetComponent<Camera>() != null) == 1 && objects.Count(t => t.GetComponent<Animator>() != null) == 4, "expected one camera and four visual actors");
            var target = m_View.Stage.Target;
            Require(Mathf.Max(target.width, target.height) <= 3072 && target.width * target.height <= 3_000_000,
                "render target exceeded budget");
            foreach (var animator in objects.Select(t => t.GetComponent<Animator>()).Where(a => a != null))
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                Require(head && float.IsFinite(head.position.x) && float.IsFinite(head.position.y), "actor pose is invalid");
                Require(animator.runtimeAnimatorController == null && !animator.fireEvents, "preview actor has gameplay animation events");
            }
            CheckRotation(image, objects.Single(t => t.GetComponent<Camera>() != null).GetComponent<Camera>(),
                objects.First(t => t.GetComponent<Animator>() != null));
            m_View.SetVisible(false);
            Require(m_View.Stage.ActorCount == 0 && m_View.Stage.Target == null && frost == null &&
                EditorSceneManager.previewSceneCount == m_SceneCount, "hidden page leaked actors, textures or scene");
            m_View.SetVisible(true); Present(); m_View.Stage.RenderForEditor(image.contentRect.size);
            Require(m_View.Stage.ActorCount == 4, "reopening did not recreate actors");
            m_View.Dispose(); m_View = null;
            Require(EditorSceneManager.previewSceneCount == m_SceneCount, "disposing leaked the shared scene");
            using (var portrait = new MenuCharacterView(idleOnly: true, characterIndex: GameSettings.DollSingerCharacterIndex))
            {
                portrait.SetEquipmentInventory(LobbyAppearanceInventory.PreviewInventory(m_Snapshot.Members.Single(m => m.PlayerId == "a").Appearance));
                portrait.Advance(.1f);
                Require(portrait.DisplayScene.GetRootGameObjects().Sum(o => o.GetComponentsInChildren<Camera>(true).Length) == 1 && portrait.HaloVisible,
                    "existing individual equipment preview regressed");
            }
            Require(EditorSceneManager.previewSceneCount == m_SceneCount, "individual preview cleanup regressed");
            File.WriteAllText(Folder + "/result.json", JsonUtility.ToJson(new Result { Status = "Passed", Checks = m_Checks }, true));
            Debug.Log("Online squad presentation: " + m_Checks + " checks passed; native sample-data preview, no Play or clients.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Folder + "/result.json", JsonUtility.ToJson(new Result { Status = "Failed", Checks = m_Checks, Message = ex.ToString() }, true));
            Debug.LogException(ex);
        }
        finally { Close(); }
    }

    private void CheckRotation(Image image, Camera camera, Transform actor)
    {
        Vector2 from = image.worldBound.center, to = from + Vector2.right * 120;
        Require(image.panel.Pick(from) == image, "the stage does not receive drags outside the cards");
        Quaternion original = actor.rotation;
        Vector3 grabbed = Quaternion.Inverse(original) * -camera.transform.forward;
        using (var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = from }))
            image.SendEvent(evt);
        using (var evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = to }))
            image.SendEvent(evt);
        using (var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = to }))
            image.SendEvent(evt);
        m_View.Stage.RenderForEditor(image.contentRect.size);
        Require(Vector3.Dot(actor.rotation * grabbed, camera.transform.right) > .05f &&
            !image.HasPointerCapture(PointerId.mousePointerId), "right drag did not move the squad right and release capture");

        using (var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = from }))
            image.SendEvent(evt);
        using (var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = from }))
            image.SendEvent(evt);
        m_View.Stage.RenderForEditor(image.contentRect.size);
        Require(Quaternion.Angle(actor.rotation, original) < .1f, "double-click did not restore the squad pose");
    }

    private void OnDisable() { m_View?.Dispose(); m_View = null; }
}
