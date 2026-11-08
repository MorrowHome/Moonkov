using Unity.MP_FPS.Missions;
using Unity.MP_FPS.Moon;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private readonly RaidInteractionOutline m_InteractionOutline = new();

        private void UpdateInteractionOutlines(bool allowed, bool expedition, bool lootAllowed, bool operate,
            Vector3 actor, RaidDeathBagClientState bags)
        {
            if (!m_InteractionOutline.BeginCollection(Camera.main, actor, allowed && Time.timeScale > 0, Time.unscaledTime)) return;
            var map = MoonRaidMap.Active;
            if (lootAllowed && map)
            {
                if (expedition && RaidFurnitureLoot.Active)
                    foreach (var drawer in RaidFurnitureLoot.Active.Drawers)
                        if (drawer != null) m_InteractionOutline.Add(drawer.OutlineMesh, drawer.Target, drawer.Position);
                foreach (var record in m_FieldRecords.Tablets)
                    if (record.Screen) m_InteractionOutline.Add(record.Screen.transform, record.Screen.bounds.center);
                for (int i = 0; i < map.LootPositions.Length; i++)
                    m_InteractionOutline.Add(map.LootVisual(i), map.LootPositions[i]);
                foreach (var bag in bags.Bags.Values)
                    if (bag.CharacterIndex != RaidLootContainers.DroppedItemCharacterIndex || !bag.Empty)
                        m_InteractionOutline.Add(map.DeathBagVisual(bag.LootId), (Vector3)bag.Position);
            }
            if (operate)
            {
                TacticalItemsFeedback.AddInteractionOutlines(m_InteractionOutline);
                if (expedition)
                {
                    foreach (var button in AirlockButton.Active)
                        if (button && button.Owner) m_InteractionOutline.Add(button.transform, button.InteractionCenter);
                    foreach (var button in LightSwitchButton.Active)
                        if (button) m_InteractionOutline.Add(button.transform, button.InteractionCenter);
                    foreach (var button in RaidLiftButton.Active)
                        if (button && button.Owner) m_InteractionOutline.Add(button.transform, button.Center);
                    AddMissionOutlines();
                }
            }
            m_InteractionOutline.FinishCollection(Client.HalosTerminalBackdrop.PrefersReducedMotion);
        }

        private void AddMissionOutlines()
        {
            var site = RaidMissionSite.Active;
            if (!site) return;
            int raid = m_Snapshot.RaidId;
            var snapshot = m_MissionSnapshot;
            if (m_MissionOperation != null && snapshot.RaidId == raid && snapshot.DefinitionIndex >= 0 &&
                snapshot.DefinitionIndex < site.Definitions.Length && snapshot.Phase != MissionPhase.Expired && snapshot.Phase != MissionPhase.Resolved)
            {
                var definition = site.Definitions[snapshot.DefinitionIndex];
                foreach (var objective in definition.Objectives) AddMissionSurface(site.Anchor(objective.Anchor));
                if (snapshot.Phase == MissionPhase.OutputReady)
                    AddMissionSurface(site.Anchor(definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive"));
            }
            if (!PlayerProfileClient.IsOffline) return;
            foreach (var quest in s_ResearchDevices)
            {
                if (!ResearchQuestRuntime.NeedsDevice(raid, quest)) continue;
                var anchor = ResearchQuestRuntime.Anchor(quest);
                if (anchor == null) continue;
                m_InteractionOutline.Add(ResearchQuestRuntime.DeviceVisual(raid, quest, anchor), ResearchQuestRuntime.InteractionPoint(quest, anchor));
            }
        }

        private void AddMissionSurface(RaidMissionAnchor anchor)
        {
            if (anchor != null) m_InteractionOutline.Add(RaidInteractionAim.MissionSurface(anchor.Id), anchor.Position);
        }
    }
}
