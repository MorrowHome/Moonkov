using Npgsql;
using System.Text.Json;
using Unity.MP_FPS.Inventory;

var settings=new NpgsqlConnectionStringBuilder(args[0]);
if(settings.Host!="127.0.0.1" || !settings.Database!.StartsWith("moon_clash_check_",StringComparison.Ordinal) || settings.Username!="moon_clash_test")
    throw new InvalidOperationException("Only an explicit disposable loopback moon_clash_check_* database is accepted.");
await using(var admin=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(settings.ConnectionString){Database="postgres"}.ConnectionString))
{
    await admin.OpenAsync();await using var exists=new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname=$1",admin);exists.Parameters.AddWithValue(settings.Database!);
    if(await exists.ExecuteScalarAsync()==null)
    {await using var create=new NpgsqlCommand("CREATE DATABASE \""+settings.Database!.Replace("\"","\"\"")+"\"",admin);await create.ExecuteNonQueryAsync();}
}
if(args.Length==2 && args[1]=="--prepare-only"){Console.WriteLine("Prepared isolated database for transport validation.");return;}
await using var db=NpgsqlDataSource.Create(settings.ConnectionString);
var store=new StashRepository(db);var inventory=new InventoryRepository(db);var clash=new BountyClashRepository(db,new BountyEconomy());
int checks=0;
void Check(bool condition,string name) { if(!condition)throw new Exception(name);checks++; }
async Task Reject(Func<Task> action,string name)
{
    try{await action();}catch(Exception ex)when(ex is ReceiptConflictException or DeploymentRejectedException or PostgresException){checks++;return;}
    throw new Exception("Accepted: "+name);
}
async Task<Profile> Player(string name)=>await store.ResolveAsync(new ResolveProfile(Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),name),default);
async Task<long> Scalar(string sql) {await using var q=db.CreateCommand(sql);return (long)(await q.ExecuteScalarAsync())!;}
async Task Write(Guid player,InventoryGraph graph)
{await using var c=await db.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();await InventoryRepository.WriteGraphAsync(c,tx,player,graph,default);await tx.CommitAsync();}
RecoveryInbox Inbox(Profile p)=>JsonSerializer.Deserialize<RecoveryInbox>(p.RecoveryJson!,new JsonSerializerOptions{IncludeFields=true})!;
Settlement Receipt(Profile profile,Deployment deployment,string outcome="Extracted",Guid? eventId=null,InventoryGraph? graph=null)
{
    graph??=InventoryRepository.Decode(profile.RaidInventoryJson!);
    return new Settlement(profile.PlayerId,deployment.DeploymentId,outcome,graph.Count("dust",true),graph.Count("alloy",true),graph.Count("cells",true),
        deployment.DeploymentId,InventoryRepository.Encode(graph),deployment.Mode,deployment.MatchId,deployment.RulesVersion,deployment.TeamId,eventId);
}
// Start with the real schema8 text; prove incremental initialization retains its account graph.
await using(var c=await db.OpenConnectionAsync())
{await using var old=new NpgsqlCommand(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"schema.sql")).Split("-- Version 9:")[0],c);await old.ExecuteNonQueryAsync();}
var oldPlayer=await Player("legacy");string oldGraph=oldPlayer.InventoryJson!;
await store.InitializeAsync();await store.InitializeAsync();
Check(await Scalar("SELECT count(*) FROM schema_version WHERE version=9")==1,"schema9 missing");
await using(var c=await db.OpenConnectionAsync())await using(var tx=await c.BeginTransactionAsync())
    Check((await InventoryRepository.ReadAsync(c,tx,oldPlayer.PlayerId,default)).InventoryJson==oldGraph,"schema8 inventory changed");
var ordinary=new Deployment(oldPlayer.PlayerId,Guid.NewGuid(),0);
var oldLive=await inventory.DeployAsync(ordinary,default);var oldSaved=await store.SettleAsync(Receipt(oldLive,ordinary),default);
Check(oldSaved.ActiveDeploymentId==null,"ordinary expedition regression");
var a=await Player("duo-a");var b=await Player("duo-b");var cplayer=await Player("enemy");var d=await Player("solo-full");
var match=Guid.NewGuid();var token=Guid.NewGuid();
Deployment D(Profile p,int team)=>new(p.PlayerId,Guid.NewGuid(),0,"BountyClash",match,1,team);
var da=D(a,0);var dbb=D(b,0);var dc=D(cplayer,1);var dd=D(d,2);
var roster=new[]{new BountyParticipant(a.PlayerId,da.DeploymentId,0),new BountyParticipant(b.PlayerId,dbb.DeploymentId,0),new BountyParticipant(cplayer.PlayerId,dc.DeploymentId,1),new BountyParticipant(d.PlayerId,dd.DeploymentId,2)};
var registration=new BountyMatchRegistration(match,1,token,roster);
await clash.RegisterAsync(registration,default);
await clash.RegisterAsync(registration with{Participants=roster.Reverse().ToArray()},default);
Check(await Scalar("SELECT count(*) FROM bounty_clash_matches")==1,"registration replay duplicated match");
await Reject(()=>clash.RegisterAsync(registration with{Participants=roster.Take(3).ToArray()},default),"changed roster");
await Reject(()=>clash.RegisterAsync(registration with{RulesVersion=2},default),"unknown rules");
await Reject(()=>inventory.DeployAsync(da,default),"mode metadata sent to ordinary endpoint");
await Reject(()=>inventory.DeployAsync(new Deployment(a.PlayerId,da.DeploymentId,0),default),"mode stripped ordinary debit");
await Reject(()=>inventory.DeployAsync(da with{MatchId=Guid.NewGuid()},default,true),"cross-match debit");
await Reject(()=>inventory.DeployAsync(da with{TeamId=1},default,true),"forged team");
await Reject(()=>inventory.DeployAsync(da with{PlayerId=b.PlayerId},default,true),"cross-account debit");
var la=await inventory.DeployAsync(da,default,true);var lb=await inventory.DeployAsync(dbb,default,true);var lc=await inventory.DeployAsync(dc,default,true);
var retry=await inventory.DeployAsync(da,default,true);
Check(retry.InventoryJson==la.InventoryJson && retry.RaidInventoryJson==la.RaidInventoryJson,"retry charged loadout again");
var extraction=new BountyRedemption(match,token,Guid.NewGuid(),da.DeploymentId,new[]{da.DeploymentId,dbb.DeploymentId});
await Reject(()=>clash.RedeemAsync(extraction with{ExtractedDeploymentIds=new[]{da.DeploymentId,dc.DeploymentId}},default),"enemy reward");
await Reject(()=>clash.RedeemAsync(extraction with{TokenId=Guid.NewGuid()},default),"forged token");
await Reject(()=>clash.RedeemAsync(extraction with{ExtractedDeploymentIds=new[]{da.DeploymentId,da.DeploymentId}},default),"duplicate recipient");
await Task.WhenAll(clash.RedeemAsync(extraction,default),clash.RedeemAsync(extraction with{ExtractedDeploymentIds=extraction.ExtractedDeploymentIds.Reverse().ToArray()},default));
Check(await Scalar("SELECT count(*) FROM bounty_clash_redemptions")==1 && await Scalar("SELECT count(*) FROM bounty_clash_awards")==2,"one token has multiple redemptions");
await Reject(()=>clash.RedeemAsync(extraction with{ExtractionEventId=Guid.NewGuid()},default),"second event");
await Reject(()=>clash.RedeemAsync(extraction with{CarrierDeploymentId=dbb.DeploymentId},default),"changed carrier");
await Reject(()=>clash.RedeemAsync(extraction with{ExtractedDeploymentIds=new[]{da.DeploymentId}},default),"changed award roster");
await Reject(()=>store.SettleAsync(Receipt(la,da,eventId:extraction.ExtractionEventId),default),"ordinary settlement with mode");
await Reject(()=>store.SettleAsync(new Settlement(a.PlayerId,da.DeploymentId,"Extracted",0,0,0),default),"stripped no-deployment settlement");
await Reject(()=>store.SettleAsync(Receipt(la,da,eventId:Guid.NewGuid()),default,true),"wrong extraction event");
await Reject(()=>store.SettleAsync(Receipt(la,da,"Dead",extraction.ExtractionEventId),default,true),"dead player award");
var raid=InventoryRepository.Decode(la.RaidInventoryJson!);raid.AddSupply("dust",1,foundInRaid:true);
var ra=Receipt(la,da,eventId:extraction.ExtractionEventId,graph:raid);
var sa=await store.SettleAsync(ra,default,true);var sb=await store.SettleAsync(Receipt(lb,dbb,eventId:extraction.ExtractionEventId),default,true);
Check(sa.Dust==a.Dust+36 && sb.Dust==b.Dust+35,"duo award is not fixed opening-size 35 per actual recipient");
Check(InventoryRepository.Decode(sa.InventoryJson!).MissionFirsts==0,"clash loot triggered MC00 first rewards");
Check(await Scalar("SELECT count(*) FROM bounty_clash_awards WHERE claimed")==2,"award not atomically claimed");
var restarted=new StashRepository(db);await restarted.InitializeAsync();var sa2=await restarted.SettleAsync(ra,default,true);
Check(sa2.InventoryJson==sa.InventoryJson && sa2.RecoveryJson==sa.RecoveryJson,"restart settlement retry duplicated reward");
await Reject(()=>restarted.SettleAsync(ra with{BountyEventId=null},default,true),"changed receipt metadata");
var sc=await store.SettleAsync(Receipt(lc,dc),default,true);Check(sc.Dust==cplayer.Dust,"unawarded enemy extraction received reward");
// An abort cancels an undebited reservation and makes any delayed debit impossible.
var cancelled=await inventory.RefundBountyDeploymentAsync(dd,store,default);
Check(cancelled.ActiveDeploymentId==null,"cancel did not close absent debit");
await Reject(()=>inventory.DeployAsync(dd,default,true),"late debit after refund tombstone");
// New match: full warehouse and 80-dust solo reward fit exactly the original two batches.
var match2=Guid.NewGuid();var token2=Guid.NewGuid();var fullD=new Deployment(d.PlayerId,Guid.NewGuid(),0,"BountyClash",match2,1,0);
var opponentD=new Deployment(cplayer.PlayerId,Guid.NewGuid(),0,"BountyClash",match2,1,1);
await clash.RegisterAsync(new(match2,1,token2,new[]{new BountyParticipant(d.PlayerId,fullD.DeploymentId,0),new BountyParticipant(cplayer.PlayerId,opponentD.DeploymentId,1)}),default);
var full=InventoryGraph.Create(true,false);full.WeaponKitVersion=1;full.StashRows=4096;
for(int i=0;i<InventoryCatalog.MaxItems-3;i++)full.Items.Add(new InventoryItem{Id=Guid.NewGuid().ToString("D"),Code="dust",Quantity=1,Parent="stash",Region="main",X=i%10,Y=i/10});
await Write(d.PlayerId,full);var ld=await inventory.DeployAsync(fullD,default,true);
var rfull=InventoryRepository.Decode(ld.RaidInventoryJson!);rfull.AddSupply("dust",1,foundInRaid:true);
var ex2=new BountyRedemption(match2,token2,Guid.NewGuid(),fullD.DeploymentId,new[]{fullD.DeploymentId});await clash.RedeemAsync(ex2,default);
var fullReceipt=Receipt(ld,fullD,eventId:ex2.ExtractionEventId,graph:rfull);var sd=await store.SettleAsync(fullReceipt,default,true);
Check(sd.ActiveDeploymentId==null && Inbox(sd).Batches.Count==2 && Inbox(sd).Batches.Sum(g=>g.Count("dust"))==81,"full warehouse lost cargo or solo80 reward");
Check(InventoryRepository.Decode(sd.InventoryJson!).MissionFirsts==0,"full warehouse clash incorrectly granted firsts");
Check((await store.SettleAsync(fullReceipt,default,true)).RecoveryJson==sd.RecoveryJson,"full reward retry changed overflow");
await Reject(()=>inventory.DeployAsync(new Deployment(d.PlayerId,Guid.NewGuid(),0),default),"recovery pending gate");
var accountAfter=InventoryRepository.Decode(sd.InventoryJson!);accountAfter.Items.RemoveRange(3,5);accountAfter.Version++;await Write(d.PlayerId,accountAfter);
var claim=new RecoveryClaim{RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=accountAfter.Version,ExpectedRecoveryVersion=Inbox(sd).Version};
var claimed=await inventory.ClaimRecoveryAsync(d.PlayerId,claim,default);
Check(Inbox(claimed).ItemCount()==0 && claimed.Dust==sd.Dust-5+81,"solo80 pending claim lost money or cargo");
Check((await inventory.ClaimRecoveryAsync(d.PlayerId,claim,default)).InventoryJson==claimed.InventoryJson,"solo pending claim duplicated reward");
// Charged but unspawned kit is refunded from the immutable backend deployment graph.
var lo=await inventory.DeployAsync(opponentD,default,true);var originalIds=InventoryRepository.Decode(lo.RaidInventoryJson!).Items.Where(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root).Select(i=>i.Id).ToArray();
var refunded=await inventory.RefundBountyDeploymentAsync(opponentD,store,default);
Check(refunded.ActiveDeploymentId==null && originalIds.All(id=>InventoryRepository.Decode(refunded.InventoryJson!).Find(id)!=null),"refund lost original kit");
Check((await inventory.RefundBountyDeploymentAsync(opponentD,store,default)).InventoryJson==refunded.InventoryJson,"refund replay duplicated kit");
Check(InventoryRepository.Decode(refunded.InventoryJson!).MissionFirsts==0,"refund minted mission firsts");
await Reject(()=>clash.RedeemAsync(ex2 with{ExtractedDeploymentIds=new[]{opponentD.DeploymentId},CarrierDeploymentId=opponentD.DeploymentId},default),"later alternate winner");
// One surviving member of an opening trio receives 20, never solo80.
var match3=Guid.NewGuid();var token3=Guid.NewGuid();
var t1=new Deployment(a.PlayerId,Guid.NewGuid(),0,"BountyClash",match3,1,0);
var t2=new Deployment(b.PlayerId,Guid.NewGuid(),0,"BountyClash",match3,1,0);
var t3=new Deployment(cplayer.PlayerId,Guid.NewGuid(),0,"BountyClash",match3,1,0);
var enemy=new Deployment(oldPlayer.PlayerId,Guid.NewGuid(),0,"BountyClash",match3,1,1);
await clash.RegisterAsync(new(match3,1,token3,new[]{new BountyParticipant(a.PlayerId,t1.DeploymentId,0),new BountyParticipant(b.PlayerId,t2.DeploymentId,0),new BountyParticipant(cplayer.PlayerId,t3.DeploymentId,0),new BountyParticipant(oldPlayer.PlayerId,enemy.DeploymentId,1)}),default);
var l1=await inventory.DeployAsync(t1,default,true);var l2=await inventory.DeployAsync(t2,default,true);var l3=await inventory.DeployAsync(t3,default,true);var le=await inventory.DeployAsync(enemy,default,true);
await store.SettleAsync(Receipt(l2,t2,"Dead"),default,true);
var ex3=new BountyRedemption(match3,token3,Guid.NewGuid(),t1.DeploymentId,new[]{t1.DeploymentId});await clash.RedeemAsync(ex3,default);
var s1=await store.SettleAsync(Receipt(l1,t1,eventId:ex3.ExtractionEventId),default,true);
Check(s1.Dust==sa.Dust+20,"trio survivor was granted solo or duo bonus");
var s3=await store.SettleAsync(Receipt(l3,t3),default,true);Check(s3.Dust==sc.Dust,"same-team separate extraction incorrectly received bounty");
await store.SettleAsync(Receipt(le,enemy,"TimedOut"),default,true);
checks+=await HttpChecks.Run(settings.ConnectionString,da);
Console.WriteLine($"PASS {checks} Bounty Clash PostgreSQL/HTTP checks: schema8 migration, immutable roster/binding, unique redemption, fixed opening-team awards, per-player replay/restart, no MC00 firsts, bounded full recovery, abort refund and late debit refusal.");
