using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
var root = Path.GetFullPath(args.Single());
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Backend/MoonPersistence/appsettings.local.json")));
var settings = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Database = "postgres", Username = "moonkov_admin", Pooling = false,
    Password = File.ReadAllText(Path.Combine(root, "LocalData/postgres-admin.password")).Trim() };
string database = "moon_mission_check_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString); await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"", "\"\"")}\"", admin)) await create.ExecuteNonQueryAsync();
try
{
    settings.Database = database;
    await using var db = NpgsqlDataSource.Create(settings.ConnectionString);
    var store = new StashRepository(db); await store.InitializeAsync();
    var repository = new InventoryRepository(db);
    var first = await store.ResolveAsync(new ResolveProfile(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), "mission_fixture_a"), default);
    var second = await store.ResolveAsync(new ResolveProfile(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), "mission_fixture_b"), default);
    var deployment = new Deployment(first.PlayerId, Guid.NewGuid(), 0);
    var live = await repository.DeployAsync(deployment, default);
    var raid = InventoryRepository.Decode(live.RaidInventoryJson!);
    var artifact = new InventoryItem { Id = Guid.NewGuid().ToString(), Code = "mooncore_fragment", Parent = "pockets", Region = "1", FoundInRaid = true,
        MissionWorldId = Guid.NewGuid().ToString(), MissionOperationId = Guid.NewGuid().ToString(), MissionContentVersion = 1 };
    raid.Items.Add(artifact);
    var receipt = new Settlement(first.PlayerId, deployment.DeploymentId, "Extracted", raid.Count("dust", true), raid.Count("alloy", true), raid.Count("cells", true),
        deployment.DeploymentId, InventoryRepository.Encode(raid));
    var saved = await store.SettleAsync(receipt, default);
    var graph = InventoryRepository.Decode(saved.InventoryJson!);
    Check(graph.MissionFirsts == 3 && graph.Count("dust") == first.Dust + 40 && graph.Find(artifact.Id).MissionRecovered, "automatic reward and real source persisted");
    var replay = await store.SettleAsync(receipt, default);
    Check(InventoryRepository.Encode(InventoryRepository.Decode(replay.InventoryJson!)) == InventoryRepository.Encode(graph), "settlement retry returns same inventory");
    var db2 = new StashRepository(db); await db2.InitializeAsync();
    var replayAfterRepositoryRestart = await db2.SettleAsync(receipt, default);
    Check(InventoryRepository.Decode(replayAfterRepositoryRestart.InventoryJson!).MissionFirsts == 3, "facts survive repository restart");
    var deploymentB = new Deployment(second.PlayerId, Guid.NewGuid(), 0);
    var liveB = await repository.DeployAsync(deploymentB, default);
    var raidB = InventoryRepository.Decode(liveB.RaidInventoryJson!); raidB.Items.Add(artifact.Clone());
    var copied = new Settlement(second.PlayerId, deploymentB.DeploymentId, "Extracted", 0, 0, 0, deploymentB.DeploymentId, InventoryRepository.Encode(raidB));
    bool rejected = false;
    try { await store.SettleAsync(copied, default); } catch (ReceiptConflictException) { rejected = true; }
    Check(rejected, "cross-account artifact duplicate rejected");
    // The failed extraction transaction must not leave either a settlement receipt or a closed deployment.
    await using var connection = await db.OpenConnectionAsync();
    await using (var query = new NpgsqlCommand("SELECT count(*) FROM raid_settlements WHERE id=$1", connection))
    { query.Parameters.AddWithValue(deploymentB.DeploymentId); Check((long)(await query.ExecuteScalarAsync())! == 0, "failed receipt rolled back"); }
    await using (var query = new NpgsqlCommand("SELECT status FROM raid_deployments WHERE id=$1", connection))
    { query.Parameters.AddWithValue(deploymentB.DeploymentId); Check((string)(await query.ExecuteScalarAsync())! == "Open", "failed deployment remains recoverable"); }
    await using (var query = new NpgsqlCommand("SELECT count(*) FROM mission_artifact_receipts", connection))
        Check((long)(await query.ExecuteScalarAsync())! == 1, "one global artifact receipt");
    var dead = await store.SettleAsync(new Settlement(second.PlayerId, deploymentB.DeploymentId, "Dead", 0, 0, 0, deploymentB.DeploymentId), default);
    Check(InventoryRepository.Decode(dead.InventoryJson!).MissionFirsts == 0 && dead.Dust == second.Dust, "death awards nothing");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
}
Console.WriteLine($"PASS {checks} mission PostgreSQL transaction checks; isolated database removed; production data untouched.");
