using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS;

var root = Path.GetFullPath(args.Single());
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Backend/MoonPersistence/appsettings.local.json")));
var settings = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) {
    Database = "postgres", Username = "moonkov_admin", Pooling = false,
    Password = File.ReadAllText(Path.Combine(root, "LocalData/postgres-admin.password")).Trim()
};
string database = "moon_halo_check_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString); await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"", "\"\"")}\"", admin)) await create.ExecuteNonQueryAsync();
try
{
    settings.Database = database; await using var db = NpgsqlDataSource.Create(settings.ConnectionString);
    await new StashRepository(db).InitializeAsync();
    Guid player = Guid.NewGuid();
    await using (var seed = db.CreateCommand("INSERT INTO players(id,display_name) VALUES($1,'halo_upgrade_fixture')"))
    { seed.Parameters.AddWithValue(player); await seed.ExecuteNonQueryAsync(); }
    var legacy = InventoryGraph.Create(starter: false);
    legacy.AddSupply("rifle", 1, "stash"); legacy.AddSupply("cells", 6, "stash"); legacy.AddSupply("dust", 27, "stash");
    var rifle = legacy.Items.Single(i => i.Code == "rifle"); rifle.LoadedAmmo = 7;
    // Reproduce a pre-upgrade profile, rather than generating a fresh starter profile.
    string oldJson = InventoryRepository.Encode(legacy).Replace("\"WeaponKitVersion\":0,", "");
    await using (var seed = db.CreateCommand("INSERT INTO inventory_profiles(player_id,revision,inventory) VALUES($1,1,$2::jsonb)"))
    { seed.Parameters.AddWithValue(player); seed.Parameters.AddWithValue(oldJson); await seed.ExecuteNonQueryAsync(); }
    async Task<InventoryGraph> Read()
    {
        await using var connection = await db.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        var graph = await InventoryRepository.ReadGraphAsync(connection, transaction, player, default);
        await transaction.CommitAsync(); return graph;
    }
    var first = await Read(); var second = await Read();
    void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    Check(first.Validate() == InventoryError.None && first.WeaponKitVersion == 1, "upgrade validation");
    Check(first.Count("dust") == 27 && first.Count("cells") == 6 && first.Count("rifle") == 1, "resources/owned weapons preserved");
    Check(first.Find(rifle.Id)?.LoadedAmmo == 7 && first.Find(rifle.Id)?.Parent == "stash", "owned identity/location/ammo preserved");
    Check(first.Count("pistol") == 1 && first.Count("shotgun") == 1 && first.Equipped("Primary") == null && first.Equipped("Secondary") == null, "new weapons stored, not intrinsically carried");
    Check(first.Version == second.Version && first.Items.Select(i => i.Id).SequenceEqual(second.Items.Select(i => i.Id)), "repeat reads cannot resupply kit");
    var shotgun = second.Items.Single(i => i.Code == "shotgun");
    var repository = new InventoryRepository(db);
    await repository.MoveAsync(player, new InventoryCommand { ExpectedVersion = second.Version, ItemId = shotgun.Id, Parent = "equipment", Region = "Secondary" }, default);
    var equipped = await Read();
    Check(equipped.Equipped("Secondary")?.Id == shotgun.Id && equipped.Find(rifle.Id)?.LoadedAmmo == 7, "authoritative configuration persists");
    var moduleFixture = equipped.Clone();
    foreach (string code in HaloModules.Codes) Check(moduleFixture.AddSupply(code, 1, "stash") == InventoryError.None, "module stock");
    InventoryError Move(InventoryGraph graph, string id, string parent, string region) => graph.TryApply(new InventoryCommand {
        ExpectedVersion = graph.Version, ItemId = id, Parent = parent, Region = region });
    var cap = moduleFixture.Items.Single(i => i.Code == "halo_capacitor");
    var focus = moduleFixture.Items.Single(i => i.Code == "halo_focus_lens");
    var pulse = moduleFixture.Items.Single(i => i.Code == "halo_pulse_controller");
    Check(Move(moduleFixture, cap.Id, shotgun.Id, "Storage") == InventoryError.None, "mount capacitor");
    Check(Move(moduleFixture, focus.Id, shotgun.Id, "Shaping") == InventoryError.None, "mount focus");
    Check(HaloModules.Mask(moduleFixture, moduleFixture.Find(shotgun.Id)) == (HaloModule.Capacitor | HaloModule.FocusLens), "per-weapon modules");
    string unchanged = InventoryRepository.Encode(moduleFixture);
    Check(Move(moduleFixture, pulse.Id, shotgun.Id, "Shaping") == InventoryError.Incompatible && InventoryRepository.Encode(moduleFixture) == unchanged, "reject rifle module atomically");
    var bridge = moduleFixture.Items.Single(i => i.Code == "halo_charge_bridge");
    Check(Move(moduleFixture, bridge.Id, shotgun.Id, "Storage") == InventoryError.None && moduleFixture.Find(cap.Id)!.Parent == "stash", "swap returns displaced module");
    Check(moduleFixture.Items.Count(i => i.Parent == shotgun.Id && i.Region == "Storage") == 1, "same-slot exclusion");
    var weaponItem = moduleFixture.Find(shotgun.Id)!; weaponItem.LoadedAmmo = 4;
    await using (var save = db.CreateCommand("UPDATE inventory_profiles SET revision=$1,inventory=$2::jsonb WHERE player_id=$3"))
    { save.Parameters.AddWithValue(moduleFixture.Version); save.Parameters.AddWithValue(InventoryRepository.Encode(moduleFixture)); save.Parameters.AddWithValue(player); await save.ExecuteNonQueryAsync(); }
    var restored = await Read();
    Check(restored.Validate() == InventoryError.None && restored.Find(bridge.Id)?.Parent == shotgun.Id && restored.Find(shotgun.Id)?.LoadedAmmo == 4, "database preserves module identity and ammo");
    var stashCopy = restored.Clone(); var raid = stashCopy.ExtractLoadout();
    Check(raid.Find(bridge.Id)?.Parent == shotgun.Id && stashCopy.Find(bridge.Id) == null, "deployment moves attachments once");
    unchanged = InventoryRepository.Encode(raid);
    Check(Move(raid, bridge.Id, "pockets", "1") == InventoryError.Inaccessible && InventoryRepository.Encode(raid) == unchanged, "raid cannot dismount modules");
    Check(stashCopy.ReturnLoadout(raid) == InventoryError.None && stashCopy.Find(bridge.Id)?.Parent == shotgun.Id, "extraction restores subtree");
    var cache = LootInventoryExchange.DropOnDeath(raid);
    Check(cache.Find(bridge.Id)?.Parent == shotgun.Id && raid.Find(bridge.Id) == null, "death drops subtree without duplication");
    var recipient = InventoryGraph.Create(false, false);
    Check(recipient.AddLoot("small_pack") == InventoryError.None, "recipient backpack");
    var bag = recipient.Items.Single(i => i.Code == "small_pack");
    Check(recipient.FindSpace(cache.Find(shotgun.Id)!, bag.Id, out var lootRegion, out var lx, out var ly), "recipient space");
    Check(LootInventoryExchange.TryApply(recipient, cache, new InventoryCommand {ExpectedVersion=recipient.Version, ItemId=shotgun.Id,
        Parent=bag.Id, Region=lootRegion, X=lx,Y=ly},cache.Version) == InventoryError.None && recipient.Find(bridge.Id)?.Parent == shotgun.Id && cache.Find(bridge.Id) == null, "loot moves attached weapon between owners");
    var saleGraph = restored.Clone();
    Check(saleGraph.FindSpace(saleGraph.Find(shotgun.Id)!, "stash",out var saleRegion,out var sx,out var sy), "sale space");
    Check(saleGraph.TryApply(new InventoryCommand {ExpectedVersion=saleGraph.Version,ItemId=shotgun.Id,Parent="stash",Region=saleRegion,X=sx,Y=sy}) == InventoryError.None, "store modified weapon");
    string saleResult=ShopRules.Apply(saleGraph,new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=saleGraph.Version,
        Operation=ShopOperation.Sell,Code="shotgun",ItemId=shotgun.Id,Quantity=1},out _);
    Check(saleResult=="shop_remove_modules_first", "cannot sell weapon with orphaned modules");
    Console.WriteLine("Module checks passed: compatible mount, atomic rejection, slot replacement, database round-trip, deploy/extract/death/loot ownership, raid lock and sale guard.");
    Console.WriteLine("Halo persistence checks passed: legacy migration, resources, item ammo/identity, one-time kit, equipment mutation.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
}
