// 统一选择准星附近的气闸、灯或电梯按钮。程序先选准星指向的一组，玩家可在这一组的按钮间切换。
// 按下时再检查一次可见性和范围，避免沿用上一帧已经失效的选项。
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Moon;
using UnityEngine;

namespace Unity.MP_FPS {
    // A typed adapter keeps the existing scene endpoints and their state owners intact.
    // Add further endpoint kinds here; selection, occlusion and HUD need no duplicate input loop.
    public readonly struct RaidInteractionTarget : IEquatable<RaidInteractionTarget> {
        // 一次选择只填气闸、灯、升降梯中的一种。外层统一读坐标、显示名和按钮 ID。
        public readonly AirlockButton Airlock;
        public readonly LightSwitchButton Light;
        public readonly RaidLiftButton Lift;
        public RaidInteractionTarget(AirlockButton button) : this() { Airlock = button; }
        public RaidInteractionTarget(LightSwitchButton button) : this() { Light = button; }
        public RaidInteractionTarget(RaidLiftButton button) : this() { Lift = button; }
        public bool IsValid => Airlock || Light || Lift;

        public Vector3 Center =>
            Airlock ? Airlock.InteractionCenter : Light ? Light.InteractionCenter : Lift ? Lift.Center : default;

        public string Label => Airlock ? Airlock.DisplayName : Light ? Light.Label : Lift ? Lift.Label : "";
        public string Name => MoonkovLocalization.Text(Label);
        public string Id => Airlock ? Airlock.ButtonId : Light ? Light.SwitchId : Lift ? Lift.ButtonId : "";

        public Transform Surface =>
            Airlock ? Airlock.transform : Light ? Light.transform : Lift ? Lift.transform : null;

        public bool InRange(Vector3 actor) {
            return Airlock ? Airlock.InRange(actor) : Light ? Light.InRange(actor) : Lift && Lift.InRange(actor);
        }

        // 离得近也要属于同一块面板才放在一组。门两边和不同灯组不能因为挨着就互相切换。
        public bool SameGroup(RaidInteractionTarget other) {
            if (!IsValid || !other.IsValid || (Center - other.Center).sqrMagnitude > .75f * .75f) return false;
            if (Lift || other.Lift) return Lift && Lift == other.Lift;
            return Airlock && other.Airlock
                ? Airlock.Owner == other.Airlock.Owner && Airlock.BankSide == other.Airlock.BankSide
                : Light && other.Light && Light.Controls == other.Light.Controls &&
                  Light.transform.parent == other.Light.transform.parent;
        }

        // 目标自己的按钮碰撞体被射中时仍算看得见；先撞到其他场景物体就判为遮挡。
        public bool VisibleFrom(Vector3 origin) {
            var delta = Center - origin;
            if (delta.sqrMagnitude < .0001f) return true;
            if (!UnityEngine.Physics.Raycast(origin, delta.normalized, out var hit, delta.magnitude,
                    UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return Airlock ? hit.collider.GetComponentInParent<AirlockButton>() == Airlock :
                Light ? hit.collider.GetComponentInParent<LightSwitchButton>() == Light :
                Lift && hit.collider.GetComponentInParent<RaidLiftButton>() == Lift;
        }

        public bool Press(Vector3 actor, World world) {
            if (!InRange(actor)) return false;
            if (Airlock) {
                Airlock.Press(actor, world);
                return true;
            }
            if (Lift) return Lift.Press(actor, world);
            return Light && LightSwitchNetworkContext.Press(world, Light);
        }

        public bool Equals(RaidInteractionTarget other) {
            return Airlock == other.Airlock && Light == other.Light && Lift == other.Lift;
        }

        public override bool Equals(object obj) { return obj is RaidInteractionTarget other && Equals(other); }
        public override int GetHashCode() { return Surface ? Surface.GetEntityId().GetHashCode() : 0; }
    }

    public sealed class RaidInteractionSelection {
        private readonly List<RaidInteractionTarget> m_Candidates = new();
        private readonly List<RaidInteractionTarget> m_Choices = new();
        private RaidInteractionTarget m_LastAimFocus;
        public IReadOnlyList<RaidInteractionTarget> Choices => m_Choices;
        public RaidInteractionTarget Selected { get; private set; }

        public void Clear() {
            Selected = m_LastAimFocus = default;
            m_Choices.Clear();
            m_Candidates.Clear();
        }

        public void Refresh(Camera camera, Vector3 actor, bool allowed) {
            if (!allowed || !camera || !camera.isActiveAndEnabled) {
                Clear();
                return;
            }
            m_Candidates.Clear();
            // 候选来自目前开启的场景组件，统一做范围和准星分数筛选。
            foreach (var button in AirlockButton.Active)
                if (button)
                    m_Candidates.Add(new RaidInteractionTarget(button));
            foreach (var button in LightSwitchButton.Active)
                if (button)
                    m_Candidates.Add(new RaidInteractionTarget(button));
            foreach (var button in RaidLiftButton.Active)
                if (button)
                    m_Candidates.Add(new RaidInteractionTarget(button));
            var focus = default(RaidInteractionTarget);
            float best = -1;
            foreach (var target in m_Candidates) {
                if (!target.InRange(actor)) continue;
                float score = RaidInteractionAim.Score(camera, actor, target.Center, 4, target.Surface);
                if (score <= best || !target.VisibleFrom(camera.transform.position)) continue;
                best = score;
                focus = target;
            }
            if (!focus.IsValid) {
                Clear();
                return;
            }
            // 视线还停在同一组时保留玩家手动切到的按钮。真正指到新的表面时再改默认选择。
            bool retain = focus.SameGroup(Selected);
            m_Choices.Clear();
            foreach (var target in m_Candidates)
                if (focus.SameGroup(target) && target.InRange(actor) && target.VisibleFrom(camera.transform.position) &&
                    camera.WorldToViewportPoint(target.Center).z > 0)
                    m_Choices.Add(target);
            // 同一面板的选项从上到下排序。同高时按横坐标和 ID 继续排，滚动顺序更稳定。
            m_Choices.Sort(Compare);
            // A new direct surface wins. Explicit cycling survives a stationary aim.
            if ((best > 900 && !focus.Equals(m_LastAimFocus)) || !retain || !m_Choices.Contains(Selected))
                Selected = focus;
            m_LastAimFocus = focus;
        }

        private static int Compare(RaidInteractionTarget a, RaidInteractionTarget b) {
            int order = b.Center.y.CompareTo(a.Center.y);
            if (order == 0) order = a.Center.x.CompareTo(b.Center.x);
            return order == 0 ? string.CompareOrdinal(a.Id, b.Id) : order;
        }

        // 在同组可见按钮中循环，翻过末尾回第一项，反向也一样。
        public void Cycle(int direction) {
            if (m_Choices.Count == 0) return;
            int index = m_Choices.IndexOf(Selected);
            Selected = m_Choices[((index + direction) % m_Choices.Count + m_Choices.Count) % m_Choices.Count];
        }

        public bool Press(Camera camera, Vector3 actor, World world) {
            // Re-evaluate on the input frame: cached UI must never operate a disabled,
            // occluded or out-of-range target after a teleport/menu transition.
            Refresh(camera, actor, true);
            // 真正执行前再按这一帧的位置和视线刷新。被挡住或离开范围后，上一帧提示不会继续触发按钮。
            return Selected.IsValid && Selected.VisibleFrom(camera.transform.position) && Selected.Press(actor, world);
        }
    }
}