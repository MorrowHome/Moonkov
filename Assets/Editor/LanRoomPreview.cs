using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Online;

// Renders the production UXML and presentation code with explicitly synthetic data.
// No Netcode worlds, LAN sockets or real profiles are opened.
public sealed class LanRoomPreview : EditorWindow
{
    private LanSquadStage m_Squad;
    private void OnDisable() { m_Squad?.Dispose(); m_Squad = null; }
    [MenuItem("Moonkov/Networking/LAN Room UI Preview")]
    public static void Open() => OpenPage(LanRoomPage.Browse);
    [MenuItem("Moonkov/Networking/LAN Lobby UI Preview")]
    public static void OpenLobby() => OpenPage(LanRoomPage.Lobby);

    public static void OpenPage(LanRoomPage page, int width = 1440, int height = 900, bool empty = false, bool guest = false)
    {
        var window = GetWindow<LanRoomPreview>(true, "LAN squad preview - sample data");
        window.m_Squad?.Dispose();
        window.position = new Rect(40, 60, width, height);
        var root = window.rootVisualElement; root.Clear(); root.style.width = StyleKeyword.Auto; root.style.height = StyleKeyword.Auto;
        var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomPanel").CloneTree();
        tree.style.flexGrow = 1; root.Add(tree); tree.Q<Button>("lanEntry").style.display = DisplayStyle.None;
        tree.Q("lanModal").RemoveFromClassList("lan-hidden");
        tree.Q<TextField>("lanPlayerName").value = "猫猫干员";
        tree.Q<Button>("lanClose").clicked += window.Close;
        LanRoomPresentation.Viewport(tree.Q(className: "lan-layer"), width);
        LanRoomPresentation.Mode(tree, RaidGameMode.Expedition);
        var rooms = empty ? Array.Empty<LanRoomSnapshot>() : new[]
        {
            new LanRoomSnapshot { Name="月面探险小队",Mode=RaidGameMode.Expedition,Address="192.168.1.20:7978",Capacity=8,Members=new LanMember[2] },
            new LanRoomSnapshot { Name="猫猫赏金团",Mode=RaidGameMode.BountyClash,Address="192.168.1.36:7978",Capacity=12,Members=new LanMember[4] },
            new LanRoomSnapshot { Name="深空夜航",Mode=RaidGameMode.Expedition,Address="192.168.1.42:7978",Capacity=4,Members=new LanMember[3] }
        };
        LanRoomPresentation.Rooms(tree, rooms, _ => { });
        LanRoomPresentation.Page(tree, page);
        var squad = window.m_Squad = new LanSquadStage(tree.Q("lanModal"));
        squad.Stage.ReducedMotion = true;
        tree.Q<Toggle>("lanReduceMotion").SetValueWithoutNotify(true);
        if (page == LanRoomPage.Lobby)
        {
            var room = new LanRoomSnapshot { Name="月面探险小队",Mode=RaidGameMode.BountyClash,Capacity=12,Phase=LanRoomPhase.Lobby,Members=new[]
            {
                new LanMember {Id="a",Name="猫猫干员",Host=true,Appearance=new LobbyAppearance {CharacterIndex=2,PoseIndex=0,Weapon="rifle",Backpack="backpack"}},
                new LanMember {Id="b",Name="月面漫游者",Ready=true,Appearance=new LobbyAppearance {CharacterIndex=3,PoseIndex=1}},
                new LanMember {Id="c",Name="第三位干员",Ready=false,Appearance=new LobbyAppearance {CharacterIndex=4,PoseIndex=2}},
                new LanMember {Id="d",Name="月尘",Ready=true,Appearance=new LobbyAppearance {CharacterIndex=2,PoseIndex=3}}
            }};
            LanRoomPresentation.Lobby(tree, room, guest ? "c" : "a", !guest, false, false, "192.168.1.20\n房间 TCP 7978\n游戏 UDP 7979");
            squad.Present(room, guest ? "c" : "a", "猫猫干员", null);
            var self = room.Members[guest ? 2 : 0].Appearance;
            var character = tree.Q<DropdownField>("lanCharacter"); character.SetValueWithoutNotify(character.choices[self.CharacterIndex - 2]);
            var pose = tree.Q<DropdownField>("lanPose"); pose.SetValueWithoutNotify(pose.choices[self.PoseIndex + 1]);
            tree.Q<TextField>("lanPlayerName").SetEnabled(false);
        }
        else
        {
            squad.Present(null, null, "猫猫干员", new LobbyAppearance {CharacterIndex=3,PoseIndex=0});
            var character = tree.Q<DropdownField>("lanCharacter"); character.SetValueWithoutNotify(character.choices[1]);
            var pose = tree.Q<DropdownField>("lanPose"); pose.SetValueWithoutNotify(pose.choices[1]);
        }
        squad.SetVisible(true);
        tree.Q<Button>("lanBrowseTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Browse);
        tree.Q<Button>("lanCreateTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Create);
        tree.Q<Button>("lanDirectTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Direct);
        tree.Q<Button>("lanPeerTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Peer);
        tree.Q<Button>("lanExpedition").clicked += () => LanRoomPresentation.Mode(tree,RaidGameMode.Expedition);
        tree.Q<Button>("lanClash").clicked += () => LanRoomPresentation.Mode(tree,RaidGameMode.BountyClash);
        LanRoomPresentation.Status(tree,"界面预览 / 示例数据，无真实连接",false);
        window.Show(); window.Repaint();
    }

    // Manual panel replay can re-enter the UI scheduler and bypass normal GPU submission.
    // Keep the public entry point so existing callers fail before touching rendering state.
    public static string CaptureOpen(string path)
    {
        throw new NotSupportedException(
            "Offscreen UI panel replay is disabled after Editor crashes. Use normal Editor rendering.");
    }
}
