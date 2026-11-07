using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Online;

public static class LanSquadChecks
{
    [MenuItem("Tools/Moonkov/Check LAN Squad")]
    public static void Run() => Debug.Log(Check());
    public static string Check()
    {
        int count = 0;
        void Expect(bool good, string name) { if (!good) throw new InvalidOperationException(name); count++; }
        void Reject(Action action, string name)
        { try { action(); } catch (ArgumentException) { count++; return; } catch (InvalidOperationException) { count++; return; } throw new InvalidOperationException(name); }
        string root = Path.GetFullPath("LocalData/LanSquadReview/check-" + Guid.NewGuid().ToString("N"));
        using (var data = new LanPersistence(root))
        using (var server = new LanRoomServer(data, "LAN squad fixture", RaidGameMode.Expedition, 32, 0, 37979, false))
        {
            string hostKey = new string('a', 64), guestKey = new string('b', 64);
            var host = server.AddHost(hostKey, "房主", 2, 0);
            var guest = server.Handle(new LanRequest { Action="join", Credential=guestKey, Name="成员", CharacterIndex=4, PoseIndex=2 });
            LanRequest Request(string action) => new LanRequest { Action=action, Credential=guestKey, RoomId=guest.Room.Id };
            var own = guest.Room.Members.Single(m => m.Id == guest.MemberId);
            Expect(own.Appearance.CharacterIndex == 4 && own.Appearance.PoseIndex == 2, "Join appearance was not synchronized");
            var graph = JsonConvert.DeserializeObject<InventoryGraph>(guest.Profile.Profile.InventoryJson);
            Expect(own.Appearance.SameAs(LobbyAppearanceInventory.FromInventory(graph, 4, 2)), "Appearance did not come from the host inventory");
            own.Appearance.PoseIndex = 0;
            Expect(server.Handle(Request("poll")).Room.Members.Single(m => m.Id == guest.MemberId).Appearance.PoseIndex == 2, "Snapshot aliased server state");
            var ready = Request("ready"); ready.Ready = true; server.Handle(ready);
            var change = Request("appearance"); change.CharacterIndex = 3; change.PoseIndex = 1;
            own = server.Handle(change).Room.Members.Single(m => m.Id == guest.MemberId);
            Expect(!own.Ready && own.Appearance.CharacterIndex == 3 && own.Appearance.PoseIndex == 1, "Appearance change did not cancel readiness");
            change.CharacterIndex = 99; Reject(() => server.Handle(change), "Invalid character accepted");
            change.CharacterIndex = 3; change.PoseIndex = 9; Reject(() => server.Handle(change), "Invalid pose accepted");
            var legacy = Request("poll"); legacy.Protocol = 1; Reject(() => server.Handle(legacy), "Legacy room contract accepted");
            string serialized = JsonConvert.SerializeObject(server.Handle(Request("poll")).Room);
            Expect(!serialized.Contains("InventoryJson") && !serialized.Contains(guestKey), "Private LAN data leaked to roster");
            server.Handle(ready);
            server.Handle(new LanRequest { Action="start", Credential=hostKey, RoomId=host.Room.Id });
            change.PoseIndex = 0; Reject(() => server.Handle(change), "Appearance changed during map load");
        }
        var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomPanel").CloneTree();
        foreach (string name in new[] { "lanStageHost", "lanStagePagerHost", "lanCharacter", "lanPose", "lanReduceMotion", "lanReady", "lanStart", "lanAddress" })
            Expect(tree.Q(name) != null, "Missing LAN control: " + name);
        if (!LanRoomClient.Active)
        {
            // Edit mode has not run the game's subsystem bootstrap. Supply its
            // read-only settings fixture and restore the original singleton.
            var previous = GameSettings.Instance;
            var instance = typeof(GameSettings).GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            try
            {
                if (previous == null) instance.SetValue(null, Activator.CreateInstance(typeof(GameSettings), true));
                var detached = new VisualElement();
                using (var live = new LanRoomView(detached, () => { }))
                    Expect(detached.Q("lanSquadStage") != null && !LanRoomClient.Active, "Live LAN UI failed to construct without opening a room");
                Expect(detached.childCount == 0, "LAN UI did not release its hierarchy");
            }
            finally { if (previous == null) instance.SetValue(null, null); }
        }
        using (var stage = new LanSquadStage())
        {
            var room = new LanRoomSnapshot { Id="sample", Capacity=32, Members=Enumerable.Range(0,32).Select(i => new LanMember
            { Id=i.ToString(), Name=i == 30 ? "<local>" : "成员 " + i, Host=i == 0, Appearance=new LobbyAppearance { CharacterIndex=2+i%3, PoseIndex=i%4 } }).ToArray() };
            stage.Present(room, "30", "local", null);
            Expect(stage.PageCount == 8 && stage.PageIndex == 7, "Large LAN roster did not page to the local member");
            stage.ShowMember("0"); Expect(stage.PageIndex == 0, "Host page navigation failed");
            Expect(stage.Element.Q<Label>(className:"lan-stage-name").enableRichText == false, "Member names allow rich text");
            stage.Present(new LanRoomSnapshot {Id="four",Members=room.Members.Take(4).ToArray()}, "0", "local", null);
            Expect(stage.PageCount == 1 && stage.Element.Q("lanStagePager").style.display.value == DisplayStyle.None, "Four members need no pagination");
            Expect(stage.Element.Query<VisualElement>(className:"lan-stage-member").ToList().Count == 4, "Stage was not bounded to four actors");
            var small = new LanRoomSnapshot {Id="permissions",Name="房间",Capacity=4,Members=new[] {new LanMember {Id="host",Host=true},new LanMember {Id="guest"}}};
            LanRoomPresentation.Lobby(tree, small, "host", true, false, false, "10.0.0.1");
            Expect(!tree.Q<Button>("lanStart").enabledSelf, "Host can launch an unready guest");
            small.Members[1].Ready = true;
            LanRoomPresentation.Lobby(tree, small, "host", true, false, false, "10.0.0.1");
            Expect(tree.Q<Button>("lanStart").enabledSelf, "Ready LAN room cannot launch");
            LanRoomPresentation.Lobby(tree, small, "guest", false, true, true, "10.0.0.1");
            Expect(!tree.Q<Button>("lanReady").enabledSelf && !tree.Q<Button>("lanEquipment").enabledSelf, "Busy LAN controls remain enabled");
        }
        return "LAN squad: " + count + " checks passed; isolated data, no gameplay client or multiplayer acceptance.";
    }
}
