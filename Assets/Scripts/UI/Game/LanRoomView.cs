using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public enum LanRoomPage { Browse, Create, Direct, Lobby }

    public sealed class LanRoomView : IDisposable
    {
        private readonly VisualElement m_Container, m_Root, m_Modal, m_Host;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly Action m_Equipment;
        private readonly IVisualElementScheduledItem m_ViewportRefresh;
        private bool m_Busy, m_Disposed;
        private string m_Roster, m_LastRoom;
        private LanRoomPage m_Page;
        private RaidGameMode m_Mode;
        private Vector3 m_LastViewport;

        public LanRoomView(VisualElement host, Action equipment)
        {
            m_Host = host; m_Equipment = equipment;
            m_Container = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomPanel").CloneTree();
            m_Container.pickingMode = PickingMode.Ignore;
            m_Container.style.position = Position.Absolute; m_Container.style.left = m_Container.style.top = 0;
            host.Add(m_Container); m_Root = m_Container.Q(className: "lan-layer"); m_Modal = m_Root.Q("lanModal");
            m_Root.Q<TextField>("lanPlayerName").value = PlayerPrefs.GetString("Moonkov.LAN.Name", "Operator");
            m_Root.Q<TextField>("lanAddress").value = PlayerPrefs.GetString("Moonkov.LAN.Address", "192.168.1.100");
            Bind("lanEntry", Open); Bind("lanClose", Close);
            Bind("lanBrowseTab", () => SelectPage(LanRoomPage.Browse));
            Bind("lanCreateTab", () => SelectPage(LanRoomPage.Create));
            Bind("lanDirectTab", () => SelectPage(LanRoomPage.Direct));
            Bind("lanEmptyDirect", () => SelectPage(LanRoomPage.Direct));
            Bind("lanExpedition", () => SelectMode(RaidGameMode.Expedition));
            Bind("lanClash", () => SelectMode(RaidGameMode.BountyClash));
            Bind("lanRefresh", () => Run(RefreshRooms, "正在寻找附近房间…", "房间列表已更新。"));
            Bind("lanCreate", () => Run(Create, "正在创建房间…", "房间已创建，邀请成员加入后出发。"));
            Bind("lanJoin", () => Run(() => Join(m_Root.Q<TextField>("lanAddress").value.Trim(), m_Root.Q<IntegerField>("lanJoinPort").value), "正在连接房主…", "已加入房间，整理装备后准备出发。"));
            Bind("lanReady", () => Run(async () => { await LanRoomClient.RequestAsync("ready", ready: !LanRoomClient.Ready, ct: m_Stop.Token); }, "正在更新准备状态…", "准备状态已更新。"));
            Bind("lanStart", () => Run(async () => { await LanRoomClient.RequestAsync("start", ct: m_Stop.Token); }, "正在发起对局…", "正在等待房主完成地图加载。"));
            Bind("lanLeave", () => Run(async () => { await LanRoomClient.LeaveAsync(); }, "正在离开房间…", "已离开房间。"));
            Bind("lanEquipment", () => Run(async () =>
            {
                if (LanRoomClient.Ready) await LanRoomClient.RequestAsync("ready", ready: false, ct: m_Stop.Token);
                await LanRoomClient.RequestAsync("profile", ct: m_Stop.Token);
                m_Modal.AddToClassList("lan-hidden"); m_ViewportRefresh.Pause(); m_Equipment();
            }, "正在打开装备…", "装备整理完成后，回到房间准备。"));
            Bind("lanCopy", () =>
            {
                GUIUtility.systemCopyBuffer = LanRoomClient.IsHost ? LanRoomClient.LocalAddresses().FirstOrDefault() ?? "" : LanRoomClient.Address;
                Status("IPv4 已复制。多个地址时，请使用双方所在网络的地址。", false);
            });
            m_Modal.RegisterCallback<KeyDownEvent>(Key);
            m_Host.RegisterCallback<GeometryChangedEvent>(GeometryChanged);
            LanRoomClient.Changed += Present;
            // Only viewport sizing is sampled while visible; room state is event-driven.
            m_ViewportRefresh = m_Root.schedule.Execute(RefreshViewport).Every(500); m_ViewportRefresh.Pause();
            SelectMode(GameSettings.Instance.RequestedMode); RefreshViewport(); Present();
        }
        private void Bind(string name, Action callback) => m_Root.Q<Button>(name).clicked += callback;
        public void Open()
        {
            m_Modal.RemoveFromClassList("lan-hidden"); m_Container.BringToFront(); m_ViewportRefresh.Resume();
            RefreshViewport(); Present();
            FocusPage();
            if (!LanRoomClient.Active && m_Page == LanRoomPage.Browse) Run(RefreshRooms, "正在寻找附近房间…", "房间列表已更新。");
        }
        private void Close()
        {
            if (m_Busy) return;
            m_Modal.AddToClassList("lan-hidden"); m_ViewportRefresh.Pause(); m_Root.Q<Button>("lanEntry").Focus();
        }
        private void Key(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            evt.StopPropagation();
            if (m_Busy) return;
            if (!LanRoomClient.Active && m_Page != LanRoomPage.Browse) SelectPage(LanRoomPage.Browse); else Close();
        }
        private void SelectPage(LanRoomPage page)
        { if (m_Busy) return; m_Page = page; LanRoomPresentation.Page(m_Root, page); FocusPage(); }
        private void FocusPage()
        {
            if (m_Page == LanRoomPage.Create) m_Root.Q<TextField>("lanRoomName").Focus();
            else if (m_Page == LanRoomPage.Direct) m_Root.Q<TextField>("lanAddress").Focus();
            else m_Root.Q<Button>(m_Page == LanRoomPage.Lobby ? "lanEquipment" : "lanRefresh").Focus();
        }
        private void SelectMode(RaidGameMode mode)
        {
            if (m_Busy) return; m_Mode = mode;
            LanRoomPresentation.Mode(m_Root, mode);
            var capacity = m_Root.Q<IntegerField>("lanCapacity"); capacity.value = Mathf.Clamp(capacity.value, 2, mode == RaidGameMode.BountyClash ? 12 : 32);
        }
        private async void Run(Func<Task> action, string waiting, string success)
        {
            if (m_Busy || m_Disposed) return; m_Busy = true; Status(waiting, false); Present();
            try { await action(); if (!m_Disposed) Status(success, false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!m_Disposed) Status(ex.Message, true); }
            finally { m_Busy = false; if (!m_Disposed) Present(); }
        }
        private string SaveName()
        {
            string name = LanRoomServer.CleanName(m_Root.Q<TextField>("lanPlayerName").value);
            PlayerPrefs.SetString("Moonkov.LAN.Name", name); PlayerPrefs.Save(); return name;
        }
        private async Task Create()
        {
            await LanRoomClient.CreateAsync(m_Root.Q<TextField>("lanRoomName").value, SaveName(), m_Mode,
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
            m_Root.Q<Label>("lanRoomCount").text = "搜索中";
            var rooms = await LanRoomClient.DiscoverAsync(m_Stop.Token);
            if (m_Disposed) return;
            LanRoomPresentation.Rooms(m_Root, rooms, room => Run(() =>
            {
                var endpoint = room.Address.Split(':'); return Join(endpoint[0], int.Parse(endpoint[1]));
            }, "正在连接房主…", "已加入房间，整理装备后准备出发。"));
        }
        private static string HostAddress()
        {
            string address = LanRoomClient.IsHost ? string.Join("\n", LanRoomClient.LocalAddresses()) : LanRoomClient.Address;
            return $"{(string.IsNullOrEmpty(address) ? "未检测到局域网 IPv4" : address)}\n房间 TCP {LanRoomClient.Port} · 游戏 UDP {LanRoomClient.Room?.GamePort}";
        }
        private void GeometryChanged(GeometryChangedEvent evt) => RefreshViewport();
        private void RefreshViewport()
        {
            float pixels = Screen.width, height = Screen.height;
            if (m_Host.contentRect.width < 1 || pixels < 1 || height < 1) return;
            var state = new Vector3(pixels, height, m_Host.contentRect.width);
            if (state == m_LastViewport) return; m_LastViewport = state;
            float width = Mathf.Min(pixels, m_Host.contentRect.width), scale = m_Host.contentRect.width / width;
            m_Container.style.width = width; m_Container.style.height = height * width / pixels;
            m_Container.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0), 0);
            m_Container.style.scale = new Scale(new Vector3(scale, scale, 1));
            LanRoomPresentation.Viewport(m_Root, width);
        }
        private void Present()
        {
            if (m_Disposed) return;
            bool active = LanRoomClient.Active;
            if (active) m_Page = LanRoomPage.Lobby; else if (m_Page == LanRoomPage.Lobby) m_Page = LanRoomPage.Browse;
            LanRoomPresentation.Page(m_Root, m_Page);
            m_Root.Q<TextField>("lanPlayerName").SetEnabled(!active && !m_Busy);
            foreach (string name in new[] { "lanBrowseTab", "lanCreateTab", "lanDirectTab", "lanRefresh", "lanJoin", "lanExpedition", "lanClash", "lanClose" }) m_Root.Q<Button>(name).SetEnabled(!m_Busy);
            m_Root.Q<Button>("lanCreate").SetEnabled(!m_Busy && PlayerProfileClient.SupportsSinglePlayer);
            m_Root.Q<Label>("lanHostRequirement").text = PlayerProfileClient.SupportsSinglePlayer ? "创建后可邀请成员，再一起准备出发。" : "此版本不包含服务器功能，可加入其他房主。";
            if (!active)
            {
                m_Root.Q<Button>("lanEntry").text = "局域网联机"; m_Roster = m_LastRoom = null;
                if (!string.IsNullOrEmpty(LanRoomClient.Error)) Status(LanRoomClient.Error, true);
                return;
            }
            var room = LanRoomClient.Room;
            m_Root.Q<Button>("lanEntry").text = $"局域网 · {room.Members.Length}/{room.Capacity}";
            var roster = room.Phase + string.Join("|", room.Members.Select(m => $"{m.Id}/{m.Name}/{m.Host}/{m.Ready}/{m.InGame}"));
            LanRoomPresentation.Lobby(m_Root, room, LanRoomClient.MemberId, LanRoomClient.IsHost, LanRoomClient.Ready, m_Busy, HostAddress(), roster != m_Roster);
            m_Roster = roster;
            if (m_LastRoom != room.Id) { m_LastRoom = room.Id; FocusPage(); }
            if (LanRoomClient.IsHost && !LanRoomClient.Server.DiscoveryAvailable) Status("自动发现暂不可用，展开邀请信息，让成员通过 IPv4 加入。", true);
            if (!string.IsNullOrEmpty(LanRoomClient.Error)) Status(LanRoomClient.Error, true);
        }
        private void Status(string message, bool error)
        { LanRoomPresentation.Status(m_Root, message, error); }
        public void Dispose()
        {
            m_Disposed = true; m_Stop.Cancel(); m_ViewportRefresh.Pause(); LanRoomClient.Changed -= Present;
            m_Host.UnregisterCallback<GeometryChangedEvent>(GeometryChanged); m_Modal.UnregisterCallback<KeyDownEvent>(Key); m_Container.RemoveFromHierarchy();
        }
    }

    // Shared by the live view and native Editor previews; preview data cannot enter the network service.
    public static class LanRoomPresentation
    {
        public static void Page(VisualElement root, LanRoomPage page)
        {
            root.Q("lanBrowser").EnableInClassList("lan-hidden", page == LanRoomPage.Lobby);
            root.Q("lanLobby").EnableInClassList("lan-hidden", page != LanRoomPage.Lobby);
            var pages = new[] { "lanBrowsePage", "lanCreatePage", "lanDirectPage" };
            var tabs = new[] { "lanBrowseTab", "lanCreateTab", "lanDirectTab" };
            for (int i = 0; i < pages.Length; i++)
            { root.Q(pages[i]).EnableInClassList("lan-hidden", (int)page != i); root.Q(tabs[i]).EnableInClassList("lan-selected", (int)page == i); }
        }
        public static void Viewport(VisualElement root, float width)
        { root.EnableInClassList("lan-compact", width < 1120); root.EnableInClassList("lan-small", width < 820); }
        public static void Mode(VisualElement root, RaidGameMode mode)
        {
            bool clash = mode == RaidGameMode.BountyClash;
            root.Q("lanExpedition").EnableInClassList("lan-selected", !clash); root.Q("lanClash").EnableInClassList("lan-selected", clash);
            root.Q<Label>("lanCapacityHint").text = clash ? "极速赏金最多 12 人，进入后再组建 1–3 人小队。" : "远征最多 32 人，包含房主。";
        }
        public static void Rooms(VisualElement root, LanRoomSnapshot[] rooms, Action<LanRoomSnapshot> join)
        {
            var list = root.Q<ScrollView>("lanRooms"); list.Clear();
            bool empty = rooms.Length == 0;
            list.EnableInClassList("lan-hidden", empty); root.Q("lanEmptyState").EnableInClassList("lan-hidden", !empty);
            root.Q<Label>("lanRoomCount").text = $"{rooms.Length} 个房间";
            root.Q<Label>("lanEmptyTitle").text = "暂未发现房间";
            root.Q<Label>("lanEmpty").text = "确认房主已开房、双方处于同一网络。\n找不到时，可以通过 IP 地址加入。";
            var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomRow");
            foreach (var room in rooms.OrderBy(r => r.Name))
            {
                var row = template.CloneTree(); list.Add(row);
                row.Q<Label>("roomName").enableRichText = false; row.Q<Label>("roomName").text = room.Name;
                row.Q<Label>("roomDetail").text = $"{ModeName(room.Mode)} · {room.Address}";
                row.Q<Label>("roomCount").text = $"{room.Members.Length}/{room.Capacity}";
                row.Q("roomArt").EnableInClassList("lan-clash-art", room.Mode == RaidGameMode.BountyClash);
                row.Q<Button>("roomJoin").SetEnabled(room.Members.Length < room.Capacity);
                row.Q<Button>("roomJoin").clicked += () => join?.Invoke(room);
            }
        }
        public static string ModeName(RaidGameMode mode) => mode == RaidGameMode.BountyClash ? "极速赏金" : "远征";
        public static void Lobby(VisualElement root, LanRoomSnapshot room, string localId, bool host, bool ready, bool busy, string address, bool rebuildRoster = true)
        {
            root.Q<Label>("lanRoomHeading").enableRichText = false; root.Q<Label>("lanRoomHeading").text = room.Name;
            root.Q<Label>("lanRoomMeta").text = $"{ModeName(room.Mode)} · {room.Members.Length}/{room.Capacity} 位干员";
            int count = room.Members.Count(m => m.Host || m.Ready);
            root.Q<Label>("lanReadyCount").text = $"{count}/{room.Members.Length} 已就绪";
            root.Q<Label>("lanHostAddress").text = address;
            root.Q<Label>("lanModeTitle").text = ModeName(room.Mode);
            root.Q("lanModeArt").EnableInClassList("lan-clash-art", room.Mode == RaidGameMode.BountyClash);
            root.Q<Label>("lanBriefing").text = room.Mode == RaidGameMode.BountyClash
                ? "进入集结区后，创建或加入 1–3 人小队。至少两支队伍准备后开战。"
                : "探索月面，带回搜集到的物资。成功撤离保留战利品，死亡丢失携带装备。";
            if (rebuildRoster)
            {
                var list = root.Q<ScrollView>("lanMembers"); list.Clear();
                var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanMemberRow");
                foreach (var member in room.Members)
                {
                    var row = template.CloneTree(); list.Add(row);
                    row.Q<Label>("memberIndex").text = string.IsNullOrEmpty(member.Name) ? "?" : StringInfo.GetNextTextElement(member.Name);
                    row.Q<Label>("memberName").enableRichText = false; row.Q<Label>("memberName").text = member.Name;
                    row.Q<Label>("memberRole").text = member.Host ? member.Id == localId ? "房主 · 你" : "房主" : member.Id == localId ? "成员 · 你" : "成员";
                    row.Q<Label>("memberState").text = member.InGame ? room.Phase == LanRoomPhase.Preparing ? "载入中" : "对局中" : member.Host || member.Ready ? "已就绪" : "未准备";
                    row.EnableInClassList("lan-is-ready", member.Host || member.Ready);
                }
            }
            bool lobby = room.Phase == LanRoomPhase.Lobby;
            root.Q<Button>("lanReady").EnableInClassList("lan-hidden", host);
            root.Q<Button>("lanReady").text = ready ? "取消准备" : "准备就绪"; root.Q<Button>("lanReady").SetEnabled(lobby && !busy);
            root.Q<Button>("lanStart").EnableInClassList("lan-hidden", !host);
            root.Q<Button>("lanStart").text = room.Mode == RaidGameMode.BountyClash ? "进入极速赏金" : "开始远征";
            root.Q<Button>("lanStart").SetEnabled(lobby && !busy && count == room.Members.Length);
            root.Q<Button>("lanEquipment").SetEnabled(lobby && !busy);
            root.Q<Button>("lanLeave").text = host ? "关闭房间" : "离开房间"; root.Q<Button>("lanLeave").SetEnabled(!busy);
            var waiting = room.Members.FirstOrDefault(m => !m.Host && !m.Ready);
            root.Q<Label>("lanLaunchTitle").text = room.Phase == LanRoomPhase.Preparing ? "房主正在加载地图" : room.Phase == LanRoomPhase.Returning ? "等待成员返回房间" : !lobby ? "对局进行中" : waiting == null ? "全员就绪" : "等待成员准备";
            root.Q<Label>("lanLaunchHint").enableRichText = false;
            root.Q<Label>("lanLaunchHint").text = !lobby ? "加载与结算期间请保持连接。" : waiting != null ? $"{waiting.Name} 尚未准备" : host ? "确认装备后，即可带领小队出发。" : "等待房主发起对局。";
        }
        public static void Status(VisualElement root, string message, bool error)
        { var label = root.Q<Label>("lanStatus"); label.text = message; label.EnableInClassList("lan-error", error); }
    }
}
