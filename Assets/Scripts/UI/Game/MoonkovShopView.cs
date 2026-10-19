using System;
using System.Linq;
using System.Threading;
using Unity.MP_FPS.Inventory;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class MoonkovShopView : IDisposable
    {
        private readonly VisualElement m_Window;
        private readonly ScrollView m_List;
        private readonly Label m_Balance, m_Status;
        private readonly Button m_Retry;
        private readonly Button[] m_Tabs;
        private readonly MoonkovResearchView m_Research;
        private readonly VisualElement m_InspectArt;
        private readonly Label m_InspectTitle, m_InspectDetail;
        private VisualElement m_SelectedRow;
        private string m_SelectedCode;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private ShopOffer[] m_Offers;
        private ShopCommand m_Pending;
        private bool m_Busy, m_Disposed;
        private int m_Tab;

        public MoonkovShopView(VisualElement window, Action openStorage, bool recovery = false, Action openWorkshop = null, bool research = false)
        {
            m_Window = window; m_Tab = research ? 3 : recovery ? 2 : 0; window.AddToClassList("terminal-shop-window");
            window.styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("Moonkov/UI/Research"));
            var catalog = TerminalLayout.Clone("Components/ShopCatalog", "shopCatalog"); window.Add(catalog);
            m_Research = new MoonkovResearchView((operation, code) => Order(operation, code, null, 1), openWorkshop);
            catalog.Insert(3, m_Research.Root);
            m_InspectArt = catalog.Q("inspectArt"); m_InspectTitle = catalog.Q<Label>("inspectTitle"); m_InspectDetail = catalog.Q<Label>("inspectDetail");
            var head = catalog.Q("shopHead");
            m_Balance = MoonkovTerminal.Text(head, "MOON DUST / —", "terminal-heading");
            MoonkovTerminal.ActionButton(head, "OPEN STORAGE", openStorage);
            var tabs = catalog.Q("shopTabs");
            string[] names = { "BUY", "SELL", "EMERGENCY SUPPLY", "HALO RESEARCH" }; m_Tabs = new Button[4];
            for (int i = 0; i < names.Length; i++)
            { int tab = i; m_Tabs[i] = MoonkovTerminal.ActionButton(tabs, names[i], () => { m_Tab = tab; Render(); }); }
            m_List = catalog.Q<ScrollView>("shopList");
            var statusHost = catalog.Q("shopStatusHost");
            m_Status = MoonkovTerminal.Text(statusHost, "Contacting lunar supply…", "terminal-copy"); m_Status.AddToClassList("terminal-shop-status");
            m_Retry = MoonkovTerminal.ActionButton(statusHost, "CHECK LAST ORDER", () => Execute(m_Pending)); m_Retry.style.display = DisplayStyle.None;
            window.RegisterCallback<DetachFromPanelEvent>(Detached); PlayerProfileClient.InventoryChanged += Changed;
            Load();
        }
        private async void Load()
        {
            m_Busy = true; Render();
            try
            {
                m_Offers = await PlayerProfileClient.GetShopOffersAsync(m_Stop.Token);
                if (!m_Disposed) MoonkovLocalization.Set(m_Status, "Moon dust is deducted only when your order is confirmed. Purchases arrive in personal storage.");
            }
            catch (OperationCanceledException) { if (!m_Disposed) MoonkovLocalization.Set(m_Status, "Supply contact timed out. Close and reopen the supplier to reconnect."); }
            catch (Exception ex) { if (!m_Disposed) MoonkovLocalization.Set(m_Status, ex.Message); }
            finally { m_Busy = false; if (!m_Disposed) Render(); }
        }
        private void Changed() { if (!m_Busy && !m_Disposed) Render(); }
        private bool Ready => !m_Busy && m_Pending == null && m_Offers != null && PlayerProfileClient.Inventory != null && !PlayerProfileClient.RaidActive;
        private void Render()
        {
            m_Window.EnableInClassList("is-busy", m_Busy);
            m_Window.Q(className: "archive-shop-body").EnableInClassList("research-hidden", m_Tab == 3);
            m_Research.Root.EnableInClassList("research-hidden", m_Tab != 3);
            m_List.Clear();
            m_SelectedRow = null; m_InspectArt.Clear(); MoonkovLocalization.Set(m_InspectTitle, ""); MoonkovLocalization.Set(m_InspectDetail, "");
            MoonkovLocalization.Set(m_Balance, "MOON DUST / {0}", PlayerProfileClient.StashDust);
            for (int i = 0; i < m_Tabs.Length; i++) m_Tabs[i].EnableInClassList("terminal-tab-selected", i == m_Tab);
            m_Retry.style.display = m_Pending != null && !m_Busy ? DisplayStyle.Flex : DisplayStyle.None;
            if (PlayerProfileClient.RaidActive)
                MoonkovTerminal.Text(m_List, "Finish or leave your active raid before trading or requesting emergency supplies.", "terminal-copy");
            if (m_Offers == null)
            { MoonkovTerminal.Text(m_List, m_Busy ? "Connecting to supplier…" : "Sign in and reopen this supplier to load current stock.", "terminal-copy"); return; }
            if (m_Tab == 0) foreach (var offer in m_Offers) { if (offer.BuyDust >= 0) BuyRow(offer); }
            else if (m_Tab == 1) SellRows();
            else if(m_Tab==2) Recovery();
            else Research();
        }
        private void Research()
        {
            m_Research.Present(PlayerProfileClient.Inventory, Ready, m_Busy, m_Pending != null);
        }
        private VisualElement Product(string code, string name, string detail)
        {
            var row = TerminalLayout.Clone("Components/ShopProduct", "shopProduct"); m_List.Add(row);
            var art = new StashItemArt(ContainerInventoryView.Art(code)); art.style.flexGrow = 1;
            row.Q("productArt").Add(art);
            MoonkovLocalization.Set(row.Q<Label>("productTitle"), name);
            MoonkovLocalization.Set(row.Q<Label>("productDetail"), detail);
            void Select()
            {
                m_SelectedRow?.RemoveFromClassList("archive-product-selected"); m_SelectedRow = row; m_SelectedCode = code;
                row.AddToClassList("archive-product-selected");
                MoonkovLocalization.Set(m_InspectTitle, name); MoonkovLocalization.Set(m_InspectDetail, detail);
                m_InspectArt.Clear(); var preview = new StashItemArt(ContainerInventoryView.Art(code)); preview.style.flexGrow = 1; m_InspectArt.Add(preview);
            }
            row.focusable = true;
            row.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) Select(); });
            row.RegisterCallback<FocusInEvent>(_ => Select());
            if (m_SelectedRow == null || m_SelectedCode == code) Select();
            return row;
        }
        private void Quantity(VisualElement parent, int maximum, int initial, Action<int> changed)
        {
            int quantity = initial;
            var picker = MoonkovTerminal.Row(parent); picker.AddToClassList("terminal-shop-quantity");
            Label value = null;
            var minus = MoonkovTerminal.ActionButton(picker, "−", () => { quantity = Math.Max(1, quantity - 1); MoonkovLocalization.Set(value, "× " + quantity); changed(quantity); });
            value = MoonkovTerminal.Text(picker, "× " + quantity, "terminal-copy");
            var plus = MoonkovTerminal.ActionButton(picker, "+", () => { quantity = Math.Min(maximum, quantity + 1); MoonkovLocalization.Set(value, "× " + quantity); changed(quantity); });
            minus.SetEnabled(maximum > 1 && Ready); plus.SetEnabled(maximum > 1 && Ready);
        }
        private void BuyRow(ShopOffer offer)
        {
            var definition = InventoryCatalog.Get(offer.Code);
            var row = Product(offer.Code, offer.Name, MoonkovLocalization.Format($"{definition.Width * definition.Height} GRID CELL(S)  /  {offer.BuyDust} DUST EACH"));
            if (offer.Code == "cells") MoonkovTerminal.Text(row.Q(className: "terminal-shop-description"), MoonkovLocalization.Format($"{BatteryEnergy.Capacity} ENERGY / FULL CELL"), "terminal-copy");
            MoonkovTerminal.Text(row.Q(className: "terminal-shop-description"), MoonkovItemPresentation.Details(offer.Code), "terminal-copy");
            var order = new VisualElement(); order.AddToClassList("terminal-shop-order"); row.Add(order);
            int quantity = offer.Code == "cells" ? 3 : 1;
            Button buy = null;
            void Update(int value) { quantity = value; if (buy != null) { MoonkovLocalization.Set(buy, "BUY / {0} DUST" , quantity * offer.BuyDust); buy.SetEnabled(Ready && PlayerProfileClient.StashDust >= quantity * offer.BuyDust); } }
            Quantity(order, definition.MaxStack, quantity, Update);
            buy = MoonkovTerminal.ActionButton(order, "", () => Order(ShopOperation.Buy, offer.Code, null, quantity), "terminal-primary");
            Update(quantity);
        }
        private void SellRows()
        {
            var graph = PlayerProfileClient.Inventory; int rows = 0;
            if (graph != null) foreach (var item in graph.Items.Where(i => m_Offers.Any(o => o.Code == i.Code)).ToArray())
            {
                var offer = m_Offers.First(o => o.Code == item.Code); rows++;
                string detail = graph.Children(item.Id).Any() ? (InventoryCatalog.Get(item.Code).Container ? "EMPTY CONTAINER BEFORE SELLING" : "REMOVE MODULES BEFORE SELLING") : item.EmergencySupply ? "EMERGENCY SUPPLY / CANNOT BE SOLD" : graph.RootOf(item.Id) != "stash" ? "RETURN TO STORAGE TO SELL" :
                    MoonkovLocalization.Format($"{offer.SellDust} DUST EACH  /  ") + StockDetail(item);
                var row = Product(item.Code, offer.Name, detail);
                var order = new VisualElement(); order.AddToClassList("terminal-shop-order"); row.Add(order);
                int quantity = 1; Button sell = null;
                Quantity(order, item.Quantity, quantity, value => { quantity = value; if (sell != null) MoonkovLocalization.Set(sell, "SELL / {0} DUST" , ShopCatalog.SaleDust(item, quantity, offer.SellDust)); });
                sell = MoonkovTerminal.ActionButton(order, MoonkovLocalization.Format($"SELL / {ShopCatalog.SaleDust(item, quantity, offer.SellDust)} DUST"), () => Order(ShopOperation.Sell, item.Code, item.Id, quantity));
                sell.SetEnabled(Ready && !item.EmergencySupply && graph.RootOf(item.Id) == "stash" && !graph.Children(item.Id).Any());
            }
            if (rows == 0) MoonkovTerminal.Text(m_List, "No tradeable items to sell. Recover supplies and valuables in a raid.", "terminal-copy");
        }
        private static string StockDetail(InventoryItem item)
        {
            if (HaloModules.FromCode(item.Code) != HaloModule.None) return MoonkovLocalization.Text(HaloModules.Description(item.Code));
            if (item.Code == "cells") return MoonkovLocalization.Format($"{BatteryEnergy.Stored(item)}/{item.Quantity * BatteryEnergy.Capacity} ENERGY / VALUE SCALES WITH CHARGE");
            if (ShopCatalog.IsWeapon(item.Code)) return item.LoadedAmmo < 0 ? MoonkovLocalization.Text("FULL MAGAZINE") : MoonkovLocalization.Format($"{item.LoadedAmmo} ROUNDS");
            return MoonkovItemPresentation.Details(item.Code);
        }
        private void Recovery()
        {
            var row = Product("pistol", "EMERGENCY FIELD KIT", "FREE / SIX-ROUND REVOLVER + 3 ENERGY CELLS");
            var order = new VisualElement(); order.AddToClassList("terminal-shop-order"); row.Add(order);
            bool eligible = ShopCatalog.CanClaim(PlayerProfileClient.Inventory);
            var claim = MoonkovTerminal.ActionButton(order, eligible ? "CLAIM FREE KIT" : "WEAPON ALREADY OWNED", () => Order(ShopOperation.Emergency, null, null, 0), "terminal-primary");
            claim.SetEnabled(Ready && eligible);
            MoonkovTerminal.Text(m_List, "Available when no halo weapon remains, including weapons stored inside containers. The revolver equips into the pistol slot; cells go into carried storage when space allows. Emergency supplies cannot be sold.", "terminal-copy");
        }
        private void Order(ShopOperation operation, string code, string itemId, int quantity)
        {
            if (!Ready) return;
            Execute(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = PlayerProfileClient.Inventory.Version,
                Operation = operation, Code = code, ItemId = itemId, Quantity = quantity });
        }
        private async void Execute(ShopCommand command)
        {
            if (command == null || m_Busy || m_Disposed) return;
            m_Pending = command; m_Busy = true; MoonkovLocalization.Set(m_Status, "Confirming supply order…"); Render();
            try
            {
                await PlayerProfileClient.TradeAsync(command, m_Stop.Token);
                m_Pending = null;
                if (!m_Disposed) MoonkovLocalization.Set(m_Status, command.Operation == ShopOperation.Fabricate ? "Module fabricated in storage. Go to Halo modification to install it before deployment." :
                    command.Operation == ShopOperation.Research && command.Code == AlienBiology.MooncoreResearch.Code ? "Mooncore knowledge saved permanently. Go to fabrication next; no module has been granted or equipped." :
                    command.Operation == ShopOperation.Research ? "Permanent halo research saved. It applies to all halo weapons on your next deployment." :
                    command.Operation == ShopOperation.Emergency ? "Emergency kit received. Open storage to check your pistol slot and carried cells." : "Trade confirmed. Inventory and moon dust updated.");
            }
            catch (AccountClient.InventoryRequestException ex)
            {
                if (ex.ErrorCode.StartsWith("service_")) Uncertain();
                else { m_Pending = null; if (!m_Disposed) MoonkovLocalization.Set(m_Status, ex.Message); }
            }
            catch (OperationCanceledException) { if (!m_Disposed) Uncertain(); }
            catch (System.Net.Http.HttpRequestException) { Uncertain(); }
            catch (Exception) { Uncertain(); }
            finally { m_Busy = false; if (!m_Disposed) Render(); }
        }
        private void Uncertain()
        { if (!m_Disposed) MoonkovLocalization.Set(m_Status, "Confirmation was interrupted. CHECK LAST ORDER safely retries the same order without a second charge."); }
        private void Detached(DetachFromPanelEvent evt) { if (evt.target == m_Window) Dispose(); }
        public void Dispose()
        {
            if (m_Disposed) return; m_Disposed = true;
            PlayerProfileClient.InventoryChanged -= Changed; m_Window.UnregisterCallback<DetachFromPanelEvent>(Detached);
            m_Stop.Cancel(); m_Stop.Dispose();
        }
    }
}
