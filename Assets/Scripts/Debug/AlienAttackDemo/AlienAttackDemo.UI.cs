using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class AlienAttackDemo
    {
        private VisualElement m_UI, m_Viewport, m_AttackButtons;
        private Label m_Title, m_Status, m_Result, m_Count;
        private Button m_PauseButton;
        private Toggle m_EnragedToggle;
        private readonly List<Button> m_ProfileButtons = new();
        private int m_ViewMode;
        private float m_Orbit = 35, m_Pitch = 23, m_Distance = 15;
        private Vector3 m_Focus;
        private string m_LastStatus, m_LastResult;
        private RenderTexture m_ViewTexture;

        private void BindControls()
        {
            m_UI = m_Document.rootVisualElement;
            m_Viewport = m_UI.Q("viewport"); m_AttackButtons = m_UI.Q("attacks");
            m_Title = m_UI.Q<Label>("creatureName"); m_Count = m_UI.Q<Label>("count");
            m_Status = m_UI.Q<Label>("status"); m_Result = m_UI.Q<Label>("result");
            var roster = m_UI.Q<ScrollView>("roster");
            for (int i = 0; i < m_Profiles.Length; i++)
            {
                int profile = i;
                var button = new Button(() => Select(m_Attacks.FindIndex(x => x.ProfileIndex == profile))) { text = m_Profiles[i].CreatureName };
                button.AddToClassList("creature"); roster.Add(button); m_ProfileButtons.Add(button);
            }
            m_PauseButton = m_UI.Q<Button>("pause"); m_PauseButton.clicked += () => SetPaused(!Paused);
            m_UI.Q<Button>("replay").clicked += () => Select(m_Selected);
            m_UI.Q<Button>("next").clicked += () => Select(m_Selected + 1);
            m_UI.Q<Toggle>("cycleAll").RegisterValueChangedCallback(e => CycleAll = e.newValue);
            m_UI.Q<Toggle>("dodge").RegisterValueChangedCallback(e => SetDodge(e.newValue));
            m_UI.Q<Toggle>("liquid").RegisterValueChangedCallback(e => SetLiquid(e.newValue));
            m_UI.Q<Toggle>("liquid").SetEnabled(SystemInfo.supportsComputeShaders);
            m_EnragedToggle = m_UI.Q<Toggle>("enraged"); m_EnragedToggle.RegisterValueChangedCallback(e => SetEnraged(e.newValue));
            for (int i = 0; i < 3; i++) { int view = i; m_UI.Q<Button>("view" + i).clicked += () => SetView(view); }
            m_Viewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateViewport());
        }

        private void UpdateSelection()
        {
            m_Title.text = Profile.CreatureName;
            m_Count.text = $"{m_Selected + 1:00} / {m_Attacks.Count:00}";
            for (int i = 0; i < m_ProfileButtons.Count; i++) m_ProfileButtons[i].EnableInClassList("selected", i == Entry.ProfileIndex);
            m_AttackButtons.Clear();
            for (int i = 0; i < m_Attacks.Count; i++)
            {
                if (m_Attacks[i].ProfileIndex != Entry.ProfileIndex) continue;
                int selected = i;
                var button = new Button(() => Select(selected)) { text = m_Attacks[i].Name };
                button.EnableInClassList("selected", i == m_Selected); m_AttackButtons.Add(button);
            }
            m_EnragedToggle.SetEnabled(Profile.Boss); UpdatePauseButton(); RefreshStatus();
            m_UI.Q<Toggle>("cycleAll").SetValueWithoutNotify(CycleAll);
            m_UI.Q<Toggle>("dodge").SetValueWithoutNotify(Dodge);
            m_UI.Q<Toggle>("liquid").SetValueWithoutNotify(Liquid);
            m_EnragedToggle.SetValueWithoutNotify(Enraged);
        }

        private void UpdatePauseButton() { if (m_PauseButton != null) m_PauseButton.text = Paused ? "继续" : "暂停"; }

        private void RefreshStatus()
        {
            string state = !m_Started ? "准备" : m_Finished ? "演示结束" : Snapshot.Action switch
            { AlienAction.Windup => "蓄力", AlienAction.Strike => "攻击", AlienAction.Recover => "收招", AlienAction.Attached => "覆面附着", _ => "移动" };
            string status = Paused ? "已暂停" : state;
            string result = $"命中 {Hits} 次    伤害 {Damage:0.#}";
            if (status != m_LastStatus) { m_Status.text = status; m_LastStatus = status; }
            if (result != m_LastResult) { m_Result.text = result; m_LastResult = result; }
        }

        public void SetView(int mode)
        {
            m_ViewMode = Mathf.Clamp(mode, 0, 2);
            m_Focus = new Vector3(0, Profile.IsUpright ? 1.8f : 1.1f, Entry.Distance * .46f);
            if (Profile.IsCeilingSpider) m_Focus.y = 1.8f;
            m_Distance = Mathf.Max(Profile.Boss ? 17 : 9, Entry.Distance * 1.55f + 5);
            m_Orbit = mode == 1 ? 90 : 35; m_Pitch = mode == 1 ? 12 : 23;
            if (mode == 2)
            {
                m_Camera.transform.position = new Vector3(0, 1.65f, Entry.Distance + 1.2f);
                m_Camera.transform.LookAt(new Vector3(0, Profile.IsCeilingSpider ? 3.0f : Profile.IsUpright ? 2.4f : Profile.Scale * .6f, 0));
            }
            else ApplyOrbit();
            for (int i = 0; i < 3; i++) m_UI?.Q<Button>("view" + i).EnableInClassList("selected", mode == i);
            UpdateViewport();
        }

        private void ApplyOrbit()
        {
            var rotation = Quaternion.Euler(m_Pitch, m_Orbit + 180, 0);
            m_Camera.transform.position = m_Focus - rotation * Vector3.forward * m_Distance;
            m_Camera.transform.LookAt(m_Focus);
        }

        private void UpdateViewport()
        {
            if (m_Viewport == null || m_UI.worldBound.width <= 0 || m_UI.worldBound.height <= 0) return;
            var bounds = m_Viewport.worldBound;
            int width = Mathf.Clamp(Mathf.RoundToInt(bounds.width * Screen.width / m_UI.worldBound.width), 64, 2048);
            int height = Mathf.Clamp(Mathf.RoundToInt(bounds.height * Screen.height / m_UI.worldBound.height), 64, 1440);
            if (m_ViewTexture && m_ViewTexture.width == width && m_ViewTexture.height == height) return;
            if (m_ViewTexture) { m_Camera.targetTexture = null; m_ViewTexture.Release(); Destroy(m_ViewTexture); }
            m_ViewTexture = new RenderTexture(width, height, 24) { name = "Attack demonstration view" };
            m_ViewTexture.Create(); m_Camera.targetTexture = m_ViewTexture; m_Camera.aspect = (float)width / height;
            var image = m_UI.Q<UnityEngine.UIElements.Image>("stageImage"); image.image = m_ViewTexture; image.scaleMode = ScaleMode.StretchToFill;
        }

        private void ReadInput()
        {
            if (m_UI?.panel == null || m_Viewport == null) return;
            var keyboard = Keyboard.current;
            // Native UI navigation owns Space/Return; use unambiguous lab shortcuts.
            if (keyboard != null)
            {
                if (keyboard.pKey.wasPressedThisFrame) SetPaused(!Paused);
                if (keyboard.rKey.wasPressedThisFrame) Select(m_Selected);
                if (keyboard.nKey.wasPressedThisFrame) Select(m_Selected + 1);
                if (keyboard.digit1Key.wasPressedThisFrame) SetView(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SetView(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SetView(2);
            }
            var mouse = Mouse.current;
            if (mouse == null || m_ViewMode == 2) return;
            Vector2 cursor = mouse.position.ReadValue(); cursor.y = Screen.height - cursor.y;
            var panelPoint = RuntimePanelUtils.ScreenToPanel(m_UI.panel, cursor);
            if (!m_Viewport.worldBound.Contains(panelPoint)) return;
            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue(); m_Orbit += delta.x * .15f; m_Pitch = Mathf.Clamp(m_Pitch - delta.y * .15f, 4, 75);
                ApplyOrbit();
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > .01f) { m_Distance = Mathf.Clamp(m_Distance - scroll * .012f, 3, 45); ApplyOrbit(); }
        }
    }
}
