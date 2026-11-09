using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    internal sealed class RaidKeyBindingView
    {
        private readonly VisualElement m_Rows;
        private readonly Button[] m_Buttons;
        private readonly Label m_Status = new Label { pickingMode = PickingMode.Ignore };
        private InputActionRebindingExtensions.RebindingOperation m_Capture;
        private RaidKey m_Action;
        private bool m_Detaching;

        public RaidKeyBindingView(VisualElement settings)
        {
            var foldout = settings.Q<Foldout>("keyBindings");
            m_Rows = foldout.Q("keyBindingRows");
            m_Buttons = new Button[RaidKeyBindings.Bindings.Count];
            m_Status.AddToClassList("key-binding-status");
            m_Status.enableRichText = false;
            foreach (var binding in RaidKeyBindings.Bindings)
            {
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
            foldout.RegisterValueChangedCallback(evt => { if (!evt.newValue) Cancel(); });
            foldout.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                m_Detaching = true;
                Cancel();
            });
        }

        private void Begin(RaidKey action)
        {
            if (m_Capture != null && m_Action == action)
            {
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
            m_Capture = new InputActionRebindingExtensions.RebindingOperation()
                .WithExpectedControlType<KeyControl>()
                .WithControlsHavingToMatchPath("<Keyboard>/*")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithMatchingEventsBeingSuppressed()
                .WithTimeout(10)
                .OnApplyBinding(Assign)
                .OnComplete(Finish)
                .OnCancel(Finish);
            m_Capture.Start();
        }

        private void Assign(InputActionRebindingExtensions.RebindingOperation operation, string path)
        {
            var key = ((KeyControl)operation.selectedControl).keyCode;
            if (!RaidKeyBindings.CanAssign(m_Action, key))
                MoonkovLocalization.Set(m_Status, "This key is reserved.");
            else if (!RaidKeyBindings.TryAssign(m_Action, key, out var conflict))
                MoonkovLocalization.Set(m_Status, "{0} is already assigned to {1}.",
                    RaidKeyBindings.DisplayName(key), RaidKeyBindings.ActionLabel(conflict));
            else
            {
                Refresh();
                m_Status.RemoveFromHierarchy();
            }
        }

        private void Finish(InputActionRebindingExtensions.RebindingOperation operation)
        {
            bool canceled = operation.canceled;
            m_Capture = null;
            Application.focusChanged -= FocusChanged;
            operation.Dispose();
            if (m_Detaching) return;
            m_Buttons[(int)m_Action].RemoveFromClassList("is-capturing");
            if (canceled) m_Status.RemoveFromHierarchy();
        }

        private void FocusChanged(bool focused) { if (!focused) Cancel(); }

        private void Cancel()
        {
            m_Capture?.Cancel();
            if (!m_Detaching) m_Status.RemoveFromHierarchy();
        }

        private void Refresh()
        {
            foreach (var binding in RaidKeyBindings.Bindings)
                MoonkovLocalization.Set(m_Buttons[(int)binding.Action], "{0}", RaidKeyBindings.DisplayName(binding.Action));
        }

        private void RestoreDefaults()
        {
            Cancel();
            RaidKeyBindings.RestoreDefaults();
            Refresh();
        }
    }
}
