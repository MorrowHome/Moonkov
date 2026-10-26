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
await ClientCatalogChecks.Run(Check);
QuickMoveChecks.Run(Check);
foreach (string code in new[] { "rifle", "pistol", "shotgun", "sniper", "cells", "medkit", "ration", "water", "oxygen_canister" })
{
    Check(ShopCatalog.Find(code).BuyDust == 0 && ShopCatalog.Find(code).FreeSupplyRestricted, code + " remains free with an advertised sale restriction");
    var empty = InventoryGraph.Create(starter: false);
    Check(ShopRules.Apply(empty, Command(empty, ShopOperation.Buy, code, 1), out var purchased) == null &&
        purchased.Count("dust") == 0 && purchased.Count(code) == 1, code + " can be bought with no dust");
    var item = purchased.Items.Single(i => i.Code == code);
    Check(item.EmergencySupply, code + " free purchase carries the sale restriction");
    Check(ShopRules.Apply(purchased, Command(purchased, ShopOperation.Sell, code, 1, item.Id), out _) == "shop_emergency_supply" &&
        purchased.Find(item.Id) != null && purchased.Count("dust") == 0,
        code + " cannot be sold for free currency");
    Check(purchased.AddSupply(code, 1, "stash", foundInRaid: true) == InventoryError.None,
        code + " found item fixture");
    var found = purchased.Items.Single(i => i.Code == code && i.FoundInRaid);
    Check(!found.EmergencySupply && ShopRules.Apply(purchased, Command(purchased, ShopOperation.Sell, code, 1, found.Id), out var sold) == null &&
        sold.Find(found.Id) == null && sold.Count("dust") == ShopCatalog.Find(code).SellDust,
        code + " found in raid remains sellable");
}
var provenance = InventoryGraph.Create(starter: false);
Check(ShopRules.Apply(provenance, Command(provenance, ShopOperation.Buy, "cells", 3), out var suppliedCells) == null,
    "free cell stack can be acquired");
Check(suppliedCells.AddSupply("cells", 1, "stash") == InventoryError.None,
    "pre-existing normal cell fixture");
var freeStack = suppliedCells.Items.Single(i => i.Code == "cells" && i.EmergencySupply);
var oldStack = suppliedCells.Items.Single(i => i.Code == "cells" && !i.EmergencySupply);
Check(suppliedCells.TryApply(new InventoryCommand { ExpectedVersion = suppliedCells.Version, Operation = InventoryOperation.Merge,
    ItemId = freeStack.Id, TargetId = oldStack.Id }) == InventoryError.Invalid,
    "free cells cannot merge into older sellable stock");
var splitCandidate = freeStack.Clone();
splitCandidate.Id = Guid.NewGuid().ToString("D");
Check(suppliedCells.FindSpace(splitCandidate, "stash", out var splitRegion, out int splitX, out int splitY), "split space exists");
Check(suppliedCells.TryApply(new InventoryCommand { ExpectedVersion = suppliedCells.Version, Operation = InventoryOperation.Split,
    ItemId = freeStack.Id, Quantity = 1, Parent = "stash", Region = splitRegion, X = splitX, Y = splitY }) == InventoryError.None &&
    suppliedCells.Items.Where(i => i.Code == "cells" && i.Id != oldStack.Id).All(i => i.EmergencySupply),
    "split preserves the free-supply restriction");
Check(ShopRules.Apply(suppliedCells, Command(suppliedCells, ShopOperation.Sell, "cells", 1, oldStack.Id), out var soldOld) == null &&
    soldOld.Count("dust") == ShopCatalog.Find("cells").SellDust,
    "existing unmarked stock remains sellable");
Check(suppliedCells.TryApply(new InventoryCommand { ExpectedVersion = suppliedCells.Version, ItemId = freeStack.Id,
    Parent = "pockets", Region = "1" }) == InventoryError.None, "free cells can be carried");
var raidCells = suppliedCells.ExtractLoadout();
var encodedRaid = JsonSerializer.Serialize(raidCells, new JsonSerializerOptions { IncludeFields = true });
var reloadedRaid = JsonSerializer.Deserialize<InventoryGraph>(encodedRaid, new JsonSerializerOptions { IncludeFields = true })!;
Check(reloadedRaid.Validate() == InventoryError.None && reloadedRaid.Find(freeStack.Id)?.EmergencySupply == true &&
    LootInventoryExchange.DropOnDeath(reloadedRaid.Clone()).Find(freeStack.Id)?.EmergencySupply == true,
    "deployment, serialization and death preserve the restriction");
Check(suppliedCells.ReturnLoadout(reloadedRaid) == InventoryError.None && suppliedCells.Find(freeStack.Id)?.EmergencySupply == true,
    "successful extraction preserves the restriction");
Check(ShopRules.Apply(suppliedCells, Command(suppliedCells, ShopOperation.Sell, "cells", 1, freeStack.Id), out _) == "shop_emergency_supply",
    "extracted free supplies cannot be sold");
var funded = InventoryGraph.Create(starter: false);
Check(funded.AddSupply("dust", 100, "stash") == InventoryError.None &&
    ShopRules.Apply(funded, Command(funded, ShopOperation.Buy, "nocturne", 1), out var paid) == null &&
    !paid.Items.Single(i => i.Code == "nocturne").EmergencySupply && paid.Count("dust") == 32,
    "paid purchases stay sellable and debit their price");
var emptyAccount = InventoryGraph.Create(starter: false);
Check(ShopRules.Apply(emptyAccount, Command(emptyAccount, ShopOperation.Buy, "nocturne", 1), out _) == "shop_insufficient_dust",
    "priced weapons still require dust");
Check(ShopRules.Apply(emptyAccount, Command(emptyAccount, ShopOperation.Buy, "cells", -1), out _) == "shop_invalid",
    "purchases still reject invalid quantities");
Check(ShopRules.Apply(emptyAccount, Command(emptyAccount, ShopOperation.Buy, "alien_tissue", 1), out _) == "shop_invalid",
    "sell-only loot cannot be purchased");
Check(ShopRules.Apply(emptyAccount, Command(emptyAccount, ShopOperation.Emergency), out var emergency) == null &&
    emergency.Equipped("Pistol") is { EmergencySupply: true, LoadedAmmo: 6 } && emergency.Count("cells", true) == 3,
    "emergency kit remains distinct from normal free purchases");
foreach (var item in emergency.Items.Where(i => i.Code == "pistol" || i.Code == "cells"))
    Check(ShopRules.Apply(emergency, Command(emergency, ShopOperation.Sell, item.Code, 1, item.Id), out _) == "shop_emergency_supply",
        "emergency " + item.Code + " cannot be sold");
var full = InventoryGraph.Create(starter: false); full.WeaponKitVersion = 1; full.StashRows = 1;
Check(full.AddSupply("dust", 200, "stash") == InventoryError.None, "full stash fixture");
foreach (string code in new[] { "rifle", "nocturne" })
    Check(ShopRules.Apply(full, Command(full, ShopOperation.Buy, code, 1), out _) == "inventory_Full" && full.Count("dust") == 200,
        "failed purchase cannot debit resources: " + code);
if (args is ["--rules-only"])
{
    Console.WriteLine($"Shop rules/client checks passed: {checks}. In-memory transport; no service or database started.");
    return;
}

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
            Check(ShopCatalog.ValidOffers(offers) && offers.Select(o => (o.Code, o.BuyDust, o.SellDust, o.FreeSupplyRestricted)).SequenceEqual(
                ShopCatalog.Offers.Select(o => (o.Code, o.BuyDust, o.SellDust, o.FreeSupplyRestricted))), "server supplies the complete current catalogue and restrictions");
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
        graph = await Refresh(); Check(graph.Count("dust") == dust && graph.Count("pistol") == pistols + 1 &&
            graph.Items.Last(i => i.Code == "pistol").EmergencySupply, "concurrent retry gives one restricted free pistol without charging");
        buy.Quantity = 2; await Reject(await Post("auth/shop/trade", buy), "shop_request_conflict");
        dust = graph.Count("dust"); pistols = graph.Count("pistol");
        var race = await Task.WhenAll(Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "pistol", 1)), Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "pistol", 1)));
        Check(race.Count(r => r.IsSuccessStatusCode) == 1, "distinct same-version orders cannot both apply");
        foreach (var response in race) { if (response.IsSuccessStatusCode) await ProfileOf(response); else await Reject(response, "inventory_Stale"); }
        graph = await Refresh(); Check(graph.Count("dust") == dust && graph.Count("pistol") == pistols + 1, "concurrent free order conservation");
        Check(graph.AddSupply("pistol", 1, "stash", foundInRaid: true) == InventoryError.None, "found pistol fixture");
        var stored = graph.Items.Last(i => i.Code == "pistol" && i.FoundInRaid); stored.LoadedAmmo = 2; await Save(graph); graph = await Refresh();
        dust = graph.Count("dust");
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "pistol", 1, stored.Id)));
        graph = InventoryRepository.Decode(profile.InventoryJson!); Check(graph.Find(stored.Id) == null && graph.Count("dust") == dust + 7, "sell exact item identity and credit");

        graph.Items.RemoveAll(i => ShopCatalog.IsWeapon(i.Code) || i.Code == "dust" || i.Code == "cells"); await Save(graph); graph = await Refresh();
        int version = graph.Version;
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "nocturne", 1)), "shop_insufficient_dust");
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "cells", -1)), "shop_invalid");
        graph = await Refresh(); Check(graph.Version == version && graph.Count("dust") == 0 && graph.Count("pistol") == 0, "rejected orders leave graph untouched");
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "pistol", 1)));
        graph = InventoryRepository.Decode(profile.InventoryJson!);
        var freePistol = graph.Items.Single(i => i.Code == "pistol");
        Check(graph.Count("dust") == 0 && freePistol.EmergencySupply, "free pistol purchase is restricted at zero dust");
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "pistol", 1, freePistol.Id)), "shop_emergency_supply");
        graph.Items.RemoveAll(i => i.Code == "pistol"); await Save(graph); graph = await Refresh();
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
        await Save(graph); graph = await Refresh();
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Buy, "cells", 2)));
        graph = InventoryRepository.Decode(profile.InventoryJson!);
        Check(graph.Items.Where(i => i.Code == "cells").Sum(i => i.Quantity) == 5 &&
            graph.Items.Where(i => i.Code == "cells").All(i => i.EmergencySupply), "free purchases retain the emergency restriction");
        Check(graph.AddSupply("cells", 2, "stash") == InventoryError.None, "older sellable cell fixture");
        var normal = graph.Items.Single(i => i.Code == "cells" && !i.EmergencySupply);
        Check(graph.TryApply(new InventoryCommand { ExpectedVersion = graph.Version, Operation = InventoryOperation.Merge, ItemId = freeCells.Id, TargetId = normal.Id }) == InventoryError.Invalid,
            "merging cannot launder free supplies");
        await Save(graph); graph = await Refresh();
        await Reject(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "cells", 1, freeCells.Id)), "shop_emergency_supply");
        profile = await ProfileOf(await Post("auth/shop/trade", Command(graph, ShopOperation.Sell, "cells", 1, normal.Id)));
        graph = InventoryRepository.Decode(profile.InventoryJson!); Check(graph.Count("dust") == 2 && graph.Items.Where(i => i.Code == "cells" && i.EmergencySupply).Sum(i => i.Quantity) == 5, "older sellable cells retain value while free cells cannot be sold");
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
