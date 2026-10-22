using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class LanRoomView : IDisposable
    {
        private readonly VisualElement m_Container, m_Root, m_Modal;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly Action m_Equipment;
        private readonly VisualTreeAsset m_RoomTemplate, m_MemberTemplate;
        private readonly IVisualElementScheduledItem m_Refresh;
        private bool m_Busy;
        private string m_Roster;
        public LanRoomView(VisualElement host, Action equipment)
        {
            m_Equipment = equipment;
            m_Container = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomPanel").CloneTree();
            m_Container.pickingMode = PickingMode.Ignore;
            m_Container.style.position = Position.Absolute;
            m_Container.style.left = m_Container.style.top = 0;
            host.Add(m_Container); m_Root = m_Container.Q(className: "lan-layer"); m_Modal = m_Root.Q("lanModal");
            m_RoomTemplate = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomRow");
            m_MemberTemplate = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanMemberRow");
            var mode = m_Root.Q<DropdownField>("lanMode"); mode.choices = new List<string> { "远征 / EXPEDITION", "极速赏金 / BOUNTY CLASH" };
            mode.index = GameSettings.Instance.RequestedMode == RaidGameMode.BountyClash ? 1 : 0;
            mode.RegisterValueChangedCallback(_ =>
            {
                var capacity = m_Root.Q<IntegerField>("lanCapacity"); capacity.value = Mathf.Clamp(capacity.value, 2, mode.index == 1 ? 12 : 32);
            });
            m_Root.Q<TextField>("lanPlayerName").value = PlayerPrefs.GetString("Moonkov.LAN.Name", "Operator");
            m_Root.Q<TextField>("lanAddress").value = PlayerPrefs.GetString("Moonkov.LAN.Address", "192.168.1.100");
            m_Root.Q<Button>("lanEntry").clicked += Open;
            m_Root.Q<Button>("lanClose").clicked += Close;
            m_Root.Q<Button>("lanRefresh").clicked += () => Run(RefreshRooms);
            m_Root.Q<Button>("lanCreate").clicked += () => Run(Create);
            m_Root.Q<Button>("lanJoin").clicked += () => Run(() => Join(m_Root.Q<TextField>("lanAddress").value.Trim(), m_Root.Q<IntegerField>("lanJoinPort").value));
            m_Root.Q<Button>("lanReady").clicked += () => Run(async () => { await LanRoomClient.RequestAsync("ready", ready: !LanRoomClient.Ready, ct: m_Stop.Token); });
            m_Root.Q<Button>("lanStart").clicked += () => Run(async () => { await LanRoomClient.RequestAsync("start", ct: m_Stop.Token); });
            m_Root.Q<Button>("lanLeave").clicked += () => Run(async () => { await LanRoomClient.LeaveAsync(); });
            m_Root.Q<Button>("lanEquipment").clicked += () => Run(async () =>
            {
                if (LanRoomClient.Ready) await LanRoomClient.RequestAsync("ready", ready: false, ct: m_Stop.Token);
                await LanRoomClient.RequestAsync("profile", ct: m_Stop.Token); Close(); m_Equipment();
            });
            m_Root.Q<Button>("lanCopy").clicked += () =>
            {
                GUIUtility.systemCopyBuffer = LanRoomClient.IsHost ? LanRoomClient.LocalAddresses().FirstOrDefault() ?? "" : LanRoomClient.Address;
                Status("IPv4 已复制，房间端口显示在上方。多个地址时请使用双方所在网络的地址。", false);
            };
            m_Modal.RegisterCallback<KeyDownEvent>(Key);
            LanRoomClient.Changed += Present;
            m_Refresh = m_Root.schedule.Execute(Present).Every(1000);
            Present();
        }
        public void Open()
        {
            m_Modal.RemoveFromClassList("lan-hidden"); m_Container.BringToFront(); Present();
            m_Root.Q<Button>("lanClose").Focus();
            if (!LanRoomClient.Active) Run(RefreshRooms);
        }
        private void Close() { m_Modal.AddToClassList("lan-hidden"); m_Root.Q<Button>("lanEntry").Focus(); }
        private void Key(KeyDownEvent evt) { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } }
        private async void Run(Func<Task> action)
        {
            if (m_Busy) return; m_Busy = true; Status("正在处理…", false); Present();
            try { await action(); if (!m_Stop.IsCancellationRequested) Status(LanRoomClient.Active ? "房间已连接，整理装备后准备出发。" : "搜索完成，可选择房间或输入地址加入。", false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!m_Stop.IsCancellationRequested) Status(ex.Message, true); }
            finally { m_Busy = false; if (!m_Stop.IsCancellationRequested) Present(); }
        }
        private string SaveName()
        {
            string name = LanRoomServer.CleanName(m_Root.Q<TextField>("lanPlayerName").value);
            PlayerPrefs.SetString("Moonkov.LAN.Name", name); PlayerPrefs.Save(); return name;
        }
        private async Task Create()
        {
            var mode = m_Root.Q<DropdownField>("lanMode").index == 1 ? RaidGameMode.BountyClash : RaidGameMode.Expedition;
            await LanRoomClient.CreateAsync(m_Root.Q<TextField>("lanRoomName").value, SaveName(), mode,
                m_Root.Q<IntegerField>("lanCapacity").value, m_Root.Q<IntegerField>("lanControlPort").value,
                m_Root.Q<IntegerField>("lanGamePort").value, m_Stop.Token);
        }
        private async Task Join(string address, int port)
        {
            await LanRoomClient.JoinAsync(address, port, SaveName(), m_Stop.Token);
            PlayerPrefs.SetString("Moonkov.LAN.Address", address); PlayerPrefs.Save();
        }
        private async Task RefreshRooms()
        {
            var rooms = await LanRoomClient.DiscoverAsync(m_Stop.Token);
            var list = m_Root.Q<ScrollView>("lanRooms"); list.Clear();
            m_Root.Q<Label>("lanEmpty").text = rooms.Length == 0 ? "暂未发现房间。可请房主提供 IPv4 地址直连。" : $"发现 {rooms.Length} 个房间";
            foreach (var room in rooms.OrderBy(r => r.Name))
            {
                var row = m_RoomTemplate.CloneTree(); list.Add(row);
                row.Q<Label>("roomName").text = room.Name;
                row.Q<Label>("roomDetail").text = $"{ModeName(room.Mode)}  /  {room.Members.Length}/{room.Capacity}  /  {room.Address}";
                var endpoint = room.Address.Split(':');
                row.Q<Button>("roomJoin").clicked += () => Run(() => Join(endpoint[0], int.Parse(endpoint[1])));
            }
        }
        private static string ModeName(RaidGameMode mode) => mode == RaidGameMode.BountyClash ? "极速赏金" : "远征";
        private static string HostAddress()
        {
            string address = LanRoomClient.IsHost ? string.Join(" / ", LanRoomClient.LocalAddresses()) : LanRoomClient.Address;
            return $"{(string.IsNullOrEmpty(address) ? "未检测到局域网 IPv4" : address)}\n房间 TCP {LanRoomClient.Port} · 游戏 UDP {LanRoomClient.Room?.GamePort}";
        }
        private void Present()
        {
            // Main menu uses a fixed reference canvas; size this overlay against the display viewport.
            var parent = m_Container.parent;
            float pixels=Screen.width, height=Screen.height;
            if(parent!=null && parent.contentRect.width>0 && pixels>0 && height>0)
            {
                float width=Mathf.Min(pixels,parent.contentRect.width), scale=parent.contentRect.width/width;
                m_Container.style.width=width; m_Container.style.height=height*width/pixels;
                m_Container.style.transformOrigin=new TransformOrigin(Length.Percent(0),Length.Percent(0),0);
                m_Container.style.scale=new Scale(new Vector3(scale,scale,1));
            }
            bool active = LanRoomClient.Active;
            m_Root.Q("lanBrowser").EnableInClassList("lan-hidden", active);
            m_Root.Q("lanLobby").EnableInClassList("lan-hidden", !active);
            m_Root.Q<Button>("lanCreate").SetEnabled(!m_Busy && PlayerProfileClient.SupportsSinglePlayer);
            m_Root.Q<Button>("lanJoin").SetEnabled(!m_Busy); m_Root.Q<Button>("lanRefresh").SetEnabled(!m_Busy);
            if (!active)
            {
                m_Root.Q<Button>("lanEntry").text = "局域网 / LAN"; m_Roster = null;
                if (!string.IsNullOrEmpty(LanRoomClient.Error)) Status(LanRoomClient.Error, true);
                return;
            }
            var room = LanRoomClient.Room;
            m_Root.Q<Button>("lanEntry").text = $"局域网房间  {room.Members.Length}/{room.Capacity}";
            m_Root.Q<Label>("lanRoomHeading").text = room.Name;
            m_Root.Q<Label>("lanRoomMeta").text = $"{ModeName(room.Mode)}  /  {room.Members.Length}/{room.Capacity} 位干员";
            m_Root.Q<Label>("lanHostAddress").text = HostAddress();
            m_Root.Q<Label>("lanBriefing").text = room.Mode == RaidGameMode.BountyClash
                ? "共同进入极速赏金集结区后，创建或加入 1–3 人小队。至少两队准备后开战。"
                : "所有成员准备后，由房主统一进入远征。死亡丢失携带装备，成功撤离保留战利品。";
            string roster = string.Join("|", room.Members.Select(m => $"{m.Id}/{m.Name}/{m.Ready}/{m.InGame}"));
            if (roster != m_Roster)
            {
                m_Roster = roster; var list = m_Root.Q<ScrollView>("lanMembers"); list.Clear(); int index = 0;
                foreach (var member in room.Members)
                {
                    var row = m_MemberTemplate.CloneTree(); list.Add(row);
                    row.Q<Label>("memberIndex").text = (++index).ToString("00");
                    row.Q<Label>("memberName").text = member.Name + (member.Id == LanRoomClient.MemberId ? "（你）" : "");
                    row.Q<Label>("memberState").text = member.InGame ? "对局中" : member.Host ? "房主" : member.Ready ? "已准备" : "未准备";
                    row.EnableInClassList("lan-is-ready", member.Ready || member.Host);
                }
            }
            bool lobby = room.Phase == LanRoomPhase.Lobby;
            m_Root.Q<Button>("lanReady").EnableInClassList("lan-hidden", LanRoomClient.IsHost);
            m_Root.Q<Button>("lanReady").text = LanRoomClient.Ready ? "取消准备" : "准备就绪";
            m_Root.Q<Button>("lanReady").SetEnabled(lobby && !m_Busy);
            m_Root.Q<Button>("lanStart").EnableInClassList("lan-hidden", !LanRoomClient.IsHost);
            m_Root.Q<Button>("lanStart").SetEnabled(lobby && !m_Busy && room.Members.All(m => m.Host || m.Ready));
            m_Root.Q<Button>("lanEquipment").SetEnabled(lobby && !m_Busy);
            m_Root.Q<Button>("lanLeave").text = LanRoomClient.IsHost ? "关闭房间" : "离开房间";
            m_Root.Q<Button>("lanLeave").SetEnabled(!m_Busy);
            if (!lobby) Status("等待本局成员返回；可离开房间，或稍后再开下一局。", false);
            if (LanRoomClient.IsHost && !LanRoomClient.Server.DiscoveryAvailable) Status("自动发现端口被占用，请让成员使用上方 IPv4 直连。", true);
            if (!string.IsNullOrEmpty(LanRoomClient.Error)) Status(LanRoomClient.Error, true);
        }
        private void Status(string message, bool error)
        { var label = m_Root.Q<Label>("lanStatus"); label.text = message; label.EnableInClassList("lan-error", error); }
        public void Dispose()
        {
            m_Stop.Cancel(); m_Refresh.Pause(); LanRoomClient.Changed -= Present;
            m_Modal.UnregisterCallback<KeyDownEvent>(Key); m_Container.RemoveFromHierarchy();
        }
    }
}
