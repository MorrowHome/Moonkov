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
    [Serializable] private sealed class Result { public string Status, Message, Preview; public int Checks; }

    [MenuItem("Tools/Moonkov/Check Online Squad Presentation")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/result.json", JsonUtility.ToJson(new Result { Status = "Running" }));
        var window = CreateInstance<OnlineSquadChecks>();
        window.titleContent = new GUIContent("小队展示检查");
        window.position = new Rect(20, 20, 1280, 720);
        window.minSize = new Vector2(640, 480);
        window.ShowUtility(); window.Build();
        window.rootVisualElement.schedule.Execute(window.Check).StartingIn(600);
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
        rootVisualElement.style.width = 1280; rootVisualElement.style.height = 720;
        m_SceneCount = EditorSceneManager.previewSceneCount;
        m_View = new OnlineSquadPanel(_ => { }, () => { }, () => { }, (character, pose) =>
        { m_ChangedCharacter = character; m_ChangedPose = pose; });
        rootVisualElement.Add(m_View.Element);
        m_View.SetVisible(true);
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
            Require(root.Q(className: "online-controls").worldBound.yMax <= root.worldBound.yMax + 1,
                "departure controls overflow the native host");
            Require(root.Q<Button>("onlineStart").enabledInHierarchy, "ready leader cannot start cooperation");
            Require(root.Q<VisualElement>("onlineMember1").Q<Label>(className: "online-member-name").text == "DOLLSINGER", "leader is not at the central stand");
            root.Q<DropdownField>("onlineCharacter").value = "SHIAN";
            Require(m_ChangedCharacter == 4, "character selector did not emit intent");
            root.Q<DropdownField>("onlinePose").value = "低姿";
            Require(m_ChangedPose == 2, "pose selector did not emit intent");
            Present();
            m_Snapshot.Members[0].Connected = false; m_Snapshot.Members[0].ReconnectSeconds = 40; Present();
            Require(!root.Q<Button>("onlineStart").enabledInHierarchy &&
                root.Query<VisualElement>(className: "online-member").ToList().Any(card => card.ClassListContains("is-offline") &&
                    card.Q<Label>(className: "online-member-name").text == "SHIAN"), "offline member did not block departure");
            m_Snapshot.Members[0].Connected = true; Present(busy: true);
            Require(!root.Q<Button>("onlineReady").enabledInHierarchy, "busy action can submit twice");
            m_Snapshot.SelfId = "b"; Present();
            Require(root.Q<Button>("onlineStart").style.display.value == DisplayStyle.None && root.Q<VisualElement>("onlineMember1").Q<Button>().style.display.value == DisplayStyle.None, "member received leader controls");
            m_Snapshot.SelfId = "a"; m_Snapshot.Mode = "Versus"; Present();
            Require(!root.Q<Button>("onlineStart").enabledInHierarchy && root.Q<Label>("onlineStatus").text.Contains("A、B"), "private versus accepted one team");
            m_Snapshot.Members[2].Team = 1; Present();
            Require(root.Q<Button>("onlineStart").enabledInHierarchy, "valid two-team roster cannot start");
            m_Snapshot.Phase = "Starting"; Present();
            Require(!root.Q<DropdownField>("onlineCharacter").enabledInHierarchy && !root.Q<Button>("onlineReady").enabledInHierarchy, "loading allows roster edits");
            m_Snapshot.Phase = "Ready"; m_Snapshot.Members[1].Returned = true; Present();
            Require(root.Q<Button>("onlineRetry").style.display.value == DisplayStyle.None && root.Q<Button>("onlineReturn").style.display.value == DisplayStyle.None,
                "returned player can rejoin the completed raid");
            m_Snapshot.Members[1].Returned = false;
            m_Snapshot.Phase = "Lobby"; m_Snapshot.Mode = "Coop"; Present();
            m_View.Stage.RenderForEditor(new Vector2(1280, 510), .1f);
            for (int i = 0; i < 14; i++) m_View.Stage.RenderForEditor(new Vector2(1280, 510), .1f);
            var scene = m_View.Stage.DisplayScene;
            var objects = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true)).ToArray();
            Require(m_View.Stage.ActorCount == 4 && EditorSceneManager.previewSceneCount == m_SceneCount + 1, "actors do not share one scene");
            Require(objects.Count(t => t.GetComponent<Camera>() != null) == 1 && objects.Count(t => t.GetComponent<Animator>() != null) == 4, "expected one camera and four visual actors");
            Require(m_View.Stage.Target.width <= 1600 && m_View.Stage.Target.height <= 1600, "render target exceeded budget");
            foreach (var animator in objects.Select(t => t.GetComponent<Animator>()).Where(a => a != null))
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                Require(head && float.IsFinite(head.position.x) && float.IsFinite(head.position.y), "actor pose is invalid");
                Require(animator.runtimeAnimatorController == null && !animator.fireEvents, "preview actor has gameplay animation events");
            }
            var target = m_View.Stage.Target; var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                var pixels = image.GetPixels32();
                Require(pixels.Count(p => p.r > 80 || p.g > 80 || p.b > 80) > pixels.Length / 40, "native preview is blank");
                File.WriteAllBytes(Folder + "/squad-four.png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; DestroyImmediate(image); }
            m_View.SetVisible(false);
            Require(m_View.Stage.ActorCount == 0 && m_View.Stage.Target == null && EditorSceneManager.previewSceneCount == m_SceneCount, "hidden page leaked actors, texture or scene");
            m_View.SetVisible(true); Present(); m_View.Stage.RenderForEditor(new Vector2(1280, 510));
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
            File.WriteAllText(Folder + "/result.json", JsonUtility.ToJson(new Result { Status = "Passed", Checks = m_Checks, Preview = Folder + "/squad-four.png" }, true));
            Debug.Log("Online squad presentation: " + m_Checks + " checks passed; native sample-data preview, no Play or clients.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Folder + "/result.json", JsonUtility.ToJson(new Result { Status = "Failed", Checks = m_Checks, Message = ex.ToString() }, true));
            Debug.LogException(ex);
        }
        finally { Close(); }
    }
    private void OnDisable() { m_View?.Dispose(); m_View = null; }
}
