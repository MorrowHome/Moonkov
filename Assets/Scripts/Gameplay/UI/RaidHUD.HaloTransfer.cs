using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public partial class RaidHUD
    {
        private void RefreshHaloTransferInteraction(bool allowed)
        {
            if (!allowed || !m_PlayerQuery.HasSingleton<PredictedPlayerGhost>()) return;
            var sender = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            bool occupied = HaloTransferRules.Occupied(sender);
            var reader = m_World.GetExistingSystemManaged<ClientInputReaderSystem>();
            var target = occupied ? sender.HaloTransferTarget : reader?.HaloTransferCandidate ?? Entity.Null;
            bool hasTarget = m_World.EntityManager.HasComponent<PredictedPlayerGhost>(target);
            if (!occupied && (!hasTarget || !HaloTransferRules.CanSend(sender))) return;
            m_Interaction.EnableInClassList("is-visible", true);
            m_Interaction.EnableInClassList("is-halo-transfer", true);
            m_Interaction.EnableInClassList("is-error", false);
            m_Interaction.EnableInClassList("is-empty", false);
            MoonkovLocalization.Set(m_InteractionKey, TutorialRuntime.UsingGamepad ? "←" : "Y");
            MoonkovLocalization.Set(m_Prompt, sender.HaloTransferPhase == HaloTransferPhase.Returning ? "Halo returning" :
                occupied ? "Transferring energy" : "Hold to transfer energy");
            m_InteractionDetails.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasTarget) return;
            var receiver = m_World.EntityManager.GetComponentData<PredictedPlayerGhost>(target);
            string name = m_World.EntityManager.HasComponent<PlayerGhost.PlayerData>(target)
                ? m_World.EntityManager.GetComponentData<PlayerGhost.PlayerData>(target).Name.ToString() : "";
            if (string.IsNullOrEmpty(name)) name = MoonkovLocalization.Text("Teammate");
            m_InteractionState.enableRichText = false;
            MoonkovLocalization.Set(m_InteractionState, $"{name}  {HaloEnergyRules.Energy(receiver) / HaloEnergyRules.Maximum(receiver) * 100:0}%");
            float distance = Vector3.Distance(HaloTransferRules.Anchor(sender), HaloTransferRules.Anchor(receiver));
            MoonkovLocalization.Set(m_InteractionRange, $"{distance:0.0} m");
        }
    }
}
