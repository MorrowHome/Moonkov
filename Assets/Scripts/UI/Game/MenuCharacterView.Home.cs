using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MenuCharacterView
    {
        private enum HomeReaction { Heart, Cat, Sniper }
        private const float HomeTurnSeconds = .45f;
        private const float HomeGestureSeconds = 1.65f;
        private const float HomeShotSeconds = .78f;
        private HomeReaction m_NextHomeReaction;
        private HomeReaction? m_HomeReaction;
        private SquadBakedMotions m_HomeMotions;
        private float m_HomeReactionTime, m_HomeFacing;
        private bool m_HomeReducedMotion, m_HomeSniperShowing, m_HomeShotFired;
        internal bool HomeReactionActive => m_HomeReaction.HasValue;

        private float HomeGestureAge => m_HomeReactionTime - (m_HomeReducedMotion ? 0 : HomeTurnSeconds);
        private float HomeYaw => Mathf.LerpAngle(150, 0, m_HomeFacing);

        internal void PlayHomeReaction()
        {
            if (m_Disposed || !m_HomePose || m_Settings == null || HomeReactionActive) return;
            m_HomeReaction = m_NextHomeReaction;
            m_NextHomeReaction = m_NextHomeReaction == HomeReaction.Sniper
                ? HomeReaction.Heart : m_NextHomeReaction + 1;
            m_HomeReducedMotion = HalosTerminalBackdrop.PrefersReducedMotion;
            m_HomeReactionTime = 0;
            m_HomeShotFired = false;
            m_ViewDirty = true;
        }

        private void AdvanceHomeReaction(float dt)
        {
            m_HomeReactionTime += dt;
            float age = HomeGestureAge;
            bool sniper = m_HomeReaction == HomeReaction.Sniper && age >= 0 && age < HomeGestureSeconds;
            if (sniper != m_HomeSniperShowing)
            {
                m_Gestures?.RestoreBeforeAnimation();
                m_HomeSniperShowing = sniper;
                m_EquipmentDischarge?.Clear();
                ApplyEquipmentVisualState();
            }

            float returnAge = age - HomeGestureSeconds;
            if (returnAge >= (m_HomeReducedMotion ? 0 : HomeTurnSeconds))
            {
                m_HomeMotions?.RestoreBeforeAnimation();
                m_HomeMotions?.RestoreExpressions();
                m_HomeMotions = null;
                m_HomeReaction = null;
                m_HomeFacing = 0;
                return;
            }
            m_HomeFacing = m_HomeReducedMotion ? 1 :
                Mathf.SmoothStep(0, 1, m_HomeReactionTime / HomeTurnSeconds) *
                (1 - Mathf.SmoothStep(0, 1, returnAge / HomeTurnSeconds));
        }

        private void ApplyHomeReactionPose(float dt)
        {
            if (!HomeReactionActive || HomeGestureAge < 0 || HomeGestureAge >= HomeGestureSeconds) return;
            float age = HomeGestureAge;
            float weight = m_HomeReducedMotion ? 1 : Mathf.SmoothStep(0, 1,
                Mathf.Min(age / .24f, (HomeGestureSeconds - age) / .3f));
            if (m_HomeSniperShowing)
            {
                HaloAimWeight = weight;
                if (m_Gestures != null && m_Chest != null)
                    m_Gestures.Apply((int)DollSingerHaloAim.HaloWeapon.Sniper, weight,
                        m_HomeReducedMotion ? float.PositiveInfinity : age - HomeShotSeconds, false, 0, .2f,
                        m_Chest, m_Model.transform.forward, dt);
                return;
            }

            m_HomeMotions ??= new SquadBakedMotions(m_Animator, CharacterIndex);
            int pose = m_HomeReaction == HomeReaction.Heart ? 2 : 3;
            m_HomeMotions.Apply(pose, m_HomeReducedMotion ? 1.55 : .45 + age * 1.9, dt, m_HomeReducedMotion, weight);
        }

        private void PlayHomeShot()
        {
            if (!m_HomeSniperShowing || m_HomeShotFired || HomeGestureAge < HomeShotSeconds) return;
            m_HomeShotFired = true;
            m_EquipmentSniper?.SetNetworkState(0, false, 0, 0, 0, 0);
            if (!m_HomeReducedMotion)
            {
                m_EquipmentSniper?.PlayShot();
                m_EquipmentDischarge?.Play(DollSingerHaloAim.HaloWeapon.Sniper, m_EquipmentHaloColor);
            }
            var registry = EquipmentRegistry;
            if (registry)
                MoonkovAudio.PlayWeaponLayer(registry.GetWeaponData(DollSingerWeapons.Sniper)?.WeaponFireSfx, Vector3.zero, .24f, 0);
        }

        private void ReleaseHomeReaction()
        {
            m_HomeReaction = null;
            m_HomeMotions = null;
            m_HomeFacing = 0;
            m_HomeSniperShowing = false;
        }
    }
}
