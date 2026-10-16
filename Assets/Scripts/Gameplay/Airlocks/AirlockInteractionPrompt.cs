using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed class AirlockInteractionPrompt : MonoBehaviour
    {
        private static readonly List<AirlockInteractionPrompt> s_Active = new List<AirlockInteractionPrompt>();
        private readonly List<AirlockButton> m_Group = new List<AirlockButton>();
        private readonly List<Label> m_Rows = new List<Label>();
        private VisualElement m_Root, m_List, m_Marker;
        private Label m_Action, m_Pressure, m_Feedback, m_Hint;
        private Camera m_Camera;
        private Transform m_Actor;
        private AirlockButton m_Selected;
        private int m_LastFrame = -1;
        public AirlockButton Selected => m_Selected;
        public int ChoiceCount => m_Group.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => s_Active.Clear();
        private void OnEnable() { if (!s_Active.Contains(this)) s_Active.Add(this); }
        private void OnDisable() { s_Active.Remove(this); Hide(); }
        public void Initialize(UIDocument document, Camera camera = null, Transform actor = null)
        {
            m_Camera = camera; m_Actor = actor;
            m_Root?.RemoveFromHierarchy();
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/AirlockPrompt");
            tree.CloneTree(document.rootVisualElement);
            m_Root = document.rootVisualElement.Q("airlockPrompt"); m_Root.pickingMode = PickingMode.Ignore;
            foreach (var e in m_Root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            m_List = m_Root.Q("airlockActions"); m_Marker = m_Root.Q("airlockMarker");
            m_Action = m_Root.Q<Label>("airlockAction"); m_Pressure = m_Root.Q<Label>("airlockPressure");
            m_Feedback = m_Root.Q<Label>("airlockFeedback"); m_Hint = m_Root.Q<Label>("airlockHint");
        }
        private void LateUpdate()
        { if (m_Actor) RefreshSelection(m_Camera, m_Actor.position, UnityEngine.Cursor.lockState == CursorLockMode.Locked); }
        private void Hide() { m_Selected = null; m_Group.Clear(); m_Root?.RemoveFromClassList("is-visible"); }
        private static bool SameBank(AirlockButton a, AirlockButton b) => a && b && a.Owner == b.Owner &&
            a.BankSide == b.BankSide && Vector3.Distance(a.InteractionCenter, b.InteractionCenter) < .75f;
        private static bool Visible(Camera camera, AirlockButton b)
        {
            var delta = b.InteractionCenter - camera.transform.position;
            if (delta.sqrMagnitude < .0001f) return true;
            return !UnityEngine.Physics.Raycast(camera.transform.position, delta.normalized, out var hit, delta.magnitude + .05f,
                ~0, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<AirlockButton>() == b || hit.distance >= delta.magnitude - .08f;
        }
        public void RefreshSelection(Camera camera, Vector3 actor, bool allowed)
        {
            if (m_Root == null || !camera || !allowed) { Hide(); return; }
            if (m_LastFrame == Time.frameCount) return;
            m_LastFrame = Time.frameCount; m_Camera = camera;
            AirlockButton focus = null; float best = .12f;
            foreach (var b in AirlockButton.Active)
            {
                if (!b || !b.InRange(actor)) continue;
                var point = camera.WorldToViewportPoint(b.InteractionCenter);
                if (point.z <= 0) continue;
                float distance = new Vector2(point.x - .5f, point.y - .5f).magnitude;
                if (distance < best && Visible(camera, b)) { best = distance; focus = b; }
            }
            if (!focus) { Hide(); return; }
            bool retain = SameBank(m_Selected, focus);
            m_Group.Clear();
            foreach (var b in AirlockButton.Active) if (SameBank(focus, b) && b.InRange(actor) && Visible(camera, b)) m_Group.Add(b);
            m_Group.Sort((a, b) => b.InteractionCenter.y.CompareTo(a.InteractionCenter.y));
            if (!retain || !m_Group.Contains(m_Selected)) m_Selected = focus;
            float wheel = Mouse.current == null ? 0 : Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > .01f && m_Group.Count > 1) Cycle(wheel > 0 ? -1 : 1);
            Render();
        }
        public void Cycle(int direction)
        {
            if (m_Group.Count == 0) return;
            int index = m_Group.IndexOf(m_Selected);
            m_Selected = m_Group[(index + direction + m_Group.Count) % m_Group.Count];
            Render();
        }
        private void Render()
        {
            if (!m_Selected || m_Root == null) return;
            m_Root.AddToClassList("is-visible");
            m_Action.text = m_Selected.DisplayName;
            var state = m_Selected.Owner.State;
            m_Pressure.text = $"{state.PressureKPa:0.0} kPa  /  OUT {state.OuterFraction * 100:0}%  /  IN {state.InnerFraction * 100:0}%";
            if (m_Rows.Count != m_Group.Count) { m_List.Clear(); m_Rows.Clear(); foreach (var b in m_Group) { var row = new Label(); row.AddToClassList("airlock-row"); row.pickingMode = PickingMode.Ignore; m_List.Add(row); m_Rows.Add(row); } }
            for (int i = 0; i < m_Group.Count; i++) { m_Rows[i].text = m_Group[i].DisplayName; m_Rows[i].EnableInClassList("is-selected", m_Group[i] == m_Selected); }
            var feedback = m_Selected.DisplayFeedback;
            m_Feedback.text = AirlockButton.FeedbackText(feedback);
            m_Feedback.EnableInClassList("is-blocked", feedback != AirlockFeedback.Accepted);
            m_Hint.text = m_Group.Count > 1 ? $"SCROLL TO SELECT  /  {m_Group.IndexOf(m_Selected) + 1} OF {m_Group.Count}  /  F TO OPERATE" : "F TO OPERATE";
            var viewport = m_Camera.WorldToViewportPoint(m_Selected.InteractionCenter);
            m_Marker.style.left = viewport.x * m_Root.resolvedStyle.width - 21;
            m_Marker.style.top = (1 - viewport.y) * m_Root.resolvedStyle.height - 16;
        }
        public static bool TryPress(Camera camera, Vector3 actor, World world)
        {
            foreach (var prompt in s_Active)
            {
                if (!prompt || prompt.m_Camera != camera || prompt.m_Root == null) continue;
                prompt.RefreshSelection(camera, actor, true);
                if (prompt.m_Selected) { prompt.m_Selected.Press(actor, world); prompt.Render(); return true; }
            }
            return false;
        }
    }
}
