// 主菜单的仓库操作队列。拖格、买卖和改装先在本地副本上算出要显示的样子，真正的写入仍逐条等待房主、本地档案或账号服务确认。
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // Menu-only prediction. Authoritative profiles and request bodies remain separate.
    // A single writer spans storage, suppliers and workshop, even when a window closes.
    public sealed class ProfileInventoryQueue : IDisposable {
        private sealed class Pending {
            public InventoryCommand Move, WireMove;
            public ShopCommand Trade, WireTrade;
            public ShopOffer[] Offers;
            public readonly List<string> Created = new();

            public readonly TaskCompletionSource<bool> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public InventoryGraph Expected;
            public bool Uncertain;
        }

        private readonly Func<InventoryGraph> m_Read;
        private readonly Func<bool> m_Writable;
        private readonly Func<InventoryCommand, CancellationToken, Task> m_Move;
        private readonly Func<ShopCommand, CancellationToken, Task> m_Trade;
        private readonly Func<CancellationToken, Task> m_Refresh;
        private readonly Func<Exception, bool> m_Rejected;
        private readonly CancellationTokenSource m_Stop = new();
        private readonly List<Pending> m_Pending = new();
        private readonly Dictionary<string, string> m_ToServer = new();
        private readonly Dictionary<string, string> m_ToDisplay = new();
        private Task m_Drain;
        private bool m_Disposed;
        public const int Capacity = 64;
        public InventoryGraph Inventory { get; private set; }
        public int Count => m_Pending.Count;
        public bool NeedsRetry { get; private set; }
        public Exception Error { get; private set; }
        public event Action Changed;

        public ProfileInventoryQueue(
            Func<InventoryGraph> read,
            Func<bool> writable,
            Func<InventoryCommand, CancellationToken, Task> move,
            Func<ShopCommand, CancellationToken, Task> trade,
            Func<CancellationToken, Task> refresh,
            Func<Exception, bool> rejected) {
            m_Read = read;
            m_Writable = writable;
            m_Move = move;
            m_Trade = trade;
            m_Refresh = refresh;
            m_Rejected = rejected;
            Inventory = Display(read());
        }

        public Task MoveAsync(InventoryCommand command) { return Enqueue(new Pending { Move = Copy(command) }); }

        public Task TradeAsync(ShopCommand command, ShopOffer[] offers) {
            return Enqueue(new Pending {
                Trade = Copy(command),
                Offers = offers?.Select(o => new ShopOffer {
                    Code = o.Code, Name = o.Name, BuyDust = o.BuyDust, SellDust = o.SellDust,
                    FreeSupplyRestricted = o.FreeSupplyRestricted
                }).ToArray()
            });
        }

        // 先拿界面正在显示的版本验证操作，再在副本里立即预览。这样连续拖动时，后一条操作可以参照前一条已经预览的结果。
        private Task Enqueue(Pending pending) {
            if (m_Disposed) throw new ObjectDisposedException(nameof(ProfileInventoryQueue));
            if (!m_Writable()) throw new AccountClient.InventoryRequestException("raid_active");
            if (NeedsRetry)
                throw new InvalidOperationException(
                    "Inventory confirmation is interrupted. Retry synchronization first.");
            if (Count >= Capacity)
                throw new InvalidOperationException("Inventory synchronization is catching up. Please wait briefly.");
            if (Inventory == null) throw new InvalidOperationException("Loading account inventory…");
            int version = pending.Move?.ExpectedVersion ?? pending.Trade.ExpectedVersion;
            if (version != Inventory.Version) throw new AccountClient.InventoryRequestException("inventory_Stale");
            var next = Predict(pending, Inventory);
            m_Pending.Add(pending);
            Inventory = next;
            Error = null;
            Start();
            Changed?.Invoke();
            return pending.Completion.Task;
        }

        // A profile refresh during a POST must not replay that POST over its own receipt.
        public void Refresh() {
            if (m_Disposed || Count > 0) return;
            Inventory = Display(m_Read());
            Changed?.Invoke();
        }

        public async Task FlushAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            NeedsRetry = false;
            Start();
            while (m_Drain != null && !m_Drain.IsCompleted) {
                var cancelled = new TaskCompletionSource<bool>();
                using (ct.Register(() => cancelled.TrySetCanceled())) {
                    var completion = await Task.WhenAny(m_Drain, cancelled.Task);
                    await completion;
                }
            }
            ct.ThrowIfCancellationRequested();
            if (Count > 0)
                throw new InvalidOperationException(
                    "Inventory confirmation is interrupted. Retry synchronization first.", Error);
        }

        private void Start() {
            if (!m_Disposed && !NeedsRetry && Count > 0 && (m_Drain == null || m_Drain.IsCompleted)) m_Drain = Drain();
        }

        private async Task Drain() {
            // Publish local feedback before any synchronous offline completion.
            await Task.Yield();
            while (!m_Disposed && Count > 0) {
                var pending = m_Pending[0];
                if (pending.WireMove == null && pending.WireTrade == null) {
                    Rebuild();
                    if (Count == 0) {
                        Changed?.Invoke();
                        break;
                    }
                    pending = m_Pending[0];
                    // 发送队首前，从最新的已确认库存计算预期结果。请求用这份库存的版本和真实物品 ID，界面刚拆堆产生的临时 ID 要先换回去。
                    pending.Expected = Predict(pending, Display(m_Read()));
                    int version = m_Read().Version;
                    if (pending.Move != null) {
                        pending.WireMove = Copy(pending.Move);
                        pending.WireMove.ExpectedVersion = version;
                        pending.WireMove.ItemId = ServerId(pending.WireMove.ItemId);
                        pending.WireMove.Parent = ServerId(pending.WireMove.Parent);
                        pending.WireMove.TargetId = ServerId(pending.WireMove.TargetId);
                    } else {
                        pending.WireTrade = Copy(pending.Trade);
                        pending.WireTrade.ExpectedVersion = version;
                        pending.WireTrade.ItemId = ServerId(pending.WireTrade.ItemId);
                    }
                }
                bool completed = false;
                for (int attempt = 0; attempt < 2 && !m_Disposed; attempt++)
                    try {
                        if (pending.WireMove != null)
                            await m_Move(pending.WireMove, m_Stop.Token);
                        else
                            await m_Trade(pending.WireTrade, m_Stop.Token);
                        if (m_Disposed) return;
                        Accept(pending);
                        completed = true;
                        break;
                    } catch (Exception ex) {
                        if (m_Disposed) return;
                        // 明确拒绝就撤掉这条预览。超时之类的情况无法确认服务端有没有写入，先刷新并比对库存，再决定是否保留原请求重试。
                        bool rejected = m_Rejected(ex);
                        // A lost move response has no order receipt. Its unchanged version
                        // makes retry safe; only an exact resulting snapshot resolves it.
                        if (pending.Uncertain && pending.Move != null && Matches(pending)) {
                            Accept(pending);
                            completed = true;
                            break;
                        }
                        if (rejected) {
                            m_Pending.RemoveAt(0);
                            Error = ex;
                            Rebuild();
                            Changed?.Invoke();
                            pending.Completion.TrySetException(ex);
                            try { await m_Refresh(m_Stop.Token); } catch (Exception) {
                                /* rejection remains definitive */
                            }
                            if (m_Disposed) return;
                            Rebuild();
                            Changed?.Invoke();
                            completed = true;
                            break;
                        }
                        pending.Uncertain = true;
                        Error = ex;
                        try { await m_Refresh(m_Stop.Token); } catch (Exception) {
                            /* keep the identical request pending */
                        }
                        if (m_Disposed) return;
                        if (pending.Move != null && Matches(pending)) {
                            Accept(pending);
                            completed = true;
                            break;
                        }
                        if (attempt == 0) await Task.Delay(250, m_Stop.Token);
                    }
                if (!completed && !m_Disposed) {
                    NeedsRetry = true;
                    Changed?.Invoke();
                    break;
                }
            }
        }

        private void Accept(Pending pending) {
            BindCreated(pending, m_Read());
            m_Pending.RemoveAt(0);
            NeedsRetry = false;
            if (pending.Uncertain) Error = null;
            Rebuild();
            pending.Completion.TrySetResult(true);
            Changed?.Invoke();
        }

        // 每次正式库存变化后，把还没确认的操作沿着队列顺序重放。依赖一条失败操作的后续动作，这里可能会被一起判无效。
        private void Rebuild() {
            var graph = Display(m_Read());
            for (int i = 0; i < m_Pending.Count;) {
                var pending = m_Pending[i];
                try {
                    if (!m_Writable()) throw new AccountClient.InventoryRequestException("raid_active");
                    graph = Predict(pending, graph);
                    i++;
                } catch (AccountClient.InventoryRequestException ex) {
                    m_Pending.RemoveAt(i);
                    if (Error == null) Error = ex;
                    pending.Completion.TrySetException(ex);
                }
            }
            Inventory = graph;
            if (Count == 0 && graph != null)
                foreach (string id in m_ToDisplay.Keys.Where(id => m_Read().Find(id) == null).ToArray()) {
                    m_ToServer.Remove(m_ToDisplay[id]);
                    m_ToDisplay.Remove(id);
                }
        }

        private InventoryGraph Predict(Pending pending, InventoryGraph graph) {
            if (graph == null) throw new AccountClient.InventoryRequestException("login_expired");
            InventoryGraph next;
            if (pending.Move != null) {
                var command = Copy(pending.Move);
                command.ExpectedVersion = graph.Version;
                next = graph.Clone();
                var error = next.TryApply(command);
                if (error != InventoryError.None)
                    throw new AccountClient.InventoryRequestException("inventory_" + error);
            } else {
                var command = Copy(pending.Trade);
                command.ExpectedVersion = graph.Version;
                string error = ShopRules.Preview(graph, command, pending.Offers, out next);
                if (error != null) throw new AccountClient.InventoryRequestException(error);
            }
            // 拆堆、购买会新建物品。给预览里的新物品保留稳定的临时 ID，收到回执后再把它和正式 ID 对上，后面的拖放才能继续找到它。
            var previous = new HashSet<string>(graph.Items.Select(item => item.Id));
            var created = next.Items.Where(item => !previous.Contains(item.Id)).ToArray();
            var renamed = new Dictionary<string, string>();
            for (int i = 0; i < created.Length; i++) {
                if (pending.Created.Count <= i) pending.Created.Add(Guid.NewGuid().ToString("D"));
                renamed.Add(created[i].Id, pending.Created[i]);
                created[i].Id = pending.Created[i];
            }
            foreach (var item in next.Items)
                if (item.Parent != null && renamed.TryGetValue(item.Parent, out string parent))
                    item.Parent = parent;
            return next;
        }

        private Dictionary<string, string> CreatedMap(Pending pending, InventoryGraph server) {
            var result = new Dictionary<string, string>();
            if (server == null || pending.Expected == null) return result;
            foreach (string displayId in pending.Created) {
                var expected = pending.Expected.Find(displayId);
                if (expected == null) continue;
                var matches = server.Items.Where(item =>
                    item.Code == expected.Code && item.Parent == ServerId(expected.Parent) &&
                    item.Region == expected.Region && item.X == expected.X && item.Y == expected.Y &&
                    item.Rotated == expected.Rotated && item.Quantity == expected.Quantity).ToArray();
                if (matches.Length == 1 && !m_ToDisplay.ContainsKey(matches[0].Id)) result[matches[0].Id] = displayId;
            }
            return result;
        }

        private void BindCreated(Pending pending, InventoryGraph server) {
            // Only the immediate receipt can unambiguously identify newly created items.
            if (server?.Version != pending.Expected?.Version) return;
            foreach (var pair in CreatedMap(pending, server)) {
                m_ToDisplay[pair.Key] = pair.Value;
                m_ToServer[pair.Value] = pair.Key;
            }
        }

        private bool Matches(Pending pending) {
            var server = m_Read();
            if (server == null || server.Version != pending.Expected.Version) return false;
            var actual = Display(server, CreatedMap(pending, server));
            var expected = pending.Expected.Clone();
            actual.Items.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            expected.Items.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return JsonConvert.SerializeObject(actual) == JsonConvert.SerializeObject(expected);
        }

        private string ServerId(string id) {
            return id != null && m_ToServer.TryGetValue(id, out string server) ? server : id;
        }

        private InventoryGraph Display(InventoryGraph graph, Dictionary<string, string> created = null) {
            if (graph == null) return null;
            var copy = graph.Clone();

            string Id(string id) {
                return id != null && m_ToDisplay.TryGetValue(id, out string display) ? display :
                    id != null && created != null && created.TryGetValue(id, out display) ? display : id;
            }

            foreach (var item in copy.Items) {
                item.Id = Id(item.Id);
                item.Parent = Id(item.Parent);
            }
            return copy;
        }

        private static InventoryCommand Copy(InventoryCommand command) {
            return command == null
                ? throw new ArgumentNullException(nameof(command))
                : new InventoryCommand {
                    Operation = command.Operation, ExpectedVersion = command.ExpectedVersion, ItemId = command.ItemId,
                    Parent = command.Parent,
                    Region = command.Region, TargetId = command.TargetId, X = command.X, Y = command.Y,
                    Quantity = command.Quantity, Rotated = command.Rotated
                };
        }

        private static ShopCommand Copy(ShopCommand command) {
            return command == null
                ? throw new ArgumentNullException(nameof(command))
                : new ShopCommand {
                    RequestId = command.RequestId, ExpectedVersion = command.ExpectedVersion,
                    Operation = command.Operation,
                    Code = command.Code, ItemId = command.ItemId, Quantity = command.Quantity
                };
        }

        public void Dispose() {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Stop.Cancel();
            foreach (var pending in m_Pending) pending.Completion.TrySetCanceled();
            m_Pending.Clear();
            Changed = null;
        }
    }
}