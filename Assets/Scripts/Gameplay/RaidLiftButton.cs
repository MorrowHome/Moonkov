using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class RaidLiftButton : MonoBehaviour
    {
        private static readonly List<RaidLiftButton> s_Active = new();
        [SerializeField] private RaidLift m_Lift;
        [SerializeField] private string m_ButtonId;
        [SerializeField] private int m_Stop;
        private BoxCollider m_Box;
        private AirlockFeedback m_Feedback;
        private float m_FeedbackUntil;

        public RaidLift Owner => m_Lift;
        public string ButtonId => m_ButtonId;
        public int Stop => m_Stop;
        public string Label => m_Stop < 0 ? "SELECT OTHER FLOOR" : "CALL ELEVATOR";
        public Vector3 Center => m_Box ? m_Box.bounds.center : transform.position;
        public AirlockFeedback Feedback => Time.unscaledTime < m_FeedbackUntil ? m_Feedback : AirlockFeedback.Accepted;
        public static IReadOnlyList<RaidLiftButton> Active => s_Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => s_Active.Clear();
        private void OnEnable() { m_Box = GetComponent<BoxCollider>(); if (!s_Active.Contains(this)) s_Active.Add(this); }
        private void OnDisable() => s_Active.Remove(this);

        public void Configure(RaidLift owner, string id, int stop)
        {
            m_Lift = owner;
            m_ButtonId = id;
            m_Stop = stop;
            m_Box = GetComponent<BoxCollider>();
        }

        public bool InRange(Vector3 actor)
        {
            var eye = actor + Vector3.up;
            return isActiveAndEnabled && m_Lift && m_Lift.Ready && m_Box && m_Box.enabled &&
                (m_Box.ClosestPoint(eye) - eye).sqrMagnitude <= 9;
        }

        public bool Press(Vector3 actor, World world)
        {
            if (!InRange(actor)) return false;
            ReceiveFeedback(world != null || AirlockNetworkContext.HasNetworkWorlds
                ? AirlockNetworkContext.Send(world, m_Lift.LiftId, m_ButtonId) ? AirlockFeedback.Pending : AirlockFeedback.Unavailable
                : m_Lift.Request(m_Stop));
            return true;
        }

        public void ReceiveFeedback(AirlockFeedback feedback)
        {
            m_Feedback = feedback;
            m_FeedbackUntil = Time.unscaledTime + 1.5f;
            if (feedback == AirlockFeedback.Accepted) MoonkovAudio.Confirm();
            else if (feedback != AirlockFeedback.Pending && feedback != AirlockFeedback.Equalizing) MoonkovAudio.Error();
        }

        public static RaidLiftButton Find(string id)
        {
            RaidLiftButton match = null;
            foreach (var button in s_Active)
                if (button && button.ButtonId == id) { if (match) return null; match = button; }
            return match;
        }
    }
}
