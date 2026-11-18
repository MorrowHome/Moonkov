// 显示准星当前能用的气闸、灯或升降梯按钮，并呈现切换与操作提示。
// RaidInteractionSelection 负责挑选，按键的一方也使用同一份选择结果。
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Moon;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    // Retain component/resource names so existing prototype and raid wiring stays valid.
    public sealed class AirlockInteractionPrompt : MonoBehaviour {
        private static readonly List<AirlockInteractionPrompt> s_Active = new();
        private readonly RaidInteractionSelection m_Selection = new();
        private readonly List<Label> m_Rows = new();
        private VisualElement m_Root, m_List, m_Marker;
        private Label m_Action, m_Pressure, m_Feedback, m_Hint, m_Caption, m_Key, m_Range;
        private Camera m_Camera;
        private Transform m_Actor;
        private World m_World;
        private Vector3 m_ActorPosition;
        private int m_LastInputFrame = -1;
        private float m_NextRender;
        private bool m_Allowed;
        public AirlockButton Selected => m_Selection.Selected.Airlock;
        public bool HasSelection => m_Selection.Selected.IsValid;
        public int ChoiceCount => m_Selection.Choices.Count;

        public float FocusScore(Camera camera, Vector3 actor) {
            return HasSelection
                ? RaidInteractionAim.Score(camera, actor, m_Selection.Selected.Center, 4, m_Selection.Selected.Surface)
                : -1;
        }

        public void SetPresentation(bool visible) { m_Root?.EnableInClassList("is-visible", visible && HasSelection); }
        public string BindingLabel { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { s_Active.Clear(); }

        private void OnEnable() {
            if (!s_Active.Contains(this)) s_Active.Add(this);
        }

        private void OnDisable() {
            s_Active.Remove(this);
            Hide();
        }

        private void OnDestroy() { m_Root?.RemoveFromHierarchy(); }

        public void Initialize(UIDocument document, Camera camera = null, Transform actor = null) {
            m_Camera = camera;
            m_Actor = actor;
            m_Root?.RemoveFromHierarchy();
            m_Rows.Clear();
            // 提示叠在原 HUD 上，子元素都忽略鼠标事件。滚轮和按键由交互输入处理，提示层本身不截走鼠标。
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/AirlockPrompt");
            if (!tree || !document) return;
            tree.CloneTree(document.rootVisualElement);
            m_Root = document.rootVisualElement.Q("airlockPrompt");
            m_Root.pickingMode = PickingMode.Ignore;
            foreach (var e in m_Root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            m_List = m_Root.Q("airlockActions");
            m_Marker = m_Root.Q("airlockMarker");
            m_Action = m_Root.Q<Label>("airlockAction");
            m_Pressure = m_Root.Q<Label>("airlockPressure");
            m_Feedback = m_Root.Q<Label>("airlockFeedback");
            m_Hint = m_Root.Q<Label>("airlockHint");
            m_Caption = m_Root.Q<Label>("interactionCaption");
            m_Key = m_Root.Q<Label>("interactionKey");
            m_Range = m_Root.Q<Label>("interactionRange");
            MoonkovLocalization.Bind(m_Root);
            Hide();
        }

        private void LateUpdate() {
            if (m_Actor)
                RefreshSelection(m_Camera, m_Actor.position,
                    Time.timeScale > 0 && UnityEngine.Cursor.lockState == CursorLockMode.Locked);
        }

        private void Hide() {
            m_Allowed = false;
            m_Selection.Clear();
            m_Root?.RemoveFromClassList("is-visible");
        }

        public void RefreshSelection(Camera camera, Vector3 actor, bool allowed, World world = null) {
            // 暂停、没镜头或 HUD 不允许交互时收起提示并清掉旧选项，免得恢复时用到过期目标。
            if (m_Root == null || !camera || !allowed || Time.timeScale <= 0) {
                Hide();
                return;
            }
            m_Allowed = true;
            m_Camera = camera;
            m_ActorPosition = actor;
            m_World = world;
            var previous = m_Selection.Selected;
            m_Selection.Refresh(camera, actor, true);
            if (!HasSelection) {
                m_Root.RemoveFromClassList("is-visible");
                return;
            }
            bool cycled = false;
            // 同一画面更新里可能重复刷新候选。滚轮或方向键只消费一次，避免一下跳两项。
            if (m_LastInputFrame != Time.frameCount) {
                m_LastInputFrame = Time.frameCount;
                float wheel = Mouse.current == null ? 0 : Mouse.current.scroll.ReadValue().y;
                int direction = wheel > .01f ? -1 : wheel < -.01f ? 1 : 0;
                if (Gamepad.current != null) {
                    if (Gamepad.current.dpad.up.wasPressedThisFrame)
                        direction = -1;
                    else if (Gamepad.current.dpad.down.wasPressedThisFrame) direction = 1;
                }
                if (direction != 0) {
                    m_Selection.Cycle(direction);
                    cycled = true;
                }
            }
            m_Root.AddToClassList("is-visible");
            // 取世界按钮在屏幕上的位置，只更新标记坐标；较贵的文案更新按目标变化或短间隔触发。
            var viewport = camera.WorldToViewportPoint(m_Selection.Selected.Center);
            if (m_Root.resolvedStyle.width > 0 && m_Root.resolvedStyle.height > 0) {
                m_Marker.style.left = viewport.x * m_Root.resolvedStyle.width - 21;
                m_Marker.style.top = (1 - viewport.y) * m_Root.resolvedStyle.height - 16;
            }
            if (!previous.Equals(m_Selection.Selected) || cycled || Time.unscaledTime >= m_NextRender) Render();
        }

        public void Cycle(int direction) {
            m_Selection.Cycle(direction);
            Render();
        }

        private void Render() {
            if (!HasSelection || m_Root == null) return;
            m_NextRender = Time.unscaledTime + .1f;
            var selected = m_Selection.Selected;
            MoonkovLocalization.Set(m_Action, selected.Label);
            MoonkovLocalization.Set(m_Caption,
                selected.Airlock ? "AIRLOCK CONTROL" : selected.Lift ? "ELEVATOR CONTROL" : "LIGHTING CONTROL");
            bool gamepad = Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame &&
                           (Keyboard.current == null || !Keyboard.current.anyKey.isPressed);
            MoonkovLocalization.Set(m_Key, BindingLabel ?? (gamepad ? "X" : "F"));
            MoonkovLocalization.Set(m_Range, "DISTANCE {0:0.0} m",
                Vector3.Distance(m_ActorPosition + Vector3.up, selected.Center));
            bool blocked;
            // 同一模板给三类设备使用。气闸显示压差和门开度，升降梯显示楼层，灯组显示当前开关状态。
            if (selected.Airlock) {
                var state = selected.Airlock.Owner.State;
                MoonkovLocalization.Set(m_Pressure, "{0:0.0} kPa / OUT {1:0}% / IN {2:0}%", state.PressureKPa,
                    state.OuterFraction * 100, state.InnerFraction * 100);
                var feedback = selected.Airlock.DisplayFeedback;
                MoonkovLocalization.Set(m_Feedback, AirlockButton.FeedbackText(feedback));
                blocked = feedback != AirlockFeedback.Accepted;
            } else if (selected.Lift) {
                var state = selected.Lift.Owner.State;
                MoonkovLocalization.Set(m_Pressure,
                    state.Phase == RaidLiftPhase.Docked
                        ? state.Dock == 0 ? "LOWER FLOOR" : "UPPER FLOOR"
                        : "ELEVATOR MOVING");
                var feedback = selected.Lift.Feedback;
                blocked = feedback != AirlockFeedback.Accepted && feedback != AirlockFeedback.Equalizing;
                MoonkovLocalization.Set(m_Feedback,
                    feedback == AirlockFeedback.Accepted || feedback == AirlockFeedback.Equalizing ? "" :
                    feedback == AirlockFeedback.Unavailable ? "ELEVATOR UNAVAILABLE" :
                    AirlockButton.FeedbackText(feedback));
            } else {
                var light = selected.Light;
                MoonkovLocalization.Set(m_Pressure,
                    light.State == LightGroupState.AllOn ? "LIGHTS ON" :
                    light.State == LightGroupState.AllOff ? "LIGHTS OFF" :
                    light.State == LightGroupState.Mixed ? "LIGHTS MIXED" : "LIGHTS UNAVAILABLE");
                var feedback = LightSwitchNetworkContext.Feedback(m_World, light);
                blocked = light.State == LightGroupState.Unavailable || feedback == LightSwitchFeedback.Unavailable ||
                          feedback == LightSwitchFeedback.Pending || feedback == LightSwitchFeedback.OutOfRange ||
                          feedback == LightSwitchFeedback.Obstructed || feedback == LightSwitchFeedback.Expired;
                MoonkovLocalization.Set(m_Feedback,
                    feedback == LightSwitchFeedback.Pending ? "WAITING FOR SERVER" :
                    feedback == LightSwitchFeedback.Accepted ? "LIGHT COMMAND ACCEPTED" :
                    feedback == LightSwitchFeedback.Expired ? "REQUEST EXPIRED / TRY AGAIN" :
                    feedback == LightSwitchFeedback.Obstructed ? "CONTROL BLOCKED BY OBSTACLE" :
                    feedback == LightSwitchFeedback.OutOfRange ? "MOVE CLOSER TO THE BUTTON" :
                    blocked ? "LIGHTS UNAVAILABLE" :
                    light.Command == LightCommand.SetOn ? "SWITCH LIGHTS ON" :
                    light.Command == LightCommand.SetOff ? "SWITCH LIGHTS OFF" : "TOGGLE LIGHTS");
            }
            m_Feedback.EnableInClassList("is-blocked", blocked);
            m_Marker.EnableInClassList("is-blocked", blocked);
            int count = m_Selection.Choices.Count;
            // 按钮数量没变就沿用现有行，只改选择和文案。视线移动时少销毁重建 UI 元素。
            if (m_Rows.Count != count) {
                m_List.Clear();
                m_Rows.Clear();
                for (int i = 0; i < count; i++) {
                    var row = new Label();
                    row.AddToClassList("airlock-row");
                    row.pickingMode = PickingMode.Ignore;
                    m_List.Add(row);
                    m_Rows.Add(row);
                }
            }
            int index = 0;
            for (int i = 0; i < count; i++) {
                var target = m_Selection.Choices[i];
                MoonkovLocalization.Set(m_Rows[i], target.Label);
                bool active = target.Equals(selected);
                if (active) index = i;
                m_Rows[i].EnableInClassList("is-selected", active);
            }
            m_Hint.text = count > 1 ? $"滚轮选择  {index + 1}/{count}" : "";
        }

        public bool TryInteract(Camera camera, Vector3 actor, World world) {
            if (!m_Allowed || !isActiveAndEnabled || camera != m_Camera || Time.timeScale <= 0 ||
                UnityEngine.Cursor.lockState != CursorLockMode.Locked)
                return false;
            m_World = world;
            // 焦点设备会优先吃掉交互键，即使它暂时不可用。按下一个没解锁的门旁边有物资时，不会顺手打开物资。
            bool pressed = m_Selection.Press(camera, actor, world);
            if (!HasSelection)
                Hide();
            else
                Render();
            // A focused unavailable control still owns this press; don't open nearby loot with X.
            return pressed || HasSelection;
        }

        public static bool TryPress(Camera camera, Vector3 actor, World world) {
            foreach (var prompt in s_Active)
                if (prompt && prompt.m_Camera == camera && prompt.m_Root != null &&
                    prompt.TryInteract(camera, actor, world))
                    return true;
            return false;
        }
    }
}