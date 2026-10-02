using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashScreen
    {
        private StashLoadout m_Loadout;
        private readonly Dictionary<LoadoutSlot, Button> m_Slots = new Dictionary<LoadoutSlot, Button>();
        private readonly bool m_Preview;
        private VisualElement m_DragGhost;
        private StashEquipmentGuides m_Guides;
        private LoadoutSlot m_HoverSlot;
        private static readonly LoadoutSlot[] s_SlotOrder = { LoadoutSlot.Helmet, LoadoutSlot.ChestRig, LoadoutSlot.Pistol, LoadoutSlot.Backpack, LoadoutSlot.Primary, LoadoutSlot.Secondary };
        private static string SlotName(LoadoutSlot slot) => slot == LoadoutSlot.ChestRig ? "CHEST RIG" : slot == LoadoutSlot.Primary ? "PRIMARY WEAPON" : slot == LoadoutSlot.Secondary ? "SECONDARY WEAPON" : slot.ToString().ToUpperInvariant();
        private Stack FindStack(string id) { foreach (var item in m_Stacks) if (item.Name == id) return item; return null; }
        private Stack Equipped(LoadoutSlot slot) => FindStack(m_Loadout?.At(slot));

        private Stack[] PreviewEquipment() => new[]
        {
            Sample("L-01 Flight helmet", "L-01", EquipmentKind.Helmet, StashArtKind.Helmet, 2, 2),
            Sample("H-02 Survey helmet", "H-02", EquipmentKind.Helmet, StashArtKind.Helmet, 2, 2),
            Sample("AR-7 Field rifle", "AR-7", EquipmentKind.LongGun, StashArtKind.Rifle, 4, 2),
            Sample("M-12 Compact rifle", "M-12", EquipmentKind.LongGun, StashArtKind.Rifle, 3, 2),
            Sample("P-03 Sidearm", "P-03", EquipmentKind.Pistol, StashArtKind.Pistol, 2, 1),
            Sample("R-04 Chest rig", "R-04", EquipmentKind.ChestRig, StashArtKind.ChestRig, 2, 3),
            Sample("B-08 Expedition pack", "B-08", EquipmentKind.Backpack, StashArtKind.Backpack, 3, 3)
        };
        private static Stack Sample(string name, string shortName, EquipmentKind kind, StashArtKind art, int width, int height) =>
            new Stack { Name = name, ShortName = shortName, Kind = kind, Art = art, Width = width, Height = height, Quantity = 1, Category = "SAMPLE EQUIPMENT", Description = "Sample equipment for the local loadout preview. This selection does not change your account or field equipment.", Class = "stash-item-equipment" };

        private void BuildEquipment()
        {
            var stage = m_Root.Q<VisualElement>("stashCharacterStage");
            var equipment = m_Root.Q<VisualElement>("stashEquipment");
            m_Guides = new StashEquipmentGuides(() => m_Terminal?.Character, m_Slots); stage.Insert(0, m_Guides);
            for (int i = 0; i < s_SlotOrder.Length; i++)
            {
                var slot = s_SlotOrder[i];
                var button = new Button(() => ChooseEquipment(slot)) { name = "loadout" + slot, tooltip = "Select or drag compatible equipment." };
                button.AddToClassList("stash-loadout-slot");
                button.style.left = new Length(i < 3 ? 0 : 76, LengthUnit.Percent);
                button.style.top = new Length(i % 3 == 0 ? 5 : i % 3 == 1 ? 36 : 67, LengthUnit.Percent);
                var label = new Label(SlotName(slot)) { pickingMode = PickingMode.Ignore }; label.AddToClassList("stash-loadout-label"); button.Add(label);
                var art = new VisualElement { name = "slotArt", pickingMode = PickingMode.Ignore }; art.AddToClassList("stash-loadout-art"); button.Add(art);
                var caption = new Label("EMPTY") { name = "slotCaption", pickingMode = PickingMode.Ignore }; caption.AddToClassList("stash-loadout-caption"); button.Add(caption);
                button.AddManipulator(new GridDrag(this, () => Equipped(slot)));
                button.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1 || Equipped(slot) == null) return;
                    var window = m_Terminal.Windows.Open(SlotName(slot), m_Root.WorldToLocal(evt.position), true);
                    MoonkovTerminal.ActionButton(window, "INSPECT", () => { var item = Equipped(slot); m_Terminal.Windows.Close(window); if (item != null) Inspect(item); });
                    MoonkovTerminal.ActionButton(window, "UNEQUIP TO STASH", () => { m_Terminal.Windows.Close(window); if (!m_Loadout.TryUnequip(slot)) m_Terminal.Windows.Toast("No free storage space, or this item is locked."); Rebuild(); });
                    evt.StopPropagation();
                }, TrickleDown.TrickleDown);
                m_Slots.Add(slot, button); equipment.Add(button);
            }
            var note = m_Root.Q<Label>("stashLoadoutNote");
            note.text = m_Preview ? "LOCAL PREVIEW / SAMPLE EQUIPMENT" : "ACCOUNT SUPPLIES / EQUIPMENT SERVICE NOT CONNECTED";
            RefreshEquipment();
        }

        private void RefreshEquipment()
        {
            foreach (var pair in m_Slots)
            {
                var item = Equipped(pair.Key); var button = pair.Value; var art = button.Q<VisualElement>("slotArt"); art.Clear();
                button.Q<Label>("slotCaption").text = item == null ? "EMPTY" : item.ShortName;
                if (item != null) { var icon = new StashItemArt(item.Art); icon.style.flexGrow = 1; art.Add(icon); }
                button.EnableInClassList("stash-loadout-equipped", item != null);
                button.tooltip = item == null ? "Click to choose or drag compatible equipment." : item.Name + " / Drag to stash or another compatible slot. Right click to unequip.";
            }
            m_Guides?.MarkDirtyRepaint();
        }
        private void ChooseEquipment(LoadoutSlot slot)
        {
            var window = m_Terminal.Windows.Open(SlotName(slot), null, true);
            bool available = false;
            foreach (var stack in m_Stacks)
            {
                if (stack.Quantity <= 0 || !m_Loadout.Accepts(stack.Name, slot)) continue;
                available = true; var item = stack;
                var choose = MoonkovTerminal.ActionButton(window, item.ShortName + (m_Loadout.Location(item.Name) == LoadoutSlot.None ? " / STASH" : " / EQUIPPED"), () =>
                {
                    if (m_Loadout.TryEquip(item.Name, slot)) { m_Terminal.Windows.Close(window); Rebuild(); }
                    else m_Terminal.Windows.Toast("Equipment is locked, or storage has no room for the replaced item.");
                });
                choose.SetEnabled(m_Loadout.CanEquip(item.Name, slot));
            }
            if (!available) MoonkovTerminal.Text(window, "No compatible equipment is available in your stash.", "terminal-copy");
            if (Equipped(slot) != null) MoonkovTerminal.ActionButton(window, "UNEQUIP", () => { if (m_Loadout.TryUnequip(slot)) { m_Terminal.Windows.Close(window); Rebuild(); } else m_Terminal.Windows.Toast("No free space in your stash, or item locked."); });
        }
        private LoadoutSlot SlotAt(Vector2 position)
        {
            foreach (var pair in m_Slots) if (pair.Value.worldBound.Contains(position)) return pair.Key;
            return LoadoutSlot.None;
        }
        private void HoverSlot(LoadoutSlot slot, Stack stack)
        {
            m_HoverSlot = slot;
            foreach (var pair in m_Slots)
            {
                bool hover = pair.Key == slot;
                pair.Value.EnableInClassList("stash-slot-drop-valid", hover && m_Loadout.CanEquip(stack.Name, slot));
                pair.Value.EnableInClassList("stash-slot-drop-invalid", hover && !m_Loadout.CanEquip(stack.Name, slot));
            }
        }
        private void ClearSlotHover()
        {
            m_HoverSlot = LoadoutSlot.None;
            foreach (var button in m_Slots.Values) { button.RemoveFromClassList("stash-slot-drop-valid"); button.RemoveFromClassList("stash-slot-drop-invalid"); }
        }
        private void CancelDrag() => m_ActiveDrag?.Cancel();
    }

    internal sealed class StashEquipmentGuides : VisualElement
    {
        private readonly Func<MenuCharacterView> m_Character;
        private readonly Dictionary<LoadoutSlot, Button> m_Slots;
        public StashEquipmentGuides(Func<MenuCharacterView> character, Dictionary<LoadoutSlot, Button> slots)
        {
            m_Character = character; m_Slots = slots; pickingMode = PickingMode.Ignore;
            AddToClassList("stash-equipment-guides"); generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var view = m_Character(); if (view == null || contentRect.width < 2) return;
            var painter = context.painter2D; painter.lineWidth = 1; painter.strokeColor = new Color(.25f, .27f, .27f, .4f);
            foreach (var pair in m_Slots)
            {
                var bone = pair.Key == LoadoutSlot.Helmet ? HumanBodyBones.Head : pair.Key == LoadoutSlot.Pistol ? HumanBodyBones.RightUpperLeg
                    : pair.Key == LoadoutSlot.Primary ? HumanBodyBones.RightUpperArm : pair.Key == LoadoutSlot.Secondary ? HumanBodyBones.LeftUpperArm : HumanBodyBones.Chest;
                if (!view.TryGetBodyAnchor(bone, out var world)) continue;
                Vector2 origin = this.WorldToLocal(world); var rect = pair.Value.worldBound;
                bool left = pair.Key == LoadoutSlot.Helmet || pair.Key == LoadoutSlot.ChestRig || pair.Key == LoadoutSlot.Pistol;
                Vector2 end = this.WorldToLocal(new Vector2(left ? rect.xMax : rect.xMin, rect.center.y));
                Vector2 bend = new Vector2(Mathf.Lerp(origin.x, end.x, .6f), origin.y);
                painter.BeginPath(); painter.MoveTo(origin); painter.LineTo(bend); painter.LineTo(end); painter.Stroke();
                painter.fillColor = new Color(.25f, .27f, .27f, .6f); painter.BeginPath(); painter.Arc(origin, 2.5f, Angle.Degrees(0), Angle.Degrees(360)); painter.Fill();
            }
        }
    }
}
