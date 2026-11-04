using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private RaidInteractionBindings m_InteractionBindings;
        private float m_WorldFocusScore = -1;
        private bool m_WorldControlFocused;

        private void SelectWorldInteraction(bool allowed, bool controls, Vector3 actor,
            RaidDeathBagClientState bags, out int loot, out float distance)
        {
            m_InteractionBindings ??= new RaidInteractionBindings();
            m_InteractionBindings.ObserveDevice();
            loot = -1; distance = RaidRules.PickupRange; m_WorldFocusScore = -1; m_WorldControlFocused = false;
            var camera = Camera.main;
            m_AirlockPrompt.RefreshSelection(camera, actor, allowed && controls, m_World);
            if (!allowed || !camera) return;
            int bestLoot = -1;
            float bestDistance = distance;

            void Consider(int id, Vector3 point, Transform surface = null)
            {
                float score = RaidInteractionAim.Score(camera, actor, point, RaidRules.PickupRange, surface);
                if (score <= m_WorldFocusScore) return;
                m_WorldFocusScore = score; bestLoot = id; bestDistance = Vector3.Distance(actor, point);
            }

            // All loot kinds use the same aim/visibility rule. Proximity alone cannot pick a cache behind the player.
            var map = MoonRaidMap.Active;
            for (int i = 0; i < map.LootPositions.Length; i++) Consider(i, map.LootPositions[i]);
            foreach (var bag in bags.Bags.Values)
                if (bag.CharacterIndex != RaidLootContainers.DroppedItemCharacterIndex || !bag.Empty)
                    Consider(bag.LootId, (Vector3)bag.Position + Vector3.up * .25f);
            if (controls && RaidFurnitureLoot.Active)
            {
                var drawers = RaidFurnitureLoot.Active.Drawers;
                for (int i = 0; i < drawers.Length; i++)
                    if (drawers[i] != null && drawers[i].Target && drawers[i].Target.gameObject.activeInHierarchy)
                        Consider(RaidFurnitureLoot.FirstId + i, drawers[i].Position, drawers[i].Target);
            }
            float controlScore = m_AirlockPrompt.FocusScore(camera, actor);
            if (controlScore > m_WorldFocusScore)
            { m_WorldFocusScore = controlScore; m_WorldControlFocused = true; bestLoot = -1; }
            loot = bestLoot; distance = bestDistance;
            m_AirlockPrompt.SetPresentation(m_WorldControlFocused);
        }
    }
}
