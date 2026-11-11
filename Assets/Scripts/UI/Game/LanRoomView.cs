using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Online;

namespace Unity.MP_FPS.Client
{
    public enum LanRoomPage { Browse, Create, Direct, Peer, Lobby }

    public sealed class LanRoomView : IDisposable
    {
        private readonly VisualElement m_Container, m_Root, m_Modal, m_Host;
        private readonly Button m_Entry;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly Action m_Equipment;
        private readonly Action m_Opened;
        private readonly LanSquadStage m_Squad;
        private readonly IVisualElementScheduledItem m_ViewportRefresh;
        private CancellationTokenSource m_JoiningPeer, m_SearchRooms;
        private LanJoinApplication[] m_Applications = Array.Empty<LanJoinApplication>();
        private bool m_Busy, m_Disposed, m_BrowseLocal;
        private string m_Roster, m_LastRoom;
        private LanRoomPage m_Page;
        private RaidGameMode m_Mode;
        private Vector3 m_LastViewport;

        public LanRoomView(VisualElement host, Action equipment, Action opened = null)
        {
            m_Host = host; m_Equipment = equipment; m_Opened = opened;
            m_Container = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomPanel").CloneTree();
            m_Container.pickingMode = PickingMode.Ignore;
            m_Container.style.position = Position.Absolute; m_Container.style.left = m_Container.style.top = 0;
            host.Add(m_Container); m_Root = m_Container.Q(className: "lan-layer"); m_Modal = m_Root.Q("lanModal");
            var trackerTip = m_Root.Q<Label>("lanTrackerTip");
            MoonkovLocalization.Set(trackerTip, trackerTip.text);
            m_Squad = new LanSquadStage(m_Modal);
            m_Root.Q<TextField>("lanPlayerName").value = PlayerPrefs.GetString("Moonkov.LAN.Name", "Operator");
            m_Root.Q<TextField>("lanAddress").value = PlayerPrefs.GetString("Moonkov.LAN.Address", "192.168.1.100");
            m_Root.Q<DropdownField>("lanCharacter").RegisterValueChangedCallback(_ => AppearanceChanged());
            m_Root.Q<DropdownField>("lanPose").RegisterValueChangedCallback(_ => AppearanceChanged());
            m_Root.Q<TextField>("lanPlayerName").RegisterValueChangedCallback(_ => { if (!LanRoomClient.Active) Present(); });
            var motion = m_Root.Q<Toggle>("lanReduceMotion");
            motion.SetValueWithoutNotify(PlayerPrefs.GetInt("Moonkov.SquadReducedMotion", OnlineSquadStage.SystemReducedMotion ? 1 : 0) != 0);
            SetReducedMotion(motion.value);
            motion.RegisterValueChangedCallback(e => { SetReducedMotion(e.newValue); PlayerPrefs.SetInt("Moonkov.SquadReducedMotion", e.newValue ? 1 : 0); });
            m_Entry = m_Root.Q<Button>("lanEntry");
            m_Entry.clicked += Open; Bind("lanClose", Close);
            Bind("lanBrowseTab", () => SelectPage(LanRoomPage.Browse));
            Bind("lanCreateTab", () => SelectPage(LanRoomPage.Create));
            Bind("lanDirectTab", () => SelectPage(LanRoomPage.Direct));
            Bind("lanPeerTab", () => SelectPage(LanRoomPage.Peer));
            Bind("lanEmptyDirect", () => SelectPage(LanRoomPage.Direct));
            Bind("lanExpedition", () => SelectMode(RaidGameMode.Expedition));
            Bind("lanClash", () => SelectMode(RaidGameMode.BountyClash));
            Bind("lanArena", () => SelectMode(RaidGameMode.Arena));
            Bind("lanClassic", () => SelectMode(RaidGameMode.Classic));
            Bind("lanBrowsePublic", () => SelectDirectory(false));
            Bind("lanBrowseLocal", () => SelectDirectory(true));
            Bind("lanRefresh", RefreshRooms);
            Bind("lanCancelApplication", CancelPeerJoin);
            Bind("lanCreate", () => Run(Create, "正在创建房间…", "房间已创建，邀请成员加入后出发。"));
            m_Root.Q<DropdownField>("lanConnectionType").RegisterValueChangedCallback(_ => Present());
            Bind("lanJoin", () => Run(() => Join(m_Root.Q<TextField>("lanAddress").value.Trim(), m_Root.Q<IntegerField>("lanJoinPort").value), "正在连接房主…", "已加入房间，整理装备后准备出发。"));
            Bind("lanPeerJoinCode", () => Run(() => JoinPeer((name, ct) =>
                LanRoomCode.JoinAsync(m_Root.Q<TextField>("lanPeerCode").value, name, ct)), "正在连接房主…", "已加入跨网房间。"));
            Bind("lanPeerJoin", () => Run(() => JoinPeer(JoinPeerManually), "正在获取跨网地址…", "已加入跨网房间。"));
            Bind("lanPeerCancelJoin", CancelPeerJoin);
            Bind("lanPeerPublish", () =>
            {
                var peer = LanRoomClient.PeerRoom;
                bool publish = !peer.Listed;
                Run(() => peer.SetListedAsync(LanRoomClient.Room, publish, m_Stop.Token),
                    "", publish ? "房间已公开。" : "房间已停止公开。");
            });
            Bind("lanPeerCopyCode", () => Run(async () =>
            {
                string code = await LanRoomClient.PeerRoom.GetRoomCodeAsync(m_Stop.Token);
                if (!m_Disposed) GUIUtility.systemCopyBuffer = code;
            }, "正在获取房间码…", "已复制房间码。"));
            Bind("lanPeerCopy", () => Run(async () =>
            {
                string invite = await LanRoomClient.PeerRoom.InviteAsync(m_Stop.Token);
                if (!m_Disposed) GUIUtility.systemCopyBuffer = invite;
            }, "正在获取跨网地址…", ""));
            Bind("lanPeerAccept", () => Run(() =>
            {
                LanRoomClient.PeerRoom.AcceptReply(GUIUtility.systemCopyBuffer);
                return Task.CompletedTask;
            }, "", ""));
            Bind("lanPeerCancelInvite", () => { LanRoomClient.PeerRoom.CancelInvite(); Status("", false); Present(); });
            Bind("lanReady", () => Run(async () => { await LanRoomClient.RequestAsync("ready", ready: !LanRoomClient.Ready, ct: m_Stop.Token); }, "正在更新准备状态…", "准备状态已更新。"));
            Bind("lanStart", () => Run(async () => { await LanRoomClient.RequestAsync("start", ct: m_Stop.Token); }, "正在发起对局…", "正在等待房主完成地图加载。"));
            Bind("lanLeave", () => Run(async () => { await LanRoomClient.LeaveAsync(); }, "正在离开房间…", "已离开房间。"));
            Bind("lanEquipment", () => Run(async () =>
            {
                if (LanRoomClient.Ready) await LanRoomClient.RequestAsync("ready", ready: false, ct: m_Stop.Token);
                await LanRoomClient.RequestAsync("profile", ct: m_Stop.Token);
                m_Modal.AddToClassList("lan-hidden"); m_ViewportRefresh.Pause(); m_Squad.SetVisible(false); m_Equipment();
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
        public void SetEntryHost(VisualElement host)
        {
            var target = host ?? m_Root;
            m_Entry.EnableInClassList("lan-entry-docked", host != null);
            if (m_Entry.parent == target) return;
            if (host == null) { target.Insert(0, m_Entry); return; }
            var logout = target.Q<Button>("stashLogout");
            if (logout != null) target.Insert(target.IndexOf(logout), m_Entry); else target.Add(m_Entry);
        }
        private void Bind(string name, Action callback) => m_Root.Q<Button>(name).clicked += callback;
        public void Open()
        {
            m_Opened?.Invoke();
            m_Modal.RemoveFromClassList("lan-hidden"); m_Container.BringToFront(); m_ViewportRefresh.Resume();
            RefreshViewport(); Present();
            FocusPage();
            if (LanRoomClient.Active && LanRoomClient.Room.Phase == LanRoomPhase.Lobby && AppearancePending())
                Run(() => LanRoomClient.RequestAsync("appearance", ct: m_Stop.Token), "正在同步角色配置…", "角色配置已更新。");
            else RefreshRooms();
        }
        private void Close()
        {
            if (m_Busy) return;
            CancelRoomSearch();
            m_Modal.AddToClassList("lan-hidden"); m_ViewportRefresh.Pause(); m_Squad.SetVisible(false); m_Entry.Focus();
        }
        private void Key(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            evt.StopPropagation();
            if (m_JoiningPeer != null) { CancelPeerJoin(); return; }
            if (m_Busy) return;
            if (!LanRoomClient.Active && m_Page != LanRoomPage.Browse) SelectPage(LanRoomPage.Browse); else Close();
        }
        private void SelectPage(LanRoomPage page)
        {
            if (m_Busy || m_Page == page) return;
            CancelRoomSearch();
            m_Page = page; LanRoomPresentation.Page(m_Root, page); FocusPage();
            RefreshRooms();
        }
        private void SelectDirectory(bool local)
        {
            if (m_Busy || local == m_BrowseLocal) return;
            CancelRoomSearch();
            m_BrowseLocal = local;
            Status("", false);
            RefreshRooms();
        }
        private void FocusPage()
        {
            if (m_Page == LanRoomPage.Create) m_Root.Q<TextField>("lanRoomName").Focus();
            else if (m_Page == LanRoomPage.Direct) m_Root.Q<TextField>("lanAddress").Focus();
            else if (m_Page == LanRoomPage.Peer) m_Root.Q<TextField>("lanPeerCode").Focus();
            else m_Root.Q<Button>(m_Page == LanRoomPage.Lobby ? "lanEquipment" : "lanRefresh").Focus();
        }
        private void SelectMode(RaidGameMode mode)
        {
            if (m_Busy) return; m_Mode = mode;
            LanRoomPresentation.Mode(m_Root, mode);
            var capacity = m_Root.Q<IntegerField>("lanCapacity"); capacity.value = Mathf.Clamp(capacity.value, 2, LanRoomServer.CapacityFor(mode));
        }
        private void SetReducedMotion(bool reduced)
        { m_Squad.Stage.ReducedMotion = reduced; m_Root.EnableInClassList("lan-reduced-motion", reduced); }
        private bool AppearancePending()
        {
            var self = LanRoomClient.Room?.Members.FirstOrDefault(m => m.Id == LanRoomClient.MemberId)?.Appearance;
            return self != null && (self.CharacterIndex != Mathf.Clamp(GameSettings.Instance.PlayerCharacter, 2, 4) || self.PoseIndex != LanRoomClient.PoseIndex);
        }
        private void AppearanceChanged()
        {
            if (m_Busy || m_Disposed) return;
            GameSettings.Instance.PlayerCharacter = m_Root.Q<DropdownField>("lanCharacter").index + 2;
            PlayerPrefs.SetInt("Moonkov.LAN.Pose", m_Root.Q<DropdownField>("lanPose").index - 1);
            PlayerPrefs.Save();
            if (LanRoomClient.Active) Run(() => LanRoomClient.RequestAsync("appearance", ct: m_Stop.Token), "正在同步角色配置…", "角色配置已更新。");
            else Present();
        }
        private async void Run(Func<Task> action, string waiting, string success)
        {
            if (m_Busy || m_Disposed) return;
            CancelRoomSearch();
            m_Busy = true; Status(waiting, false); Present();
            try { await action(); if (!m_Disposed) Status(success, false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!m_Disposed) Status(ex.Message, true); }
            finally
            {
                m_Busy = false;
                if (!m_Disposed)
                {
                    Present();
                    if (m_Page == LanRoomPage.Peer) FocusPage();
                }
            }
        }
        private string SaveName()
        {
            string name = LanRoomServer.CleanName(m_Root.Q<TextField>("lanPlayerName").value);
            PlayerPrefs.SetString("Moonkov.LAN.Name", name); PlayerPrefs.Save(); return name;
        }
        private async Task Create()
        {
            GameSettings.Instance.PlayerCharacter = m_Root.Q<DropdownField>("lanCharacter").index + 2;
            await LanRoomClient.CreateAsync(m_Root.Q<TextField>("lanRoomName").value, SaveName(), m_Mode,
                m_Root.Q<IntegerField>("lanCapacity").value, m_Root.Q<IntegerField>("lanControlPort").value,
                m_Root.Q<IntegerField>("lanGamePort").value, m_Stop.Token, crossNetwork: m_Root.Q<DropdownField>("lanConnectionType").index == 1);
        }
        private async Task Join(string address, int port)
        {
            GameSettings.Instance.PlayerCharacter = m_Root.Q<DropdownField>("lanCharacter").index + 2;
            await LanRoomClient.JoinAsync(address, port, SaveName(), m_Stop.Token);
            PlayerPrefs.SetString("Moonkov.LAN.Address", address); PlayerPrefs.Save();
        }
        private async Task JoinPeer(Func<string, CancellationToken, Task> join)
        {
            GameSettings.Instance.PlayerCharacter = m_Root.Q<DropdownField>("lanCharacter").index + 2;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(m_Stop.Token);
            m_JoiningPeer = stop;
            Present();
            m_Root.Q<Button>(m_Page == LanRoomPage.Browse ? "lanCancelApplication" : "lanPeerCancelJoin").Focus();
            try { await join(SaveName(), stop.Token); }
            finally { m_JoiningPeer = null; }
        }
        private Task JoinPeerManually(string name, CancellationToken ct) =>
            LanRoomClient.JoinPeerAsync(GUIUtility.systemCopyBuffer, name, (reply, _) =>
            {
                if (!m_Disposed)
                {
                    GUIUtility.systemCopyBuffer = reply;
                    Status("回执已复制。发给房主，请他尽快粘贴。", false);
                }
                return Task.CompletedTask;
            }, ct);
        private void CancelPeerJoin()
        {
            m_JoiningPeer?.Cancel();
            Status(m_Page == LanRoomPage.Browse ? "已撤回申请。" : "已取消加入。", false);
        }
        private void CancelRoomSearch()
        {
            var search = m_SearchRooms;
            m_SearchRooms = null;
            search?.Cancel();
        }
        private async void RefreshRooms()
        {
            if (m_Disposed || m_Busy || m_Page != LanRoomPage.Browse || m_SearchRooms != null || m_Modal.ClassListContains("lan-hidden")) return;
            using var search = CancellationTokenSource.CreateLinkedTokenSource(m_Stop.Token);
            m_SearchRooms = search;
            LanRoomPresentation.DirectoryHeading(m_Root, m_BrowseLocal, "搜索中");
            m_Root.Q("lanRooms").AddToClassList("lan-hidden");
            m_Root.Q("lanEmptyState").AddToClassList("lan-hidden");
            m_Root.Q("lanRefresh").SetEnabled(false);
            try
            {
                if (m_BrowseLocal)
                {
                    var rooms = await LanRoomClient.DiscoverAsync(search.Token);
                    if (m_SearchRooms == search) LanRoomPresentation.Rooms(m_Root, rooms, room => Run(() =>
                    {
                        var endpoint = room.Address.Split(':'); return Join(endpoint[0], int.Parse(endpoint[1]));
                    }, "正在连接房主…", "已加入房间，整理装备后准备出发。"));
                }
                else
                {
                    var rooms = await LanRoomCode.BrowseAsync(search.Token);
                    if (m_SearchRooms == search) LanRoomPresentation.PeerRooms(m_Root, rooms, room => Run(() => JoinPeer((name, ct) =>
                        LanRoomCode.ApplyAsync(room.Id, name, message => Status(message, false), ct)),
                        "正在发送申请…", "已加入跨网房间。"));
                }
            }
            catch (Exception)
            {
                if (m_SearchRooms == search)
                    LanRoomPresentation.DirectoryEmpty(m_Root, m_BrowseLocal, m_BrowseLocal ? "暂时无法搜索局域网" : "暂时无法加载大厅");
            }
            finally
            {
                if (m_SearchRooms == search)
                {
                    m_SearchRooms = null;
                    m_Root.Q("lanRefresh").SetEnabled(!m_Busy);
                }
            }
        }
        private static string HostAddress()
        {
            string address = LanRoomClient.IsHost ? string.Join("\n", LanRoomClient.LocalAddresses()) : LanRoomClient.Address;
            return $"{(string.IsNullOrEmpty(address) ? "未检测到局域网 IPv4" : address)}\n房间 TCP {LanRoomClient.Port}\n游戏 UDP {LanRoomClient.Room?.GamePort}";
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
            var character = m_Root.Q<DropdownField>("lanCharacter");
            character.SetValueWithoutNotify(character.choices[Mathf.Clamp(GameSettings.Instance.PlayerCharacter - 2, 0, 2)]);
            var pose = m_Root.Q<DropdownField>("lanPose"); pose.SetValueWithoutNotify(pose.choices[LanRoomClient.PoseIndex + 1]);
            character.SetEnabled(!m_Busy && (!active || LanRoomClient.Room.Phase == LanRoomPhase.Lobby)); pose.SetEnabled(character.enabledSelf);
            m_Squad.Present(active ? LanRoomClient.Room : null, LanRoomClient.MemberId, m_Root.Q<TextField>("lanPlayerName").value,
                new LobbyAppearance { CharacterIndex = Mathf.Clamp(GameSettings.Instance.PlayerCharacter, 2, 4), PoseIndex = LanRoomClient.PoseIndex });
            m_Squad.SetVisible(!m_Modal.ClassListContains("lan-hidden"));
            if (active) m_Page = LanRoomPage.Lobby;
            else if (m_Page == LanRoomPage.Lobby)
            {
                m_Page = LanRoomPage.Browse;
                m_Root.schedule.Execute(RefreshRooms);
            }
            LanRoomPresentation.Page(m_Root, m_Page);
            m_Root.Q<TextField>("lanPlayerName").SetEnabled(!active && !m_Busy);
            foreach (string name in new[] { "lanBrowseTab", "lanCreateTab", "lanDirectTab", "lanPeerTab", "lanBrowsePublic", "lanBrowseLocal", "lanRooms", "lanJoin", "lanExpedition", "lanClash", "lanClose" })
                m_Root.Q(name).SetEnabled(!m_Busy);
            bool applying = m_JoiningPeer != null && m_Page == LanRoomPage.Browse;
            m_Root.Q("lanRefresh").EnableInClassList("lan-hidden", applying);
            m_Root.Q("lanRefresh").SetEnabled(!m_Busy && m_SearchRooms == null);
            m_Root.Q("lanCancelApplication").EnableInClassList("lan-hidden", !applying);
            var connectionType = m_Root.Q<DropdownField>("lanConnectionType");
            connectionType.SetEnabled(!m_Busy);
            m_Root.Q("lanCreatePorts").EnableInClassList("lan-hidden", connectionType.index == 1);
            m_Root.Q("lanPeerJoinCode").EnableInClassList("lan-hidden", m_JoiningPeer != null);
            m_Root.Q("lanPeerCode").SetEnabled(!m_Busy);
            m_Root.Q("lanPeerJoin").SetEnabled(!m_Busy);
            m_Root.Q("lanPeerCancelJoin").EnableInClassList("lan-hidden", m_JoiningPeer == null);
            m_Root.Q<Button>("lanCreate").SetEnabled(!m_Busy && PlayerProfileClient.SupportsSinglePlayer);
            m_Root.Q<Label>("lanHostRequirement").text = PlayerProfileClient.SupportsSinglePlayer ? "创建后可邀请成员，再一起准备出发。" : "此版本不包含服务器功能，可加入其他房主。";
            if (!active)
            {
                m_Entry.text = "好友联机"; m_Roster = m_LastRoom = null;
                PresentApplications(Array.Empty<LanJoinApplication>(), false);
                if (!string.IsNullOrEmpty(LanRoomClient.Error)) Status(LanRoomClient.Error, true);
                return;
            }
            var room = LanRoomClient.Room;
            var peerRoom = LanRoomClient.PeerRoom;
            m_Entry.text = $"好友房间 {room.Members.Length}/{room.Capacity}";
            var roster = room.Phase + string.Join("|", room.Members.Select(m => $"{m.Id}/{m.Name}/{m.Host}/{m.Ready}/{m.InGame}/{m.Appearance?.CharacterIndex}"));
            LanRoomPresentation.Lobby(m_Root, room, LanRoomClient.MemberId, LanRoomClient.IsHost, LanRoomClient.Ready, m_Busy,
                peerRoom == null ? HostAddress() : "", roster != m_Roster);
            m_Root.Q("lanAddressInvite").EnableInClassList("lan-hidden", peerRoom != null);
            bool peerHost = peerRoom != null && LanRoomClient.IsHost && room.Phase == LanRoomPhase.Lobby;
            bool canInvite = !m_Busy && room.Phase == LanRoomPhase.Lobby && room.Members.Length < room.Capacity;
            m_Root.Q("lanPeerInvite").EnableInClassList("lan-hidden", !peerHost);
            PresentApplications(peerHost ? peerRoom.Applications : Array.Empty<LanJoinApplication>(), canInvite && peerHost && !peerRoom.HasManualOffer);
            if (peerHost)
            {
                var publish = m_Root.Q<Button>("lanPeerPublish");
                publish.text = peerRoom.Listed ? "停止公开" : "公开到大厅";
                publish.SetEnabled(!m_Busy && (peerRoom.Listed || canInvite && !peerRoom.HasManualOffer));
                var copyCode = m_Root.Q<Button>("lanPeerCopyCode");
                copyCode.text = peerRoom.RoomCode == null ? "复制房间码" : $"复制 {peerRoom.RoomCode}";
                copyCode.SetEnabled(canInvite && !peerRoom.HasManualOffer);
                m_Root.Q<Button>("lanPeerCopy").SetEnabled(canInvite && (peerRoom.CanInvite || peerRoom.WaitingForManualReply));
                var accept = m_Root.Q<Button>("lanPeerAccept");
                accept.EnableInClassList("lan-hidden", !peerRoom.WaitingForManualReply);
                accept.SetEnabled(canInvite);
                var cancel = m_Root.Q<Button>("lanPeerCancelInvite");
                cancel.EnableInClassList("lan-hidden", !peerRoom.HasManualOffer);
                cancel.SetEnabled(!m_Busy);
                var inviteStatus = m_Root.Q<Label>("lanPeerInviteStatus");
                inviteStatus.text = peerRoom.InviteStatus ?? "";
                inviteStatus.EnableInClassList("lan-hidden", inviteStatus.text.Length == 0);
                if (peerRoom.HasManualOffer) m_Root.Q<Button>("lanStart").SetEnabled(false);
            }
            if (AppearancePending()) { m_Root.Q<Button>("lanReady").SetEnabled(false); m_Root.Q<Button>("lanStart").SetEnabled(false); }
            m_Roster = roster;
            if (m_LastRoom != room.Id) { m_LastRoom = room.Id; FocusPage(); }
            if (LanRoomClient.IsHost && peerRoom == null && !LanRoomClient.Server.DiscoveryAvailable) Status("自动发现暂不可用，展开邀请信息，让成员通过 IPv4 加入。", true);
            if (!string.IsNullOrEmpty(LanRoomClient.Error)) Status(LanRoomClient.Error, true);
        }
        private void PresentApplications(LanJoinApplication[] applications, bool canApprove)
        {
            var section = m_Root.Q<Foldout>("lanPeerApplications");
            var rows = m_Root.Q("lanPeerApplicationRows");
            bool changed = m_Applications.Length != applications.Length ||
                m_Applications.Where((previous, i) => previous.Id != applications[i].Id).Any();
            if (changed)
            {
                if (m_Applications.Length == 0) section.SetValueWithoutNotify(true);
                m_Applications = applications;
                LanRoomPresentation.Applications(m_Root, applications, (id, approve) => Run(() =>
                    LanRoomClient.PeerRoom.ReviewAsync(id, approve, m_Stop.Token), "", ""));
            }
            rows.SetEnabled(!m_Busy);
            foreach (var row in rows.Children()) row.Q("lanApplicationApprove").SetEnabled(canApprove);
        }
        private void Status(string message, bool error)
        { LanRoomPresentation.Status(m_Root, message, error); }
        public void Dispose()
        {
            m_Entry.clicked -= Open; m_Entry.RemoveFromHierarchy();
            m_Disposed = true; CancelRoomSearch(); m_Stop.Cancel(); m_ViewportRefresh.Pause(); m_Squad.Dispose(); LanRoomClient.Changed -= Present;
            m_Host.UnregisterCallback<GeometryChangedEvent>(GeometryChanged); m_Modal.UnregisterCallback<KeyDownEvent>(Key); m_Container.RemoveFromHierarchy();
        }
    }

    // Shared by the live view and native Editor previews; preview data cannot enter the network service.
    public static class LanRoomPresentation
    {
        public static void Page(VisualElement root, LanRoomPage page)
        {
            root.Q("lanModal").EnableInClassList("lan-in-room", page == LanRoomPage.Lobby);
            root.Q("lanBrowser").EnableInClassList("lan-hidden", page == LanRoomPage.Lobby);
            root.Q("lanLobby").EnableInClassList("lan-hidden", page != LanRoomPage.Lobby);
            var pages = new[] { "lanBrowsePage", "lanCreatePage", "lanDirectPage", "lanPeerPage" };
            var tabs = new[] { "lanBrowseTab", "lanCreateTab", "lanDirectTab", "lanPeerTab" };
            for (int i = 0; i < pages.Length; i++)
            { root.Q(pages[i]).EnableInClassList("lan-hidden", (int)page != i); root.Q(tabs[i]).EnableInClassList("lan-selected", (int)page == i); }
        }
        public static void Viewport(VisualElement root, float width)
        { root.EnableInClassList("lan-compact", width < 1120); root.EnableInClassList("lan-small", width < 820); }
        public static void Mode(VisualElement root, RaidGameMode mode)
        {
            root.Q("lanExpedition").EnableInClassList("lan-selected", mode == RaidGameMode.Expedition);
            root.Q("lanClash").EnableInClassList("lan-selected", mode == RaidGameMode.BountyClash);
            root.Q("lanArena").EnableInClassList("lan-selected", mode == RaidGameMode.Arena);
            root.Q("lanClassic").EnableInClassList("lan-selected", mode == RaidGameMode.Classic);
            root.Q("lanClassic").SetEnabled(ClassicDust2Map.Available);
            root.Q<Label>("lanClassicHint").text = ClassicDust2Map.Available ? "沙2，空位由人机补齐" : "待导入沙2";
            root.Q<Label>("lanCapacityHint").text = mode switch
            {
                RaidGameMode.Arena => "2–5 人，装备免费",
                RaidGameMode.Classic => "最多 10 人",
                RaidGameMode.BountyClash => "极速赏金最多 12 人，进入后再组建 1–3 人小队。",
                _ => "远征最多 32 人，包含房主。"
            };
        }
        public static void Rooms(VisualElement root, LanRoomSnapshot[] rooms, Action<LanRoomSnapshot> join)
        {
            var list = RoomList(root, true, rooms.Length);
            var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomRow");
            foreach (var room in rooms.OrderBy(r => r.Name))
                RoomRow(list, template, room.Name, $"{ModeName(room.Mode)} / {room.Address}", room.Mode == RaidGameMode.BountyClash,
                    room.Members.Length, room.Capacity, "加入", () => join?.Invoke(room));
        }
        internal static void PeerRooms(VisualElement root, LanRoomListing[] rooms, Action<LanRoomListing> apply)
        {
            var list = RoomList(root, false, rooms.Length);
            var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanRoomRow");
            foreach (var room in rooms)
                RoomRow(list, template, room.Name, ModeName(room.Clash ? RaidGameMode.BountyClash : RaidGameMode.Expedition),
                    room.Clash, room.Members, room.Capacity, "申请加入", () => apply?.Invoke(room));
        }
        internal static void DirectoryHeading(VisualElement root, bool local, string count)
        {
            root.Q("lanBrowsePublic").EnableInClassList("lan-selected", !local);
            root.Q("lanBrowseLocal").EnableInClassList("lan-selected", local);
            root.Q<Label>("lanDirectoryTitle").text = local ? "附近的房间" : "跨网大厅";
            root.Q<Label>("lanRoomCount").text = count;
        }
        internal static void DirectoryEmpty(VisualElement root, bool local, string title)
        {
            root.Q<Label>("lanRoomCount").text = "";
            root.Q("lanRooms").AddToClassList("lan-hidden");
            root.Q("lanEmptyState").RemoveFromClassList("lan-hidden");
            root.Q<Label>("lanEmptyTitle").text = title;
            root.Q("lanEmpty").EnableInClassList("lan-hidden", !local);
            root.Q("lanEmptyDirect").EnableInClassList("lan-hidden", !local);
        }
        private static ScrollView RoomList(VisualElement root, bool local, int count)
        {
            if (count == 0) DirectoryEmpty(root, local, local ? "暂未发现房间" : "暂无公开房间");
            else root.Q("lanEmptyState").AddToClassList("lan-hidden");
            DirectoryHeading(root, local, $"{count} 个房间");
            var list = root.Q<ScrollView>("lanRooms"); list.Clear();
            list.EnableInClassList("lan-hidden", count == 0);
            return list;
        }
        private static void RoomRow(ScrollView list, VisualTreeAsset template, string name, string detail, bool clash, int members, int capacity, string action, Action clicked)
        {
            var row = template.CloneTree(); list.Add(row);
            var title = row.Q<Label>("roomName");
            title.enableRichText = false; title.text = name; title.tooltip = name;
            row.Q<Label>("roomDetail").text = detail;
            row.Q<Label>("roomCount").text = $"{members}/{capacity}";
            row.Q<Label>("roomAction").text = action;
            row.Q("roomArt").EnableInClassList("lan-clash-art", clash);
            var button = row.Q<Button>("roomJoin");
            button.SetEnabled(members < capacity);
            button.clicked += clicked;
        }
        internal static void Applications(VisualElement root, LanJoinApplication[] applications, Action<string, bool> review)
        {
            var section = root.Q<Foldout>("lanPeerApplications");
            section.EnableInClassList("lan-hidden", applications.Length == 0);
            section.text = $"加入申请 {applications.Length}";
            var rows = root.Q("lanPeerApplicationRows"); rows.Clear();
            foreach (var application in applications)
            {
                var row = new VisualElement(); row.AddToClassList("lan-application-row"); rows.Add(row);
                var name = new Label(application.Name) { enableRichText = false, tooltip = application.Name };
                name.AddToClassList("lan-application-name"); row.Add(name);
                var actions = new VisualElement(); actions.AddToClassList("lan-application-actions"); row.Add(actions);
                var decline = new Button(() => review(application.Id, false)) { text = "拒绝" };
                decline.AddToClassList("lan-secondary"); actions.Add(decline);
                var approve = new Button(() => review(application.Id, true)) { name = "lanApplicationApprove", text = "同意" };
                approve.AddToClassList("lan-primary"); actions.Add(approve);
            }
        }
        public static string ModeName(RaidGameMode mode) => mode switch
        {
            RaidGameMode.Arena => "竞技场",
            RaidGameMode.Classic => "经典爆破",
            RaidGameMode.BountyClash => "极速赏金",
            _ => "远征"
        };
        public static void Lobby(VisualElement root, LanRoomSnapshot room, string localId, bool host, bool ready, bool busy, string address, bool rebuildRoster = true)
        {
            var heading = root.Q<Label>("lanRoomHeading");
            heading.enableRichText = false; heading.text = room.Name; heading.tooltip = room.Name;
            root.Q<Label>("lanRoomMeta").text = $"{ModeName(room.Mode)}  {room.Members.Length}/{room.Capacity} 人";
            int count = room.Members.Count(m => m.Host || m.Ready);
            root.Q<Label>("lanReadyCount").text = $"{count}/{room.Members.Length} 已就绪";
            root.Q<Label>("lanHostAddress").text = address;
            root.Q("lanRosterDetails").EnableInClassList("lan-hidden", room.Members.Length <= 4);
            if (rebuildRoster)
            {
                var list = root.Q<ScrollView>("lanMembers"); list.Clear();
                var template = Resources.Load<VisualTreeAsset>("Moonkov/UI/LanMemberRow");
                foreach (var member in room.Members)
                {
                    var row = template.CloneTree(); list.Add(row);
                    row.Q<Label>("memberIndex").text = string.IsNullOrEmpty(member.Name) ? "?" : StringInfo.GetNextTextElement(member.Name);
                    row.Q<Label>("memberName").enableRichText = false; row.Q<Label>("memberName").text = member.Name;
                    row.Q<Label>("memberRole").text = member.Host ? member.Id == localId ? "房主 / 你" : "房主" : member.Id == localId ? "成员 / 你" : "成员";
                    row.Q<Label>("memberState").text = member.InGame ? room.Phase == LanRoomPhase.Preparing ? "载入中" : "对局中" : member.Host || member.Ready ? "已就绪" : "未准备";
                    row.EnableInClassList("lan-is-ready", member.Host || member.Ready);
                }
            }
            bool lobby = room.Phase == LanRoomPhase.Lobby;
            root.Q<Button>("lanReady").EnableInClassList("lan-hidden", host);
            root.Q<Button>("lanReady").text = ready ? "取消准备" : "准备就绪"; root.Q<Button>("lanReady").SetEnabled(lobby && !busy);
            root.Q<Button>("lanStart").EnableInClassList("lan-hidden", !host);
            bool needsOpponent = room.Mode == RaidGameMode.Arena && room.Members.Length < 2;
            root.Q<Button>("lanStart").text = room.Mode == RaidGameMode.Expedition ? "开始远征" : "进入" + ModeName(room.Mode);
            root.Q<Button>("lanStart").SetEnabled(lobby && !busy && !needsOpponent && count == room.Members.Length &&
                (room.Mode != RaidGameMode.Classic || ClassicDust2Map.Available));
            root.Q<Button>("lanEquipment").SetEnabled(lobby && !busy);
            root.Q<Button>("lanLeave").text = host ? "关闭房间" : "离开房间"; root.Q<Button>("lanLeave").SetEnabled(!busy);
            var waiting = room.Members.FirstOrDefault(m => !m.Host && !m.Ready);
            root.Q<Label>("lanLaunchTitle").text = room.Phase == LanRoomPhase.Preparing ? "房主正在加载地图" : room.Phase == LanRoomPhase.Returning ? "等待成员返回房间" : !lobby ? "对局进行中" : waiting == null ? "全员就绪" : "等待成员准备";
            root.Q<Label>("lanLaunchHint").enableRichText = false;
            var hint = root.Q<Label>("lanLaunchHint");
            hint.text = !lobby ? "" : needsOpponent ? "等待一名玩家加入" : waiting != null ? $"{waiting.Name} 尚未准备"
                : !host ? "等待房主出发" : room.Mode == RaidGameMode.BountyClash ? "进入集结区后组队" : "";
            hint.EnableInClassList("lan-hidden", hint.text.Length == 0);
        }
        public static void Status(VisualElement root, string message, bool error)
        {
            root.Q("lanNotice").EnableInClassList("lan-hidden", string.IsNullOrWhiteSpace(message));
            var label = root.Q<Label>("lanStatus"); label.text = message; label.EnableInClassList("lan-error", error);
        }
    }
}
