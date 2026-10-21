using System;
using Unity.MP_FPS.Client;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed class BountyClashView : IDisposable
    {
        public VisualElement Element { get; }
        private readonly Action<ClashIntent, int, string> m_Send;
        private readonly Action m_Return;
        private readonly VisualElement m_Lobby, m_Battle, m_Finished, m_SenseLayer, m_MarkerLayer;
        private readonly DropdownField m_Capacity;
        private readonly TextField m_Code;
        private readonly Button m_Create, m_Join, m_Resize, m_Ready, m_Leave, m_Copy, m_Next, m_LobbyReturn, m_FinishReturn;
        private readonly Label[] m_LobbyRows = new Label[BountyClashRules.MaxPlayers], m_TeamRows = new Label[3], m_Markers = new Label[3];
        private readonly BountyClashMapView m_Map;
        private ClashSnapshotRpc m_Last;
        private bool m_LocalReady;
        private int m_LastTeam = int.MinValue;
        public BountyClashView(VisualElement host, Action<ClashIntent, int, string> send, Action returnToShip)
        {
            m_Send = send; m_Return = returnToShip;
            Element = Resources.Load<VisualTreeAsset>("Moonkov/UI/BountyClash").CloneTree();
            Element.pickingMode = PickingMode.Ignore; Element.AddToClassList("raid-ui"); host.Add(Element);
            Element.style.position = Position.Absolute; Element.style.left = Element.style.top = Element.style.right = Element.style.bottom = 0;
            Element.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0), 0);
            var originalHeader = Element.Q("clashHeader");
            var instrument = TerminalLayout.Clone(Resources.Load<VisualTreeAsset>("Moonkov/RaidUI"), "raidStatus");
            instrument.name = "clashInstrument"; instrument.AddToClassList("clash-header");
            instrument.Q<Label>("raidTimer").name = "clashClock";
            instrument.Q<Label>("raidClock").name = "clashBounty";
            foreach (var child in instrument.Children()) if (child is Label && child.name != "clashClock" && child.name != "clashBounty") { child.name = "clashPhase"; break; }
            instrument.Add(originalHeader.Q("clashObjective")); instrument.Add(originalHeader.Q("clashBanish"));
            var headerParent = originalHeader.parent; originalHeader.RemoveFromHierarchy(); headerParent.Add(instrument);
            BountyUIComposition.Window(Element.Q("clashLobbyWindow"));
            BountyUIComposition.Window(Element.Q("clashFinished"));
            m_Lobby = Element.Q("clashLobby"); m_Battle = Element.Q("clashBattle"); m_Finished = Element.Q("clashFinished");
            var sidebar = new VisualElement { name = "clashSidebar", pickingMode = PickingMode.Ignore };
            sidebar.AddToClassList("clash-sidebar"); m_Battle.Add(sidebar);
            sidebar.Add(Element.Q("clashMap")); sidebar.Add(Element.Q<Label>("clashSense").parent); sidebar.Add(Element.Q("clashExits"));
            m_SenseLayer = Element.Q("clashSenseLayer"); m_MarkerLayer = Element.Q("clashTeamMarkers");
            m_Capacity = Element.Q<DropdownField>("clashCapacity"); m_Code = Element.Q<TextField>("clashInviteInput");
            m_Create = Element.Q<Button>("clashCreate"); m_Create.clicked += Create;
            m_Join = Element.Q<Button>("clashJoin"); m_Join.clicked += Join;
            m_Resize = Element.Q<Button>("clashResize"); m_Resize.clicked += Resize;
            m_Ready = Element.Q<Button>("clashReady"); m_Ready.clicked += Ready;
            m_Leave = Element.Q<Button>("clashLeave"); m_Leave.clicked += Leave;
            m_Copy = Element.Q<Button>("clashCopy"); m_Copy.clicked += Copy;
            m_Next = Element.Q<Button>("clashNextRound"); m_Next.clicked += Next;
            m_LobbyReturn = Element.Q<Button>("clashLobbyReturn"); m_LobbyReturn.clicked += Return;
            m_FinishReturn = Element.Q<Button>("clashFinishReturn"); m_FinishReturn.clicked += Return;
            for (int i = 0; i < m_LobbyRows.Length; i++) m_LobbyRows[i] = Row(Element.Q("clashLobbyRows"));
            for (int i = 0; i < 3; i++) { m_TeamRows[i] = Row(Element.Q("clashTeamRows")); m_Markers[i] = Row(m_MarkerLayer); m_Markers[i].AddToClassList("clash-friendly-marker"); }
            m_Map = new BountyClashMapView(); m_Map.style.flexGrow = 1; Element.Q("clashMap").Add(m_Map);
            MoonkovLocalization.Bind(Element); Show(false);
            m_Capacity.choices = new System.Collections.Generic.List<string> { MoonkovLocalization.Text("1 / SOLO"), MoonkovLocalization.Text("2 / DUO"), MoonkovLocalization.Text("3 / TRIO") };
            m_Capacity.index = 0;
        }
        private static Label Row(VisualElement parent)
        { var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false }; label.AddToClassList("clash-member"); parent.Add(label); return label; }
        public void Show(bool show) => Element.EnableInClassList("clash-hidden", !show);
        private static void Visible(VisualElement element, bool show) => element.EnableInClassList("clash-hidden", !show);
        private Label Label(string name) => Element.Q<Label>(name);
        private static string Clock(float seconds) { int whole = Mathf.CeilToInt(Mathf.Max(0, seconds)); return $"{whole / 60:00}:{whole % 60:00}"; }
        public void Present(BountyClashClientState data, Vector3 local, bool hasLocal, Camera camera, bool saving, bool canReturn, bool darkSight = false, bool boost = false)
        {
            RefreshViewport(camera);
            Show(true); double now = Time.realtimeSinceStartupAsDouble;
            if (!data.Approved || !data.HasSnapshot)
            {
                Visible(m_SenseLayer, false); Visible(m_MarkerLayer, false);
                Visible(m_Lobby, true); Visible(m_Battle, false); Visible(m_Finished, false);
                MoonkovLocalization.Set(Label("clashLobbyStatus"), data.ApprovalReceived ? data.ApprovalReason : "Connecting to the match...");
                m_Create.SetEnabled(false); m_Join.SetEnabled(false); m_Ready.SetEnabled(false); m_Leave.SetEnabled(false); m_Resize.SetEnabled(false); m_Copy.SetEnabled(false);
                return;
            }
            m_Last = data.Snapshot; var s = m_Last;
            bool lobby = s.Phase == ClashPhase.Lobby || s.Phase == ClashPhase.Preparing || s.Phase == ClashPhase.Deploying;
            bool ended = BountyClashRules.Ended(s.Life) || s.Phase == ClashPhase.Finished, pending = data.ActionPending(now);
            Visible(m_MarkerLayer, !lobby);
            Visible(m_Lobby, lobby); Visible(m_Battle, !lobby); Visible(m_Finished, !lobby && ended);
            m_LobbyReturn.SetEnabled(!saving && canReturn); m_FinishReturn.SetEnabled(!saving && canReturn);
            bool editable = s.Phase == ClashPhase.Lobby || s.Phase == ClashPhase.Preparing;
            m_Create.SetEnabled(editable && s.LocalTeamId < 0 && !pending);
            m_Join.SetEnabled(editable && s.LocalTeamId < 0 && !pending);
            m_Resize.SetEnabled(editable && s.IsLeader && !pending);
            m_Leave.SetEnabled(editable && s.LocalTeamId >= 0 && !pending);
            m_Copy.SetEnabled(s.LocalTeamId >= 0 && s.InviteCode.Length > 0);
            if (m_LastTeam != s.LocalTeamId) { m_LastTeam = s.LocalTeamId; if (s.TeamCapacity > 0) m_Capacity.SetValueWithoutNotify(m_Capacity.choices[Mathf.Clamp(s.TeamCapacity - 1, 0, 2)]); }
            MoonkovLocalization.Set(Label("clashInvite"), s.LocalTeamId < 0 ? "Create a team or enter a crew invite code." : "TEAM {0} / INVITE {1}", s.LocalTeamId + 1, s.InviteCode.ToString());
            MoonkovLocalization.Set(Label("clashLobbyStatus"), s.Phase == ClashPhase.Preparing ? "ALL TEAMS READY / STARTING IN {0}s" :
                s.Phase == ClashPhase.Deploying ? "ENTERING MATCH / {0}s" : "WAITING FOR TEAMS TO GET READY",
                s.Phase == ClashPhase.Preparing ? Mathf.CeilToInt(Mathf.Max(0, s.PrepareRemaining - (float)(now - data.ReceivedAt))) :
                s.Phase == ClashPhase.Deploying ? Mathf.CeilToInt(Mathf.Max(0, s.DeploymentRemaining - (float)(now - data.ReceivedAt))) : s.RulesVersion);
            MoonkovLocalization.Set(Label("clashRosterHeading"), "PLAYERS / {0}/12 / TEAMS / {1}/4", s.PlayerCount, s.TeamCount);
            int rowIndex = 0, teamIndex = 0, markerIndex = 0; bool localRow = false; m_LocalReady = false;
            foreach (var row in data.Participants.Values)
            {
                if (rowIndex < m_LobbyRows.Length)
                {
                    if (!row.IsLocal && !row.OwnTeam && row.Name.Length == 0)
                        MoonkovLocalization.Set(m_LobbyRows[rowIndex++], "RIVAL PLAYER");
                    else MoonkovLocalization.Set(m_LobbyRows[rowIndex++], "{0} / TEAM {1} / {2}", row.Name.ToString(), row.TeamId >= 0 ? (row.TeamId + 1).ToString() : "—",
                            !row.Connected ? "DISCONNECTED" : row.Ready ? "READY" : "NOT READY");
                }
                if (row.IsLocal) { m_LocalReady = row.Ready; localRow = true; }
                if (!row.OwnTeam || row.TeamId != s.LocalTeamId) continue;
                string status = !row.Connected ? MoonkovLocalization.Format($"RECONNECT / {row.ReconnectRemaining:0}s") : MoonkovLocalization.Text(Life(row.Life));
                if (teamIndex < 3) MoonkovLocalization.Set(m_TeamRows[teamIndex++], "{0} / {1}", row.Name.ToString(), status);
                if (row.IsLocal || markerIndex >= 3 || !hasLocal || !camera) continue;
                var marker = m_Markers[markerIndex++]; var point = camera.WorldToViewportPoint((Vector3)row.Position + Vector3.up * .9f);
                bool visible = point.z > 0 && !BountyClashRules.Ended(row.Life);
                Visible(marker, visible);
                marker.style.left = Length.Percent(Mathf.Clamp(point.x * 100, 8, 92)); marker.style.top = Length.Percent(Mathf.Clamp((1 - point.y) * 100, 8, 85));
                marker.EnableInClassList("clash-friendly-down", row.Life == ClashLife.Downed);
                MoonkovLocalization.Set(marker, row.Life == ClashLife.Downed ? "+ {0} / DOWNED / {1:0} m" : "+ {0} / {1:0} m", row.Name.ToString(), Vector3.Distance(local, row.Position));
            }
            for (int i = 0; i < m_LobbyRows.Length; i++) Visible(m_LobbyRows[i], i < rowIndex);
            for (int i = 0; i < m_TeamRows.Length; i++) Visible(m_TeamRows[i], i < teamIndex);
            for (; markerIndex < 3; markerIndex++) Visible(m_Markers[markerIndex], false);
            m_Ready.SetEnabled(editable && s.LocalTeamId >= 0 && localRow && !pending);
            MoonkovLocalization.Set(m_Ready, m_LocalReady ? "NOT READY" : "READY");
            MoonkovLocalization.Set(Label("clashLobbyError"), pending ? "Waiting for confirmation..." : now - data.FailureAt < 4 && data.LastFailure != ClashResult.Ok ? Result(data.LastFailure) : "");
            MoonkovLocalization.Set(Label("clashClock"), Clock(data.Remaining(now)));
            MoonkovLocalization.Set(Label("clashPhase"), s.Closing ? "BOUNTY EXTRACTED / FINAL COUNTDOWN" : "BOUNTY CLASH");
            Visible(Label("clashObjective"), s.Bounty == ClashBountyState.Unbanished || s.Bounty == ClashBountyState.Banishing);
            MoonkovLocalization.Set(Label("clashObjective"), s.Bounty == ClashBountyState.Unbanished ? "DEAD TARGET / APPROACH AND BANISH" :
                s.Bounty == ClashBountyState.Banishing ? "BANISHING / ALL TEAMS CAN CONTEST" : "ONE BOUNTY / RECOVER IT AND EXTRACT");
            var progress = Element.Q<ProgressBar>("clashBanish"); Visible(progress, s.Bounty == ClashBountyState.Banishing); progress.value = Mathf.Clamp01(s.BanishProgress) * 100; progress.title = Mathf.RoundToInt(progress.value) + "%";
            MoonkovLocalization.Set(Label("clashBounty"), s.IsCarrier ? "YOU CARRY THE UNIQUE BOUNTY" : s.TeamHasBounty ? "YOUR TEAM CARRIES THE BOUNTY" :
                s.Bounty == ClashBountyState.Carried ? "RIVAL CARRIER / APPROXIMATE AREA ON MAP" : s.Bounty == ClashBountyState.Ground ? "BOUNTY ON THE GROUND" :
                s.Bounty == ClashBountyState.Extracted ? "BOUNTY HAS LEFT THE ARENA" : "BOUNTY NOT YET AVAILABLE");
            MoonkovLocalization.Set(Label("clashExits"), "EXIT A / {0}\nEXIT B / {1}", Exit(s.ExitRemainingA, s.ExitBlockA, now - data.ReceivedAt), Exit(s.ExitRemainingB, s.ExitBlockB, now - data.ReceivedAt));
            bool warning = s.BoundaryRemaining >= 0 || s.Life == ClashLife.Downed;
            Visible(Label("clashWarning"), warning);
            MoonkovLocalization.Set(Label("clashWarning"), s.Life == ClashLife.Downed ? "DOWNED / WAIT FOR A TEAMMATE / EQUIPMENT RETAINED" :
                "OUTSIDE ARENA / RETURN WITHIN {0:0}s", Mathf.Max(0, s.BoundaryRemaining - (float)(now - data.ReceivedAt)));
            Visible(m_SenseLayer, false);
            MoonkovLocalization.Set(Label("clashSense"), s.IsCarrier ? "[V] DARK SIGHT / [RMB] BOOST / {0:0.0}s" : "[V] DARK SIGHT", s.SenseRemaining);
            Label("clashSense").tooltip = MoonkovLocalization.Text("125 m CLOUDS / 75 m INSTINCT WHILE IN DARK SIGHT");
            m_Map.Present(s, data.Participants.Values, local, hasLocal);
            MoonkovLocalization.Set(Label("clashFinishTitle"), s.DeploymentAborted ? "DEPLOYMENT CANCELLED" : s.Life == ClashLife.Extracted ? "EXTRACTED" : s.Life == ClashLife.TimedOut ? "MATCH TIME EXPIRED" : "DEPLOYMENT ENDED");
            MoonkovLocalization.Set(Label("clashFinishCopy"), s.DeploymentAborted ? s.CanStartNext ? "Deployment cancelled. Your original equipment has been returned." : "Deployment cancelled. Returning your original equipment..."
                : saving || s.Phase == ClashPhase.Finished && !s.CanStartNext ? "Saving results..." : s.Phase != ClashPhase.Finished
                ? "Your deployment is over. The shared match is still running; no redeployment into this roster."
                : "The match has ended. Check your inventory before preparing the next round.");
            m_Next.SetEnabled(s.Phase == ClashPhase.Finished && s.CanStartNext && !saving && !pending);
        }
        private void RefreshViewport(Camera camera)
        {
            var parent = Element.parent;
            if (parent == null || parent.contentRect.width < 1) return;
            float pixels = camera ? camera.pixelWidth : Screen.width;
            float pixelHeight = camera ? camera.pixelHeight : Screen.height;
            if (pixels < 1 || pixelHeight < 1) return;
            float width = Mathf.Min(pixels, parent.contentRect.width);
            Element.style.right = StyleKeyword.Auto; Element.style.bottom = StyleKeyword.Auto;
            Element.style.width = width; Element.style.height = pixelHeight * width / pixels;
            float scale = parent.contentRect.width / width;
            Element.style.scale = new Scale(new Vector3(scale, scale, 1));
            Element.EnableInClassList("clash-compact", width < 1000);
        }
        public void SetMenuOpen(bool open) => Element.EnableInClassList("clash-menu-open", open);
        public void SetAction(string text, params object[] args) { Visible(Label("clashAction"), !string.IsNullOrEmpty(text)); MoonkovLocalization.Set(Label("clashAction"), text ?? "", args); }
        public static string Life(ClashLife life) => life == ClashLife.Downed ? "DOWNED" : life == ClashLife.Alive ? "ALIVE" : life == ClashLife.Waiting ? "WAITING" : life == ClashLife.Extracted ? "EXTRACTED" : life == ClashLife.TimedOut ? "TIMED OUT" : "DEAD";
        private static string Result(ClashResult result) => result == ClashResult.Full ? "TEAM OR SERVER IS FULL" : result == ClashResult.Forbidden ? "REQUEST NOT PERMITTED / CHECK INVITE OR TEAM LEADER" :
            result == ClashResult.Busy ? "ACTION ALREADY IN PROGRESS" : result == ClashResult.Blocked ? "GET CLOSER AND CLEAR THE LINE OF SIGHT" : result == ClashResult.WrongPhase ? "MATCH STATE CHANGED / WAIT FOR SERVER" : "REQUEST REJECTED / REFRESH SERVER STATE";
        private static string Exit(float remaining, ClashExitBlock blocked, double elapsed) => blocked == ClashExitBlock.Enemy ? MoonkovLocalization.Format($"PAUSED {Clock(remaining)} / ENEMY TEAM") :
            blocked == ClashExitBlock.DownedTeammate ? MoonkovLocalization.Format($"PAUSED {Clock(remaining)} / TEAMMATE DOWNED") : remaining >= 0 ? Clock(Mathf.Max(0, remaining - (float)elapsed)) : MoonkovLocalization.Text("ENTER ZONE / HOLD 30s");
        private void Create() => m_Send(ClashIntent.CreateTeam, m_Capacity.index + 1, null);
        private void Join() => m_Send(ClashIntent.JoinTeam, 0, m_Code.value.Trim().ToUpperInvariant());
        private void Resize() => m_Send(ClashIntent.SetCapacity, m_Capacity.index + 1, null);
        private void Ready() => m_Send(ClashIntent.SetReady, m_LocalReady ? 0 : 1, null);
        private void Leave() => m_Send(ClashIntent.LeaveTeam, 0, null);
        private void Copy() => GUIUtility.systemCopyBuffer = m_Last.InviteCode.ToString();
        private void Next() => m_Send(ClashIntent.NextRound, 0, null);
        private void Return() => m_Return();
        public void Dispose()
        {
            m_Create.clicked -= Create; m_Join.clicked -= Join; m_Resize.clicked -= Resize; m_Ready.clicked -= Ready;
            m_Leave.clicked -= Leave; m_Copy.clicked -= Copy; m_Next.clicked -= Next; m_LobbyReturn.clicked -= Return; m_FinishReturn.clicked -= Return;
            Element.RemoveFromHierarchy();
        }
    }
}
