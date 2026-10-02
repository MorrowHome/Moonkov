using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Presentation of the existing aggregate stash. No equipment or inventory writes occur here.
    public sealed class StashScreen : IDisposable
    {
        private sealed class Stack
        {
            public string Name, ShortName, Category, Description, Class;
            public StashArtKind Art;
            public int Width, Height, Quantity;
            public Button Tile;
            public Label Count;
        }
        private const int Columns = 10, Rows = 24;
        private readonly VisualElement m_Host, m_Root, m_Grid, m_InspectArt;
        private readonly ScrollView m_Scroll;
        private readonly TextField m_Search;
        private readonly Button m_All, m_Materials, m_Energy, m_Sort, m_Refresh, m_Logout, m_Deploy, m_MainMenu;
        private readonly Action m_OnPrepare, m_OnLogout, m_OnRefresh;
        private readonly Label m_EmptyTitle, m_EmptyDescription;
        private readonly Stack[] m_Stacks;
        private readonly List<Stack> m_Visible = new List<Stack>(3);
        private Stack m_Selected;
        private int m_Filter;
        private bool m_SortByQuantity;
        private float m_CellSize;

        public StashScreen(VisualElement host, Action prepare, Action logout, Action refresh)
        {
            m_Host = host; m_Root = host.Q<VisualElement>("stashScreen");
            m_OnPrepare = prepare; m_OnLogout = logout; m_OnRefresh = refresh;
            m_Search = m_Root.Q<TextField>("stashSearch");
            m_Search.textEdition.placeholder = "Search supplies...";
            m_All = Button("stashFilterAll"); m_Materials = Button("stashFilterMaterials"); m_Energy = Button("stashFilterEnergy");
            m_Sort = Button("stashSort"); m_Refresh = Button("stashRefresh"); m_Logout = Button("stashLogout");
            m_Deploy = Button("stashDeploy"); m_MainMenu = Button("stashMainMenu");
            m_All.clicked += All; m_Materials.clicked += Materials; m_Energy.clicked += Energy;
            m_Sort.clicked += Sort; m_Refresh.clicked += m_OnRefresh; m_Logout.clicked += m_OnLogout;
            m_Deploy.clicked += m_OnPrepare; m_MainMenu.clicked += m_OnPrepare;
            m_Search.RegisterValueChangedCallback(SearchChanged);
            m_Scroll = m_Root.Q<ScrollView>("stashScroll");
            m_Grid = new StashGridVisual(Columns, Rows);
            m_Grid.AddToClassList("stash-grid");
            m_Root.Q<VisualElement>("stashGridHost").Add(m_Grid);
            m_Scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(ViewportChanged);
            m_InspectArt = m_Root.Q<VisualElement>("stashInspectArt");
            m_Stacks = new[]
            {
                new Stack { Name = "Moon dust", ShortName = "Moon dust", Category = "LUNAR MATERIAL", Art = StashArtKind.Dust, Width = 2, Height = 2, Class = "stash-item-dust", Description = "Fine regolith recovered from lunar supply caches. Stored as a single stack in your personal stash." },
                new Stack { Name = "Alloy", ShortName = "Alloy", Category = "STRUCTURAL MATERIAL", Art = StashArtKind.Alloy, Width = 2, Height = 1, Class = "stash-item-alloy", Description = "Recovered structural alloy. Each supply collected and extracted adds to this material stack." },
                new Stack { Name = "Energy cell", ShortName = "Cell", Category = "ENERGY RESOURCE", Art = StashArtKind.Cell, Width = 1, Height = 2, Class = "stash-item-cell", Description = "A compact energy resource retrieved from lunar supply caches. Kept in storage between expeditions." }
            };
            foreach (var stack in m_Stacks)
            {
                stack.Tile = new Button(() => Select(stack)) { tooltip = stack.Name };
                stack.Tile.AddToClassList("stash-item"); stack.Tile.AddToClassList(stack.Class);
                var art = new StashItemArt(stack.Art); art.AddToClassList("stash-item-art"); stack.Tile.Add(art);
                var name = new Label(stack.ShortName) { pickingMode = PickingMode.Ignore }; name.AddToClassList("stash-item-name"); stack.Tile.Add(name);
                stack.Count = new Label { pickingMode = PickingMode.Ignore }; stack.Count.AddToClassList("stash-item-quantity"); stack.Tile.Add(stack.Count);
                m_Grid.Add(stack.Tile);
            }
            m_EmptyTitle = new Label { pickingMode = PickingMode.Ignore }; m_EmptyTitle.AddToClassList("stash-empty-title");
            m_EmptyDescription = new Label { pickingMode = PickingMode.Ignore }; m_EmptyDescription.AddToClassList("stash-empty-description");
            m_Grid.Add(m_EmptyTitle); m_Grid.Add(m_EmptyDescription);
            BuildEquipment();
            m_Search.SetValueWithoutNotify("");
            m_Sort.text = "SORT: NAME";
            Select(null);
            Filter(0);
        }

        private Button Button(string name) => m_Root.Q<Button>(name);
        private Label Label(string name) => m_Root.Q<Label>(name);
        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        public void Present(string playerName, int character, int dust, int alloy, int cells, bool visible, bool busy, string error = null)
        {
            m_Host.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            Label("stashAccountName").text = playerName;
            Label("stashCharacterName").text = character == 2 ? "DOLLSINGER" : "OPERATOR " + (character + 1).ToString("00");
            m_Stacks[0].Quantity = dust; m_Stacks[1].Quantity = alloy; m_Stacks[2].Quantity = cells;
            Label("stashDustCount").text = Number(dust); Label("stashAlloyCount").text = Number(alloy); Label("stashCellsCount").text = Number(cells);
            Label("stashSyncStatus").text = busy ? "UPDATING..." : error != null ? "UPDATE FAILED" : "UP TO DATE";
            Label("stashSyncStatus").tooltip = error ?? "Inventory loaded from your account.";
            m_Refresh.SetEnabled(!busy); m_Logout.SetEnabled(!busy); m_Deploy.SetEnabled(!busy); m_MainMenu.SetEnabled(!busy);
            Rebuild();
        }

        private void All() => Filter(0);
        private void Materials() => Filter(1);
        private void Energy() => Filter(2);
        private void Filter(int filter)
        {
            m_Filter = filter;
            m_All.EnableInClassList("stash-filter-active", filter == 0);
            m_Materials.EnableInClassList("stash-filter-active", filter == 1);
            m_Energy.EnableInClassList("stash-filter-active", filter == 2);
            Rebuild();
        }
        private void Sort() { m_SortByQuantity = !m_SortByQuantity; m_Sort.text = m_SortByQuantity ? "SORT: AMOUNT" : "SORT: NAME"; Rebuild(); }
        private void SearchChanged(ChangeEvent<string> evt) => Rebuild();
        private void ViewportChanged(GeometryChangedEvent evt)
        {
            m_CellSize = evt.newRect.width / Columns;
            if (m_CellSize <= 0) return;
            m_Grid.style.height = m_CellSize * Rows;
            Layout();
        }

        private void Rebuild()
        {
            m_Visible.Clear(); int ownedStacks = 0;
            string search = (m_Search.value ?? "").Trim();
            foreach (var stack in m_Stacks)
            {
                bool material = stack.Art != StashArtKind.Cell;
                bool show = stack.Quantity > 0 && (m_Filter == 0 || m_Filter == 1 && material || m_Filter == 2 && !material)
                    && stack.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                stack.Tile.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                stack.Count.text = Number(stack.Quantity);
                if (stack.Quantity > 0) ownedStacks++;
                if (show) m_Visible.Add(stack);
            }
            m_Visible.Sort((a, b) => m_SortByQuantity && a.Quantity != b.Quantity
                ? b.Quantity.CompareTo(a.Quantity) : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            Label("stashStackCount").text = ownedStacks + (ownedStacks == 1 ? " STACK" : " STACKS");
            bool empty = m_Visible.Count == 0;
            m_EmptyTitle.style.display = m_EmptyDescription.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            m_EmptyTitle.text = ownedStacks == 0 ? "YOUR STASH IS EMPTY" : "NO MATCHING SUPPLIES";
            m_EmptyDescription.text = ownedStacks == 0 ? "Recover supplies on the Moon. Extract successfully to bring them home." : "Try another category or search term.";
            if (m_Selected != null && !m_Visible.Contains(m_Selected)) Select(null);
            else if (m_Selected != null) Label("stashInspectQuantity").text = Number(m_Selected.Quantity);
            Layout();
        }

        private void Layout()
        {
            if (m_CellSize <= 0) return;
            int column = 0;
            foreach (var stack in m_Visible)
            {
                stack.Tile.style.left = column * m_CellSize;
                stack.Tile.style.top = 0;
                stack.Tile.style.width = stack.Width * m_CellSize;
                stack.Tile.style.height = stack.Height * m_CellSize;
                column += stack.Width;
            }
        }

        private void Select(Stack selected)
        {
            m_Selected = selected;
            foreach (var stack in m_Stacks) stack.Tile.EnableInClassList("stash-item-selected", stack == selected);
            m_InspectArt.Clear();
            if (selected != null)
            {
                var art = new StashItemArt(selected.Art); art.style.flexGrow = 1; art.style.marginTop = art.style.marginBottom = 16; art.style.marginLeft = art.style.marginRight = 18;
                m_InspectArt.Add(art);
            }
            Label("stashInspectName").text = selected == null ? "SELECT AN ITEM" : selected.Name.ToUpperInvariant();
            Label("stashInspectCategory").text = selected == null ? "PERSONAL STORAGE" : selected.Category;
            Label("stashInspectDescription").text = selected == null ? "Select a supply stack to inspect its contents." : selected.Description;
            Label("stashInspectQuantity").text = selected == null ? "--" : Number(selected.Quantity);
        }

        private void BuildEquipment()
        {
            var equipment = m_Root.Q<VisualElement>("stashEquipment");
            Slot(equipment, "HALO", StashArtKind.Halo, "DEFAULT");
            Slot(equipment, "OUTFIT", StashArtKind.Outfit, "DEFAULT");
            Slot(equipment, "COMMS", StashArtKind.None, "");
            Slot(equipment, "FACE COVER", StashArtKind.None, "");
            Slot(equipment, "UTILITY", StashArtKind.None, "");
            Slot(equipment, "PACK MODULE", StashArtKind.None, "");
            var pockets = m_Root.Q<VisualElement>("stashPockets");
            for (int i = 0; i < 4; i++) { var pocket = new VisualElement(); pocket.AddToClassList("stash-pocket"); pockets.Add(pocket); }
            var pack = new StashGridVisual(4, 3); pack.style.flexGrow = 1; m_Root.Q<VisualElement>("stashPack").Add(pack);
        }
        private static void Slot(VisualElement parent, string label, StashArtKind kind, string caption)
        {
            var slot = new VisualElement { tooltip = kind == StashArtKind.None ? "No equipment assigned." : "Default field kit. Equipment selection is not available yet." };
            slot.AddToClassList("stash-equipment-slot");
            var title = new Label(label); title.AddToClassList("stash-slot-label"); slot.Add(title);
            if (kind == StashArtKind.None)
            {
                var empty = new Label("EMPTY"); empty.AddToClassList("stash-slot-empty"); slot.Add(empty);
            }
            else
            {
                slot.AddToClassList("stash-equipped-slot");
                var art = new StashItemArt(kind); art.AddToClassList("stash-slot-art"); slot.Add(art);
                var note = new Label(caption); note.AddToClassList("stash-slot-caption"); slot.Add(note);
            }
            parent.Add(slot);
        }

        public void Dispose()
        {
            m_All.clicked -= All; m_Materials.clicked -= Materials; m_Energy.clicked -= Energy;
            m_Sort.clicked -= Sort; m_Refresh.clicked -= m_OnRefresh; m_Logout.clicked -= m_OnLogout;
            m_Deploy.clicked -= m_OnPrepare; m_MainMenu.clicked -= m_OnPrepare;
            m_Search.UnregisterValueChangedCallback(SearchChanged);
            m_Scroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(ViewportChanged);
            m_Grid.RemoveFromHierarchy(); m_Root.Q<VisualElement>("stashEquipment").Clear();
            m_Root.Q<VisualElement>("stashPockets").Clear(); m_Root.Q<VisualElement>("stashPack").Clear();
            m_Host.style.display = DisplayStyle.None;
        }
    }
}
