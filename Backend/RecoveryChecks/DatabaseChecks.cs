using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

internal static class DatabaseChecks
{
    public static async Task Run(string root)
    {
        int checks=0;void Check(bool ok,string name) {if(!ok)throw new Exception(name);checks++;}
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Backend/MoonPersistence/appsettings.local.json")));
        var settings=new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
        var adminSettings=new NpgsqlConnectionStringBuilder(settings.ConnectionString) {Database="postgres",Username="moonkov_admin",Pooling=false,
            Password=File.ReadAllText(Path.Combine(root,"LocalData/postgres-admin.password")).Trim()};
        string database="moon_recovery_check_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(adminSettings.ConnectionString);await admin.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"","\"\"")}\"",admin))await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database=database;await using var db=NpgsqlDataSource.Create(settings.ConnectionString);
            var store=new StashRepository(db);await store.InitializeAsync();await store.InitializeAsync();
            var repository=new InventoryRepository(db);
            var first=await store.ResolveAsync(new ResolveProfile(Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),"recovery-a"),default);
            var second=await store.ResolveAsync(new ResolveProfile(Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),"recovery-b"),default);
            var full=InventoryGraph.Create(true,false);full.WeaponKitVersion=1;full.StashRows=4096;
            for(int i=0;i<InventoryCatalog.MaxItems-3;i++)full.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Quantity=1,Parent="stash",Region="main",X=i%10,Y=i/10});
            async Task Write(InventoryGraph graph)
            {await using var c=await db.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();await InventoryRepository.WriteGraphAsync(c,tx,first.PlayerId,graph,default);await tx.CommitAsync();}
            await Write(full);
            var deployment=new Deployment(first.PlayerId,Guid.NewGuid(),0);var live=await repository.DeployAsync(deployment,default);
            var raid=InventoryRepository.Decode(live.RaidInventoryJson!);
            var artifact=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="mooncore_fragment",Parent="pockets",Region="1",FoundInRaid=true,
                MissionWorldId=Guid.NewGuid().ToString("D"),MissionOperationId=Guid.NewGuid().ToString("D"),MissionContentVersion=1};raid.Items.Add(artifact);
            var receipt=new Settlement(first.PlayerId,deployment.DeploymentId,"Extracted",0,0,0,deployment.DeploymentId,InventoryRepository.Encode(raid));
            var settled=await store.SettleAsync(receipt,default);
            var account=InventoryRepository.Decode(settled.InventoryJson!);
            RecoveryInbox Inbox(Profile p)=>JsonSerializer.Deserialize<RecoveryInbox>(p.RecoveryJson!,new JsonSerializerOptions {IncludeFields=true})!;
            var inbox=Inbox(settled);
            Check(settled.ActiveDeploymentId==null && account.MissionFirsts==3 && inbox.Batches.Count==2,"full extraction did not close deployment/commit overflow and first facts");
            Check(inbox.Batches.SelectMany(b=>b.Items).Single(i=>i.Id==artifact.Id).MissionRecovered && inbox.Batches.Sum(b=>b.Count("dust"))==40,"pending source/reward not preserved");
            var replay=await store.SettleAsync(receipt,default);Check(replay.RecoveryJson==settled.RecoveryJson && replay.InventoryJson==settled.InventoryJson,"settlement replay changed recovery");
            try {await repository.DeployAsync(new Deployment(first.PlayerId,Guid.NewGuid(),0),default);throw new Exception("pending deployment accepted");}
            catch(DeploymentRejectedException ex) {Check(ex.Message=="recovery_pending","wrong pending rejection");}
            var claim=new RecoveryClaim {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=account.Version,ExpectedRecoveryVersion=inbox.Version,ItemId=artifact.Id};
            try {await repository.ClaimRecoveryAsync(second.PlayerId,new RecoveryClaim {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=InventoryRepository.Decode(second.InventoryJson!).Version,ExpectedRecoveryVersion=Inbox(second).Version,ItemId=artifact.Id},default);throw new Exception("cross-account claim accepted");}
            catch(DeploymentRejectedException) {checks++;}
            try {await repository.ClaimRecoveryAsync(first.PlayerId,claim,default);throw new Exception("full claim accepted");}
            catch(DeploymentRejectedException ex) {Check(ex.Message=="recovery_Full","full claim mutated source");}
            // Existing account trades remain available after the successful settlement.
            var sell=account.Items.First(i=>i.Code=="dust");
            var move=new InventoryCommand {ExpectedVersion=account.Version,ItemId=sell.Id,Parent="stash",Region="main",X=0,Y=300};
            var moved=await repository.MoveAsync(first.PlayerId,move,default);Check(moved.ActiveDeploymentId==null,"overflow locks warehouse movement");
            account=InventoryRepository.Decode(moved.InventoryJson!);
            account.Items.RemoveRange(3,60);account.Version++;await Write(account);
            claim.ExpectedVersion=account.Version;claim.ExpectedRecoveryVersion=inbox.Version;claim.RequestId=Guid.NewGuid().ToString("D");claim.ItemId=null;
            var claimed=await repository.ClaimRecoveryAsync(first.PlayerId,claim,default);
            Check(Inbox(claimed).ItemCount()==0 && InventoryRepository.Decode(claimed.InventoryJson!).Find(artifact.Id)?.MissionRecovered==true,"claim lost mission source");
            var retry=await repository.ClaimRecoveryAsync(first.PlayerId,claim,default);
            Check(retry.InventoryJson==claimed.InventoryJson && retry.RecoveryJson==claimed.RecoveryJson,"claim receipt replay duplicated items");
            await store.InitializeAsync();var restarted=new InventoryRepository(db);
            var lateRetry=await restarted.ClaimRecoveryAsync(first.PlayerId,claim,default);
            Check(lateRetry.InventoryJson==claimed.InventoryJson,"restart lost claim receipt");
            try {await restarted.ClaimRecoveryAsync(first.PlayerId,new RecoveryClaim {RequestId=claim.RequestId,ItemId=artifact.Id,ExpectedVersion=claim.ExpectedVersion,ExpectedRecoveryVersion=claim.ExpectedRecoveryVersion},default);throw new Exception("claim receipt conflict accepted");}
            catch(DeploymentRejectedException ex) {Check(ex.Message=="recovery_request_conflict","wrong claim conflict");}
            var claimGraph=InventoryRepository.Decode(claimed.InventoryJson!);
            var sold=await restarted.TradeAsync(first.PlayerId,new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=claimGraph.Version,
                Operation=ShopOperation.Sell,Code=artifact.Code,ItemId=artifact.Id,Quantity=1},default);
            Check(InventoryRepository.Decode(sold.InventoryJson!).Find(artifact.Id)==null && sold.Dust==claimed.Dust+50,"claimed artifact cannot be sold");
            var next=await restarted.DeployAsync(new Deployment(first.PlayerId,Guid.NewGuid(),0),default);Check(next.DeploymentId!=null,"empty inbox blocks next deployment");
            Console.WriteLine($"Recovery database checks passed: {checks} assertions; isolated migration, full extraction closes deployment, bounded overflow, source/first rewards, replay/restart, account isolation, sorting/claim and deployment gate.");
        }
        finally
        {
            await using var drop=new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
        }
    }
}
