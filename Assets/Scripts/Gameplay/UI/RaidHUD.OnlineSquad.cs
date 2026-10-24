using System.Text;
using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private VisualElement m_OnlineSquad;
        private float m_NextSquadRefresh;
        private void PresentOnlineSquad()
        {
            if (!OnlineLobbyClient.InInstance || m_PlayerQuery.IsEmptyIgnoreFilter)
            { if (m_OnlineSquad != null) m_OnlineSquad.style.display = DisplayStyle.None; return; }
            if (Time.unscaledTime < m_NextSquadRefresh) return;
            m_NextSquadRefresh = Time.unscaledTime + .5f;
            if (m_OnlineSquad == null)
            {
                m_OnlineSquad = Client.TerminalLayout.Clone("OnlineSquad", "onlineSquad");
                m_Root.Q("raidRightRail").Add(m_OnlineSquad);
            }
            m_OnlineSquad.style.display = DisplayStyle.Flex;
            var local = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            var roster = new StringBuilder();
            using var query = m_World.EntityManager.CreateEntityQuery(typeof(PredictedPlayerGhost), typeof(PlayerGhost.PlayerData));
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var health = m_World.EntityManager.GetComponentData<PredictedPlayerGhost>(entity);
                if (!health.OnlineHuman || health.OnlineTeam != local.OnlineTeam) continue;
                roster.Append(m_World.EntityManager.GetComponentData<PlayerGhost.PlayerData>(entity).Name.ToString());
                roster.Append(health.CurrentHealth > 0 ? " · 生命 " + Mathf.CeilToInt(health.CurrentHealth) : " · 已阵亡").Append('\n');
            }
            m_OnlineSquad.Q<Label>("onlineSquadTitle").text = OnlineLobbyClient.Snapshot?.Mode == "Versus" ? "真人对抗 / " + (local.OnlineTeam == 0 ? "A 队" : "B 队") : "合作远征 / AI 敌人";
            m_OnlineSquad.Q<Label>("onlineSquadRoster").text = roster.ToString().TrimEnd();
        }
    }
}
