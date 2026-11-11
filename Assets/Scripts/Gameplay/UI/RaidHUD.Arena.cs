using System.Linq;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private VisualElement m_ArenaHud, m_ArenaArmory, m_ArenaResult;
        private Label m_ArenaScore, m_ArenaStatus, m_ArenaResultText;
        private DropdownField m_ArenaItems;
        private Button m_ArenaTake;
        private ItemDefinition[] m_ArenaDefinitions;
        private bool m_ArenaFinished;

        private void InitializeArenaHud()
        {
            m_ArenaHud = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ArenaHud.AddToClassList("arena-hud");
            m_ArenaScore = new Label { pickingMode = PickingMode.Ignore };
            m_ArenaScore.AddToClassList("arena-score");
            m_ArenaStatus = new Label { pickingMode = PickingMode.Ignore };
            m_ArenaHud.Add(m_ArenaScore);
            m_ArenaHud.Add(m_ArenaStatus);
            m_Root.Add(m_ArenaHud);

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

            m_ClassicHud = GameSettings.Instance.RequestedMode == RaidGameMode.Classic;
            m_ArenaDefinitions = InventoryCatalog.Definitions.Values.Where(item => m_ClassicHud ? ClassicMatch.Price(item) > 0 : ArenaArmory.Available(item))
                .OrderBy(item => item.Kind).ThenBy(item => item.Name).ToArray();
            m_ArenaItems = new DropdownField(m_ArenaDefinitions.Select(item => m_ClassicHud
                ? $"{MoonkovLocalization.Text(item.Name)}   ${ClassicMatch.Price(item)}" : MoonkovLocalization.Text(item.Name)).ToList(), 0);
            m_ArenaTake = new Button(TakeArenaSupply);
            MoonkovLocalization.Set(m_ArenaTake, m_ClassicHud ? "购买" : "免费领取");
            m_ArenaArmory = new VisualElement();
            m_ArenaArmory.AddToClassList("arena-armory");
            m_ArenaArmory.Add(m_ArenaItems);
            m_ArenaArmory.Add(m_ArenaTake);
            m_Inventory.Q(className: "raid-inventory-window").Insert(1, m_ArenaArmory);
        }

        private void TakeArenaSupply()
        {
            if (m_InventoryRequestPending || m_InventoryQuery.IsEmptyIgnoreFilter ||
                m_ArenaItems.index < 0 || m_ArenaItems.index >= m_ArenaDefinitions.Length) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            if (inventory.Graph == null) return;
            var code = new Unity.Collections.FixedString64Bytes(m_ArenaDefinitions[m_ArenaItems.index].Code);
            if (Send(new ArenaSupplyRpc { Code = code, RequestId = NextManualActionRequest(), ExpectedVersion = inventory.Graph.Version }))
            {
                TrackInventoryAudio(MoonkovAudio.Library?.Equipment);
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
            if ((m_ArenaFinished || !alive) && m_InventoryVisible) SetInventory(false);
            if (!m_ArenaFinished && alive && m_Snapshot.RaidId > 0 && !ChatInputBlocked && !GameSettings.Instance.IsPauseMenuOpen &&
                (RaidKeyBindings.Pressed(RaidKey.Inventory) || (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)))
                SetInventory(!m_InventoryVisible);
            m_ArenaArmory.style.display = m_HealthSelected ? DisplayStyle.None : DisplayStyle.Flex;
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
    }
}
