using Newtonsoft.Json;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

internal static class ProfileQueueChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Match Unity's single-threaded UI continuation ordering without an Editor.
        var previous = SynchronizationContext.Current;
        using var context = new MenuContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = RunAsync(check);
            while (!task.IsCompleted) context.Pump();
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private sealed class MenuContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> m_Work = new();
        public override void Post(SendOrPostCallback callback, object? state) => m_Work.Add((callback, state));
        public void Pump() { if (m_Work.TryTake(out var work, 50)) work.Item1(work.Item2); }
        public void Dispose() => m_Work.Dispose();
    }
    private static async Task RunAsync(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            string cells = f.View.Items.Single(i => i.Code == "cells").Id;
            var first = f.Queue.MoveAsync(Move(f.View, cells, 3));
            var second = f.Queue.MoveAsync(Move(f.View, cells, 6));
            check(f.View.Find(cells).X == 6 && f.Authority.Find(cells).X != 6 && !first.IsCompleted && !second.IsCompleted,
                "two gestures present immediately without mutating authoritative inventory");
            var a = await f.Next();
            check(f.Requests.Count == 1, "writes are serialized in the background");
            a.Finish(); await first;
            var b = await f.Next();
            check(f.View.Find(cells).X == 6 && b.Move!.ExpectedVersion == f.Authority.Version,
                "an earlier receipt preserves the later prediction and rebases its version");
            b.Finish(); await second;
            check(f.Authority.Find(cells).X == 6 && f.Queue.Count == 0, "continuous move sequence commits in order");
        }
        using (var f = new Fixture())
        {
            var buy = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            string local = f.View.Items.Single(i => i.Code == "nocturne").Id;
            var equip = f.Queue.MoveAsync(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = f.View.Version,
                ItemId = local, Parent = "equipment", Region = "Primary" });
            check(f.View.Equipped("Primary")?.Id == local && f.View.Count("dust") == 432 && f.Authority.Count("dust") == 500,
                "a pending purchase can be equipped immediately and reserves its displayed balance");
            (await f.Next()).Finish(); await buy;
            var move = await f.Next();
            check(move.Move!.ItemId != local && move.Move.ItemId == f.Authority.Items.Single(i => i.Code == "nocturne").Id,
                "a dependent move resolves the real server-created identity");
            check(f.View.Equipped("Primary").Id == local, "receipt does not replace the dragged item's display identity");
            move.Finish(); await equip;
            check(f.Authority.Equipped("Primary")?.Code == "nocturne" && f.Authority.Count("dust") == 432,
                "the purchased weapon and one deduction are authoritative");
            f.Queue.Refresh(); check(f.View.Equipped("Primary").Id == local, "idle refresh retains stable display identity");
        }
        using (var f = new Fixture())
        {
            string cells = f.View.Items.Single(i => i.Code == "cells").Id;
            var split = Move(f.View, cells, 3); split.Operation = InventoryOperation.Split; split.Quantity = 2;
            var one = f.Queue.MoveAsync(split);
            string child = f.View.Items.Single(i => i.Code == "cells" && i.Id != cells).Id;
            var two = f.Queue.MoveAsync(Move(f.View, child, 6));
            (await f.Next()).Finish(); await one;
            (await f.Next()).Finish(); await two;
            check(f.Authority.Count("cells") == 6 && f.Authority.Items.Single(i => i.Code == "cells" && i.Quantity == 2).X == 6,
                "split then move conserves supply and resolves the new stack identity");
        }
        using (var f = new Fixture())
        {
            string cells = f.View.Items.Single(i => i.Code == "cells").Id;
            var buy = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            string local = f.View.Items.Single(i => i.Code == "nocturne").Id;
            var dependent = f.Queue.MoveAsync(Move(f.View, local, 5));
            var independent = f.Queue.MoveAsync(Move(f.View, cells, 7));
            (await f.Next()).Finish("shop_insufficient_dust");
            check(await Rejected(buy) && await Rejected(dependent), "definite rejection rolls back the order and its dependent gesture");
            var remaining = await f.Next(); remaining.Finish(); await independent;
            check(f.View.Count("nocturne") == 0 && f.View.Count("dust") == 500 && f.Authority.Find(cells).X == 7,
                "rollback preserves independent later work without invented money or items");
        }
        using (var f = new Fixture())
        {
            f.Server.AddSupply("nocturne", 1, "stash", foundInRaid: true);
            f.Authority = f.Server.Clone(); f.Queue.Refresh();
            string item = f.View.Items.Single(i => i.Code == "nocturne").Id;
            var sale = f.Queue.TradeAsync(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"),
                Operation = ShopOperation.Sell, ExpectedVersion = f.View.Version, ItemId = item, Code = "nocturne", Quantity = 1 }, ShopCatalog.Offers);
            check(f.View.Find(item) == null && f.View.Count("dust") == 522 && f.Authority.Find(item) != null,
                "selling removes the visible item and reserves the refund immediately");
            var buy = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            (await f.Next()).Finish(); await sale;
            (await f.Next()).Finish(); await buy;
            check(f.Authority.Count("nocturne") == 1 && f.Authority.Count("dust") == 454,
                "queued sale then purchase spends the confirmed refund once");
        }
        using (var f = new Fixture())
        {
            var task = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            (await f.Next()).Finish(lostAfterCommit: true);
            var retry = await f.Next();
            check(f.Requests[0].Body == retry.Body, "uncertain purchase retries the identical order ID, version and body");
            retry.Finish(); await task;
            check(f.Commits == 1 && f.Authority.Count("nocturne") == 1 && f.Authority.Count("dust") == 432 && f.Queue.Error == null,
                "lost purchase receipt never charges or delivers twice");
        }
        using (var f = new Fixture())
        {
            string cells = f.View.Items.Single(i => i.Code == "cells").Id;
            var split = Move(f.View, cells, 3); split.Operation = InventoryOperation.Split; split.Quantity = 2;
            var task = f.Queue.MoveAsync(split);
            (await f.Next()).Finish(lostAfterCommit: true); await task;
            check(f.Requests.Count == 1 && f.Commits == 1 && f.View.Count("cells") == 6,
                "a lost split reply reconciles exact authoritative state without repeating the split");
        }
        using (var f = new Fixture())
        {
            var task = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            (await f.Next()).Finish(lostBeforeCommit: true);
            (await f.Next()).Finish(lostBeforeCommit: true);
            await Until(() => f.Queue.NeedsRetry);
            check(!task.IsCompleted && f.Queue.Count == 1 && f.View.Count("nocturne") == 1 && f.Authority.Count("nocturne") == 0,
                "exhausted automatic attempts retain uncertain intent without claiming persistence");
            bool blocked = false;
            try { f.Queue.TradeAsync(Buy(f.View, "pistol"), ShopCatalog.Offers); } catch (InvalidOperationException) { blocked = true; }
            check(blocked, "uncertain queue rejects additional speculative writes until reconciliation");
            var flush = f.Queue.FlushAsync(default);
            var retry = await f.Next(); check(retry.Body == f.Requests[0].Body && !flush.IsCompleted,
                "departure flush waits for confirmation and preserves the original immutable request");
            retry.Finish(); await flush; await task;
            check(f.Commits == 1 && !f.Queue.NeedsRetry && f.Queue.Count == 0 && f.Queue.Error == null,
                "manual retry drains the queue and clears the interruption message");
        }
        using (var f = new Fixture())
        {
            var task = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            var request = await f.Next();
            using var stop = new CancellationTokenSource();
            var flush = f.Queue.FlushAsync(stop.Token); stop.Cancel();
            bool cancelled = false; try { await flush; } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && !task.IsCompleted, "closing a wait does not cancel a submitted transaction");
            request.Finish(); await task;
            check(f.Authority.Count("nocturne") == 1, "queued work survives closing the originating window");
        }
        using (var f = new Fixture())
        {
            var task = f.Queue.TradeAsync(Buy(f.View, "nocturne"), ShopCatalog.Offers);
            var request = await f.Next();
            f.Queue.Refresh(); check(f.View.Count("nocturne") == 1, "background refresh does not erase pending local feedback");
            f.Queue.Dispose(); request.Finish();
            bool cancelled = false; try { await task; } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "changing profiles cancels old local continuations");
        }
        using (var f = new Fixture())
        {
            var offers = ShopCatalog.Offers.Select(o => new ShopOffer(o.Code, o.BuyDust, o.SellDust)
                { FreeSupplyRestricted = o.FreeSupplyRestricted }).ToArray();
            offers.Single(o => o.Code == "nocturne").BuyDust = 12;
            check(ShopRules.Preview(f.View, Buy(f.View, "nocturne"), offers, out var preview) == null && preview.Count("dust") == 488,
                "preview reserves the downloaded price rather than the compiled catalogue");
            offers.Single(o => o.Code == "pistol").FreeSupplyRestricted = false;
            check(ShopRules.Preview(f.View, Buy(f.View, "pistol"), offers, out preview) == null &&
                !preview.Items.Single(i => i.Code == "pistol").EmergencySupply, "old-server free item flags are preserved in prediction");
            check(ShopRules.Apply(f.Authority, Buy(f.Authority, "nocturne"), out var server) == null && server.Count("dust") == 432,
                "preview catalogue cannot change authoritative prices");
        }
        using (var f = new Fixture())
        {
            var tasks = new List<Task>(); string id = f.View.Items.Single(i => i.Code == "cells").Id;
            for (int i = 0; i < ProfileInventoryQueue.Capacity; i++) tasks.Add(f.Queue.MoveAsync(Move(f.View, id, 3 + i % 2)));
            bool bounded = false; try { f.Queue.MoveAsync(Move(f.View, id, 5)); } catch (InvalidOperationException) { bounded = true; }
            check(bounded && f.Queue.Count == ProfileInventoryQueue.Capacity, "pending memory and work are bounded");
            f.Queue.Dispose(); foreach (var task in tasks) { try { await task; } catch (OperationCanceledException) { } }
        }
    }
    private static InventoryCommand Move(InventoryGraph graph, string id, int x) => new()
    { Operation = InventoryOperation.Move, ExpectedVersion = graph.Version, ItemId = id, Parent = "stash", Region = "main", X = x, Y = 60 };
    private static ShopCommand Buy(InventoryGraph graph, string code) => new()
    { Operation = ShopOperation.Buy, ExpectedVersion = graph.Version, RequestId = Guid.NewGuid().ToString("D"), Code = code, Quantity = 1 };
    private static async Task<bool> Rejected(Task task)
    { try { await task; return false; } catch (AccountClient.InventoryRequestException) { return true; } }
    private static async Task Until(Func<bool> condition)
    { using var timeout = new CancellationTokenSource(5000); while (!condition()) await Task.Delay(5, timeout.Token); }

    private sealed class Request
    {
        public InventoryCommand? Move;
        public ShopCommand? Trade;
        public string Body = "";
        public string? Rejection;
        public bool LostBefore, LostAfter;
        public readonly TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish(string? rejection = null, bool lostBeforeCommit = false, bool lostAfterCommit = false)
        { Rejection = rejection; LostBefore = lostBeforeCommit; LostAfter = lostAfterCommit; Done.TrySetResult(true); }
    }
    private sealed class Fixture : IDisposable
    {
        public InventoryGraph Server = InventoryGraph.Create(starter: false);
        public InventoryGraph Authority;
        public InventoryGraph View => Queue.Inventory;
        public readonly ProfileInventoryQueue Queue;
        public readonly List<Request> Requests = new();
        private readonly System.Threading.Channels.Channel<Request> m_Incoming = System.Threading.Channels.Channel.CreateUnbounded<Request>();
        private readonly Dictionary<string, string> m_Receipts = new();
        public int Commits;
        public Fixture()
        {
            Server.AddSupply("dust", 500, "stash"); Server.AddSupply("cells", 6, "stash"); Authority = Server.Clone();
            Queue = new ProfileInventoryQueue(() => Authority, () => true,
                (move, ct) => Send(new Request { Move = move, Body = JsonConvert.SerializeObject(move) }, ct),
                (trade, ct) => Send(new Request { Trade = trade, Body = JsonConvert.SerializeObject(trade) }, ct),
                ct => { Authority = Server.Clone(); Queue?.Refresh(); return Task.CompletedTask; },
                ex => ex is AccountClient.InventoryRequestException);
        }
        public async Task<Request> Next()
        { using var timeout = new CancellationTokenSource(5000); return await m_Incoming.Reader.ReadAsync(timeout.Token); }
        private async Task Send(Request request, CancellationToken ct)
        {
            Requests.Add(request); m_Incoming.Writer.TryWrite(request);
            await request.Done.Task.WaitAsync(ct);
            if (request.Rejection != null) throw new AccountClient.InventoryRequestException(request.Rejection);
            if (request.LostBefore) throw new HttpRequestException("fixture lost request");
            if (request.Trade != null && m_Receipts.TryGetValue(request.Trade.RequestId, out var receipt))
            {
                if (receipt != request.Body) throw new AccountClient.InventoryRequestException("shop_request_conflict");
            }
            else
            {
                if (request.Move != null)
                {
                    var error = Server.TryApply(request.Move);
                    if (error != InventoryError.None) throw new AccountClient.InventoryRequestException("inventory_" + error);
                }
                else
                {
                    string error = ShopRules.Apply(Server, request.Trade!, out var updated);
                    if (error != null) throw new AccountClient.InventoryRequestException(error);
                    Server = updated; m_Receipts.Add(request.Trade!.RequestId, request.Body);
                }
                Commits++;
            }
            if (request.LostAfter) throw new HttpRequestException("fixture lost receipt");
            Authority = Server.Clone(); Queue.Refresh();
        }
        public void Dispose() => Queue.Dispose();
    }
}
