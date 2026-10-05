using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

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
    Console.WriteLine("Halo persistence checks passed: legacy migration, resources, item ammo/identity, one-time kit, equipment mutation.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
}
