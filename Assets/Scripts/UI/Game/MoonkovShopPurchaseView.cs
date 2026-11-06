using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace Unity.MP_FPS.Client
{
    public sealed class MoonkovShopPurchaseView : IDisposable
    {
        public VisualElement Element { get; }
        private readonly Action<ShopOffer, int> m_Buy;
        private readonly string m_InitialCode;
        private readonly TextField m_Search;
        private readonly ScrollView m_Products, m_Details;
        private readonly VisualElement m_Grid, m_Art, m_Tooltip, m_Quantity;
        private readonly Label m_Empty, m_Notice, m_Reason, m_Amount;
        private readonly Button m_Purchase, m_Minus, m_Plus;
        private readonly Button[] m_Filters;
        private readonly MoonkovItemInformation m_Information, m_HoverInformation;
        private readonly Dictionary<string, Button> m_Tiles = new Dictionary<string, Button>();
        private readonly List<ShopOffer> m_Visible = new List<ShopOffer>();
        private ShopOffer[] m_Offers;
        private InventoryGraph m_Graph;
        private string m_OfferKey, m_SelectedCode, m_ArtCode, m_Query = "", m_ReadOnlyReason;
        private int m_Filter, m_QuantityValue = 1, m_Columns = 1;
        private bool m_CanTrade, m_Loading, m_Disposed;
        private VisualElement m_HoverTarget;
        private string m_HoverCode;
        private Vector2 m_HoverPosition;

        public MoonkovShopPurchaseView(Action<ShopOffer, int> buy, string initialCode = null)
        {
            m_Buy = buy; m_InitialCode = initialCode;
            Element = TerminalLayout.Clone("Components/ShopPurchase", "shopPurchase");
            Element.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ShopPurchase"));
            m_Search = Element.Q<TextField>("purchaseSearch");
            m_Search.textEdition.placeholder = MoonkovLocalization.Text("Search supplies by name");
            m_Search.RegisterValueChangedCallback(SearchChanged);
            m_Products = Element.Q<ScrollView>("purchaseProducts"); m_Grid = m_Products.contentContainer;
            m_Grid.name = "purchaseGrid"; m_Grid.AddToClassList("shop-purchase-grid");
            m_Products.contentViewport.RegisterCallback<GeometryChangedEvent>(GridGeometryChanged);
            m_Grid.RegisterCallback<NavigationMoveEvent>(NavigateGrid);
            m_Details = Element.Q<ScrollView>("purchaseDetails"); m_Art = Element.Q("purchaseArt");
            m_Information = new MoonkovItemInformation(Element.Q("purchaseInformation"));
            m_Tooltip = Element.Q("purchaseTooltip"); m_HoverInformation = new MoonkovItemInformation(m_Tooltip);
            m_Empty = Element.Q<Label>("purchaseEmpty"); m_Notice = Element.Q<Label>("purchaseNotice"); m_Reason = Element.Q<Label>("purchaseReason");
            m_Quantity = Element.Q("purchaseQuantity"); m_Amount = Element.Q<Label>("purchaseAmount");
            m_Minus = Element.Q<Button>("purchaseMinus"); m_Plus = Element.Q<Button>("purchasePlus"); m_Purchase = Element.Q<Button>("purchaseBuy");
            m_Minus.clicked += Decrease; m_Plus.clicked += Increase; m_Purchase.clicked += Purchase;
            string[] groups = { "ALL", "WEAPONS", "EQUIPMENT", "SUPPLIES", "MODULES" }; m_Filters = new Button[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                int index = i; var button = new Button(() => { m_Filter = index; Rebuild(); });
                button.name = "purchaseFilter" + i; button.AddToClassList("shop-purchase-filter");
                Element.Q("purchaseFilters").Add(button); MoonkovLocalization.Set(button, groups[i]); m_Filters[i] = button;
            }
            Element.RegisterCallback<GeometryChangedEvent>(GeometryChanged);
            Element.RegisterCallback<WheelEvent>(Scrolled, TrickleDown.TrickleDown);
            Element.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_Tooltip.RegisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            Application.focusChanged += FocusChanged; LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            Present(null, null, false, true);
        }

        public void Present(ShopOffer[] offers, InventoryGraph graph, bool canTrade, bool loading, string readOnlyReason = null)
        {
            if (m_Disposed) return;
            m_Offers = offers; m_Graph = graph; m_CanTrade = canTrade && graph != null; m_Loading = loading; m_ReadOnlyReason = readOnlyReason;
            string key = offers == null ? null : string.Join("|", offers.Where(o => o.BuyDust >= 0).Select(o => o.Code + ":" + o.Name + ":" + o.BuyDust + ":" + o.FreeSupplyRestricted));
            if (m_OfferKey != key || m_Tiles.Count == 0) { m_OfferKey = key; Rebuild(); }
            else RefreshSelection();
            if (m_HoverCode != null) m_HoverInformation.Present(NewItem(m_HoverCode), graph);
        }

        private static InventoryItem NewItem(string code, int quantity = 1) => new InventoryItem { Id = "shop:" + code, Code = code, Quantity = quantity };
        private static string CategoryClass(string code)
        {
            var tactical = TacticalItemRules.Find(code);
            if (tactical != null && (tactical.Kind == TacticalItemKind.Vitality || tactical.Kind == TacticalItemKind.Stamina || tactical.Kind == TacticalItemKind.Antidote))
                return "shop-item-medical";
            return InventoryCatalog.Get(code)?.Kind switch
            {
                ItemKind.LongGun or ItemKind.Pistol or ItemKind.Melee => "shop-item-weapon",
                ItemKind.Helmet or ItemKind.Shield or ItemKind.Rig or ItemKind.Backpack or ItemKind.SecureContainer or ItemKind.SpecialEquipment => "shop-item-equipment",
                ItemKind.Medical => "shop-item-medical",
                ItemKind.Food => "shop-item-food",
                ItemKind.Drink => "shop-item-drink",
                ItemKind.Oxygen => "shop-item-oxygen",
                ItemKind.Cell => "shop-item-energy",
                ItemKind.Throwable => "shop-item-tactical",
                ItemKind.StorageModule or ItemKind.ShapingModule or ItemKind.UtilityModule => "shop-item-module",
                ItemKind.Material => "shop-item-material",
                _ => "shop-item-other"
            };
        }
        private ShopOffer Selected => m_Offers?.FirstOrDefault(offer => offer.Code == m_SelectedCode && offer.BuyDust >= 0);
        private bool Matches(ShopOffer offer)
        {
            var definition = InventoryCatalog.Get(offer.Code); if (definition == null || offer.BuyDust < 0) return false;
            bool category = m_Filter == 0 || m_Filter == 1 && (definition.Kind == ItemKind.LongGun || definition.Kind == ItemKind.Pistol || definition.Kind == ItemKind.Melee) ||
                m_Filter == 2 && (definition.Kind == ItemKind.Helmet || definition.Kind == ItemKind.Shield || definition.Kind == ItemKind.Rig || definition.Kind == ItemKind.Backpack || definition.Kind == ItemKind.SecureContainer || definition.Kind == ItemKind.SpecialEquipment) ||
                m_Filter == 3 && (definition.Kind == ItemKind.Medical || definition.Kind == ItemKind.Food || definition.Kind == ItemKind.Drink || definition.Kind == ItemKind.Oxygen || definition.Kind == ItemKind.Cell || definition.Kind == ItemKind.Throwable) ||
                m_Filter == 4 && HaloModules.FromCode(offer.Code) != HaloModule.None;
            string name = offer.Name ?? definition.Name;
            return category && (string.IsNullOrEmpty(m_Query) || name.IndexOf(m_Query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                MoonkovLocalization.Text(name).IndexOf(m_Query, StringComparison.OrdinalIgnoreCase) >= 0 || offer.Code.IndexOf(m_Query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Rebuild()
        {
            if (m_Disposed) return;
            HideHover(); m_Grid.Clear(); m_Tiles.Clear(); m_Visible.Clear();
            for (int i = 0; i < m_Filters.Length; i++) m_Filters[i].EnableInClassList("is-selected", i == m_Filter);
            if (m_Offers != null) m_Visible.AddRange(m_Offers.Where(Matches).OrderByDescending(offer => offer.Code == m_InitialCode));
            foreach (var offer in m_Visible)
            {
                var captured = offer; var tile = new Button(() => Select(captured.Code));
                tile.name = "purchaseProduct" + offer.Code; tile.AddToClassList("shop-purchase-tile");
                tile.AddToClassList(CategoryClass(offer.Code));
                var artHost = new VisualElement { pickingMode = PickingMode.Ignore }; artHost.AddToClassList("shop-purchase-tile-art");
                var art = new StashItemArt(offer.Code) { pickingMode = PickingMode.Ignore }; art.style.flexGrow = 1; artHost.Add(art); tile.Add(artHost);
                var title = new Label { pickingMode = PickingMode.Ignore, enableRichText = false }; title.AddToClassList("shop-purchase-tile-name"); tile.Add(title); MoonkovLocalization.Set(title, offer.Name ?? InventoryCatalog.Get(offer.Code).Name);
                var price = new Label { name = "productPrice", pickingMode = PickingMode.Ignore }; price.AddToClassList("shop-purchase-tile-price");
                price.EnableInClassList("shop-purchase-price-free", offer.BuyDust == 0); tile.Add(price);
                if (offer.BuyDust == 0) MoonkovLocalization.Set(price, "FREE"); else MoonkovLocalization.Set(price, "{0} DUST", offer.BuyDust);
                tile.RegisterCallback<FocusInEvent>(_ => Select(captured.Code));
                tile.RegisterCallback<PointerEnterEvent>(evt => { if (evt.pointerType == PointerType.mouse && evt.pressedButtons == 0) Hover(tile, captured.Code, evt.position); });
                tile.RegisterCallback<PointerMoveEvent>(evt => { if (evt.pointerType == PointerType.mouse && evt.pressedButtons == 0) Hover(tile, captured.Code, evt.position); });
                tile.RegisterCallback<PointerLeaveEvent>(_ => HideHover(tile));
                tile.RegisterCallback<PointerDownEvent>(_ => HideHover(), TrickleDown.TrickleDown);
                tile.RegisterCallback<DetachFromPanelEvent>(_ => HideHover(tile));
                m_Grid.Add(tile); m_Tiles.Add(offer.Code, tile);
            }
            if (!m_Tiles.ContainsKey(m_SelectedCode ?? "")) Select(m_Visible.FirstOrDefault()?.Code);
            else RefreshSelection();
            m_Empty.style.display = m_Visible.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_Empty, m_Offers == null ? m_Loading ? "Connecting to supplier…" : "Sign in and reopen this supplier to load current stock." : "No matching supplies.");
            LayoutGrid();
        }

        private void Select(string code)
        {
            HideHover();
            if (m_SelectedCode != code)
            {
                m_SelectedCode = code; m_QuantityValue = code == "cells" ? 3 : 1;
                m_Details.scrollOffset = Vector2.zero;
            }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (var pair in m_Tiles) pair.Value.EnableInClassList("is-selected", pair.Key == m_SelectedCode);
            var offer = Selected; bool selected = offer != null && m_Tiles.ContainsKey(offer.Code);
            m_Details.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            m_Quantity.style.display = selected && InventoryCatalog.Get(offer.Code).MaxStack > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (selected)
            {
                var definition = InventoryCatalog.Get(offer.Code); m_QuantityValue = Math.Max(1, Math.Min(definition.MaxStack, m_QuantityValue));
                if (m_ArtCode != offer.Code)
                {
                    if (m_ArtCode != null) m_Art.RemoveFromClassList(CategoryClass(m_ArtCode));
                    m_Art.AddToClassList(CategoryClass(offer.Code)); m_ArtCode = offer.Code; m_Art.Clear();
                    var art = new StashItemArt(offer.Code) { pickingMode = PickingMode.Ignore }; art.style.flexGrow = 1; m_Art.Add(art);
                }
                m_Information.Present(NewItem(offer.Code, m_QuantityValue), m_Graph);
                MoonkovLocalization.Set(m_Amount, "× {0}", m_QuantityValue);
                m_Minus.SetEnabled(m_CanTrade && m_QuantityValue > 1); m_Plus.SetEnabled(m_CanTrade && m_QuantityValue < definition.MaxStack);
                MoonkovLocalization.Set(m_Purchase, offer.BuyDust == 0 ? "CLAIM FREE SUPPLY" : "BUY / {0} DUST", offer.BuyDust * m_QuantityValue);
            }
            else MoonkovLocalization.Set(m_Purchase, "SELECT AN ITEM");
            bool affordable = selected && m_Graph != null && m_Graph.Count("dust") >= offer.BuyDust * m_QuantityValue;
            m_Purchase.SetEnabled(selected && m_CanTrade && affordable);
            string reason = m_ReadOnlyReason ?? (selected && m_CanTrade && !affordable ? "INSUFFICIENT MOON DUST" : "");
            MoonkovLocalization.Set(m_Reason, reason); m_Reason.style.display = string.IsNullOrEmpty(reason) ? DisplayStyle.None : DisplayStyle.Flex;
            bool restricted = selected && offer.FreeSupplyRestricted;
            m_Notice.style.display = restricted ? DisplayStyle.Flex : DisplayStyle.None;
            if (restricted) MoonkovLocalization.Set(m_Notice, "EMERGENCY SUPPLY / CANNOT BE SOLD");
        }

        private void Decrease() { m_QuantityValue--; RefreshSelection(); }
        private void Increase() { m_QuantityValue++; RefreshSelection(); }
        private void Purchase()
        {
            var offer = Selected;
            if (!m_Disposed && m_CanTrade && offer != null && m_Tiles.ContainsKey(offer.Code) && m_Graph != null && m_Graph.Count("dust") >= offer.BuyDust * m_QuantityValue)
            { HideHover(); m_Buy?.Invoke(offer, m_QuantityValue); }
        }
        private void SearchChanged(ChangeEvent<string> evt) { m_Query = (evt.newValue ?? "").Trim(); Rebuild(); }
        private void GridGeometryChanged(GeometryChangedEvent evt) => LayoutGrid();
        private void GeometryChanged(GeometryChangedEvent evt) { Element.EnableInClassList("shop-purchase-compact", evt.newRect.width < 620); PlaceHover(); }
        private void LayoutGrid()
        {
            float width = m_Products.contentViewport.contentRect.width; if (width <= 16 || float.IsNaN(width)) return;
            m_Columns = Mathf.Max(1, Mathf.FloorToInt((width + 8) / 136));
            float tileWidth = Mathf.Floor((width - m_Columns * 8 - 2) / m_Columns);
            foreach (var tile in m_Tiles.Values) tile.style.width = tileWidth;
        }
        private void NavigateGrid(NavigationMoveEvent evt)
        {
            var focused = Element.panel?.focusController.focusedElement as VisualElement;
            int index = m_Visible.FindIndex(offer => m_Tiles[offer.Code] == focused); if (index < 0) return;
            int step = evt.direction == NavigationMoveEvent.Direction.Left ? -1 : evt.direction == NavigationMoveEvent.Direction.Right ? 1 : evt.direction == NavigationMoveEvent.Direction.Up ? -m_Columns : evt.direction == NavigationMoveEvent.Direction.Down ? m_Columns : 0;
            if (step == 0) return;
            int next = index + step;
            if (next < 0 || next >= m_Visible.Count || step == -1 && index % m_Columns == 0 || step == 1 && index % m_Columns == m_Columns - 1) return;
            var tile = m_Tiles[m_Visible[next].Code]; tile.Focus(); m_Products.ScrollTo(tile); evt.StopPropagation();
        }
        private void LocaleChanged(Locale locale) { m_Search.textEdition.placeholder = MoonkovLocalization.Text("Search supplies by name"); Rebuild(); }

        private void Hover(VisualElement target, string code, Vector2 position)
        {
            if (m_Disposed || target.panel == null) return;
            bool changed = m_HoverTarget != target || m_HoverCode != code; m_HoverTarget = target; m_HoverCode = code; m_HoverPosition = Element.WorldToLocal(position);
            if (changed) { m_HoverInformation.Present(NewItem(code), m_Graph); m_Tooltip.style.display = DisplayStyle.Flex; m_Tooltip.BringToFront(); }
            PlaceHover();
        }
        private void PlaceHover()
        {
            if (m_HoverTarget == null) return;
            var bounds = Element.contentRect; if (bounds.width <= 16 || bounds.height <= 16) return;
            m_Tooltip.style.width = Mathf.Min(400, bounds.width - 16); m_Tooltip.style.maxHeight = bounds.height - 16;
            float width = m_Tooltip.resolvedStyle.width, height = m_Tooltip.resolvedStyle.height; if (float.IsNaN(width) || float.IsNaN(height)) return;
            float x = m_HoverPosition.x + 18, y = m_HoverPosition.y + 18;
            if (x + width > bounds.xMax - 8) x = m_HoverPosition.x - width - 18;
            if (y + height > bounds.yMax - 8) y = m_HoverPosition.y - height - 18;
            m_Tooltip.style.left = Mathf.Clamp(x, bounds.xMin + 8, Mathf.Max(bounds.xMin + 8, bounds.xMax - width - 8));
            m_Tooltip.style.top = Mathf.Clamp(y, bounds.yMin + 8, Mathf.Max(bounds.yMin + 8, bounds.yMax - height - 8));
        }
        private void HideHover(VisualElement target = null) { if (target != null && target != m_HoverTarget) return; m_HoverTarget = null; m_HoverCode = null; m_Tooltip.style.display = DisplayStyle.None; }
        private void TooltipGeometryChanged(GeometryChangedEvent evt) => PlaceHover();
        private void Scrolled(WheelEvent evt) => HideHover();
        private void FocusChanged(bool focused) { if (!focused) HideHover(); }
        public void Suspend() => HideHover();
        private void Detached(DetachFromPanelEvent evt) { if (evt.target == Element) Dispose(); }
        public void Dispose()
        {
            if (m_Disposed) return; m_Disposed = true; HideHover();
            Application.focusChanged -= FocusChanged; LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
            m_Search.UnregisterValueChangedCallback(SearchChanged); m_Products.contentViewport.UnregisterCallback<GeometryChangedEvent>(GridGeometryChanged);
            m_Grid.UnregisterCallback<NavigationMoveEvent>(NavigateGrid); Element.UnregisterCallback<GeometryChangedEvent>(GeometryChanged);
            Element.UnregisterCallback<WheelEvent>(Scrolled, TrickleDown.TrickleDown); Element.UnregisterCallback<DetachFromPanelEvent>(Detached);
            m_Tooltip.UnregisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Minus.clicked -= Decrease; m_Plus.clicked -= Increase; m_Purchase.clicked -= Purchase;
        }
    }
}
