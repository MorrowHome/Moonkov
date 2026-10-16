using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;


namespace Unity.MP_FPS
{
    [DisallowMultipleComponent, RequireComponent(typeof(Collider))]
    public sealed class AirlockButton : MonoBehaviour
    {
        private static readonly List<AirlockButton> s_Active = new List<AirlockButton>();
        [SerializeField] private string m_ButtonId, m_Label;
        [SerializeField] private AirlockController m_Owner;
        [SerializeField] private AirlockCommand m_Command;
        [SerializeField] private TextMesh m_Status;
        [SerializeField] private Renderer m_Indicator;
        private Collider m_Collider;
        private MaterialPropertyBlock m_Block;
        private float m_FeedbackUntil;
        private AirlockFeedback m_LastFeedback;
        public string ButtonId => m_ButtonId;
        public AirlockController Owner => m_Owner;
        public AirlockCommand Command => m_Command;
        public static IReadOnlyList<AirlockButton> Active => s_Active;
        public Vector3 InteractionCenter => m_Collider ? m_Collider.bounds.center : transform.position;
        public string DisplayName => MoonkovLocalization.Text(m_Label);
        public string BankSide => string.IsNullOrEmpty(m_ButtonId) ? "" : m_ButtonId.Substring(0, Mathf.Max(0, m_ButtonId.LastIndexOf('.')));
        public AirlockFeedback DisplayFeedback => m_FeedbackUntil > Time.unscaledTime ? m_LastFeedback : m_Owner ? m_Owner.Check(m_Command) : AirlockFeedback.Unavailable;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void ResetRegistry() => s_Active.Clear();
        private void OnEnable() { m_Collider = GetComponent<Collider>(); if (!s_Active.Contains(this)) s_Active.Add(this); if (m_Owner) m_Owner.StateChanged += Refresh; Refresh(); }
        private void OnDisable() { s_Active.Remove(this); if (m_Owner) m_Owner.StateChanged -= Refresh; }
        public void Configure(string id, string label, AirlockController owner, AirlockCommand command, TextMesh text = null, Renderer indicator = null)
        {
            if (m_Owner) m_Owner.StateChanged -= Refresh;
            m_ButtonId = id; m_Label = label; m_Owner = owner; m_Command = command; m_Status = text; m_Indicator = indicator;
            m_Collider = GetComponent<Collider>(); if (isActiveAndEnabled && m_Owner) m_Owner.StateChanged += Refresh; Refresh();
        }
        public bool InRange(Vector3 actor) => m_Owner && m_Collider && m_Collider.enabled && isActiveAndEnabled &&
            Vector3.Distance(actor + Vector3.up, m_Collider.ClosestPoint(actor + Vector3.up)) <= m_Owner.Settings.InteractionDistanceM;
        public AirlockFeedback Press(Vector3 actor, World world = null)
        {
            AirlockFeedback result;
            if (!InRange(actor)) result = AirlockFeedback.OutOfRange;
            else if (world != null || AirlockNetworkContext.HasNetworkWorlds)
                result = AirlockNetworkContext.Send(world, this) ? AirlockFeedback.Pending : AirlockFeedback.Unavailable;
            else result = m_Owner.RequestAuthoritative(m_Command);
            ReceiveFeedback(result); return result;
        }
        public void ReceiveFeedback(AirlockFeedback result) { m_LastFeedback = result; m_FeedbackUntil = Time.unscaledTime + 1.5f; Refresh(); }
        private void Update() { if (m_FeedbackUntil > 0 && Time.unscaledTime >= m_FeedbackUntil) { m_FeedbackUntil = 0; Refresh(); } }
        private void Refresh()
        {
            var feedback = m_FeedbackUntil > Time.unscaledTime ? m_LastFeedback : m_Owner ? m_Owner.Check(m_Command) : AirlockFeedback.Unavailable;
            if (m_Status && m_Owner)
            {
                var s = m_Owner.State;
                m_Status.text = $"{m_Label}\n{s.PressureKPa:0.0} kPa  /  {(m_Owner.Settings.SupplyOxygenFraction * 100):0}% O2\n" +
                    $"OUT {s.OuterFraction * 100:0}% / IN {s.InnerFraction * 100:0}%\n{(s.Equalizing ? "EQUALIZING" : "HOLD")} / {FeedbackText(feedback)}";
            }
            if (m_Indicator)
            {
                if (m_Block == null) m_Block = new MaterialPropertyBlock();
                m_Indicator.GetPropertyBlock(m_Block); m_Block.SetColor("_BaseColor", feedback == AirlockFeedback.Accepted ? Color.green : Color.red);
                m_Indicator.SetPropertyBlock(m_Block);
            }
        }
        public static string FeedbackText(AirlockFeedback feedback)
        {
            switch (feedback)
            {
                case AirlockFeedback.Accepted: return MoonkovLocalization.Text("READY / ACCEPTED");
                case AirlockFeedback.Pending: return MoonkovLocalization.Text("WAITING FOR SERVER");
                case AirlockFeedback.OtherDoorNotSealed: return MoonkovLocalization.Text("CLOSE THE OTHER DOOR FIRST");
                case AirlockFeedback.PressureMismatch: return MoonkovLocalization.Text("PRESSURE DIFFERENCE TOO HIGH");
                case AirlockFeedback.Equalizing: return MoonkovLocalization.Text("EQUALIZATION IN PROGRESS");
                case AirlockFeedback.DoorsNotSealed: return MoonkovLocalization.Text("CLOSE BOTH DOORS TO EQUALIZE");
                case AirlockFeedback.Obstructed: return MoonkovLocalization.Text("STEP CLEAR OF THE DOOR");
                case AirlockFeedback.OutOfRange: return MoonkovLocalization.Text("MOVE CLOSER TO THE BUTTON");
                case AirlockFeedback.StaleRequest: return MoonkovLocalization.Text("REQUEST EXPIRED / TRY AGAIN");
                default: return MoonkovLocalization.Text("AIRLOCK UNAVAILABLE");
            }
        }
        public static AirlockButton Find(string id)
        {
            AirlockButton match = null;
            foreach (var b in s_Active) if (b && b.isActiveAndEnabled && b.ButtonId == id) { if (match) return null; match = b; }
            return match;
        }
        public static bool TryPressFromCamera(Camera camera, Vector3 actor, World world = null)
        {
            if (AirlockInteractionPrompt.TryPress(camera, actor, world)) return true;
            if (!camera || !UnityEngine.Physics.Raycast(camera.ViewportPointToRay(new Vector3(.5f, .5f)), out var hit, 4, ~0, QueryTriggerInteraction.Ignore)) return false;
            var button = hit.collider.GetComponentInParent<AirlockButton>();
            if (!button) return false;
            button.Press(actor, world); return true;
        }
    }
}
