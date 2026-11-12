using System.Linq;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Client;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private VisualElement m_ArenaHud, m_ArenaArmory, m_ArenaResult;
        private VisualElement m_ClassicCenter;
        private VisualElement m_ClassicScoreboard;
        private VisualElement m_ArenaInventoryWindow;
        private Label m_ArenaScore, m_ArenaStatus, m_ArenaResultText;
        private DropdownField m_ArenaItems;
        private Button m_ArenaTake;
        private VisualElement m_ClassicStatusA, m_ClassicStatusB;
        private Label m_ClassicBoardRound, m_ClassicBoardClock;
        private ClassicBoardTeam[] m_ClassicBoardTeams;
        private uint m_ClassicBoardSequence;
        private bool m_ClassicBoardVisible;
        private MoonkovShopPurchaseView m_ClassicShop;
        private ShopOffer[] m_ClassicOffers;
        private ItemDefinition[] m_ArenaDefinitions;
        private bool m_ArenaFinished, m_ArenaResultSoundPlayed;

        private void InitializeArenaHud()
        {
            m_ArenaHud = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ArenaHud.AddToClassList("arena-hud");
            m_ArenaScore = new Label { pickingMode = PickingMode.Ignore };
            m_ArenaScore.AddToClassList("arena-score");
            m_ArenaStatus = new Label { pickingMode = PickingMode.Ignore };
            m_ArenaHud.Add(m_ArenaScore);
            m_ArenaHud.Add(m_ArenaStatus);
            m_ClassicHud = GameSettings.Instance.RequestedMode == RaidGameMode.Classic;
            if (m_ClassicHud)
            {
                m_ArenaHud.AddToClassList("is-classic");
                m_ArenaHud.AddToClassList("moonkov-style-font");
                m_ClassicStatusA = CreateClassicTeamStatus("T");
                m_ClassicStatusB = CreateClassicTeamStatus("CT");
                m_ArenaHud.Add(m_ClassicStatusA);
                m_ClassicCenter = new VisualElement { pickingMode = PickingMode.Ignore };
                m_ClassicCenter.AddToClassList("classic-score-center");
                m_ArenaScore.AddToClassList("classic-score-line");
                m_ArenaStatus.AddToClassList("classic-score-status");
                m_ClassicCenter.Add(m_ArenaScore);
                m_ClassicCenter.Add(m_ArenaStatus);
                m_ArenaHud.Add(m_ClassicCenter);
                m_ArenaHud.Add(m_ClassicStatusB);
            }
            m_Root.Add(m_ArenaHud);
            if (m_ClassicHud)
            {
                m_ClassicMinimap = new ClassicMinimapView(m_Root);
                m_ClassicScoreboard = CreateClassicScoreboard();
                m_ClassicScoreboard.style.display = DisplayStyle.None;
                m_Root.Add(m_ClassicScoreboard);
            }

            m_ArenaResult = new VisualElement();
            m_ArenaResult.AddToClassList("arena-result");
            m_ArenaResultText = new Label();
            m_ArenaResultText.AddToClassList("arena-score");
            var exit = new Button(() => GameManager.Instance.ReturnToMainMenuAsync());
            MoonkovLocalization.Set(exit, PlayerProfileClient.IsLan ? "返回好友房间" : "返回主菜单");
            m_ArenaResult.Add(m_ArenaResultText);
            m_ArenaResult.Add(exit);
            m_ArenaResult.style.display = DisplayStyle.None;
            m_Root.Add(m_ArenaResult);

            m_ArenaDefinitions = InventoryCatalog.Definitions.Values.Where(item => m_ClassicHud ? ClassicMatch.Price(item) > 0 : ArenaArmory.Available(item))
                .OrderBy(item => item.Kind).ThenBy(item => item.Name).ToArray();
            if (!m_ClassicHud)
            {
                m_ArenaItems = new DropdownField(m_ArenaDefinitions.Select(item => MoonkovLocalization.Text(item.Name)).ToList(), 0);
                m_ArenaTake = new Button(TakeArenaSupply);
                MoonkovLocalization.Set(m_ArenaTake, "免费领取");
            }
            m_ArenaArmory = new VisualElement();
            m_ArenaArmory.AddToClassList("arena-armory");
            if (m_ClassicHud)
            {
                m_ArenaArmory.AddToClassList("is-classic");
                m_ClassicOffers = m_ArenaDefinitions.Select(item => new ShopOffer
                {
                    Code = item.Code, Name = item.Name, BuyDust = ClassicMatch.Price(item), SellDust = 0
                }).ToArray();
                m_ClassicShop = new MoonkovShopPurchaseView(TakeClassicSupply, quantityLimit: ClassicMatch.PurchaseLimit);
                m_ArenaArmory.Add(m_ClassicShop.Element);
            }
            else
            {
                m_ArenaArmory.Add(m_ArenaItems);
                m_ArenaArmory.Add(m_ArenaTake);
            }
            m_ArenaArmory.style.display = DisplayStyle.None;
            m_ArenaInventoryWindow = m_Inventory.Q(className: "raid-inventory-window");
            if (m_ClassicHud) m_Root.Add(m_ArenaArmory);
            else m_ArenaInventoryWindow.Insert(1, m_ArenaArmory);
        }

        private static VisualElement CreateClassicTeamStatus(string team)
        {
            var row = new VisualElement { name = "classic-status-" + team.ToLowerInvariant(), pickingMode = PickingMode.Ignore };
            row.AddToClassList("classic-status-row");
            row.AddToClassList(team == "T" ? "classic-status-attackers" : "classic-status-defenders");
            var label = new Label(team) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("classic-status-team");
            row.Add(label);
            for (int i = 0; i < ClassicMatch.TeamSize; i++)
            {
                var player = new Label("●") { name = "player-" + i, pickingMode = PickingMode.Ignore };
                player.AddToClassList("classic-status-player");
                row.Add(player);
            }
            return row;
        }

        private VisualElement CreateClassicScoreboard()
        {
            m_ClassicBoardSequence = 0;
            m_ClassicBoardVisible = false;
            var board = new VisualElement { name = "classic-scoreboard", pickingMode = PickingMode.Ignore };
            board.AddToClassList("classic-scoreboard");
            board.AddToClassList("moonkov-style-font");
            var title = new VisualElement { pickingMode = PickingMode.Ignore };
            title.AddToClassList("classic-scoreboard-title");
            m_ClassicBoardRound = AddClassicBoardLabel(title, "classic-scoreboard-round");
            m_ClassicBoardClock = AddClassicBoardLabel(title, "classic-scoreboard-clock");
            board.Add(title);
            var teams = new VisualElement { pickingMode = PickingMode.Ignore };
            teams.AddToClassList("classic-scoreboard-teams");
            m_ClassicBoardTeams = new[] { new ClassicBoardTeam(teams), new ClassicBoardTeam(teams) };
            board.Add(teams);
            return board;
        }

        private static Label AddClassicBoardLabel(VisualElement parent, string className, string text = null)
        {
            var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            parent.Add(label);
            if (text != null) MoonkovLocalization.Set(label, text);
            return label;
        }

        private sealed class ClassicBoardTeam
        {
            private readonly VisualElement m_Column;
            private readonly Label m_Role, m_Score;
            private readonly ClassicBoardRow[] m_Rows = new ClassicBoardRow[ClassicMatch.TeamSize];

            public ClassicBoardTeam(VisualElement parent)
            {
                m_Column = new VisualElement { pickingMode = PickingMode.Ignore };
                m_Column.AddToClassList("classic-scoreboard-team");
                var heading = new VisualElement { pickingMode = PickingMode.Ignore };
                heading.AddToClassList("classic-scoreboard-team-heading");
                m_Role = AddClassicBoardLabel(heading, "classic-scoreboard-team-name");
                m_Score = AddClassicBoardLabel(heading, "classic-scoreboard-team-score");
                m_Column.Add(heading);
                var header = new VisualElement { pickingMode = PickingMode.Ignore };
                header.AddToClassList("classic-scoreboard-player");
                header.AddToClassList("classic-scoreboard-columns");
                AddClassicBoardLabel(header, "classic-scoreboard-player-name", "PLAYER");
                AddClassicBoardLabel(header, "classic-scoreboard-number", "KILLS");
                AddClassicBoardLabel(header, "classic-scoreboard-number", "DEATHS");
                AddClassicBoardLabel(header, "classic-scoreboard-number", "K/D");
                AddClassicBoardLabel(header, "classic-scoreboard-number", "ADR");
                AddClassicBoardLabel(header, "classic-scoreboard-player-alive", "STATUS");
                m_Column.Add(header);
                for (int i = 0; i < m_Rows.Length; i++) m_Rows[i] = new ClassicBoardRow(m_Column);
                parent.Add(m_Column);
            }

            public void Present(in ClassicSnapshotV4Rpc match, int team)
            {
                bool attacking = match.AttackTeam == team;
                m_Column.EnableInClassList("classic-scoreboard-attackers", attacking);
                m_Column.EnableInClassList("classic-scoreboard-defenders", !attacking);
                MoonkovLocalization.Set(m_Role, attacking ? "ATTACKERS" : "DEFENDERS");
                m_Score.text = (team == 0 ? match.ScoreA : match.ScoreB).ToString();
                byte aliveMask = team == 0 ? match.AliveMaskA : match.AliveMaskB;
                for (int i = 0; i < m_Rows.Length; i++)
                    m_Rows[i].Present(match.GetSeat(team * ClassicMatch.TeamSize + i), (aliveMask & (1 << i)) != 0,
                        match.Team == team && match.Slot == i);
            }
        }

        private readonly struct ClassicBoardRow
        {
            private readonly VisualElement m_Row;
            private readonly Label m_Name, m_Kills, m_Deaths, m_Ratio, m_Damage, m_Status;

            public ClassicBoardRow(VisualElement parent)
            {
                m_Row = new VisualElement { pickingMode = PickingMode.Ignore };
                m_Row.AddToClassList("classic-scoreboard-player");
                m_Name = AddClassicBoardLabel(m_Row, "classic-scoreboard-player-name");
                m_Kills = AddClassicBoardLabel(m_Row, "classic-scoreboard-number");
                m_Deaths = AddClassicBoardLabel(m_Row, "classic-scoreboard-number");
                m_Ratio = AddClassicBoardLabel(m_Row, "classic-scoreboard-number");
                m_Damage = AddClassicBoardLabel(m_Row, "classic-scoreboard-number");
                m_Status = AddClassicBoardLabel(m_Row, "classic-scoreboard-player-alive");
                parent.Add(m_Row);
            }

            public void Present(in ClassicScoreboardEntry entry, bool alive, bool local)
            {
                bool occupied = entry.Name.Length > 0;
                m_Row.EnableInClassList("is-local", occupied && local);
                m_Name.text = occupied ? entry.Name.ToString() : "—";
                m_Kills.text = occupied ? entry.Kills.ToString() : "—";
                m_Deaths.text = occupied ? entry.Deaths.ToString() : "—";
                m_Ratio.text = occupied ? (entry.Kills / (float)Mathf.Max(1, entry.Deaths)).ToString("0.00") : "—";
                m_Damage.text = occupied ? entry.AverageDamage.ToString() : "—";
                MoonkovLocalization.Set(m_Status, !occupied ? "—" : alive ? "ALIVE" : "DEAD");
                m_Status.EnableInClassList("is-dead", !alive || !occupied);
            }
        }

        private void TakeArenaSupply()
        {
            if (m_InventoryRequestPending || m_InventoryQuery.IsEmptyIgnoreFilter ||
                m_ArenaItems.index < 0 || m_ArenaItems.index >= m_ArenaDefinitions.Length) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            if (inventory.Graph == null) return;
            uint requestId = NextManualActionRequest();
            var code = new Unity.Collections.FixedString64Bytes(m_ArenaDefinitions[m_ArenaItems.index].Code);
            if (Send(new ArenaSupplyRpc { Code = code, RequestId = requestId, Quantity = 1, ExpectedVersion = inventory.Graph.Version }))
            {
                TrackInventoryAudio(MoonkovAudio.Library?.Equipment);
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
            }
        }

        private void TakeClassicSupply(ShopOffer offer, int quantity)
        {
            if (m_InventoryRequestPending || offer == null || quantity < 1 || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            if (inventory.Graph == null) return;
            uint requestId = NextManualActionRequest();
            if (Send(new ArenaSupplyRpc
            {
                Code = new Unity.Collections.FixedString64Bytes(offer.Code), Quantity = quantity,
                RequestId = requestId, ExpectedVersion = inventory.Graph.Version
            }))
            {
                TrackInventoryAudio(MoonkovAudio.Library?.Confirm);
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
            }
        }

        private void PresentArenaHud()
        {
            if (m_ClassicHud) { PresentClassicHud(); return; }
            var state = m_World.EntityManager.GetComponentData<ArenaClientState>(m_StateQuery.GetSingletonEntity());
            var match = state.Snapshot;
            bool alive = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() && m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().CurrentHealth > 0;
            bool fighting = alive && match.Phase == ArenaPhase.Fighting;
            m_ArenaFinished = state.Received && match.Phase == ArenaPhase.Finished;
            PresentArenaResultSound(m_ArenaFinished, match.Won, match.Draw);
            if ((m_ArenaFinished || !alive) && m_InventoryVisible) SetInventory(false);
            if (!m_ArenaFinished && alive && m_Snapshot.RaidId > 0 && !ChatInputBlocked && !GameSettings.Instance.IsPauseMenuOpen &&
                (RaidKeyBindings.Pressed(RaidKey.Inventory) || (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)))
                SetInventory(!m_InventoryVisible);
            m_ArenaArmory.style.display = m_InventoryVisible && !m_HealthSelected ? DisplayStyle.Flex : DisplayStyle.None;
            m_ArenaTake.SetEnabled(!m_InventoryRequestPending && !TimedActionActive);
            UpdateInventoryBackdrop();
            UpdateChat();
            PresentQuickSlots(alive && !m_ArenaFinished);
            PresentMelee(fighting);
            UpdateGrenades(fighting && !GameSettings.Instance.IsPauseMenuOpen);
            PresentTimedAction();
            m_ArenaResult.style.display = m_ArenaFinished && !GameSettings.Instance.IsPauseMenuOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_ArenaFinished) Utils.SetCursorVisible(true);
            m_RefreshTimer -= Time.deltaTime;
            if (m_RefreshTimer > 0) return;
            m_RefreshTimer = .1f;
            RefreshHealth();

            int seconds = Mathf.CeilToInt(match.Remaining);
            m_ArenaScore.text = match.Phase == ArenaPhase.Waiting
                ? MoonkovLocalization.Text("竞技场") : $"{seconds / 60:00}:{seconds % 60:00}   {match.Kills} / {ArenaMatch.TargetScore}";
            MoonkovLocalization.Set(m_ArenaStatus, match.Phase == ArenaPhase.Waiting ? "等待玩家进入" :
                !alive && match.Respawn > 0 ? MoonkovLocalization.Format($"复活 {Mathf.CeilToInt(match.Respawn)} 秒") : "");
            if (m_ArenaFinished)
                MoonkovLocalization.Set(m_ArenaResultText, match.Players < 2 && match.BestScore == 0 && match.Draw
                    ? "对局人数不足" : match.Draw ? "平局" : match.Won ? "获胜" : "对局结束");
        }

        private void PresentArenaResultSound(bool finished, bool won, bool draw)
        {
            if (!finished) { m_ArenaResultSoundPlayed = false; return; }
            if (m_ArenaResultSoundPlayed) return;
            m_ArenaResultSoundPlayed = true;
            if (draw) MoonkovAudio.Play(MoonkovAudio.Library?.Notification, Vector3.zero);
            else if (won) MoonkovAudio.Confirm();
            else MoonkovAudio.Error();
        }

        private void PresentClassicScoreboard(in ClassicClientState data, bool visible)
        {
            visible &= data.Received;
            if (visible != m_ClassicBoardVisible)
            {
                m_ClassicBoardVisible = visible;
                m_ClassicScoreboard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) m_ClassicScoreboard.BringToFront();
            }
            if (!visible || m_ClassicBoardSequence == data.Sequence) return;
            m_ClassicBoardSequence = data.Sequence;
            ref readonly var match = ref data.Snapshot;
            int seconds = Mathf.CeilToInt(match.Remaining);
            if (match.Round == 0) MoonkovLocalization.Set(m_ClassicBoardRound, "WAITING FOR PLAYERS");
            else MoonkovLocalization.Set(m_ClassicBoardRound, "ROUND {0}", match.Round);
            m_ClassicBoardClock.text = $"{seconds / 60:00}:{seconds % 60:00}";
            m_ClassicBoardTeams[0].Present(match, 0);
            m_ClassicBoardTeams[1].Present(match, 1);
        }
    }
}
