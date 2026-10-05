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
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private ShopOffer[] m_Offers;
        private ShopCommand m_Pending;
        private bool m_Busy, m_Disposed;
        private int m_Tab;

        public MoonkovShopView(VisualElement window, Action openStorage, bool recovery = false)
        {
            m_Window = window; m_Tab = recovery ? 2 : 0; window.AddToClassList("terminal-shop-window");
            var head = MoonkovTerminal.Row(window); head.AddToClassList("terminal-shop-head");
            m_Balance = MoonkovTerminal.Text(head, "MOON DUST / —", "terminal-heading");
            MoonkovTerminal.ActionButton(head, "OPEN STORAGE", openStorage);
            var tabs = MoonkovTerminal.Row(window); tabs.AddToClassList("terminal-shop-tabs");
            string[] names = { "BUY", "SELL", "EMERGENCY SUPPLY" }; m_Tabs = new Button[3];
            for (int i = 0; i < names.Length; i++)
            { int tab = i; m_Tabs[i] = MoonkovTerminal.ActionButton(tabs, names[i], () => { m_Tab = tab; Render(); }); }
            m_List = new ScrollView(ScrollViewMode.Vertical); m_List.AddToClassList("terminal-shop-list"); window.Add(m_List);
            m_Status = MoonkovTerminal.Text(window, "Contacting lunar supply…", "terminal-copy"); m_Status.AddToClassList("terminal-shop-status");
            m_Retry = MoonkovTerminal.ActionButton(window, "CHECK LAST ORDER", () => Execute(m_Pending)); m_Retry.style.display = DisplayStyle.None;
            window.RegisterCallback<DetachFromPanelEvent>(Detached); AccountClient.InventoryChanged += Changed;
            Load();
        }
        private async void Load()
        {
            m_Busy = true; Render();
            try
            {
                m_Offers = await AccountClient.GetShopOffersAsync(m_Stop.Token);
                if (!m_Disposed) m_Status.text = "Moon dust is deducted only when your order is confirmed. Purchases arrive in personal storage.";
            }
            catch (OperationCanceledException) { if (!m_Disposed) m_Status.text = "Supply contact timed out. Close and reopen the supplier to reconnect."; }
            catch (Exception ex) { if (!m_Disposed) m_Status.text = ex.Message; }
            finally { m_Busy = false; if (!m_Disposed) Render(); }
        }
        private void Changed() { if (!m_Busy && !m_Disposed) Render(); }
        private bool Ready => !m_Busy && m_Pending == null && m_Offers != null && AccountClient.Inventory != null && !AccountClient.RaidActive;
        private void Render()
        {
            m_List.Clear();
            m_Balance.text = "MOON DUST / " + AccountClient.StashDust;
            for (int i = 0; i < m_Tabs.Length; i++) m_Tabs[i].EnableInClassList("terminal-tab-selected", i == m_Tab);
            m_Retry.style.display = m_Pending != null && !m_Busy ? DisplayStyle.Flex : DisplayStyle.None;
            if (AccountClient.RaidActive)
                MoonkovTerminal.Text(m_List, "Finish or leave your active raid before trading or requesting emergency supplies.", "terminal-copy");
            if (m_Offers == null)
            { MoonkovTerminal.Text(m_List, m_Busy ? "Connecting to supplier…" : "Sign in and reopen this supplier to load current stock.", "terminal-copy"); return; }
            if (m_Tab == 0) foreach (var offer in m_Offers) BuyRow(offer);
            else if (m_Tab == 1) SellRows();
            else Recovery();
        }
        private VisualElement Product(string code, string name, string detail)
        {
            var row = new VisualElement(); row.AddToClassList("terminal-shop-product"); m_List.Add(row);
            var art = new StashItemArt(ContainerInventoryView.Art(code)); art.AddToClassList("terminal-shop-art"); row.Add(art);
            var description = new VisualElement(); description.AddToClassList("terminal-shop-description"); row.Add(description);
            MoonkovTerminal.Text(description, name, "terminal-heading"); MoonkovTerminal.Text(description, detail, "terminal-copy");
            return row;
        }
        private void Quantity(VisualElement parent, int maximum, int initial, Action<int> changed)
        {
            int quantity = initial;
            var picker = MoonkovTerminal.Row(parent); picker.AddToClassList("terminal-shop-quantity");
            Label value = null;
            var minus = MoonkovTerminal.ActionButton(picker, "−", () => { quantity = Math.Max(1, quantity - 1); value.text = "× " + quantity; changed(quantity); });
            value = MoonkovTerminal.Text(picker, "× " + quantity, "terminal-copy");
            var plus = MoonkovTerminal.ActionButton(picker, "+", () => { quantity = Math.Min(maximum, quantity + 1); value.text = "× " + quantity; changed(quantity); });
            minus.SetEnabled(maximum > 1 && Ready); plus.SetEnabled(maximum > 1 && Ready);
        }
        private void BuyRow(ShopOffer offer)
        {
            var definition = InventoryCatalog.Get(offer.Code);
            var row = Product(offer.Code, offer.Name, $"{definition.Width * definition.Height} GRID CELL(S)  /  {offer.BuyDust} DUST EACH");
            if (offer.Code == "cells") MoonkovTerminal.Text(row.Q(className: "terminal-shop-description"), $"{BatteryEnergy.Capacity} ENERGY / FULL CELL", "terminal-copy");
            var order = new VisualElement(); order.AddToClassList("terminal-shop-order"); row.Add(order);
            int quantity = offer.Code == "cells" ? 3 : 1;
            Button buy = null;
            void Update(int value) { quantity = value; if (buy != null) { buy.text = $"BUY / {quantity * offer.BuyDust} DUST"; buy.SetEnabled(Ready && AccountClient.StashDust >= quantity * offer.BuyDust); } }
            Quantity(order, definition.MaxStack, quantity, Update);
            buy = MoonkovTerminal.ActionButton(order, "", () => Order(ShopOperation.Buy, offer.Code, null, quantity), "terminal-primary");
            Update(quantity);
        }
        private void SellRows()
        {
            var graph = AccountClient.Inventory; int rows = 0;
            if (graph != null) foreach (var item in graph.Items.Where(i => m_Offers.Any(o => o.Code == i.Code)).ToArray())
            {
                var offer = m_Offers.First(o => o.Code == item.Code); rows++;
                string detail = item.EmergencySupply ? "EMERGENCY SUPPLY / CANNOT BE SOLD" : graph.RootOf(item.Id) != "stash" ? "RETURN TO STORAGE TO SELL" :
                    $"{offer.SellDust} DUST EACH  /  " + (item.Code == "cells" ? $"{BatteryEnergy.Stored(item)}/{item.Quantity * BatteryEnergy.Capacity} ENERGY / VALUE SCALES WITH CHARGE" : item.LoadedAmmo < 0 ? "FULL MAGAZINE" : $"{item.LoadedAmmo} ROUNDS");
                var row = Product(item.Code, offer.Name, detail);
                var order = new VisualElement(); order.AddToClassList("terminal-shop-order"); row.Add(order);
                int quantity = 1; Button sell = null;
                Quantity(order, item.Quantity, quantity, value => { quantity = value; if (sell != null) sell.text = $"SELL / {ShopCatalog.SaleDust(item, quantity, offer.SellDust)} DUST"; });
                sell = MoonkovTerminal.ActionButton(order, $"SELL / {ShopCatalog.SaleDust(item, quantity, offer.SellDust)} DUST", () => Order(ShopOperation.Sell, item.Code, item.Id, quantity));
                sell.SetEnabled(Ready && !item.EmergencySupply && graph.RootOf(item.Id) == "stash");
            }
            if (rows == 0) MoonkovTerminal.Text(m_List, "No halo weapons or energy cells to sell. Recover supplies in a raid to restock.", "terminal-copy");
        }
        private void Recovery()
        {
            var row = Product("pistol", "EMERGENCY FIELD KIT", "FREE / SIX-ROUND REVOLVER + 3 ENERGY CELLS");
            var order = new VisualElement(); order.AddToClassList("terminal-shop-order"); row.Add(order);
            bool eligible = ShopCatalog.CanClaim(AccountClient.Inventory);
            var claim = MoonkovTerminal.ActionButton(order, eligible ? "CLAIM FREE KIT" : "WEAPON ALREADY OWNED", () => Order(ShopOperation.Emergency, null, null, 0), "terminal-primary");
            claim.SetEnabled(Ready && eligible);
            MoonkovTerminal.Text(m_List, "Available when no halo weapon remains, including weapons stored inside containers. The revolver equips into the pistol slot; cells go into carried storage when space allows. Emergency supplies cannot be sold.", "terminal-copy");
        }
        private void Order(ShopOperation operation, string code, string itemId, int quantity)
        {
            if (!Ready) return;
            Execute(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = AccountClient.Inventory.Version,
                Operation = operation, Code = code, ItemId = itemId, Quantity = quantity });
        }
        private async void Execute(ShopCommand command)
        {
            if (command == null || m_Busy || m_Disposed) return;
            m_Pending = command; m_Busy = true; m_Status.text = "Confirming supply order…"; Render();
            try
            {
                await AccountClient.TradeAsync(command, m_Stop.Token);
                m_Pending = null;
                if (!m_Disposed) m_Status.text = command.Operation == ShopOperation.Emergency ? "Emergency kit received. Open storage to check your pistol slot and carried cells." : "Trade confirmed. Inventory and moon dust updated.";
            }
            catch (AccountClient.InventoryRequestException ex)
            {
                if (ex.ErrorCode.StartsWith("service_")) Uncertain();
                else { m_Pending = null; if (!m_Disposed) m_Status.text = ex.Message; }
            }
            catch (OperationCanceledException) { if (!m_Disposed) Uncertain(); }
            catch (System.Net.Http.HttpRequestException) { Uncertain(); }
            catch (Exception) { Uncertain(); }
            finally { m_Busy = false; if (!m_Disposed) Render(); }
        }
        private void Uncertain()
        { if (!m_Disposed) m_Status.text = "Confirmation was interrupted. CHECK LAST ORDER safely retries the same order without a second charge."; }
        private void Detached(DetachFromPanelEvent evt) { if (evt.target == m_Window) Dispose(); }
        public void Dispose()
        {
            if (m_Disposed) return; m_Disposed = true;
            AccountClient.InventoryChanged -= Changed; m_Window.UnregisterCallback<DetachFromPanelEvent>(Detached);
            m_Stop.Cancel(); m_Stop.Dispose();
        }
    }
}
