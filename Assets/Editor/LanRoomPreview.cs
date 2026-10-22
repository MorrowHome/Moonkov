using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

// Native UXML preview; never enters Play or opens a gameplay connection.
public sealed class LanRoomPreview : EditorWindow
{
    [MenuItem("Moonkov/Networking/LAN Room UI Preview")]
    public static void Open() => ShowPreview(false);
    [MenuItem("Moonkov/Networking/LAN Lobby UI Preview")]
    public static void OpenLobby() => ShowPreview(true);

    public static void ShowPreview(bool lobby)
    {
        var window = GetWindow<LanRoomPreview>(true, "LAN UI layout preview");
        window.position = new Rect(40, 80, 1100, 800);
        var root = window.rootVisualElement; root.Clear();
        var font = AssetDatabase.LoadAssetAtPath<FontAsset>("Assets/Localization/Fonts/NotoSansCJKsc.asset");
        if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
        var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomPanel").CloneTree(); tree.style.flexGrow = 1; root.Add(tree);
        tree.Q<Button>("lanEntry").style.display = DisplayStyle.None;
        tree.Q("lanModal").RemoveFromClassList("lan-hidden");
        tree.Q<Button>("lanClose").clicked += window.Close;
        var mode = tree.Q<DropdownField>("lanMode"); mode.choices = new List<string> { "远征 / EXPEDITION", "极速赏金 / BOUNTY CLASH" }; mode.index = 0;
        tree.Q<Label>("lanStatus").text = "原生界面布局预览 / 示例数据，尚未运行联机。";
        if (lobby)
        {
            tree.Q("lanBrowser").AddToClassList("lan-hidden"); tree.Q("lanLobby").RemoveFromClassList("lan-hidden");
            tree.Q<Label>("lanRoomHeading").text = "月面探险小队";
            tree.Q<Label>("lanRoomMeta").text = "极速赏金 / 3/12 位干员";
            tree.Q<Label>("lanHostAddress").text = "192.168.1.20\n房间 TCP 7978 · 游戏 UDP 7979";
            tree.Q<Label>("lanBriefing").text = "共同进入极速赏金集结区后，创建或加入 1–3 人小队。至少两队准备后开战。";
            tree.Q<Button>("lanReady").AddToClassList("lan-hidden");
            tree.Q<Button>("lanLeave").text = "关闭房间";
            var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanMemberRow");
            var names = new[] { "猫猫干员（你）", "月面漫游者", "第三位干员" };
            for (int i = 0; i < names.Length; i++)
            {
                var row = template.CloneTree(); tree.Q<ScrollView>("lanMembers").Add(row);
                row.Q<Label>("memberIndex").text = (i + 1).ToString("00"); row.Q<Label>("memberName").text = names[i];
                row.Q<Label>("memberState").text = i == 0 ? "房主" : i == 1 ? "已准备" : "未准备";
                row.EnableInClassList("lan-is-ready", i < 2);
            }
            tree.Q<Button>("lanStart").SetEnabled(false);
        }
        else
        {
            var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomRow");
            foreach (var entry in new[] { ("月面探险小队", "远征 / 2/8 / 192.168.1.20:7978"), ("猫猫赏金团", "极速赏金 / 4/12 / 192.168.1.36:7978") })
            {
                var row = template.CloneTree(); tree.Q<ScrollView>("lanRooms").Add(row);
                row.Q<Label>("roomName").text = entry.Item1; row.Q<Label>("roomDetail").text = entry.Item2;
            }
            tree.Q<Label>("lanEmpty").text = "2 个示例房间 / 布局预览";
        }
        window.Show(); window.Focus();
    }
}
