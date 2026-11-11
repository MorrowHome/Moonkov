using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private bool m_ClassicHud, m_ClassicHeld;
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
            if ((m_ArenaFinished || !match.Alive) && m_InventoryVisible) SetInventory(false);
            if (match.Alive && !m_ArenaFinished && !ChatInputBlocked && !paused &&
                (RaidKeyBindings.Pressed(RaidKey.Inventory) || (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)))
                SetInventory(!m_InventoryVisible);

            m_ArenaArmory.style.display = buy && !m_HealthSelected ? DisplayStyle.Flex : DisplayStyle.None;
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

            bool canUse = fighting && match.Alive && match.Use != ClassicUse.None && !paused &&
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
                string action = MoonkovLocalization.Text(match.Use == ClassicUse.Defuse ? "拆弹" : match.Use == ClassicUse.PickUp ? "拾取炸弹" : "安放炸弹");
                phase = match.UseProgress > 0 ? $"{action}   {Mathf.FloorToInt(match.UseProgress * 100)}%" : $"{key}  {action}";
            }
            m_ArenaStatus.text = phase;
            if (m_ArenaFinished)
                MoonkovLocalization.Set(m_ArenaResultText, match.Winner < 0 ? "平局" : match.Winner == match.Team ? "获胜" : "对局结束");
        }
    }
}
