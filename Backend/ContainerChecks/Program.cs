using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

int checks=0;
void Check(bool ok,string name) { if(!ok) throw new Exception(name); checks++; }
var graph=InventoryGraph.Create();
Check(graph.AddLoot("small_pack")==InventoryError.None,"nested backpack pickup");
var nested=graph.Items.Single(i=>i.Code=="small_pack");
var pack=graph.Equipped("Backpack")!;
Check(nested.Parent==pack.Id && graph.AddSupply("dust",2,nested.Id,true)==InventoryError.None,"items belong to nested container");
int count=graph.Items.Count;
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,ItemId=pack.Id,Parent=nested.Id,Region="main"})==InventoryError.Cycle && graph.Items.Count==count,"cycle rejection without loss");
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,ItemId=nested.Id,Parent="stash",Region="main",X=4,Y=5})==InventoryError.None
    && graph.Items.Single(i=>i.Code=="dust").Parent==nested.Id,"whole-container movement keeps contents");
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,ItemId=nested.Id,Parent="pockets",Region="1"})==InventoryError.Incompatible,"pocket filtering");
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,ItemId=nested.Id,Parent="stash",Region="main",X=int.MaxValue})==InventoryError.Bounds,"overflow and bounds rejection");
Check(graph.AddSupply("cells",6)==InventoryError.None,"carried cells assigned to container");
var cells=graph.Items.Single(i=>i.Code=="cells");
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,Operation=InventoryOperation.Split,ItemId=cells.Id,Quantity=2,Parent="pockets",Region="2"})==InventoryError.None,"split without duplication");
var split=graph.Items.Single(i=>i.Code=="cells" && i.Id!=cells.Id);
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,Operation=InventoryOperation.Merge,ItemId=split.Id,TargetId=cells.Id})==InventoryError.None && graph.Count("cells")==6,"merge conserves quantity");
Check(graph.TryApply(new InventoryCommand {ExpectedVersion=0,ItemId=cells.Id,Parent="pockets",Region="3"})==InventoryError.Stale,"stale command rejected");
Check(graph.ConsumeAccessibleCell() && graph.Count("cells")==5,"accessible cell consumption");
var malformed=InventoryGraph.Create(); malformed.Items.Add(null!);
Check(malformed.Validate()==InventoryError.Invalid,"null item fails without exception");
malformed=InventoryGraph.Create(); malformed.Items.RemoveAll(i=>i.Code=="pockets");
Check(malformed.Validate()==InventoryError.Invalid,"missing required roots rejected");
malformed=InventoryGraph.Create(); malformed.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString(),Code="unknown",Parent="equipment",Region="Backpack"});
Check(malformed.Validate()==InventoryError.Invalid,"invalid neighbour rejected before placement inspection");

// One disposable PostgreSQL flow checks migration, debit, persistence and replay together.
var root=Path.GetFullPath(args.Single());
using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Backend/MoonPersistence/appsettings.local.json")));
var settings=new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings=new NpgsqlConnectionStringBuilder(settings.ConnectionString) {Database="postgres",Username="moonkov_admin",Password=File.ReadAllText(Path.Combine(root,"LocalData/postgres-admin.password")).Trim(),Pooling=false};
string database="moon_container_check_"+Guid.NewGuid().ToString("N");
await using var admin=new NpgsqlConnection(adminSettings.ConnectionString); await admin.OpenAsync();
await using(var create=new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"","\"\"")}\"",admin)) await create.ExecuteNonQueryAsync();
try
{
    settings.Database=database; await using var db=NpgsqlDataSource.Create(settings.ConnectionString);
    var store=new StashRepository(db); var inventory=new InventoryRepository(db);
    await store.InitializeAsync();
    Guid player=Guid.NewGuid();
    await using(var seed=db.CreateCommand("INSERT INTO players(id,display_name) VALUES($1,'container_fixture')")) {seed.Parameters.AddWithValue(player);await seed.ExecuteNonQueryAsync();}
    await using(var seed=db.CreateCommand("INSERT INTO inventory_stacks(player_id,item_code,quantity,grid_x) VALUES($1,'dust',27,0),($1,'alloy',3,2),($1,'cells',6,4)")) {seed.Parameters.AddWithValue(player);await seed.ExecuteNonQueryAsync();}
    async Task<Profile> Read()
    {await using var c=await db.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();var p=await InventoryRepository.ReadAsync(c,t,player,default);await t.CommitAsync();return p;}
    var before=await Read(); var owned=InventoryRepository.Decode(before.InventoryJson!);
    Check(before.Dust==27 && before.Alloy==3 && before.Cells==6 && owned.Validate()==InventoryError.None,"migration preserves quantities and creates containers");
    await using(var connection=await db.OpenConnectionAsync())
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var invalidAccount=owned.Clone();invalidAccount.Items.Add(new InventoryItem {Id="loot",Code="loot"});
        try {await InventoryRepository.WriteGraphAsync(connection,transaction,player,invalidAccount,default);Check(false,"world cache cannot be persisted in account");}
        catch(DeploymentRejectedException ex) {Check(ex.Message=="invalid_inventory","account boundary rejects cache graph");}
    }
    await using(var connection=await db.OpenConnectionAsync())
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var invalidCargo=InventoryGraph.Create(false,false);invalidCargo.Items.Add(new InventoryItem {Id="loot",Code="loot"});
        try {await InventoryRepository.ReturnInventoryAsync(connection,transaction,new Settlement(player,Guid.NewGuid(),"Extracted",0,0,0,InventoryJson:InventoryRepository.Encode(invalidCargo)),default);Check(false,"world cache cannot be settled as cargo");}
        catch(ReceiptConflictException) {Check(true,"settlement boundary rejects cache graph");}
    }
    var deployment=new Deployment(player,Guid.NewGuid(),2);
    var deployed=await inventory.DeployAsync(deployment,default); var repeated=await inventory.DeployAsync(deployment,default);
    Check(deployed.Cells==4 && deployed.RaidInventoryJson==repeated.RaidInventoryJson,"idempotent whole-kit deployment");
    var raid=InventoryRepository.Decode(deployed.RaidInventoryJson!);
    Check(raid.Equipped("Backpack")!=null && InventoryRepository.Decode(deployed.InventoryJson!).Equipped("Backpack")==null,"kit has exactly one owner after debit");
    Check(raid.AddLoot("small_pack")==InventoryError.None && raid.ConsumeAccessibleCell(),"loot and consumption use actual containers");
    Check(raid.AddSupply("dust",15)==InventoryError.None,"container cargo exceeds the legacy 12-supply cap");
    var settlement=new Settlement(player,deployment.DeploymentId,"Extracted",raid.Count("dust",true),raid.Count("alloy",true),raid.Count("cells",true),deployment.DeploymentId,InventoryRepository.Encode(raid));
    await store.SettleAsync(settlement,default); await store.SettleAsync(settlement,default);
    var after=await Read(); var restored=InventoryRepository.Decode(after.InventoryJson!);
    Check(after.Cells==5 && after.Dust==42 && restored.Equipped("Backpack")!=null && restored.Items.Count(i=>i.Code=="small_pack")==1,"extraction restores nested kit once");
    var next=new Deployment(player,Guid.NewGuid(),1); var second=await inventory.DeployAsync(next,default);
    var lost=InventoryRepository.Decode(second.RaidInventoryJson!);
    var droppedPack=lost.Equipped("Backpack")!;var droppedNested=lost.Items.Single(i=>i.Code=="small_pack");
    var deathBag=LootInventoryExchange.DropOnDeath(lost);
    var deathReceipt=new Settlement(player,next.DeploymentId,"Dead",0,0,1,next.DeploymentId,InventoryRepository.Encode(lost));
    await store.SettleAsync(deathReceipt,default);await store.SettleAsync(deathReceipt,default);
    var dead=InventoryRepository.Decode((await Read()).InventoryJson!);
    Check(dead.Equipped("Backpack")==null && dead.Items.All(i=>i.Code!="small_pack"),"death does not return nested kit");
    Check(lost.Items.All(i=>InventoryCatalog.Get(i.Code)!.Kind==ItemKind.Root),"death receipt has no ownership of dropped kit");
    Guid survivor=Guid.NewGuid();
    await using(var seed=db.CreateCommand("INSERT INTO players(id,display_name) VALUES($1,'survivor_fixture')")) {seed.Parameters.AddWithValue(survivor);await seed.ExecuteNonQueryAsync();}
    var survivorDeploy=new Deployment(survivor,Guid.NewGuid(),0);var survivorProfile=await inventory.DeployAsync(survivorDeploy,default);
    var survivorCargo=InventoryRepository.Decode(survivorProfile.RaidInventoryJson!);
    Check(LootInventoryExchange.TryApply(survivorCargo,deathBag,new InventoryCommand {ExpectedVersion=survivorCargo.Version,ItemId=droppedPack.Id,Parent="equipment",Region="Backpack"},deathBag.Version)==InventoryError.None,"survivor takes actual fallen kit");
    var recovered=new Settlement(survivor,survivorDeploy.DeploymentId,"Extracted",survivorCargo.Count("dust",true),survivorCargo.Count("alloy",true),survivorCargo.Count("cells",true),survivorDeploy.DeploymentId,InventoryRepository.Encode(survivorCargo));
    await store.SettleAsync(recovered,default);await store.SettleAsync(recovered,default);
    await using(var connection=await db.OpenConnectionAsync())
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var returned=InventoryRepository.Decode((await InventoryRepository.ReadAsync(connection,transaction,survivor,default)).InventoryJson!);
        Check(returned.Find(droppedPack.Id)!=null && returned.Find(droppedNested.Id)?.Parent==droppedPack.Id && deathBag.Find(droppedNested.Id)==null,"looted kit persists once under survivor identity");
    }
    checks += await HttpInventoryChecks.RunAsync(root,settings.ConnectionString,db);
}
finally
{NpgsqlConnection.ClearAllPools();await using var drop=new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();}
Console.WriteLine($"Container checks passed: {checks}. Temporary database removed; project database untouched.");
