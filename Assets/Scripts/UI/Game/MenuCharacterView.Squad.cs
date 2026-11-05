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
        internal Transform SquadRoot => m_Stage != null ? m_Stage.transform : null;

        internal void PresentSquad(LobbyAppearance appearance, int slot, bool ready)
        {
            if (m_SharedStage == null) return;
            if (m_Model == null) CreateStage();
            if (m_SquadPose < 0) m_BaseTime = slot * .73f;
            m_SquadPose = appearance.PoseIndex < 0 ? slot % 4 : Mathf.Clamp(appearance.PoseIndex, 0, 3);
            if (ready && !m_SquadReady) m_SquadReadyPulse = .9f;
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
            int pose = m_SquadReadyPulse > 0 ? 0 : m_SquadPose;
            int motion = 0;
            if (pose == 2)
            {
                for (int i = 0; i < m_Settings.Motions.Length; i++)
                    if (m_Settings.Motion(i).Name == "Crouch_Idle") { motion = i; break; }
            }
            else if (pose == 0 || pose == 1)
            {
                for (int i = 1; i < m_Settings.Motions.Length; i++)
                {
                    var option = m_Settings.Motion(i);
                    if (option.Weapon > 0 && option.WeaponAction == MenuWeaponAction.Aim && EquipmentMotionAllowed(i))
                    { motion = i; break; }
                }
            }
            if (motion != m_Motion && !m_Transition)
            {
                BeginMotion(motion);
                if (motion == 0 || pose == 2) m_Layers.SetLayerMaskFromAvatarMask(1, m_FullBodyMask);
            }
            if (m_Stage != null)
            {
                var facing = Quaternion.Euler(0, pose == 1 ? -28 : pose == 2 ? 14 : pose == 3 ? 18 : 0, 0);
                m_Stage.transform.localRotation = dt > 0
                    ? Quaternion.Slerp(m_Stage.transform.localRotation, facing, 1 - Mathf.Exp(-dt * 10)) : facing;
            }
        }

        internal void SettleSquadPose()
        {
            if (m_Model == null) return;
            m_SquadReadyPulse = 0;
            AdvanceSquadPose(0);
            if (m_Transition)
            {
                m_Mixer.SetInputWeight(m_Slot, 0); m_Mixer.SetInputWeight(1 - m_Slot, 1);
                m_Slot = 1 - m_Slot; m_Transition = false; m_Blend = 1;
            }
            m_Times[m_Slot] = m_Settings.Motions[m_Motion].length * .9;
            Advance(0);
        }
    }
}
