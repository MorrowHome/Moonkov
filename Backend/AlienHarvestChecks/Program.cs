using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

int checks=0;
void Check(bool ok,string message) { if(!ok)throw new Exception(message);checks++; }
var root=Path.GetFullPath(args.Single());
var wire=new JsonSerializerOptions(JsonSerializerDefaults.Web) {IncludeFields=true};
using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Backend/MoonPersistence/appsettings.local.json")));
var settings=new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString());
var adminSettings=new NpgsqlConnectionStringBuilder(settings.ConnectionString) {Database="postgres",Username="moonkov_admin",Pooling=false,
    Password=File.ReadAllText(Path.Combine(root,"LocalData/postgres-admin.password")).Trim()};
string database="moon_alien_check_"+Guid.NewGuid().ToString("N");
await using var admin=new NpgsqlConnection(adminSettings.ConnectionString);await admin.OpenAsync();
await using(var create=new NpgsqlCommand($"CREATE DATABASE {database} OWNER \"{settings.Username!.Replace("\"","\"\"")}\"",admin))await create.ExecuteNonQueryAsync();
try
{
    settings.Database=database;
    await using var db=NpgsqlDataSource.Create(settings.ConnectionString);
    await new StashRepository(db).InitializeAsync();
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
    string key=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
    var start=new ProcessStartInfo("dotnet") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.Combine(root,"Backend/MoonPersistence"),
        RedirectStandardOutput=true,RedirectStandardError=true};
    start.ArgumentList.Add(typeof(InventoryRepository).Assembly.Location);
    start.Environment["ConnectionStrings__Postgres"]=settings.ConnectionString;start.Environment["ServerKey"]=key;start.Environment["Urls"]=$"http://127.0.0.1:{port}";
    Process? service=null;Task<string>? stdout=null,stderr=null;
    using var server=new HttpClient {BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(10)};
    server.DefaultRequestHeaders.Add("X-Moon-Server-Key",key);
    async Task Start()
    {
        service=Process.Start(start)!;stdout=service.StandardOutput.ReadToEndAsync();stderr=service.StandardError.ReadToEndAsync();
        for(int i=0;i<80 && !service.HasExited;i++)
        {
            try
            {
                using var response=await server.GetAsync("internal/health");
                if(response.IsSuccessStatusCode)
                {
                    using var health=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Check(health.RootElement.GetProperty("features").EnumerateArray().Any(f=>f.GetString()=="halo-research-v1"),"deployed backend capability");return;
                }
            }
            catch(HttpRequestException) { }
            await Task.Delay(100);
        }
        throw new Exception("Isolated backend did not start");
    }
    async Task Stop()
    {
        if(service==null)return;
        if(!service.HasExited)service.Kill(entireProcessTree:true);
        await service.WaitForExitAsync();await Task.WhenAll(stdout!,stderr!);service.Dispose();service=null;
    }
    async Task<Profile> ProfileOf(HttpResponseMessage response)
    {
        using(response) {Check(response.IsSuccessStatusCode,"profile HTTP "+response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<Profile>(wire))!;}
    }
    async Task Reject(HttpResponseMessage response,string code)
    {
        using(response) {Check(response.StatusCode==HttpStatusCode.Conflict,"reject HTTP "+code);
            using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());string? actual=body.RootElement.GetProperty("error").GetString();
            Check(actual==code,"reject reason expected "+code+", received "+actual);}
    }
    async Task Save(Guid player,InventoryGraph graph)
    {
        await using var connection=await db.OpenConnectionAsync();await using var transaction=await connection.BeginTransactionAsync();
        await InventoryRepository.LockPlayerAsync(connection,transaction,player,default);
        await InventoryRepository.WriteGraphAsync(connection,transaction,player,graph,default);await transaction.CommitAsync();
    }
    HttpClient Client()=>new() {BaseAddress=server.BaseAddress,Timeout=server.Timeout};
    try
    {
        await Start();
        using var a=Client();using var b=Client();
        async Task<Profile> Register(HttpClient client,string name)
        {
            using var response=await client.PostAsJsonAsync("auth/register",new Credentials(name,Guid.NewGuid().ToString("N")),wire);
            Check(response.IsSuccessStatusCode,"register independent account");
            var login=(await response.Content.ReadFromJsonAsync<LoginResult>(wire))!;
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.Token);
            return await ProfileOf(await client.GetAsync("auth/me"));
        }
        var pa=await Register(a,"alien_a");var pb=await Register(b,"alien_b");
        Check(pa.PlayerId!=pb.PlayerId,"two authenticated accounts");
        var graph=InventoryGraph.Create(starter:false);graph.WeaponKitVersion=1;
        graph.AddSupply("alien_neural_core",3,"stash");graph.AddSupply("alien_tissue",5,"stash");await Save(pa.PlayerId,graph);
        var empty=InventoryGraph.Create(starter:false);empty.WeaponKitVersion=1;
        empty.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="cargo_pack",Parent="equipment",Region="Backpack"});await Save(pb.PlayerId,empty);
        var command=new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=graph.Version,Operation=ShopOperation.Research,Code="halo_biology",Quantity=1};
        Task<HttpResponseMessage> Trade(HttpClient client,ShopCommand request)=>client.PostAsJsonAsync("auth/shop/trade",request,wire);
        var races=await Task.WhenAll(Trade(a,command),Trade(a,command));
        foreach(var response in races)pa=await ProfileOf(response);
        graph=InventoryRepository.Decode(pa.InventoryJson!);
        Check(graph.HaloResearchLevel==1 && graph.Count("alien_neural_core")==0 && graph.Count("alien_tissue")==0,"concurrent retries consume materials once");
        Check(graph.Version==command.ExpectedVersion+1,"one committed revision");
        await Reject(await Trade(b,new ShopCommand {RequestId=command.RequestId,ExpectedVersion=empty.Version,Operation=ShopOperation.Research,Code="halo_biology",Quantity=1}),"research_materials");
        pb=await ProfileOf(await b.GetAsync("auth/me"));
        Check(InventoryRepository.Decode(pb.InventoryJson!).HaloResearchLevel==0,"research belongs to authenticated owner");
        var conflict=new ShopCommand {RequestId=command.RequestId,ExpectedVersion=command.ExpectedVersion,Operation=ShopOperation.Research,Code="invalid",Quantity=1};
        await Reject(await Trade(a,conflict),"shop_request_conflict");
        command.RequestId=Guid.NewGuid().ToString("D");command.ExpectedVersion=graph.Version;
        await Reject(await Trade(a,command),"research_complete");
        foreach(var research in AlienBiology.Research.Where(r=>r.Branch!=0))
        {
            for(int i=0;i<research.Materials.Length;i++)graph.AddSupply(research.Materials[i],research.Quantities[i],"stash");
            await Save(pa.PlayerId,graph);
            var request=new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=graph.Version,Operation=ShopOperation.Research,Code=research.Code,Quantity=1};
            pa=await ProfileOf(await Trade(a,request));graph=InventoryRepository.Decode(pa.InventoryJson!);
            Check(research.Unlocked(graph),"online research branch "+research.Code);
        }
        Check(graph.HaloResearchBranches==7,"all independent branches persisted");
        var deploymentA=new Deployment(pa.PlayerId,Guid.NewGuid(),0);
        pa=await ProfileOf(await server.PostAsJsonAsync("internal/deployments",deploymentA,wire));
        Check(InventoryRepository.Decode(pa.RaidInventoryJson!).HaloResearchLevel==1 && InventoryRepository.Decode(pa.RaidInventoryJson!).HaloResearchBranches==7,"online deploy carries permanent research");
        await Reject(await Trade(a,command),"raid_active");
        var deploymentB=new Deployment(pb.PlayerId,Guid.NewGuid(),0);
        pb=await ProfileOf(await server.PostAsJsonAsync("internal/deployments",deploymentB,wire));
        var raid=InventoryRepository.Decode(pb.RaidInventoryJson!);
        var corpse=AlienHarvest.Create(true,new Random(1),"Tentacle");
        string cargo=raid.Equipped("Backpack")!.Id;
        foreach(var item in corpse.Items.Where(i=>i.Parent=="loot").ToArray())
        {
            Check(raid.FindSpace(item,cargo,out var region,out int x,out int y),"harvest cargo space");
            Check(LootInventoryExchange.TryApply(raid,corpse,new InventoryCommand {ExpectedVersion=raid.Version,ItemId=item.Id,Parent=cargo,Region=region,X=x,Y=y},corpse.Version)==InventoryError.None,"shared corpse loot enters raid inventory");
        }
        var settlement=new Settlement(pb.PlayerId,deploymentB.DeploymentId,"Extracted",0,0,0,deploymentB.DeploymentId,InventoryRepository.Encode(raid));
        pb=await ProfileOf(await server.PostAsJsonAsync("internal/settlements",settlement,wire));
        pb=await ProfileOf(await server.PostAsJsonAsync("internal/settlements",settlement,wire));
        var extracted=InventoryRepository.Decode(pb.InventoryJson!);
        Check(extracted.Count("alien_neural_core")==1 && extracted.Count("alien_raw_meat")==4 && extracted.Count("alien_tissue")==5 && extracted.Count("alien_contractile_heart")==1 &&
            extracted.Items.Where(i=>i.Code.StartsWith("alien_")).All(i=>i.FoundInRaid),"extraction persists new items once with provenance");
        pa=await ProfileOf(await server.PostAsJsonAsync("internal/settlements",new Settlement(pa.PlayerId,deploymentA.DeploymentId,"Dead",0,0,0,deploymentA.DeploymentId),wire));
        Check(InventoryRepository.Decode(pa.InventoryJson!).HaloResearchLevel==1 && InventoryRepository.Decode(pa.InventoryJson!).HaloResearchBranches==7,"online death retains research");
        await Stop();await Start();
        pa=await ProfileOf(await a.GetAsync("auth/me"));pb=await ProfileOf(await b.GetAsync("auth/me"));
        Check(InventoryRepository.Decode(pa.InventoryJson!).HaloResearchLevel==1 && InventoryRepository.Decode(pa.InventoryJson!).HaloResearchBranches==7 && InventoryRepository.Decode(pb.InventoryJson!).Count("alien_neural_core")==1,"backend restart preserves both accounts");
    }
    finally {await Stop();}
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop=new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
}
Console.WriteLine($"Alien online checks passed: {checks}. Two HTTP clients, concurrent research, ownership, raid lock, deploy, extraction replay, death and restart. Isolated database removed.");
