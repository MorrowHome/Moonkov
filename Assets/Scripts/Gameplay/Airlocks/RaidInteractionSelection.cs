using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Moon;
using UnityEngine;

namespace Unity.MP_FPS
{
    // A typed adapter keeps the existing scene endpoints and their state owners intact.
    // Add further endpoint kinds here; selection, occlusion and HUD need no duplicate input loop.
    public readonly struct RaidInteractionTarget : IEquatable<RaidInteractionTarget>
    {
        public readonly AirlockButton Airlock;
        public readonly LightSwitchButton Light;
        public RaidInteractionTarget(AirlockButton button) { Airlock = button; Light = null; }
        public RaidInteractionTarget(LightSwitchButton button) { Light = button; Airlock = null; }
        public bool IsValid => Airlock || Light;
        public Vector3 Center => Airlock ? Airlock.InteractionCenter : Light ? Light.InteractionCenter : default;
        public string Name => Airlock ? Airlock.DisplayName : Light ? MoonkovLocalization.Text(Light.Label) : "";
        public string Id => Airlock ? Airlock.ButtonId : Light ? Light.SwitchId : "";
        public bool InRange(Vector3 actor) => Airlock ? Airlock.InRange(actor) : Light && Light.InRange(actor);
        public bool SameGroup(RaidInteractionTarget other)
        {
            if (!IsValid || !other.IsValid || (Center - other.Center).sqrMagnitude > .75f * .75f) return false;
            return Airlock && other.Airlock ? Airlock.Owner == other.Airlock.Owner && Airlock.BankSide == other.Airlock.BankSide
                : Light && other.Light && Light.Controls == other.Light.Controls && Light.transform.parent == other.Light.transform.parent;
        }
        public bool VisibleFrom(Vector3 origin)
        {
            var delta = Center - origin;
            if (delta.sqrMagnitude < .0001f) return true;
            if (!UnityEngine.Physics.Raycast(origin, delta.normalized, out var hit, delta.magnitude, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            return Airlock ? hit.collider.GetComponentInParent<AirlockButton>() == Airlock
                : Light && hit.collider.GetComponentInParent<LightSwitchButton>() == Light;
        }
        public bool Press(Vector3 actor, World world)
        {
            if (!InRange(actor)) return false;
            if (Airlock) { Airlock.Press(actor, world); return true; }
            return Light && LightSwitchNetworkContext.Press(world, Light);
        }
        public bool Equals(RaidInteractionTarget other) => Airlock == other.Airlock && Light == other.Light;
        public override bool Equals(object obj) => obj is RaidInteractionTarget other && Equals(other);
        public override int GetHashCode() => Airlock ? Airlock.GetEntityId().GetHashCode() : Light ? Light.GetEntityId().GetHashCode() : 0;
    }

    public sealed class RaidInteractionSelection
    {
        private readonly List<RaidInteractionTarget> m_Candidates = new List<RaidInteractionTarget>();
        private readonly List<RaidInteractionTarget> m_Choices = new List<RaidInteractionTarget>();
        public IReadOnlyList<RaidInteractionTarget> Choices => m_Choices;
        public RaidInteractionTarget Selected { get; private set; }
        public void Clear() { Selected = default; m_Choices.Clear(); m_Candidates.Clear(); }

        public void Refresh(Camera camera, Vector3 actor, bool allowed)
        {
            if (!allowed || !camera || !camera.isActiveAndEnabled) { Clear(); return; }
            m_Candidates.Clear();
            foreach (var button in AirlockButton.Active) if (button) m_Candidates.Add(new RaidInteractionTarget(button));
            foreach (var button in LightSwitchButton.Active) if (button) m_Candidates.Add(new RaidInteractionTarget(button));
            var focus = default(RaidInteractionTarget);
            float best = .12f;
            foreach (var target in m_Candidates)
            {
                if (!target.InRange(actor)) continue;
                var point = camera.WorldToViewportPoint(target.Center);
                if (point.z <= 0) continue;
                float distance = new Vector2(point.x - .5f, point.y - .5f).magnitude;
                if (distance >= best || !target.VisibleFrom(camera.transform.position)) continue;
                best = distance; focus = target;
            }
            if (!focus.IsValid) { Clear(); return; }
            bool retain = focus.SameGroup(Selected);
            m_Choices.Clear();
            foreach (var target in m_Candidates)
                if (focus.SameGroup(target) && target.InRange(actor) && target.VisibleFrom(camera.transform.position) &&
                    camera.WorldToViewportPoint(target.Center).z > 0) m_Choices.Add(target);
            m_Choices.Sort(Compare);
            if (!retain || !m_Choices.Contains(Selected)) Selected = focus;
        }
        private static int Compare(RaidInteractionTarget a, RaidInteractionTarget b)
        {
            int order = b.Center.y.CompareTo(a.Center.y);
            if (order == 0) order = a.Center.x.CompareTo(b.Center.x);
            return order == 0 ? string.CompareOrdinal(a.Id, b.Id) : order;
        }
        public void Cycle(int direction)
        {
            if (m_Choices.Count == 0) return;
            int index = m_Choices.IndexOf(Selected);
            Selected = m_Choices[((index + direction) % m_Choices.Count + m_Choices.Count) % m_Choices.Count];
        }
        public bool Press(Camera camera, Vector3 actor, World world)
        {
            // Re-evaluate on the input frame: cached UI must never operate a disabled,
            // occluded or out-of-range target after a teleport/menu transition.
            Refresh(camera, actor, true);
            return Selected.IsValid && Selected.VisibleFrom(camera.transform.position) && Selected.Press(actor, world);
        }
    }
}
