using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MoonkovTerminal
    {
        private int m_OperationStep;
        private static readonly string[] OperationLayouts =
        {
            "OperationOperator", "OperationLocation", "OperationBriefing", "OperationRecovery", "OperationReady"
        };

        private void ShowOperationStep(int step)
        {
            step = Mathf.Clamp(step, 0, OperationLayouts.Length - 1);
            if (m_OperationStep == step) return;
            m_OperationStep = step;
            Render(); // Retain the terminal's existing reveal/transition implementation.
            m_Content.Q<Button>(m_OperationStep == 4 ? "launch" : "operationContinue")?.Focus();
        }

        private void Operation()
        {
            TerminalLayout.Populate(m_Content, "Operation");
            TerminalLayout.Populate(m_Content.Q("operationMain"), OperationLayouts[m_OperationStep]);
            for (int i = 0; i < OperationLayouts.Length; i++)
            {
                int step = i;
                var button = m_Content.Q<Button>("operationStep" + i);
                button.EnableInClassList("terminal-tab-selected", i == m_OperationStep);
                button.clicked += () => ShowOperationStep(step);
            }
            m_Content.Q<Button>("back").clicked += () =>
            {
                if (m_OperationStep == 0) Navigate(0);
                else ShowOperationStep(m_OperationStep - 1);
            };
            m_Content.Q<Button>("operationContinue").clicked += () => ShowOperationStep(m_OperationStep + 1);
            switch (m_OperationStep)
            {
                case 0:
                    m_Character = new MenuCharacterView(element: m_Content.Q<Image>("operatorPortrait"));
                    // MenuCharacterView adds the shared home class; this exhibit owns its framing.
                    m_Character.Element.RemoveFromClassList("terminal-character-art");
                    PopulateOperationEquipment();
                    m_Content.Q<Button>("reviewEquipment").clicked += () => Navigate(1);
                    break;
                case 2:
                    BindOperationCargo();
                    break;
                case 4:
                    AddOperationCellArt("readyCellArt");
                    MoonkovLocalization.Set(m_Content.Q<Label>("readyCarry"), "{0} ENERGY CELLS", PlayerProfileClient.CarryCells);
                    var launch = m_Content.Q<Button>("launch");
                    launch.clicked += m_Prepare;
                    launch.SetEnabled(!m_Busy);
                    if (m_Busy) MoonkovLocalization.Set(launch.Q<Label>(className: "op-button-label"), "LINK BUSY  /  STAND BY");
                    break;
            }
        }

        private void PopulateOperationEquipment()
        {
            var host = m_Content.Q("operationEquipment");
            var snapshot = PlayerProfileClient.Inventory;
            string[] slots = { "Helmet", "ChestRig", "Backpack", "Primary" };
            string[] labels = { "COLLAR", "CHEST RIG", "BACKPACK", "PRIMARY" };
            for (int i = 0; i < slots.Length; i++)
            {
                var row = TerminalLayout.Clone("Components/OperationEquipmentRow", "equipmentRow");
                host.Add(row);
                var item = snapshot?.Equipped(slots[i]);
                MoonkovLocalization.Set(row.Q<Label>("equipmentSlot"), labels[i]);
                MoonkovLocalization.Set(row.Q<Label>("equipmentName"), item == null
                    ? snapshot == null ? "—" : "EMPTY"
                    : global::Unity.MP_FPS.Inventory.InventoryCatalog.Get(item.Code).Name);
                row.Q<Label>("equipmentState").text = item == null ? "—" : "✓";
                if (item == null) continue;
                var art = new StashItemArt(ContainerInventoryView.Art(item.Code));
                art.style.flexGrow = 1;
                row.Q("equipmentArt").Add(art);
            }
        }

        private void AddOperationCellArt(string host)
        {
            var art = new StashItemArt(StashArtKind.Cell);
            art.style.flexGrow = 1;
            m_Content.Q(host).Add(art);
        }

        private void BindOperationCargo()
        {
            AddOperationCellArt("carryCellArt");
            var carry = m_Content.Q<IntegerField>("operationCarryCells");
            var minus = m_Content.Q<Button>("carryMinus");
            var plus = m_Content.Q<Button>("carryPlus");
            var summary = m_Content.Q<Label>("carrySummary");
            var gauge = m_Content.Q(className: "terminal-cargo-gauge-fill");
            carry.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
            carry.SetEnabled(!m_Busy);
            void PresentCarry()
            {
                int selected = PlayerProfileClient.CarryCells;
                MoonkovLocalization.Set(summary, "Carry {0}/{1} / Stash {2}", selected, RaidRules.BagCapacity, m_Cells);
                gauge.style.width = Length.Percent(100f * selected / Mathf.Max(1, RaidRules.BagCapacity));
                minus.SetEnabled(!m_Busy && selected > 0);
                plus.SetEnabled(!m_Busy && selected < Mathf.Min(m_Cells, RaidRules.BagCapacity));
            }
            // Rendering never writes the profile. Only a deliberate control change uses
            // the existing profile adapter, which owns clamping and offline/online routing.
            void Select(int value)
            {
                PlayerProfileClient.SelectCarryCells(value);
                carry.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
                PresentCarry();
            }
            carry.RegisterValueChangedCallback(evt => Select(evt.newValue));
            minus.clicked += () => Select(PlayerProfileClient.CarryCells - 1);
            plus.clicked += () => Select(PlayerProfileClient.CarryCells + 1);
            PresentCarry();
        }
    }
}
