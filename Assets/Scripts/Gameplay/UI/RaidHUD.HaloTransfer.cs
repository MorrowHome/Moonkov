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
            bool gamepad = TutorialRuntime.UsingGamepad;
            MoonkovLocalization.Set(m_InteractionKey, gamepad ? "←" : "Y");
            if (occupied && HaloSupportRules.Anchored(sender))
            {
                MoonkovLocalization.Set(m_InteractionKey, gamepad ? "RB+←" : "U");
                MoonkovLocalization.Set(m_Prompt, sender.HaloTransferPhase == HaloTransferPhase.Returning ? "Halo returning" : "Recall halo");
                m_InteractionDetails.style.display = DisplayStyle.Flex;
                string mode = sender.HaloDeploymentMode == HaloDeploymentMode.Light ? "Halo lighting" :
                    sender.HaloDeploymentMode == HaloDeploymentMode.Shield ? "Area shield" : "Halo sentry";
                MoonkovLocalization.Set(m_InteractionState, "{0}  {1} Switch", MoonkovLocalization.Text(mode), gamepad ? "RB+→" : "K");
                MoonkovLocalization.Set(m_InteractionRange, $"{Vector3.Distance(HaloTransferRules.Anchor(sender), sender.HaloTransferPoint):0.0} m");
                return;
            }
            MoonkovLocalization.Set(m_Prompt, sender.HaloTransferPhase == HaloTransferPhase.Returning ? "Halo returning" :
                occupied ? sender.HaloDeploymentMode == HaloDeploymentMode.AllyGuard ? "Guarding teammate" : "Transferring energy" :
                gamepad ? "Hold to transfer / LT to guard" : "Hold to transfer / RMB to guard");
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
