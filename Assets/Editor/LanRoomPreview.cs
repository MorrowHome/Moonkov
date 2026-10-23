using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;

// Renders the production UXML and presentation code with explicitly synthetic data.
// No Netcode worlds, LAN sockets or real profiles are opened.
public sealed class LanRoomPreview : EditorWindow
{
    [MenuItem("Moonkov/Networking/LAN Room UI Preview")]
    public static void Open() => OpenPage(LanRoomPage.Browse);
    [MenuItem("Moonkov/Networking/LAN Lobby UI Preview")]
    public static void OpenLobby() => OpenPage(LanRoomPage.Lobby);

    public static void OpenPage(LanRoomPage page, int width = 1440, int height = 900, bool empty = false, bool guest = false)
    {
        var window = GetWindow<LanRoomPreview>(true, "LAN UI preview · sample data");
        window.position = new Rect(40, 60, width, height);
        var root = window.rootVisualElement; root.Clear(); root.style.width = width; root.style.height = height;
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
            new LanRoomSnapshot { Name="深空夜航 · 欢迎新干员",Mode=RaidGameMode.Expedition,Address="192.168.1.42:7978",Capacity=4,Members=new LanMember[3] }
        };
        LanRoomPresentation.Rooms(tree, rooms, _ => { });
        LanRoomPresentation.Page(tree, page);
        if (page == LanRoomPage.Lobby)
        {
            var room = new LanRoomSnapshot { Name="月面探险小队",Mode=RaidGameMode.BountyClash,Capacity=12,Phase=LanRoomPhase.Lobby,Members=new[]
            {
                new LanMember {Id="a",Name="猫猫干员",Host=true},
                new LanMember {Id="b",Name="月面漫游者",Ready=true},
                new LanMember {Id="c",Name="第三位还在整理装备的干员",Ready=false}
            }};
            LanRoomPresentation.Lobby(tree, room, guest ? "c" : "a", !guest, false, false, "192.168.1.20\n房间 TCP 7978 · 游戏 UDP 7979");
            tree.Q<TextField>("lanPlayerName").SetEnabled(false);
        }
        tree.Q<Button>("lanBrowseTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Browse);
        tree.Q<Button>("lanCreateTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Create);
        tree.Q<Button>("lanDirectTab").clicked += () => LanRoomPresentation.Page(tree,LanRoomPage.Direct);
        tree.Q<Button>("lanExpedition").clicked += () => LanRoomPresentation.Mode(tree,RaidGameMode.Expedition);
        tree.Q<Button>("lanClash").clicked += () => LanRoomPresentation.Mode(tree,RaidGameMode.BountyClash);
        LanRoomPresentation.Status(tree,"界面预览 · 示例房间与成员，无真实连接",false);
        window.Show(); window.Repaint();
    }

    // Call after one Editor update so ScrollView layout and font uploads have settled.
    public static string CaptureOpen(string path)
    {
        var windows=Resources.FindObjectsOfTypeAll<LanRoomPreview>();
        if(windows.Length!=1)throw new InvalidOperationException("Open exactly one LAN UI preview before capture.");
        var window=windows[0]; var root=window.rootVisualElement;
        int width=(int)root.resolvedStyle.width,height=(int)root.resolvedStyle.height;
        var panel=root.panel;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        float dpi=EditorGUIUtility.pixelsPerPoint;
        int pixelWidth=Mathf.RoundToInt(width*dpi), pixelHeight=Mathf.RoundToInt(height*dpi);
        var target=new RenderTexture(pixelWidth,pixelHeight,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.Create();
        var previous=RenderTexture.active; bool previousSrgb=GL.sRGBWrite; Texture2D image=null;
        try
        {
            RenderTexture.active=target; GL.sRGBWrite=false; GL.Clear(true,true,Color.black); GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0,width,height,0);
                panel.GetType().GetMethod("Repaint",flags,null,new[]{typeof(Event)},null).Invoke(panel,new object[]{new Event{type=EventType.Repaint}});
                panel.GetType().GetMethod("Render",flags).Invoke(panel,null);
            }
            finally {GL.PopMatrix();}
            image=new Texture2D(pixelWidth,pixelHeight,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,pixelWidth,pixelHeight),0,0);image.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            return $"Native UXML capture {width}x{height} at {dpi}x DPI; primary bounds: {root.Q<Button>("lanCreate").worldBound}; start bounds: {root.Q<Button>("lanStart").worldBound}";
        }
        finally
        {
            RenderTexture.active=previous;GL.sRGBWrite=previousSrgb;if(image!=null)DestroyImmediate(image);
            target.Release();DestroyImmediate(target);window.Close();
        }
    }
}
