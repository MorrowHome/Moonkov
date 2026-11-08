using System;
using System.Linq;
using Unity.MP_FPS.Online;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class OnlineSquadPanel
    {
        private ListView m_RoomList;
        private LobbyRoomInfo[] m_Rooms = Array.Empty<LobbyRoomInfo>();
        private LobbyJoinRequest[] m_JoinRequests = Array.Empty<LobbyJoinRequest>();
        private bool m_BrowseFromParty, m_AllowRoomActions;
        private string m_PendingRoomId;

        public bool BrowsingRooms => string.IsNullOrEmpty(m_Snapshot?.Code) || m_BrowseFromParty;

        private void InitializeRooms(Action browseRooms)
        {
            m_RoomList = Element.Q<ListView>("onlineRoomList");
            m_RoomList.makeItem = () => new RoomRow(m_Action);
            m_RoomList.bindItem = (row, index) => ((RoomRow)row).Bind(m_Rooms[index], m_AllowRoomActions, m_PendingRoomId);
            Element.Q<Button>("onlineBrowseRooms").clicked += () =>
            {
                m_BrowseFromParty = !m_BrowseFromParty;
                UpdateRoomVisibility(true);
                if (m_BrowseFromParty) browseRooms?.Invoke();
            };
            Element.Q<Button>("onlineWithdrawJoin").clicked += () =>
            {
                if (m_Snapshot?.JoinRequest?.Status == "Pending")
                    m_Action?.Invoke(new LobbyAction { Action = "withdraw-join", RequestId = m_Snapshot.JoinRequest.RequestId });
            };
        }

        private void UpdateRoomVisibility(bool party)
        {
            Element.Q("onlineParty").style.display = party && !BrowsingRooms ? DisplayStyle.Flex : DisplayStyle.None;
            Element.Q("onlineDirectory").style.display = BrowsingRooms ? DisplayStyle.Flex : DisplayStyle.None;
            var browse = Element.Q<Button>("onlineBrowseRooms");
            browse.style.display = party ? DisplayStyle.Flex : DisplayStyle.None;
            int requests = m_Snapshot?.JoinRequests?.Length ?? 0;
            if (m_BrowseFromParty && requests > 0) MoonkovLocalization.Set(browse, "返回小队 ({0})", requests);
            else MoonkovLocalization.Set(browse, m_BrowseFromParty ? "返回小队" : "房间列表");
        }

        private void PresentRooms(bool party, bool canReview, bool busy, string error)
        {
            UpdateRoomVisibility(party);
            PresentJoinRequests(canReview ? m_Snapshot.JoinRequests : null, busy);
            if (!BrowsingRooms) return;

            var rooms = m_Snapshot?.Rooms ?? Array.Empty<LobbyRoomInfo>();
            var request = m_Snapshot?.JoinRequest;
            bool pending = request?.Status == "Pending";
            string pendingRoomId = pending ? request.RoomId : null;
            bool allow = !party && !busy && !pending && m_Snapshot?.CanRequestJoin == true;
            bool actionsChanged = allow != m_AllowRoomActions || m_PendingRoomId != pendingRoomId;
            m_AllowRoomActions = allow;
            m_PendingRoomId = pendingRoomId;
            if (!SameRooms(rooms))
            {
                m_Rooms = rooms;
                m_RoomList.itemsSource = m_Rooms;
            }
            else if (actionsChanged) m_RoomList.RefreshItems();
            m_RoomList.style.height = m_RoomList.fixedItemHeight * Math.Min(4, rooms.Length);
            m_RoomList.style.display = rooms.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            var count = Element.Q<Label>("onlineRoomCount");
            count.style.display = m_Snapshot?.Rooms == null ? DisplayStyle.None : DisplayStyle.Flex;
            MoonkovLocalization.Set(count, "{0} 间", rooms.Length);
            string status = m_Snapshot == null ? "正在获取房间…"
                : !m_Snapshot.RoomDirectoryAvailable ? "更新账号服务后可浏览房间。"
                : m_Snapshot.Rooms == null ? "正在获取房间…"
                : rooms.Length == 0 ? "暂无在线房间"
                : party ? "离队后可申请加入其他房间。"
                : !m_Snapshot.CanRequestJoin ? "战局结算后可申请加入。" : "";
            if (m_Snapshot?.Rooms == null && !string.IsNullOrEmpty(error)) status = "房间列表暂不可用。";
            var notice = Element.Q<Label>("onlineDirectoryStatus");
            notice.style.display = status.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            MoonkovLocalization.Set(notice, status);

            var application = Element.Q("onlineApplication");
            application.style.display = request != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (request == null) return;
            string result = request.Status switch
            {
                "Pending" => "等待 {0} 审批",
                "Declined" => "{0} 拒绝了加入申请",
                "Expired" => "加入申请已过期",
                _ => "加入申请已失效"
            };
            MoonkovLocalization.Set(application.Q<Label>("onlineApplicationStatus"), result, request.LeaderName);
            var withdraw = application.Q<Button>("onlineWithdrawJoin");
            withdraw.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
            withdraw.SetEnabled(!busy);
        }

        private bool SameRooms(LobbyRoomInfo[] rooms)
        {
            if (ReferenceEquals(rooms, m_Rooms)) return true;
            if (rooms.Length != m_Rooms.Length) return false;
            for (int i = 0; i < rooms.Length; i++)
                if (!rooms[i].SameAs(m_Rooms[i])) return false;
            return true;
        }

        private void PresentJoinRequests(LobbyJoinRequest[] requests, bool busy)
        {
            requests ??= Array.Empty<LobbyJoinRequest>();
            var section = Element.Q("onlineJoinRequests");
            section.style.display = requests.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            section.SetEnabled(!busy);
            if (m_JoinRequests.Select(r => (r.RequestId, r.ApplicantName)).SequenceEqual(requests.Select(r => (r.RequestId, r.ApplicantName)))) return;
            m_JoinRequests = requests;
            MoonkovLocalization.Set(section.Q<Label>("onlineJoinRequestCount"), "加入申请 ({0})", requests.Length);
            var rows = section.Q("onlineJoinRequestRows");
            rows.Clear();
            foreach (var request in requests)
            {
                var row = new VisualElement();
                row.AddToClassList("online-member-row");
                row.Add(new Label(request.ApplicantName) { enableRichText = false });
                var decline = new Button(() => ReviewJoinRequest("decline-join", request.RequestId)) { name = "onlineDeclineJoin" };
                decline.AddToClassList("online-member-action");
                var approve = new Button(() => ReviewJoinRequest("approve-join", request.RequestId)) { name = "onlineApproveJoin" };
                approve.AddToClassList("online-member-action");
                approve.AddToClassList("online-approve");
                row.Add(decline); row.Add(approve); rows.Add(row);
                MoonkovLocalization.Set(decline, "拒绝");
                MoonkovLocalization.Set(approve, "同意");
            }
        }

        private void ReviewJoinRequest(string action, string requestId) =>
            m_Action?.Invoke(new LobbyAction { Action = action, RequestId = requestId });

        private sealed class RoomRow : VisualElement
        {
            private readonly Label m_Title = new Label { enableRichText = false };
            private readonly Label m_Metadata = new Label();
            private readonly Label m_Status = new Label();
            private readonly Button m_Apply;
            private string m_RoomId;

            public RoomRow(Action<LobbyAction> action)
            {
                AddToClassList("online-room-row");
                var summary = new VisualElement();
                summary.AddToClassList("online-room-summary");
                m_Title.AddToClassList("online-room-title");
                m_Metadata.AddToClassList("online-room-meta");
                summary.Add(m_Title); summary.Add(m_Metadata); Add(summary);
                m_Status.AddToClassList("online-room-state");
                Add(m_Status);
                m_Apply = new Button(() => action?.Invoke(new LobbyAction { Action = "request-join", RoomId = m_RoomId })) { name = "onlineApplyJoin" };
                m_Apply.AddToClassList("online-secondary");
                m_Apply.AddToClassList("online-apply");
                Add(m_Apply);
                MoonkovLocalization.Set(m_Apply, "申请加入");
            }

            public void Bind(LobbyRoomInfo room, bool allow, string pendingRoomId)
            {
                m_RoomId = room.RoomId;
                MoonkovLocalization.Set(m_Title, "{0} 的小队", room.LeaderName);
                MoonkovLocalization.Set(m_Metadata, "{0} / {1} / {2}/{3} 人", MoonkovLocalization.Text(room.Mode == "Versus" ? "对抗" : "合作"),
                    MoonkovLocalization.Text(room.ConnectionPath == OnlineNetworkProtocol.Vps ? "VPS 中转" : "P2P 直连"), room.MemberCount, room.Capacity);
                bool apply = room.Status == "Open" && room.RoomId != pendingRoomId;
                m_Apply.style.display = apply ? DisplayStyle.Flex : DisplayStyle.None;
                m_Apply.SetEnabled(allow);
                m_Status.style.display = apply ? DisplayStyle.None : DisplayStyle.Flex;
                MoonkovLocalization.Set(m_Status, room.Status switch
                {
                    "Open" => "待审批",
                    "Full" => "已满",
                    "Queued" => "等待出发",
                    "Starting" => "加载中",
                    "InRaid" => "战局中",
                    "Offline" => "队长离线",
                    _ => "不可加入"
                });
            }
        }
    }
}
