using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

internal static class HttpChecks
{
    public static async Task<int> Run(string connectionString,Deployment deployment)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var info=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.GetDirectoryName(typeof(StashRepository).Assembly.Location)!,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(typeof(StashRepository).Assembly.Location);
        string key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        info.Environment["ConnectionStrings__Postgres"]=connectionString;info.Environment["ServerKey"]=key;info.Environment["Urls"]=$"http://127.0.0.1:{port}";
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try
        {
            using var http=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}/"),Timeout=TimeSpan.FromSeconds(3)};
            http.DefaultRequestHeaders.Add("X-Moon-Server-Key",key);
            HttpResponseMessage? health=null;
            for(int i=0;i<60;i++)
            {
                try{health=await http.GetAsync("internal/health");if(health.IsSuccessStatusCode)break;health.Dispose();health=null;}catch(HttpRequestException){}
                await Task.Delay(50);
            }
            if(health==null)throw new Exception("Isolated HTTP backend did not become ready.");
            int checks=0;
            using(health)
            {
                var data=await health.Content.ReadFromJsonAsync<JsonElement>();
                if(data.GetProperty("status").GetString()!="ready" || !data.GetProperty("features").EnumerateArray().Any(f=>f.GetString()=="bounty-clash-v1"))throw new Exception("Missing ready/capability");checks++;
            }
            using(var stripped=await http.PostAsJsonAsync("internal/deployments",new Deployment(deployment.PlayerId,deployment.DeploymentId,0)))
            {if(stripped.StatusCode!=HttpStatusCode.Conflict)throw new Exception("HTTP stripped mode debit accepted");checks++;}
            async Task<T> Post<T>(string path,object body)
            {
                using var response=await http.PostAsJsonAsync(path,body);response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<T>())!;
            }
            async Task<(Profile Profile,string Token)> Register()
            {
                var auth=await Post<JsonElement>("auth/register",new{Username="clash_"+Guid.NewGuid().ToString("N").Substring(0,16),Password=Convert.ToHexString(RandomNumberGenerator.GetBytes(16))});
                string token=auth.GetProperty("token").GetString()!;
                return (await Post<Profile>("internal/sessions/resolve",new{Token=token}),token);
            }
            var first=await Register();var second=await Register();var match=Guid.NewGuid();var tokenId=Guid.NewGuid();
            var a=new Deployment(first.Profile.PlayerId,Guid.NewGuid(),0,"BountyClash",match,1,0);
            var b=new Deployment(second.Profile.PlayerId,Guid.NewGuid(),0,"BountyClash",match,1,1);
            await Post<JsonElement>("internal/bounty-clash/matches",new BountyMatchRegistration(match,1,tokenId,new[]{new BountyParticipant(a.PlayerId,a.DeploymentId,0),new BountyParticipant(b.PlayerId,b.DeploymentId,1)}));
            var liveA=await Post<Profile>("internal/bounty-clash/deployments",a);
            var liveB=await Post<Profile>("internal/bounty-clash/deployments",b);
            if(liveA.Mode!="BountyClash" || liveA.MatchId!=match || liveA.DeploymentId!=a.DeploymentId)throw new Exception("HTTP deployment lost binding");checks++;
            var eventId=Guid.NewGuid();var award=new BountyRedemption(match,tokenId,eventId,a.DeploymentId,new[]{a.DeploymentId});
            await Post<JsonElement>("internal/bounty-clash/redemptions",award);await Post<JsonElement>("internal/bounty-clash/redemptions",award);
            Settlement Receipt(Profile p,Deployment d,string outcome,Guid? extractedEvent)
            {
                var graph=InventoryRepository.Decode(p.RaidInventoryJson!);
                return new Settlement(d.PlayerId,d.DeploymentId,outcome,graph.Count("dust",true),graph.Count("alloy",true),graph.Count("cells",true),
                    d.DeploymentId,p.RaidInventoryJson,d.Mode,d.MatchId,d.RulesVersion,d.TeamId,extractedEvent);
            }
            var receipt=Receipt(liveA,a,"Extracted",eventId);
            var saved=await Post<Profile>("internal/bounty-clash/settlements",receipt);
            var replay=await Post<Profile>("internal/bounty-clash/settlements",receipt);
            if(saved.Dust!=first.Profile.Dust+80 || replay.InventoryJson!=saved.InventoryJson || saved.ActiveDeploymentId!=null)throw new Exception("HTTP settlement duplicated or lost solo reward");checks++;
            var dead=await Post<Profile>("internal/bounty-clash/settlements",Receipt(liveB,b,"Dead",null));
            if(dead.Dust!=second.Profile.Dust || dead.ActiveDeploymentId!=null)throw new Exception("HTTP death awarded bounty");checks++;
            using(var me=new HttpRequestMessage(HttpMethod.Get,"auth/me"))
            {
                me.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",first.Token);
                using var response=await http.SendAsync(me);response.EnsureSuccessStatusCode();var profile=await response.Content.ReadFromJsonAsync<Profile>();
                if(profile!.Dust!=saved.Dust || profile.ActiveDeploymentId!=null)throw new Exception("HTTP account refresh lost settlement");checks++;
            }
            http.DefaultRequestHeaders.Remove("X-Moon-Server-Key");
            foreach(string path in new[]{"internal/bounty-clash/matches","internal/bounty-clash/deployments","internal/bounty-clash/deployments/refund","internal/bounty-clash/redemptions","internal/bounty-clash/settlements","auth/inventory/recovery/claim"})
            {
                using var response=await http.PostAsJsonAsync(path,new{});if(response.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Missing unauthorized refusal for "+path);checks++;
            }
            using(var response=await http.GetAsync("auth/me")){if(response.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Unauthenticated me accepted");checks++;}
            using(var response=await http.GetAsync("internal/health")){if(response.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Unauthenticated health accepted");checks++;}
            Console.WriteLine($"PASS {checks} isolated real HTTP checks: real account→roster→debit→redemption→per-player extraction/death→replay→account refresh, health and authentication.");return checks;
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await stdout;await stderr;
        }
    }
}
