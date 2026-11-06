using Unity.MP_FPS.Online;
using UnityEngine;
using UnityEngine.Playables;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MenuCharacterView
    {
        private int m_SquadPose = -1;
        private bool m_SquadReady;
        private float m_SquadReadyPulse;
        private SquadBakedMotions m_SquadMotions;
        private bool m_SquadSettled;
        internal Transform SquadRoot => m_Stage != null ? m_Stage.transform : null;

        internal void PresentSquad(LobbyAppearance appearance, int slot, bool ready)
        {
            if (m_SharedStage == null) return;
            if (m_Model == null) CreateStage();
            if (m_SquadMotions == null)
            {
                m_SquadMotions = new SquadBakedMotions(m_Animator, CharacterIndex);
                m_BaseTime = slot * .73f;
            }
            m_SquadPose = appearance.PoseIndex < 0 ? slot % 4 : Mathf.Clamp(appearance.PoseIndex, 0, 3);
            if (ready && !m_SquadReady) m_SquadReadyPulse = 1.2f;
            m_SquadReady = ready;
            if (!ready) m_SquadReadyPulse = 0;
            // A single shared lighting rig owns the stage. Halo emission remains visible.
            m_EquipmentLightEnabled = false;
            ApplyEquipmentVisualState();
            if (m_EquipmentFaceFront) m_EquipmentFaceFront.enabled = false;
            if (m_EquipmentFaceBack) m_EquipmentFaceBack.enabled = false;
        }

        private void AdvanceSquadPose(float dt)
        {
            m_SquadReadyPulse = Mathf.Max(0, m_SquadReadyPulse - dt);
            if (m_Motion != 0 && !m_Transition)
            {
                BeginMotion(0);
                m_Layers.SetLayerMaskFromAvatarMask(1, m_FullBodyMask);
            }
            if (m_Stage != null)
            {
                int pose = m_SquadReadyPulse > 0 ? 1 : m_SquadPose;
                var facing = Quaternion.Euler(0, pose == 0 ? 12 : pose == 1 ? 8 : pose == 2 ? 6 : -4, 0);
                m_Stage.transform.localRotation = dt > 0
                    ? Quaternion.Slerp(m_Stage.transform.localRotation, facing, 1 - Mathf.Exp(-dt * 10)) : facing;
            }
        }

        private void ApplySquadPose(float dt)
        {
            if (m_SharedStage == null || m_SquadMotions == null) return;
            m_SquadMotions.Apply(m_SquadReadyPulse > 0 ? 1 : m_SquadPose, m_BaseTime, dt, m_SquadSettled);
        }

        internal void SettleSquadPose()
        {
            if (m_Model == null) return;
            m_SquadReadyPulse = 0;
            m_SquadSettled = true;
            AdvanceSquadPose(0);
            if (m_Transition)
            {
                m_Mixer.SetInputWeight(m_Slot, 0); m_Mixer.SetInputWeight(1 - m_Slot, 1);
                m_Slot = 1 - m_Slot; m_Transition = false; m_Blend = 1;
            }
            m_Times[m_Slot] = m_Settings.Motions[m_Motion].length * .9;
            Advance(0);
            m_SquadSettled = false;
        }
    }
}
