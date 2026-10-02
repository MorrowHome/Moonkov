using System.Text.Json;
using Npgsql;

// Creates and drops only a unique disposable database. The project database is never modified.
var root = args.Length == 1 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass the project root.");
using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Backend/MoonPersistence/appsettings.local.json")));
var settings = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{ Database="postgres", Username="moonkov_admin", Password=File.ReadAllText(Path.Combine(root,"LocalData/postgres-admin.password")).Trim(), Pooling=false };
string database = "moon_inventory_check_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"","\"\"")}\"",admin)) await create.ExecuteNonQueryAsync();
int checks=0;
void Check(bool result, string name) { if (!result) throw new Exception("Failed: "+name); checks++; }
try
{
    settings.Database=database;
    await using var db = NpgsqlDataSource.Create(settings.ConnectionString);
    var store = new StashRepository(db);
    var inventory = new InventoryRepository(db);
    string schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"schema.sql"));
    await using (var legacy = db.CreateCommand(schema.Split("-- Version 3:")[0])) await legacy.ExecuteNonQueryAsync();
    Guid player=Guid.NewGuid();
    await using (var seed = db.CreateCommand("INSERT INTO players (id,display_name) VALUES ($1,'fixture')"))
    { seed.Parameters.AddWithValue(player); await seed.ExecuteNonQueryAsync(); }
    await using (var stash = db.CreateCommand("INSERT INTO stashes (player_id,dust,alloy,cells) VALUES ($1,2,3,6)"))
    { stash.Parameters.AddWithValue(player); await stash.ExecuteNonQueryAsync(); }
    await store.InitializeAsync();
    await store.InitializeAsync(); // Every ordinary restart runs schema initialization.
    async Task<Profile> Read()
    {
        await using var connection=await db.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        var profile=await InventoryRepository.ReadAsync(connection,transaction,player,default);
        await transaction.CommitAsync(); return profile;
    }
    var original=await Read();
    Check(original is {Dust:2,Alloy:3,Cells:6} && original.Items!.Count==3,"legacy migration and repeat initialization");
    var cellsId=original.Items!.Single(item=>item.ItemCode=="cells").Id;
    var deployment=new Deployment(player,Guid.NewGuid(),2);
    var started=await inventory.DeployAsync(deployment,default);
    var retry=await inventory.DeployAsync(deployment,default);
    Check(started.Cells==4 && retry.Cells==4 && started.CellStackId==retry.CellStackId && started.CarriedCells==2,"idempotent debit and carried stack ID");
    try { await inventory.DeployAsync(deployment with {Cells=3},default); throw new Exception("Missing deployment conflict"); }
    catch(ReceiptConflictException) { checks++; }
    try { await inventory.DeployAsync(new Deployment(player,Guid.NewGuid(),5),default); throw new Exception("Missing insufficient-stock rejection"); }
    catch(DeploymentRejectedException) { checks++; }
    var settlement=new Settlement(player,deployment.DeploymentId,"Extracted",1,0,1,deployment.DeploymentId);
    await store.SettleAsync(settlement,default); await store.SettleAsync(settlement,default);
    var extracted=await Read();
    Check(extracted is {Dust:3,Alloy:3,Cells:5} && extracted.Items!.Single(item=>item.ItemCode=="cells").Id==cellsId,"return unused battery once and retain stash stack ID");
    var death=new Deployment(player,Guid.NewGuid(),2);
    await inventory.DeployAsync(death,default);
    await store.SettleAsync(new Settlement(player,death.DeploymentId,"Dead",0,0,2,death.DeploymentId),default);
    Check((await Read()).Cells==3,"death does not refund carried batteries");
    var cancelled=new Deployment(player,Guid.NewGuid(),2);
    await inventory.AbandonAsync(cancelled,default); await inventory.AbandonAsync(cancelled,default);
    Check((await Read()).Cells==3,"unacknowledged request recovery never starts a fresh debit");
    try { await inventory.DeployAsync(cancelled,default); throw new Exception("Missing cancelled-deployment rejection"); }
    catch(DeploymentRejectedException) { checks++; }
    var abandoned=new Deployment(player,Guid.NewGuid(),1);
    await inventory.DeployAsync(abandoned,default); await inventory.AbandonAsync(abandoned,default); await inventory.AbandonAsync(abandoned,default);
    Check((await Read()).Cells==2,"disconnect/crash loses accepted loadout once");
    var legacyReceipt=new Settlement(player,Guid.NewGuid(),"Extracted",0,0,1);
    await store.SettleAsync(legacyReceipt,default);
    Check((await Read()).Cells==3,"old outbox receipt compatibility");
    var tasks=Enumerable.Range(0,2).Select(async _ =>
    {
        try { await inventory.DeployAsync(new Deployment(player,Guid.NewGuid(),3),default); return true; }
        catch(DeploymentRejectedException) { return false; }
    });
    var results=await Task.WhenAll(tasks);
    Check(results.Count(success=>success)==1 && (await Read()).Cells==0,"concurrent debit cannot overspend");
    await using(var view=db.CreateCommand("SELECT cells FROM stashes WHERE player_id=$1"))
    { view.Parameters.AddWithValue(player); Check((int)(await view.ExecuteScalarAsync())! ==0,"legacy stash view reflects canonical inventory"); }
    Console.WriteLine($"Inventory checks passed: {checks}. Disposable database only.");
}
finally
{
    if (!database.StartsWith("moon_inventory_check_",StringComparison.Ordinal) || database.Length!=53) throw new Exception("Refusing to drop unexpected database.");
    await using var drop=new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)",admin);
    await drop.ExecuteNonQueryAsync();
}
