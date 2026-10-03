using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

internal static class HttpInventoryChecks
{
    // Run the real executable/endpoints against the same disposable database, never production.
    public static async Task<int> RunAsync(string root,string connectionString,NpgsqlDataSource db)
    {
        int checks=0;
        void Check(bool value,string name) { if(!value) throw new Exception("HTTP: "+name); checks++; }
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        string key=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
        var start=new ProcessStartInfo("dotnet") {UseShellExecute=false,CreateNoWindow=true,
            WorkingDirectory=Path.Combine(root,"Backend/MoonPersistence"),RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(typeof(InventoryRepository).Assembly.Location);
        start.Environment["ConnectionStrings__Postgres"]=connectionString;
        start.Environment["ServerKey"]=key; start.Environment["Urls"]=$"http://127.0.0.1:{port}";
        using var process=Process.Start(start)!;
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        try
        {
            using var http=new HttpClient {BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(10)};
            http.DefaultRequestHeaders.Add("X-Moon-Server-Key",key);
            bool ready=false;
            for(int attempt=0;attempt<100 && !process.HasExited;attempt++)
            {try {using var health=await http.GetAsync("internal/health"); ready=health.IsSuccessStatusCode;if(ready)break;}catch(HttpRequestException){}await Task.Delay(100);}
            Check(ready,"isolated service starts");
            var json=new JsonSerializerOptions {IncludeFields=true,PropertyNameCaseInsensitive=true};
            async Task<HttpResponseMessage> Post(string path,object payload)
            {using var body=new StringContent(JsonSerializer.Serialize(payload,json),Encoding.UTF8,"application/json");return await http.PostAsync(path,body);}
            async Task<Profile> ProfileOf(HttpResponseMessage response)
            {Check(response.IsSuccessStatusCode,"expected successful inventory response");return JsonSerializer.Deserialize<Profile>(await response.Content.ReadAsStringAsync(),json)!;}
            async Task Rejected(HttpResponseMessage response,string code)
            {using(response){Check(response.StatusCode==HttpStatusCode.Conflict,"conflict status");using var error=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Check(error.RootElement.GetProperty("error").GetString()==code,"specific rejection: "+code);}}
            using var registration=await Post("auth/register",new Credentials("http_fixture",Guid.NewGuid().ToString("N"),null));
            Check(registration.IsSuccessStatusCode,"register");
            var login=JsonSerializer.Deserialize<LoginResult>(await registration.Content.ReadAsStringAsync(),json)!;
            http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.Token);
            Profile profile;
            using(var me=await http.GetAsync("auth/me"))profile=await ProfileOf(me);
            var graph=InventoryRepository.Decode(profile.InventoryJson!); string pack=graph.Equipped("Backpack")!.Id;
            var helmet=new InventoryItem {Id=Guid.NewGuid().ToString(),Code="helmet",Parent="stash",Region="main",X=6,Y=6};
            graph.Items.Add(helmet); graph.Version++;
            Check(graph.AddSupply("dust",3,pack)==InventoryError.None,"backpack cargo fixture");
            await using(var connection=await db.OpenConnectionAsync())
            await using(var transaction=await connection.BeginTransactionAsync())
            {await InventoryRepository.LockPlayerAsync(connection,transaction,profile.PlayerId,default);await InventoryRepository.WriteGraphAsync(connection,transaction,profile.PlayerId,graph,default);await transaction.CommitAsync();}
            // Unity sends PascalCase public fields and numeric enums, not a server-only DTO.
            using(var move=await Post("auth/inventory/move",new InventoryCommand {ExpectedVersion=graph.Version,ItemId=helmet.Id,Parent="equipment",Region="Helmet"}))
                profile=await ProfileOf(move);
            graph=InventoryRepository.Decode(profile.InventoryJson!);
            Check(graph.Equipped("Helmet")?.Id==helmet.Id,"public-field command equips helmet");
            int version=graph.Version;
            await Rejected(await Post("auth/inventory/move",new InventoryCommand {ExpectedVersion=version,ItemId=helmet.Id,Parent="equipment",Region="Backpack"}),"inventory_Incompatible");
            await Rejected(await Post("auth/inventory/move",new InventoryCommand {ExpectedVersion=version-1,ItemId=helmet.Id,Parent="stash",Region="main",X=6,Y=6}),"inventory_Stale");
            // >20 ordinary refreshes must not exhaust the login rate limiter.
            for(int i=0;i<25;i++){using var refresh=await http.GetAsync("auth/me");Check(refresh.IsSuccessStatusCode,"normal refresh not login throttled");}
            using(var move=await Post("auth/inventory/move",new InventoryCommand {ExpectedVersion=version,ItemId=pack,Parent="stash",Region="main",X=1,Y=10}))profile=await ProfileOf(move);
            graph=InventoryRepository.Decode(profile.InventoryJson!);
            Check(graph.Find(pack)!.Parent=="stash" && graph.Items.Single(i=>i.Code=="dust").Parent==pack,"unequip entire backpack preserves cargo");
            using(var move=await Post("auth/inventory/move",new InventoryCommand {ExpectedVersion=graph.Version,ItemId=pack,Parent="equipment",Region="Backpack"}))profile=await ProfileOf(move);
            graph=InventoryRepository.Decode(profile.InventoryJson!);
            var racing=new InventoryCommand {ExpectedVersion=graph.Version,ItemId=helmet.Id,Parent="stash",Region="main",X=6,Y=6};
            var replies=await Task.WhenAll(Post("auth/inventory/move",racing),Post("auth/inventory/move",racing));
            try {Check(replies.Count(r=>r.StatusCode==HttpStatusCode.OK)==1 && replies.Count(r=>r.StatusCode==HttpStatusCode.Conflict)==1,"concurrent same-version intent applies once");}
            finally {foreach(var reply in replies)reply.Dispose();}
            var deployment=new Deployment(profile.PlayerId,Guid.NewGuid(),0);
            using(var deploy=await Post("internal/deployments",deployment))profile=await ProfileOf(deploy);
            using(var me=await http.GetAsync("auth/me"))profile=await ProfileOf(me);
            Check(profile.ActiveDeploymentId==deployment.DeploymentId,"account exposes active raid ownership");
            await Rejected(await Post("auth/inventory/move",racing),"raid_active");
            using(var abandon=await Post("internal/deployments/abandon",deployment))profile=await ProfileOf(abandon);
            Check(profile.ActiveDeploymentId==null,"disconnect closes account lock");
            graph=InventoryRepository.Decode(profile.InventoryJson!);
            using(var move=await Post("auth/inventory/move",new InventoryCommand {ExpectedVersion=graph.Version,ItemId=helmet.Id,Parent="equipment",Region="Helmet"}))profile=await ProfileOf(move);
            Check(InventoryRepository.Decode(profile.InventoryJson!).Equipped("Helmet")?.Id==helmet.Id,"equip works after disconnect");
            http.DefaultRequestHeaders.Authorization=null;
            using(var unauthorized=await Post("auth/inventory/move",racing))Check(unauthorized.StatusCode==HttpStatusCode.Unauthorized,"unauthenticated move rejected");
        }
        finally
        {
            if(!process.HasExited)process.Kill(entireProcessTree:true);
            await process.WaitForExitAsync(); await Task.WhenAll(stdout,stderr);
        }
        return checks;
    }
}
