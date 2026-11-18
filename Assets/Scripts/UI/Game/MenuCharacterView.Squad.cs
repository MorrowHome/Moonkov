// 小队舞台的角色动作和交互。它更新成员摆出的姿势，并提供预览舞台选中、拖动某个模型需要的命中范围。
using Unity.MP_FPS.Online;
using UnityEngine;
using UnityEngine.Playables;

namespace Unity.MP_FPS.Client {
    public sealed partial class MenuCharacterView {
        private int m_SquadPose = -1;
        private bool m_SquadReady;
        private float m_SquadReadyPulse;
        private SquadBakedMotions m_SquadMotions;
        private bool m_SquadSettled;
        private float m_SquadPoseYaw;
        internal float SquadInspectionYaw { get; set; }
        internal Transform SquadRoot => m_Stage != null ? m_Stage.transform : null;

        internal bool HitSquadModel(Ray ray) {
            if (m_Renderers == null) return false;
            // 角色是显示到一张纹理上的。鼠标射线与预览角色的渲染范围相交时，认为点到了这个角色。
            foreach (var renderer in m_Renderers)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    renderer.bounds.IntersectRay(ray))
                    return true;
            return false;
        }

        internal void PresentSquad(LobbyAppearance appearance, int slot, bool ready) {
            if (m_SharedStage == null) return;
            if (m_Model == null) CreateStage();
            if (m_SquadMotions == null) {
                m_SquadMotions = new SquadBakedMotions(m_Animator, CharacterIndex);
                // 让不同座位从稍有差异的动作时间开始。几个人预览同一套待机时不会完全同步。
                m_BaseTime = slot * .73f;
            }
            m_SquadPose = appearance.PoseIndex < 0 ? slot % 4 : Mathf.Clamp(appearance.PoseIndex, 0, 3);
            // 从没准备变为准备时短暂切姿势。保持准备状态不会不断重启动作。
            if (ready && !m_SquadReady) m_SquadReadyPulse = 1.2f;
            m_SquadReady = ready;
            if (!ready) m_SquadReadyPulse = 0;
            // 小队舞台共用一组灯光。各自的光环发光仍可见。
            // 小队预览共享舱内灯光。保留武器本身的发光，取消每个角色单独投下的灯，避免重复照亮舞台。
            m_EquipmentLightEnabled = false;
            ApplyEquipmentVisualState();
            if (m_EquipmentFaceFront) m_EquipmentFaceFront.enabled = false;
            if (m_EquipmentFaceBack) m_EquipmentFaceBack.enabled = false;
        }

        private void AdvanceSquadPose(float dt) {
            m_SquadReadyPulse = Mathf.Max(0, m_SquadReadyPulse - dt);
            if (m_Motion != 0 && !m_Transition) {
                BeginMotion(0);
                m_Layers.SetLayerMaskFromAvatarMask(1, m_FullBodyMask);
            }
            if (m_Stage != null) {
                int pose = m_SquadReadyPulse > 0 ? 1 : m_SquadPose;
                float facing = pose == 0 ? 12 : pose == 1 ? 8 : pose == 2 ? 6 : -4;
                m_SquadPoseYaw = dt > 0 ? Mathf.LerpAngle(m_SquadPoseYaw, facing, 1 - Mathf.Exp(-dt * 10)) : facing;
                m_Stage.transform.localRotation = Quaternion.Euler(0, m_SquadPoseYaw + SquadInspectionYaw, 0);
            }
        }

        private void ApplySquadPose(float dt) {
            if (m_SharedStage == null || m_SquadMotions == null) return;
            m_SquadMotions.Apply(m_SquadReadyPulse > 0 ? 1 : m_SquadPose, m_BaseTime, dt, m_SquadSettled);
        }

        internal void SettleSquadPose() {
            if (m_Model == null) return;
            m_SquadReadyPulse = 0;
            // 静态模式在确定时刻采样姿势。先结束正在混合的两段动作，让截图式呈现和当前配置一致。
            m_SquadSettled = true;
            AdvanceSquadPose(0);
            if (m_Transition) {
                m_Mixer.SetInputWeight(m_Slot, 0);
                m_Mixer.SetInputWeight(1 - m_Slot, 1);
                m_Slot = 1 - m_Slot;
                m_Transition = false;
                m_Blend = 1;
            }
            m_Times[m_Slot] = m_Settings.Motions[m_Motion].length * .9;
            Advance(0);
            m_SquadSettled = false;
        }
    }
}