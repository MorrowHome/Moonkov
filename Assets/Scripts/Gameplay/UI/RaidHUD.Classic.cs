using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private bool m_ClassicHud, m_ClassicHeld, m_ClassicBuyOpen;
        private double m_NextClassicUse;
        private GameObject m_ClassicBomb;

        private void PresentClassicHud()
        {
            var data = m_World.EntityManager.GetComponentData<ClassicClientState>(m_StateQuery.GetSingletonEntity());
            var match = data.Snapshot;
            bool buy = match.Phase == ClassicPhase.Buy;
            bool fighting = match.Phase is ClassicPhase.Live or ClassicPhase.Planted;
            bool paused = GameSettings.Instance.IsPauseMenuOpen;
            m_ArenaFinished = data.Received && match.Phase == ClassicPhase.Finished;
            if (!buy && m_ClassicBuyOpen) SetClassicBuyMenu(false);
            if ((m_ArenaFinished || !match.Alive) && m_InventoryVisible) SetInventory(false);
            bool tabPressed = RaidKeyBindings.Pressed(RaidKey.Inventory);
            bool gamepadPressed = Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame;
            if (match.Alive && !m_ArenaFinished && !ChatInputBlocked && !paused && (tabPressed || buy && gamepadPressed))
            {
                if (buy) SetClassicBuyMenu(!m_ClassicBuyOpen);
                else SetInventory(!m_InventoryVisible);
            }
            if (!buy && match.Alive && !m_ArenaFinished && !ChatInputBlocked && !paused && gamepadPressed)
                SetInventory(!m_InventoryVisible);

            m_ArenaArmory.style.display = buy && m_ClassicBuyOpen ? DisplayStyle.Flex : DisplayStyle.None;
            int selected = m_ArenaItems.index;
            bool affordable = selected >= 0 && selected < m_ArenaDefinitions.Length &&
                ClassicMatch.Price(m_ArenaDefinitions[selected]) <= match.Credits;
            m_ArenaTake.SetEnabled(affordable && !m_InventoryRequestPending && !TimedActionActive);
            UpdateInventoryBackdrop();
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
            PresentClassicTeamStatus(m_ClassicStatusA, match.AliveMaskA);
            PresentClassicTeamStatus(m_ClassicStatusB, match.AliveMaskB);
            int seconds = Mathf.CeilToInt(match.Remaining);
            m_ArenaScore.text = match.Round == 0 ? MoonkovLocalization.Text("经典爆破") :
                $"{match.ScoreA}   {seconds / 60:00}:{seconds % 60:00}   {match.ScoreB}";
            string role = MoonkovLocalization.Text(match.Team == match.AttackTeam ? "进攻" : "防守");
            string phase = match.Phase switch
            {
                ClassicPhase.Waiting => MoonkovLocalization.Text("等待玩家进入"),
                ClassicPhase.Buy => $"{role}   ${match.Credits}   {MoonkovLocalization.Text("Tab 购买")}",
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
                SetInventory(true);
                m_ArenaInventoryWindow.style.display = DisplayStyle.None;
                m_ArenaArmory.style.display = DisplayStyle.Flex;
                Utils.SetCursorVisible(true);
            }
            else
            {
                m_ArenaInventoryWindow.style.display = DisplayStyle.Flex;
                m_ArenaArmory.style.display = DisplayStyle.None;
                SetInventory(false);
            }
        }

        private static void PresentClassicTeamStatus(VisualElement row, byte aliveMask)
        {
            if (row == null) return;
            for (int i = 0; i < ClassicMatch.TeamSize; i++)
            {
                bool alive = (aliveMask & (1 << i)) != 0;
                var player = row.Q<Label>("player-" + i);
                MoonkovLocalization.Set(player, alive ? "●" : "×");
                player.EnableInClassList("is-dead", !alive);
            }
        }
    }
}
