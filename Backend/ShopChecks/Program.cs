using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
ShopCommand Command(InventoryGraph graph, ShopOperation operation, string? code = null, int quantity = 0, string? item = null) =>
    new() { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = graph.Version, Operation = operation, Code = code!, Quantity = quantity, ItemId = item! };
var full = InventoryGraph.Create(starter: false); full.WeaponKitVersion = 1; full.StashRows = 1;
Check(full.AddSupply("dust", 200, "stash") == InventoryError.None, "full stash fixture");
Check(ShopRules.Apply(full, Command(full, ShopOperation.Buy, "rifle", 1), out _) == "inventory_Full" && full.Count("dust") == 200, "failed purchase cannot debit resources");

var root = Path.GetFullPath(args.Single());
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Backend/MoonPersistence/appsettings.local.json")));
var settings = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Database = "postgres", Username = "moonkov_admin", Pooling = false,
    Password = File.ReadAllText(Path.Combine(root, "LocalData/postgres-admin.password")).Trim() };
string database = "moon_shop_check_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString); await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"", "\"\"")}\"", admin)) await create.ExecuteNonQueryAsync();
try
{
    settings.Database = database; await using var db = NpgsqlDataSource.Create(settings.ConnectionString);
    await new StashRepository(db).InitializeAsync();
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    string key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
        WorkingDirectory = Path.Combine(root, "Backend/MoonPersistence"), RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(typeof(InventoryRepository).Assembly.Location);
    start.Environment["ConnectionStrings__Postgres"] = settings.ConnectionString; start.Environment["ServerKey"] = key; start.Environment["Urls"] = $"http://127.0.0.1:{port}";
    using var service = Process.Start(start)!; var stdout = service.StandardOutput.ReadToEndAsync(); var stderr = service.StandardError.ReadToEndAsync();
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("X-Moon-Server-Key", key);
        bool ready = false;
        for (int i = 0; i < 100 && !service.HasExited; i++)
        { try { using var health = await http.GetAsync("internal/health"); ready = health.IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { } await Task.Delay(100); }
        Check(ready, "isolated shop service starts");
        var wire = new JsonSerializerOptions { IncludeFields = true };
        var read = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true };
        Task<HttpResponseMessage> Post(string path, object body) => http.PostAsync(path, new StringContent(JsonSerializer.Serialize(body, wire), Encoding.UTF8, "application/json"));
        async Task<Profile> ProfileOf(HttpResponseMessage response)
        { using (response) { Check(response.IsSuccessStatusCode, "successful response " + response.StatusCode); return JsonSerializer.Deserialize<Profile>(await response.Content.ReadAsStringAsync(), read)!; } }
        async Task Reject(HttpResponseMessage response, string code)
        { using (response) { Check(response.StatusCode == HttpStatusCode.Conflict, "conflict for " + code); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Check(json.RootElement.GetProperty("error").GetString() == code, "reason " + code); } }
        using (var anonymous = await http.GetAsync("auth/shop")) Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "unauthenticated catalogue rejected");
        using (var anonymous = await Post("auth/shop/trade", new ShopCommand())) Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "unauthenticated transaction rejected");
        LoginResult login;
        using (var registration = await Post("auth/register", new Credentials("shop_fixture", Guid.NewGuid().ToString("N"), null)))
        { Check(registration.IsSuccessStatusCode, "fixture account registration"); login = JsonSerializer.Deserialize<LoginResult>(await registration.Content.ReadAsStringAsync(), read)!; }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        Profile profile;
        using (var catalogue = await http.GetAsync("auth/shop"))
        {
            Check(catalogue.IsSuccessStatusCode, "catalogue is available"); using var payload = JsonDocument.Parse(await catalogue.Content.ReadAsStringAsync());
            var offers = JsonSerializer.Deserialize<ShopOffer[]>(payload.RootElement.GetProperty("offers").GetRawText(), read)!;
            Check(offers.Length == 4 && offers.Single(o => o.Code == "pistol").BuyDust == 15, "server supplies current prices");
            profile = JsonSerializer.Deserialize<Profile>(payload.RootElement.GetProperty("profile").GetRawText(), read)!;
        }
        var graph = InventoryRepository.Decode(profile.InventoryJson!); Guid player = profile.PlayerId;
        async Task Save(InventoryGraph value)
        {
            await using var connection = await db.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
            await InventoryRepository.LockPlayerAsync(connection, transaction, player, default);
            value.Version++; await InventoryRepository.WriteGraphAsync(connection, transaction, player, value, default); await transaction.CommitAsync();
        }
        async Task<InventoryGraph> Refresh()
        { profile = await ProfileOf(await http.GetAsync("auth/me")); return InventoryRepository.Decode(profile.InventoryJson!); }
        graph.AddSupply("dust", 100, "stash"); await Save(graph); graph = await Refresh();
        int dust = graph.Count("dust"), pistols = graph.Count("pistol"); var buy = Command(graph, ShopOperation.Buy, "pistol", 1);
        var twins = await Task.WhenAll(Post("auth/shop/trade", buy), Post("auth/shop/trade", buy));
        foreach (var response in twins) await ProfileOf(response);
        graph = await Refresh(); Check(graph.Count("dust") == dust - 15 && graph.Count("pistol") == pistols + 1, "concurrent retry charges/gives exactly once");
        buy.Quantity = 2; await Reject(await Post("auth/shop/trade", buy), "shop_request_conflict");
        dust = graph.Count("dust"); pistols = graph.Count("pistol");
        var race = await Task.WhenAll(Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "pistol", 1)), Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "pistol", 1)));
        Check(race.Count(r => r.IsSuccessStatusCode) == 1, "distinct same-version orders cannot both apply");
        foreach (var response in race) { if (response.IsSuccessStatusCode) await ProfileOf(response); else await Reject(response, "inventory_Stale"); }
        graph = await Refresh(); Check(graph.Count("dust") == dust - 15 && graph.Count("pistol") == pistols + 1, "concurrent order conservation");
        var stored = graph.Items.Last(i => i.Code == "pistol"); stored.LoadedAmmo = 2; await Save(graph); graph = await Refresh();
        dust = graph.Count("dust");
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "pistol", 1, stored.Id)));
        graph = InventoryRepository.Decode(profile.InventoryJson!); Check(graph.Find(stored.Id) == null && graph.Count("dust") == dust + 7, "sell exact item identity and credit");

        graph.Items.RemoveAll(i => ShopCatalog.IsWeapon(i.Code) || i.Code == "dust" || i.Code == "cells"); await Save(graph); graph = await Refresh();
        int version = graph.Version;
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "pistol", 1)), "shop_insufficient_dust");
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "cells", -1)), "shop_invalid");
        graph = await Refresh(); Check(graph.Version == version && graph.Count("dust") == 0 && graph.Count("pistol") == 0, "rejected orders leave graph untouched");
        var claim = Command(graph, ShopOperation.Emergency);
        profile = await ProfileOf(await Post("auth/shop/trade", claim)); graph = InventoryRepository.Decode(profile.InventoryJson!);
        Check(graph.Equipped("Pistol") is { EmergencySupply: true, LoadedAmmo: 6 } && graph.Count("cells", true) == 3, "free kit equips pistol and provides carried cells");
        profile = await ProfileOf(await Post("auth/shop/trade", claim));
        Check(InventoryRepository.Decode(profile.InventoryJson!).Count("cells") == 3, "emergency receipt replay does not resupply");
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Emergency)), "shop_has_weapon");
        var pistol = graph.Equipped("Pistol")!;
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "pistol", 1, pistol.Id)), "shop_emergency_supply");
        var freeCells = graph.Items.Single(i => i.Code == "cells");
        Check(graph.TryApply(new InventoryCommand { ExpectedVersion = graph.Version, ItemId = freeCells.Id, Parent = "stash", Region = "main", X = 8, Y = 8 }) == InventoryError.None, "store emergency cells");
        Check(graph.AddSupply("cells", 2, "stash") == InventoryError.None, "paid cells remain separate");
        var normal = graph.Items.Single(i => i.Code == "cells" && !i.EmergencySupply);
        Check(graph.TryApply(new InventoryCommand { ExpectedVersion = graph.Version, Operation = InventoryOperation.Merge, ItemId = freeCells.Id, TargetId = normal.Id }) == InventoryError.Invalid,
            "merging cannot launder free supplies");
        await Save(graph); graph = await Refresh();
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "cells", 1, freeCells.Id)), "shop_emergency_supply");
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "cells", 1, normal.Id)));
        graph = InventoryRepository.Decode(profile.InventoryJson!); Check(graph.Count("dust") == 2 && graph.Items.Where(i => i.Code == "cells" && i.EmergencySupply).Sum(i => i.Quantity) == 3, "only paid cells can earn dust");
        var deployment = new Deployment(player, Guid.NewGuid(), 3);
        profile = await ProfileOf(await Post("internal/deployments", deployment));
        var raid = InventoryRepository.Decode(profile.RaidInventoryJson!);
        Check(raid.Items.Where(i => i.Code == "cells").All(i => i.EmergencySupply) && raid.Count("cells") == 3, "deploy preserves emergency provenance when taking cells from stash");
        var death = LootInventoryExchange.DropOnDeath(raid);
        Check(death.Items.Where(i => i.Code == "cells" || i.Code == "pistol").All(i => i.EmergencySupply), "death bags preserve emergency provenance");
        graph = await Refresh();
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Emergency)), "raid_active");
        profile = await ProfileOf(await Post("internal/deployments/abandon", deployment)); graph = InventoryRepository.Decode(profile.InventoryJson!);
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Emergency))); graph = InventoryRepository.Decode(profile.InventoryJson!);
        Check(graph.Equipped("Pistol")?.EmergencySupply == true && graph.Count("cells", true) == 3 && graph.Count("dust") == 2, "losing the kit allows recovery without earning currency");
    }
    finally { if (!service.HasExited) service.Kill(entireProcessTree: true); await service.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); }
}
finally { NpgsqlConnection.ClearAllPools(); await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync(); }
Console.WriteLine($"Shop checks passed: {checks}. Auth, real HTTP trades, retries, concurrency, emergency recovery and provenance. Temporary database removed; project data untouched.");
