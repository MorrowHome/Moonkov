using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    internal static class BountyUIComposition
    {
        // Reuse the terminal's actual window/header hierarchy and Archive control states.
        internal static void Window(VisualElement content)
        {
            var window = TerminalLayout.Clone("Window", "terminalWindow");
            window.name = content.name + "ArchiveWindow";
            window.AddToClassList("bounty-archive-window");
            var header = window.Q("windowHeader");
            header.Clear();
            var children = new List<VisualElement>(content.Children());
            foreach (var child in children)
            {
                if (header.childCount == 0 && child is Label) header.Add(child);
                else window.Add(child);
            }
            content.Add(window);
        }
        internal static Button ModeCard(Button original)
        {
            var card = (Button)TerminalLayout.Clone("Components/InventoryItem", "inventoryItem");
            card.name = original.name; card.ClearClassList();
            card.AddToClassList("mk-card"); card.AddToClassList("bounty-mode-card");
            card.Q("itemLabel").RemoveFromHierarchy(); card.Q("quantity").RemoveFromHierarchy();
            foreach (var child in new List<VisualElement>(original.Children())) card.Add(child);
            var parent = original.parent; int index = parent.IndexOf(original);
            original.RemoveFromHierarchy(); parent.Insert(index, card);
            return card;
        }
    }
}
