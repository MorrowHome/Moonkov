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
        private Label m_ClassicBoardScore, m_ClassicBoardClock, m_ClassicBoardTeamAScore, m_ClassicBoardTeamBScore;
        private Label[] m_ClassicBoardNamesA, m_ClassicBoardNamesB, m_ClassicBoardAliveA, m_ClassicBoardAliveB;
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
                m_ClassicStatusA = CreateClassicTeamStatus("T");
                m_ClassicStatusB = CreateClassicTeamStatus("CT");
                m_ArenaHud.Add(m_ClassicStatusA);
                m_ArenaHud.Add(m_ClassicStatusB);
                m_ClassicCenter = new VisualElement { pickingMode = PickingMode.Ignore };
                m_ClassicCenter.AddToClassList("classic-score-center");
                m_ArenaScore.AddToClassList("classic-score-line");
                m_ArenaStatus.AddToClassList("classic-score-status");
                m_ClassicCenter.Add(m_ArenaScore);
                m_ClassicCenter.Add(m_ArenaStatus);
                m_ArenaHud.Add(m_ClassicCenter);
            }
            m_Root.Add(m_ArenaHud);
            if (m_ClassicHud)
            {
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
                m_ClassicShop = new MoonkovShopPurchaseView(TakeClassicSupply);
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
            var board = new VisualElement { name = "classic-scoreboard" };
            board.AddToClassList("classic-scoreboard");
            var title = new VisualElement();
            title.AddToClassList("classic-scoreboard-title");
            m_ClassicBoardScore = new Label();
            m_ClassicBoardClock = new Label();
            title.Add(m_ClassicBoardScore);
            title.Add(m_ClassicBoardClock);
            board.Add(title);
            var teams = new VisualElement();
            teams.AddToClassList("classic-scoreboard-teams");
            m_ClassicBoardNamesA = CreateClassicBoardTeam(teams, "T", out m_ClassicBoardTeamAScore, out m_ClassicBoardAliveA);
            m_ClassicBoardNamesB = CreateClassicBoardTeam(teams, "CT", out m_ClassicBoardTeamBScore, out m_ClassicBoardAliveB);
            board.Add(teams);
            return board;
        }

        private static Label[] CreateClassicBoardTeam(VisualElement parent, string team, out Label score, out Label[] alive)
        {
            var column = new VisualElement();
            column.AddToClassList("classic-scoreboard-team");
            column.AddToClassList(team == "T" ? "classic-scoreboard-attackers" : "classic-scoreboard-defenders");
            var heading = new VisualElement();
            heading.AddToClassList("classic-scoreboard-team-heading");
            var title = new Label(team);
            title.AddToClassList("classic-scoreboard-team-name");
            score = new Label("0");
            score.AddToClassList("classic-scoreboard-team-score");
            heading.Add(title);
            heading.Add(score);
            column.Add(heading);
            var names = new Label[ClassicMatch.TeamSize];
            alive = new Label[ClassicMatch.TeamSize];
            for (int i = 0; i < ClassicMatch.TeamSize; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("classic-scoreboard-player");
                names[i] = new Label();
                names[i].AddToClassList("classic-scoreboard-player-name");
                alive[i] = new Label();
                alive[i].AddToClassList("classic-scoreboard-player-alive");
                row.Add(names[i]);
                row.Add(alive[i]);
                column.Add(row);
            }
            parent.Add(column);
            return names;
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

        private void PresentClassicScoreboard(ClassicSnapshotRpc match, bool visible)
        {
            m_ClassicScoreboard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            m_ClassicScoreboard.BringToFront();
            int seconds = Mathf.CeilToInt(match.Remaining);
            int own = match.Team == 0 ? match.ScoreA : match.ScoreB;
            int enemy = match.Team == 0 ? match.ScoreB : match.ScoreA;
            m_ClassicBoardScore.text = $"{own}  —  {enemy}";
            m_ClassicBoardClock.text = $"{seconds / 60:00}:{seconds % 60:00}";
            m_ClassicBoardTeamAScore.text = match.ScoreA.ToString();
            m_ClassicBoardTeamBScore.text = match.ScoreB.ToString();
            UpdateClassicBoardTeam(m_ClassicBoardNamesA, m_ClassicBoardAliveA, match.AliveMaskA,
                match.NameA0, match.NameA1, match.NameA2, match.NameA3, match.NameA4);
            UpdateClassicBoardTeam(m_ClassicBoardNamesB, m_ClassicBoardAliveB, match.AliveMaskB,
                match.NameB0, match.NameB1, match.NameB2, match.NameB3, match.NameB4);
        }

        private static void UpdateClassicBoardTeam(Label[] names, Label[] statuses, byte mask,
            Unity.Collections.FixedString64Bytes n0, Unity.Collections.FixedString64Bytes n1,
            Unity.Collections.FixedString64Bytes n2, Unity.Collections.FixedString64Bytes n3,
            Unity.Collections.FixedString64Bytes n4)
        {
            for (int i = 0; i < names.Length; i++)
            {
                bool alive = (mask & (1 << i)) != 0;
                names[i].text = i switch { 0 => n0.ToString(), 1 => n1.ToString(), 2 => n2.ToString(), 3 => n3.ToString(), _ => n4.ToString() };
                MoonkovLocalization.Set(statuses[i], alive ? "存活" : "阵亡");
                statuses[i].EnableInClassList("is-dead", !alive);
            }
        }
    }
}
