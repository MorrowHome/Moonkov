using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private bool m_ClassicHud, m_ClassicHeld, m_ClassicBuyOpen;
        private uint m_ClassicPurchaseSequence;
        private string m_ClassicPurchaseMessage;
        private float m_ClassicPurchaseMessageUntil;
        private double m_NextClassicUse;
        private GameObject m_ClassicBomb;
        private ClassicMinimapView m_ClassicMinimap;

        private void PresentClassicHud()
        {
            var data = m_World.EntityManager.GetComponentData<ClassicClientState>(m_StateQuery.GetSingletonEntity());
            ref readonly var match = ref data.Snapshot;
            bool buy = match.Phase == ClassicPhase.Buy;
            bool fighting = match.Phase is ClassicPhase.Live or ClassicPhase.Planted;
            bool paused = GameSettings.Instance.IsPauseMenuOpen;
            bool typing = QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement);
            m_ArenaFinished = data.Received && match.Phase == ClassicPhase.Finished;
            PresentArenaResultSound(m_ArenaFinished, match.Winner == match.Team, match.Winner < 0);
            if (!buy && m_ClassicBuyOpen) SetClassicBuyMenu(false);
            if ((m_ArenaFinished || !match.Alive) && m_InventoryVisible) SetInventory(false);
            bool buyPressed = RaidKeyBindings.Pressed(RaidKey.Buy);
            bool gamepadPressed = Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame;
            if (match.Alive && !m_ArenaFinished && !ChatInputBlocked && !typing && !paused && (buyPressed || buy && gamepadPressed))
            {
                if (buy) SetClassicBuyMenu(!m_ClassicBuyOpen);
            }
            if (!buy && match.Alive && !m_ArenaFinished && !ChatInputBlocked && !typing && !paused && gamepadPressed)
                SetInventory(!m_InventoryVisible);
            bool scoreboard = !ChatInputBlocked && !typing && !paused && RaidKeyBindings.Held(RaidKey.Scoreboard);
            bool inventoryPressed = RaidKeyBindings.Pressed(RaidKey.Inventory);
            if (!buy && match.Alive && !m_ArenaFinished && !ChatInputBlocked && !typing && !paused && inventoryPressed)
                SetInventory(!m_InventoryVisible);
            PresentClassicScoreboard(data, scoreboard);
            if (data.Received && match.Round > 0 && !paused && !m_ArenaFinished && !m_ClassicBuyOpen && !m_InventoryVisible && !scoreboard)
                m_ClassicMinimap.Present(ClassicDust2Map.Active, m_World, match.Team, match.Team == match.AttackTeam, match.Alive);
            else m_ClassicMinimap.Hide();

            m_ArenaArmory.style.display = buy && m_ClassicBuyOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!m_InventoryQuery.IsEmptyIgnoreFilter)
            {
                var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
                if (inventory.Sequence != m_ClassicPurchaseSequence)
                {
                    m_ClassicPurchaseSequence = inventory.Sequence;
                    if (inventory.RequestId == m_InventoryRequestId)
                    {
                        m_ClassicPurchaseMessage = inventory.Error == Inventory.InventoryError.None
                            ? null : ClassicPurchaseError(inventory.Error);
                        m_ClassicPurchaseMessageUntil = Time.unscaledTime + 3;
                    }
                }
                string purchaseError = Time.unscaledTime < m_ClassicPurchaseMessageUntil ? m_ClassicPurchaseMessage : null;
                m_ClassicShop.Present(m_ClassicOffers, inventory.Graph, buy && match.Alive && !m_ArenaFinished &&
                    !m_InventoryRequestPending && !TimedActionActive, false, purchaseError, match.Credits, "CREDITS");
            }
            UpdateChat();
            PresentQuickSlots(match.Alive && !m_ArenaFinished);
            PresentMelee(fighting && match.Alive);
            UpdateGrenades(fighting && match.Alive && !paused);
            PresentTimedAction();
            m_ArenaResult.style.display = m_ArenaFinished && !paused ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_ArenaFinished) Utils.SetCursorVisible(true);

            bool canUse = (fighting || buy && (match.Use is ClassicUse.CacheA or ClassicUse.CacheMid or ClassicUse.CacheB)) &&
                match.Alive && match.Use != ClassicUse.None && !paused &&
                !m_InventoryVisible && !ChatInputBlocked && Application.isFocused;
            bool held = canUse && m_InteractionBindings.Operate.IsPressed();
            double now = Time.unscaledTimeAsDouble;
            if (held != m_ClassicHeld || held && now >= m_NextClassicUse)
            {
                Send(new ClassicUseRpc { Round = match.Round, Held = held });
                m_ClassicHeld = held;
                m_NextClassicUse = now + .25;
            }
            if (match.BombVisible && !m_ClassicBomb)
            {
                m_ClassicBomb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_ClassicBomb.name = "Bomb";
                m_ClassicBomb.transform.SetParent(transform, false);
                m_ClassicBomb.transform.localScale = new Vector3(.28f, .13f, .18f);
                var collider = m_ClassicBomb.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                var color = new MaterialPropertyBlock();
                color.SetColor("_BaseColor", new Color(.58f, .09f, .06f));
                m_ClassicBomb.GetComponent<Renderer>().SetPropertyBlock(color);
            }
            if (m_ClassicBomb)
            {
                m_ClassicBomb.SetActive(match.BombVisible);
                m_ClassicBomb.transform.position = (Vector3)match.BombPosition + Vector3.up * .08f;
            }

            m_RefreshTimer -= Time.deltaTime;
            if (m_RefreshTimer > 0) return;
            m_RefreshTimer = .1f;
            RefreshHealth();
            ClassicDust2Map.Active?.PresentSupplyCacheMask(match.CacheMask);
            PresentClassicTeamStatus(m_ClassicStatusA, match.AliveMaskA, match.AttackTeam == 0, match.Team != 0);
            PresentClassicTeamStatus(m_ClassicStatusB, match.AliveMaskB, match.AttackTeam == 1, match.Team != 1);
            var left = match.Team == 0 ? m_ClassicStatusA : m_ClassicStatusB;
            var right = match.Team == 0 ? m_ClassicStatusB : m_ClassicStatusA;
            if (m_ArenaHud.IndexOf(left) != 0)
            {
                left.SendToBack();
                right.BringToFront();
            }
            int seconds = Mathf.CeilToInt(match.Remaining);
            int ownScore = match.Team == 0 ? match.ScoreA : match.ScoreB;
            int enemyScore = match.Team == 0 ? match.ScoreB : match.ScoreA;
            m_ArenaScore.text = match.Round == 0 ? MoonkovLocalization.Text("经典爆破") :
                $"{ownScore}   {seconds / 60:00}:{seconds % 60:00}   {enemyScore}";
            string role = MoonkovLocalization.Text(match.Team == match.AttackTeam ? "进攻" : "防守");
            string phase = match.Phase switch
            {
                ClassicPhase.Waiting => MoonkovLocalization.Text("等待玩家进入"),
                ClassicPhase.Buy => $"{role}   ${match.Credits}   {RaidKeyBindings.DisplayName(RaidKey.Buy)} {MoonkovLocalization.Text("购买")}",
                ClassicPhase.RoundOver => MoonkovLocalization.Text(match.Winner == match.Team ? "本回合获胜" : "本回合落败"),
                ClassicPhase.Planted => MoonkovLocalization.Text("炸弹已安放"),
                _ when !match.Alive => MoonkovLocalization.Text("等待下回合"),
                _ => match.HasBomb ? $"{role}   {MoonkovLocalization.Text("携带炸弹")}" : role
            };
            if (canUse)
            {
                var key = m_InteractionBindings.Label(m_InteractionBindings.Operate);
                bool cache = match.Use is ClassicUse.CacheA or ClassicUse.CacheMid or ClassicUse.CacheB;
                string action = MoonkovLocalization.Text(cache ? "开启补给箱" :
                    match.Use == ClassicUse.Defuse ? "拆弹" : match.Use == ClassicUse.PickUp ? "拾取炸弹" : "安放炸弹");
                phase = match.UseProgress > 0 ? $"{action}   {Mathf.FloorToInt(match.UseProgress * 100)}%" : $"{key}  {action}";
            }
            m_ArenaStatus.text = phase;
            if (m_ArenaFinished)
                MoonkovLocalization.Set(m_ArenaResultText, match.Winner < 0 ? "平局" : match.Winner == match.Team ? "获胜" : "对局结束");
        }

        private void SetClassicBuyMenu(bool visible)
        {
            m_ClassicBuyOpen = visible;
            if (visible)
            {
                if (m_InventoryVisible) SetInventory(false);
                m_ArenaArmory.style.display = DisplayStyle.Flex;
                Utils.SetCursorVisible(true);
            }
            else
            {
                m_ArenaArmory.style.display = DisplayStyle.None;
                Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen || m_MapOpen || m_WasSettled);
            }
        }

        private static void PresentClassicTeamStatus(VisualElement row, byte aliveMask, bool attacking, bool right)
        {
            row.EnableInClassList("classic-status-attackers", attacking);
            row.EnableInClassList("classic-status-defenders", !attacking);
            row.EnableInClassList("is-right", right);
            row.Q<Label>(className: "classic-status-team").text = attacking ? "T" : "CT";
            for (int i = 0; i < ClassicMatch.TeamSize; i++)
            {
                bool alive = (aliveMask & (1 << i)) != 0;
                var player = row.Q<Label>("player-" + i);
                player.text = alive ? "●" : "×";
                player.EnableInClassList("is-dead", !alive);
            }
        }

        private static string ClassicPurchaseError(Inventory.InventoryError error) => error switch
        {
            Inventory.InventoryError.Full => "背包空间不足",
            Inventory.InventoryError.Overweight => "超过携带重量",
            Inventory.InventoryError.Stale => "库存已变化，请重试",
            Inventory.InventoryError.Inaccessible => "当前无法购买",
            _ => "购买失败"
        };
    }
}
