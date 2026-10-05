using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
InventoryGraph Cells(int count, int charge = -1, string parent = "pockets", bool emergency = false)
{
    var graph = InventoryGraph.Create(starter: false); graph.WeaponKitVersion = 1;
    Check(graph.AddSupply("cells", count, parent, emergencySupply: emergency, cellCharge: charge) == InventoryError.None, "cell fixture");
    return graph;
}
InventoryItem Cell(InventoryGraph graph) => graph.Items.First(i => i.Code == "cells");
var left = Cells(1); string batteryId = Cell(left).Id;
Check(left.TryRecharge(0, 6, 1, out int rounds) && rounds == 6 && left.CellEnergy() == 94 && left.Count("cells") == 1 && Cell(left).Id == batteryId, "revolver retains battery with 94 energy");
Check(left.TryRecharge(4, 6, 1, out rounds) && rounds == 6 && left.CellEnergy() == 92, "top-up charges only missing rounds");
int unchanged = left.Version;
Check(!left.TryRecharge(6, 6, 1, out _) && left.Version == unchanged && left.CellEnergy() == 92, "full magazine is free and does not reload");
var rifle = Cells(1); Check(rifle.TryRecharge(0, 30, 3, out rounds) && rounds == 30 && rifle.CellEnergy() == 10, "rifle full charge costs 90");
var shotgun = Cells(1); Check(shotgun.TryRecharge(0, 4, 12, out rounds) && rounds == 4 && shotgun.CellEnergy() == 52, "shotgun charges trigger rounds, not pellets");
var partial = Cells(1, 8); Check(partial.TryRecharge(0, 30, 3, out rounds) && rounds == 2 && partial.CellEnergy() == 2, "partial recharge uses whole rounds and preserves remainder");
unchanged = partial.Version; Check(!partial.TryRecharge(2, 30, 3, out _) && partial.Version == unchanged && partial.CellEnergy() == 2, "insufficient energy does not mutate inventory");
Check(partial.TryRecharge(0, 6, 1, out rounds) && rounds == 2 && partial.Count("cells") == 0, "depleted disposable disappears");
var backpack = InventoryGraph.Create(); backpack.AddSupply("cells", 1, backpack.Equipped("Backpack")!.Id);
Check(!backpack.TryRecharge(0, 30, 3, out _) && backpack.CellEnergy() == 100 && backpack.CellEnergy(accessibleOnly: true) == 0, "backpack energy cannot be used remotely");
var mixed = Cells(1, 2); mixed.AddSupply("cells", 1, "pockets", foundInRaid: true);
Check(mixed.TryRecharge(0, 4, 12, out rounds) && rounds == 4 && mixed.CellEnergy() == 54 && mixed.Count("cells") == 1, "energy spans accessible stacks without loss");

var split = Cells(3, 40); var original = Cell(split);
Check(split.TryApply(new InventoryCommand { ExpectedVersion = split.Version, Operation = InventoryOperation.Split, ItemId = original.Id,
    Quantity = 1, Parent = "pockets", Region = "2" }) == InventoryError.None, "split partial stack");
Check(split.CellEnergy() == 240 && split.Find(original.Id)!.CellCharge == 40 && split.Items.Single(i => i.Code == "cells" && i.Id != original.Id).CellCharge == -1, "split keeps one partial battery and transfers full cell");
var fullCell = split.Items.Single(i => i.Code == "cells" && i.Id != original.Id);
Check(split.TryApply(new InventoryCommand { ExpectedVersion = split.Version, Operation = InventoryOperation.Merge, ItemId = fullCell.Id, TargetId = original.Id }) == InventoryError.None && split.CellEnergy() == 240, "merge full cells conserves partial charge");
split.AddSupply("cells", 1, "stash", cellCharge: 20); var secondPartial = split.Items.Single(i => i.Code == "cells" && i.Parent == "stash");
unchanged = split.Version;
Check(split.TryApply(new InventoryCommand { ExpectedVersion = split.Version, Operation = InventoryOperation.Merge, ItemId = secondPartial.Id, TargetId = original.Id }) == InventoryError.Invalid && split.CellEnergy() == 260 && split.Version == unchanged, "two partial batteries cannot manufacture a full one");
Check(split.AddSupply("cells", 2, "pockets") == InventoryError.None && split.CellEnergy() == 460, "adding full cells retains existing charge");
var bad = split.Clone(); Cell(bad).CellCharge = 101; Check(bad.Validate() == InventoryError.Invalid, "reject over-capacity charge");
bad = split.Clone(); Cell(bad).CellCharge = 0; Check(bad.Validate() == InventoryError.Invalid, "reject exhausted persisted batteries");
string legacy = InventoryRepository.Encode(Cells(2)).Replace("\"CellCharge\":-1,", "");
var migrated = InventoryRepository.Decode(legacy); Check(migrated.CellEnergy() == 200 && migrated.Validate() == InventoryError.None, "missing charge field means full legacy batteries");
var roundTrip = InventoryRepository.Decode(InventoryRepository.Encode(split)); Check(roundTrip.CellEnergy() == 460 && Cell(roundTrip).CellCharge == 40, "serialized partial charge survives");
var sale = Cells(2, 40, "stash"); string saleId = Cell(sale).Id;
Check(ShopRules.Apply(sale, new ShopCommand { RequestId = Guid.NewGuid().ToString(), Operation = ShopOperation.Sell, ExpectedVersion = sale.Version, Code = "cells", ItemId = saleId, Quantity = 1 }, out var sold) == null && sold.Count("dust") == 2 && sold.CellEnergy() == 40, "selling one full cell retains the partial battery");
Check(ShopRules.Apply(sold, new ShopCommand { RequestId = Guid.NewGuid().ToString(), Operation = ShopOperation.Sell, ExpectedVersion = sold.Version, Code = "cells", ItemId = saleId, Quantity = 1 }, out var empty) == null && empty.Count("dust") == 2 && empty.Count("cells") == 0, "40 energy cell yields zero dust, not full value");
var death = Cells(2, 35, "pockets", true); death.Items.RemoveAll(i => i.Id == "stash");
var bag = LootInventoryExchange.DropOnDeath(death); Check(bag.CellEnergy() == 135 && Cell(bag).EmergencySupply, "death drop preserves energy and emergency provenance");
var looter = InventoryGraph.Create(false, false);
Check(LootInventoryExchange.TryApply(looter, bag, new InventoryCommand { ExpectedVersion = looter.Version, ItemId = Cell(bag).Id, Parent = "pockets", Region = "1" }, bag.Version) == InventoryError.None && looter.CellEnergy() == 135 && Cell(looter).CellCharge == 35, "looting preserves exact battery charge");

var root = Path.GetFullPath(args.Single());
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Backend/MoonPersistence/appsettings.local.json")));
var settings = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Database = "postgres", Username = "moonkov_admin", Pooling = false,
    Password = File.ReadAllText(Path.Combine(root, "LocalData/postgres-admin.password")).Trim() };
string database = "moon_energy_check_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString); await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"", "\"\"")}\"", admin)) await create.ExecuteNonQueryAsync();
try
{
    settings.Database = database; await using var db = NpgsqlDataSource.Create(settings.ConnectionString);
    var store = new StashRepository(db); await store.InitializeAsync(); var repository = new InventoryRepository(db);
    Guid player = Guid.NewGuid();
    await using (var seed = db.CreateCommand("INSERT INTO players(id,display_name) VALUES($1,'energy_fixture')"))
    { seed.Parameters.AddWithValue(player); await seed.ExecuteNonQueryAsync(); }
    async Task Save(InventoryGraph graph)
    {
        await using var connection = await db.OpenConnectionAsync(); await using var tx = await connection.BeginTransactionAsync();
        await InventoryRepository.LockPlayerAsync(connection, tx, player, default);
        await InventoryRepository.WriteGraphAsync(connection, tx, player, graph, default); await tx.CommitAsync();
    }
    async Task<InventoryGraph> Read()
    {
        await using var connection = await db.OpenConnectionAsync(); await using var tx = await connection.BeginTransactionAsync();
        var graph = await InventoryRepository.ReadGraphAsync(connection, tx, player, default); await tx.CommitAsync(); return graph;
    }
    var stored = Cells(3, 40, "stash", true); await Save(stored);
    var deployment = new Deployment(player, Guid.NewGuid(), 1); var profile = await repository.DeployAsync(deployment, default);
    var raid = InventoryRepository.Decode(profile.RaidInventoryJson!); var warehouse = await Read();
    Check(raid.CellEnergy() == 100 && warehouse.CellEnergy() == 140 && Cell(warehouse).CellCharge == 40 && Cell(raid).EmergencySupply, "deployment split conserves charge, quantity, and provenance");
    Check(raid.TryRecharge(0, 30, 3, out rounds) && rounds == 30 && raid.CellEnergy() == 10, "live raid spends 90 energy");
    var settlement = new Settlement(player, deployment.DeploymentId, "Extracted", 0, 0, raid.Count("cells", true), deployment.DeploymentId, InventoryRepository.Encode(raid));
    await store.SettleAsync(settlement, default); await store.SettleAsync(settlement, default);
    var returned = await Read(); Check(returned.CellEnergy() == 150 && returned.Count("cells") == 3 && Cell(returned).EmergencySupply, "extraction and repeated receipt retain only unspent energy");
    var next = new Deployment(player, Guid.NewGuid(), 0); await repository.DeployAsync(next, default);
    var leftBehind = await Read(); Check(leftBehind.CellEnergy() == 150 && leftBehind.Count("cells") == 3 && leftBehind.Items.All(i => i.Code != "cells" || leftBehind.RootOf(i.Id) == "stash"), "reducing carried loadout stores partial cells without refilling");
    Console.WriteLine($"Battery energy checks passed: {checks}. Partial reload, conservation, legacy saves, trading, drops, deployment and extraction. Project data untouched.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
}
