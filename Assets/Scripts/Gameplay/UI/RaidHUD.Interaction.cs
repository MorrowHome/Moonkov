// 选出准星此刻指向的交互目标。箱子、掉落、尸体、抽屉、日志和控制按钮一起按视线、方向及距离评分，HUD 再按目标发对应操作。
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private RaidInteractionBindings m_InteractionBindings;
        private float m_WorldFocusScore = -1;
        private bool m_WorldControlFocused;

        private void SelectWorldInteraction(
            bool allowed,
            bool controls,
            Vector3 actor,
            RaidDeathBagClientState bags,
            out int loot,
            out float distance,
            bool worldControls = true) {
            m_InteractionBindings ??= new RaidInteractionBindings();
            m_InteractionBindings.ObserveDevice();
            m_FieldRecordFocus = null;
            loot = -1;
            distance = RaidRules.PickupRange;
            m_WorldFocusScore = -1;
            m_WorldControlFocused = false;
            var camera = Camera.main;
            m_AirlockPrompt.RefreshSelection(camera, actor, allowed && controls && worldControls, m_World);
            if (!allowed || !camera) return;
            int bestLoot = -1;
            float bestDistance = distance;

            void Consider(int id, Vector3 point, Transform surface = null, Vector3? rangeAnchor = null) {
                float score =
                    RaidInteractionAim.Score(camera, actor, point, RaidRules.PickupRange, surface, rangeAnchor);
                // 所有候选都用同一套瞄准分数。只有分数更好的候选替换当前目标，距离读数随后由它的测距位置决定。
                if (score <= m_WorldFocusScore) return;
                m_WorldFocusScore = score;
                bestLoot = id;
                bestDistance = Vector3.Distance(actor, rangeAnchor ?? point);
            }

            // 所有战利品用相同的瞄准和可见规则。靠得近也需要朝向和视线能到。
            var map = MoonRaidMap.Active;
            for (int i = 0; i < map.LootPositions.Length; i++) Consider(i, map.LootPositions[i]);
            foreach (var bag in bags.Bags.Values)
                if (bag.CharacterIndex != RaidLootContainers.DroppedItemCharacterIndex || !bag.Empty) {
                    var anchor = (Vector3)bag.Position + Vector3.up * .25f;
                    // 先按范围排除远处尸体，再去找骨骼的位置。没必要为一具够不到的尸体做额外计算。
                    if ((actor - anchor).sqrMagnitude > RaidRules.PickupRange * RaidRules.PickupRange) continue;
                    var point = anchor;
                    // 人形尸体倒地后，镜头瞄身体的实际位置。距离仍按同步的尸体位置判断，动画改变姿势时也能选到。
                    if (bag.CharacterIndex >= 0 && map.TryGetCorpseAimPoint(bag.LootId, camera, out var bodyPoint))
                        point = bodyPoint;
                    Consider(bag.LootId, point, rangeAnchor: anchor);
                }
            if (controls && RaidFurnitureLoot.Active) {
                var drawers = RaidFurnitureLoot.Active.Drawers;
                for (int i = 0; i < drawers.Length; i++)
                    if (drawers[i] != null && drawers[i].Target && drawers[i].Target.gameObject.activeInHierarchy)
                        Consider(RaidFurnitureLoot.FirstId + i, drawers[i].Position, drawers[i].Target);
            }
            // 日志也是可交互候选，比箱子更贴近准星时接管。此时清空搜刮目标才能避免一次按键开两个面板。
            foreach (var record in m_FieldRecords.Tablets) {
                if (!record.Screen || !record.Screen.gameObject.activeInHierarchy) continue;
                var point = record.Screen.bounds.center;
                float score = RaidInteractionAim.Score(camera, actor, point, RaidRules.PickupRange,
                    record.Screen.transform);
                if (score <= m_WorldFocusScore) continue;
                m_FieldRecordFocus = record;
                m_WorldFocusScore = score;
                bestLoot = -1;
                bestDistance = Vector3.Distance(actor, point);
            }
            float controlScore = worldControls ? m_AirlockPrompt.FocusScore(camera, actor) : -1;
            // 控制按钮赢得选择时把搜刮与日志都清掉。因此提示和按键调用都指向同一个按钮。
            if (controlScore > m_WorldFocusScore) {
                m_WorldFocusScore = controlScore;
                m_WorldControlFocused = true;
                bestLoot = -1;
                m_FieldRecordFocus = null;
            }
            loot = bestLoot;
            distance = bestDistance;
            m_AirlockPrompt.SetPresentation(m_WorldControlFocused);
        }
    }
}