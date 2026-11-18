// 给玩法弹窗复用的组装方法。它从 UI Toolkit 模板克隆窗口和玩法卡片，调用方再接按钮和填内容。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    internal static class BountyUIComposition {
        // 复用终端原有窗口、标题栏结构和 Archive 控件状态。
        internal static void Window(VisualElement content) {
            var window = TerminalLayout.Clone("Window", "terminalWindow");
            window.name = content.name + "ArchiveWindow";
            window.AddToClassList("bounty-archive-window");
            var header = window.Q("windowHeader");
            header.Clear();
            // 套用通用窗口时保留原有元素和回调。复制子节点列表再移动它们，避免遍历时原集合不断变化。
            var children = new List<VisualElement>(content.Children());
            foreach (var child in children)
                // 第一个标签可成为标题。其他内容仍按原顺序放在窗口正文中。
                if (header.childCount == 0 && child is Label)
                    header.Add(child);
                else
                    window.Add(child);
            content.Add(window);
        }

        internal static Button ModeCard(Button original) {
            var card = (Button)TerminalLayout.Clone("Components/InventoryItem", "inventoryItem");
            card.name = original.name;
            card.ClearClassList();
            card.AddToClassList("mk-card");
            card.AddToClassList("bounty-mode-card");
            card.Q("itemLabel").RemoveFromHierarchy();
            card.Q("quantity").RemoveFromHierarchy();
            foreach (var child in new List<VisualElement>(original.Children())) card.Add(child);
            // 用格子模板作为玩法卡片，同时保留原按钮的显示位置。外面拿返回的按钮绑定对应玩法。
            var parent = original.parent;
            int index = parent.IndexOf(original);
            original.RemoveFromHierarchy();
            parent.Insert(index, card);
            return card;
        }
    }
}