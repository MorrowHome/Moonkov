using System;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class HaloLightPanel : IDisposable
    {
        private readonly VisualElement m_Root, m_IdleControls, m_AimControls;
        private readonly DollSingerInput m_Input;
        private readonly DollSingerHaloAim m_Halo;
        private readonly Slider m_Range, m_AimRange;
        private readonly SliderInt m_Idle, m_Aim;
        private readonly Label m_RangeValue, m_AimRangeValue, m_IdleValue, m_AimValue, m_Status;
        private readonly Button m_IdleTab, m_AimTab, m_Reset;
        private bool m_Visible;
        private uint m_LastSettings = uint.MaxValue;

        public HaloLightPanel(VisualElement host, DollSingerInput input, DollSingerHaloAim halo)
        {
            m_Input = input; m_Halo = halo;
            m_Root = Resources.Load<VisualTreeAsset>("Moonkov/UI/HaloLightPanel").Instantiate();
            m_Root.pickingMode = PickingMode.Ignore;
            m_Root.AddToClassList("halo-light-host");
            host.Add(m_Root);
            m_IdleControls = m_Root.Q("haloIdleControls"); m_AimControls = m_Root.Q("haloAimControls");
            m_Range = m_Root.Q<Slider>("haloRange");
            m_AimRange = m_Root.Q<Slider>("haloAimRange");
            m_Idle = m_Root.Q<SliderInt>("haloIdleStrength"); m_Aim = m_Root.Q<SliderInt>("haloAimStrength");
            m_AimRangeValue = m_Root.Q<Label>("haloAimRangeValue");
            m_RangeValue = m_Root.Q<Label>("haloRangeValue"); m_IdleValue = m_Root.Q<Label>("haloIdleValue");
            m_AimValue = m_Root.Q<Label>("haloAimValue"); m_Status = m_Root.Q<Label>("haloLightStatus");
            m_IdleTab = m_Root.Q<Button>("haloIdleTab"); m_AimTab = m_Root.Q<Button>("haloAimTab");
            m_Reset = m_Root.Q<Button>("haloLightReset");
            m_IdleTab.clicked += PreviewIdle; m_AimTab.clicked += PreviewAim; m_Reset.clicked += Reset;
            m_Range.RegisterValueChangedCallback(RangeChanged);
            m_AimRange.RegisterValueChangedCallback(RangeChanged);
            m_Idle.RegisterValueChangedCallback(IdleChanged); m_Aim.RegisterValueChangedCallback(AimChanged);
            MoonkovLocalization.Bind(m_Root);
            m_Root.AddToClassList("halo-hidden");
        }

        public void Refresh(bool enabled)
        {
            bool visible = m_Input && m_Input.LightAdjusting;
            m_Root.EnableInClassList("halo-hidden", !visible);
            if (!visible) { m_Visible = false; return; }
            if (!m_Visible || m_LastSettings != m_Input.LightingSettings)
            {
                m_LastSettings = m_Input.LightingSettings;
                m_Range.SetValueWithoutNotify(HaloLighting.Range(m_LastSettings, m_Halo.lightRange));
                m_AimRange.SetValueWithoutNotify(HaloLighting.AimRange(m_LastSettings, m_Halo.flashlightRange));
                m_Idle.SetValueWithoutNotify(HaloLighting.IdlePercent(m_LastSettings));
                m_Aim.SetValueWithoutNotify(HaloLighting.AimPercent(m_LastSettings));
                MoonkovLocalization.Set(m_AimRangeValue, "{0:0} m", m_AimRange.value);
                MoonkovLocalization.Set(m_RangeValue, "{0:0.0} m", m_Range.value);
                MoonkovLocalization.Set(m_IdleValue, "{0}%", m_Idle.value);
                MoonkovLocalization.Set(m_AimValue, "{0}%", m_Aim.value);
            }
            bool aiming = m_Input.LightPreviewAiming;
            m_IdleControls.EnableInClassList("halo-hidden", aiming);
            m_AimControls.EnableInClassList("halo-hidden", !aiming);
            m_IdleTab.EnableInClassList("halo-selected", !aiming);
            m_AimTab.EnableInClassList("halo-selected", aiming);
            MoonkovLocalization.Set(m_Status, enabled ? "LIGHTS ON" : "LIGHTS OFF");
            m_Visible = true;
        }
        private void PreviewIdle() => m_Input.LightPreviewAiming = false;
        private void PreviewAim() => m_Input.LightPreviewAiming = true;
        private void Reset() => m_Input.LightingSettings = 0;
        private void Store() => m_Input.LightingSettings = HaloLighting.Pack(m_Range.value, m_Idle.value, m_Aim.value, m_AimRange.value);
        private void RangeChanged(ChangeEvent<float> evt) => Store();
        private void IdleChanged(ChangeEvent<int> evt) => Store();
        private void AimChanged(ChangeEvent<int> evt) => Store();
        public void Dispose()
        {
            m_IdleTab.clicked -= PreviewIdle; m_AimTab.clicked -= PreviewAim; m_Reset.clicked -= Reset;
            m_Range.UnregisterValueChangedCallback(RangeChanged);
            m_AimRange.UnregisterValueChangedCallback(RangeChanged);
            m_Idle.UnregisterValueChangedCallback(IdleChanged); m_Aim.UnregisterValueChangedCallback(AimChanged);
            m_Root.RemoveFromHierarchy();
        }
    }
}
