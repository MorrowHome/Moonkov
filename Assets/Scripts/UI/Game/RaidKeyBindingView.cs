// 仓库和暂停设置共用的按键调整列表。进入录入时捕获下一次按键，由 RaidKeyBindings 检查并保存。可以取消录入和恢复默认。
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    internal sealed class RaidKeyBindingView {
        private readonly VisualElement m_Rows;
        private readonly Button[] m_Buttons;
        private readonly Label m_Status = new() { pickingMode = PickingMode.Ignore };
        private InputActionRebindingExtensions.RebindingOperation m_Capture;
        private RaidKey m_Action;
        private bool m_Detaching;

        public RaidKeyBindingView(VisualElement settings) {
            var foldout = settings.Q<Foldout>("keyBindings");
            m_Rows = foldout.Q("keyBindingRows");
            m_Buttons = new Button[RaidKeyBindings.Bindings.Count];
            m_Status.AddToClassList("key-binding-status");
            m_Status.enableRichText = false;
            foreach (var binding in RaidKeyBindings.Bindings) {
                var action = binding.Action;
                var row = new VisualElement();
                row.AddToClassList("key-binding-row");
                var label = new Label { pickingMode = PickingMode.Ignore };
                row.Add(label);
                MoonkovLocalization.Set(label, binding.Label);
                var button = new Button(() => Begin(action)) { name = "keyBinding" + action, enableRichText = false };
                button.AddToClassList("key-binding-button");
                row.Add(button);
                m_Buttons[(int)action] = button;
                m_Rows.Add(row);
            }
            Refresh();
            foldout.Q<Button>("keyBindingReset").clicked += RestoreDefaults;
            foldout.RegisterValueChangedCallback(evt => {
                if (!evt.newValue) Cancel();
            });
            foldout.RegisterCallback<DetachFromPanelEvent>(_ => {
                m_Detaching = true;
                Cancel();
            });
        }

        private void Begin(RaidKey action) {
            // 再次点击同一个待录入的项目会取消。点另一个则先停止前一次的捕获。
            if (m_Capture != null && m_Action == action) {
                Cancel();
                return;
            }
            Cancel();
            m_Action = action;
            var button = m_Buttons[(int)action];
            button.AddToClassList("is-capturing");
            m_Status.RemoveFromHierarchy();
            m_Rows.Insert(m_Rows.IndexOf(button.parent) + 1, m_Status);
            MoonkovLocalization.Set(m_Status, "Press a key. Esc cancels.");
            Application.focusChanged += FocusChanged;
            // 监听下一个键盘键，并在这段时间屏蔽它的原操作。Esc、超时和失焦都有退出路径。
            m_Capture = new InputActionRebindingExtensions.RebindingOperation().WithExpectedControlType<KeyControl>()
                .WithControlsHavingToMatchPath("<Keyboard>/*").WithCancelingThrough("<Keyboard>/escape")
                .WithMatchingEventsBeingSuppressed().WithTimeout(10).OnApplyBinding(Assign).OnComplete(Finish)
                .OnCancel(Finish);
            m_Capture.Start();
        }

        private void Assign(InputActionRebindingExtensions.RebindingOperation operation, string path) {
            var key = ((KeyControl)operation.selectedControl).keyCode;
            // 区分硬保留键和已经被另一动作占用的键。检查失败时仅显示原因，不写回绑定。
            if (!RaidKeyBindings.CanAssign(m_Action, key)) {
                MoonkovLocalization.Set(m_Status, "This key is reserved.");
            } else if (!RaidKeyBindings.TryAssign(m_Action, key, out var conflict)) {
                MoonkovLocalization.Set(m_Status, "{0} is already assigned to {1}.", RaidKeyBindings.DisplayName(key),
                    RaidKeyBindings.ActionLabel(conflict));
            } else {
                Refresh();
                m_Status.RemoveFromHierarchy();
            }
        }

        private void Finish(InputActionRebindingExtensions.RebindingOperation operation) {
            bool canceled = operation.canceled;
            // 完成、取消或超时共用清理。解除焦点监听并释放捕获器，页面已经卸载时不再恢复按钮样式。
            m_Capture = null;
            Application.focusChanged -= FocusChanged;
            operation.Dispose();
            if (m_Detaching) return;
            m_Buttons[(int)m_Action].RemoveFromClassList("is-capturing");
            if (canceled) m_Status.RemoveFromHierarchy();
        }

        private void FocusChanged(bool focused) {
            if (!focused) Cancel();
        }

        private void Cancel() {
            m_Capture?.Cancel();
            if (!m_Detaching) m_Status.RemoveFromHierarchy();
        }

        private void Refresh() {
            foreach (var binding in RaidKeyBindings.Bindings)
                MoonkovLocalization.Set(m_Buttons[(int)binding.Action], "{0}",
                    RaidKeyBindings.DisplayName(binding.Action));
        }

        private void RestoreDefaults() {
            Cancel();
            RaidKeyBindings.RestoreDefaults();
            Refresh();
        }
    }
}