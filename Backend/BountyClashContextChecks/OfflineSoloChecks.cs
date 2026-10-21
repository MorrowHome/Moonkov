using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Context = Unity.MP_FPS.RaidPersistenceContext;

// Source-only until the coordinator grants a test window. Every fixture is a
// unique Library child; never opens PlayerProfileClient's actual offline save.
internal static class OfflineSoloChecks
{
    public static async Task Run(string parent)
    {
        parent=Path.GetFullPath(parent);
        if(!parent.Contains(Path.DirectorySeparatorChar+"Library"+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new InvalidOperationException("Solo persistence fixtures require an isolated Library directory.");
        string directory=Path.Combine(parent,"solo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        int checks=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        void Reject(Action action,string name)
        {
            try{action();}catch(Exception ex)when(ex is InvalidDataException || ex is Context.LoadoutRejectedException || ex is AccountClient.InventoryRequestException){checks++;return;}
            throw new Exception("Accepted: "+name);
        }
        InventoryGraph Graph(OfflineProfileStore store)=>RaidInventoryTransport.Decode(store.Read().InventoryJson);
        Context.Deployment Deploy(string player,Guid match,Guid id,int cells=0)=>new Context.Deployment {PlayerId=player,DeploymentId=id.ToString("D"),Cells=cells,
            Mode="BountyClash",MatchId=match.ToString("D"),RulesVersion=1,TeamId=0};
        Context.BountyParticipant[] Human(string player,Guid deployment)=>new[]{new Context.BountyParticipant{PlayerId=player,DeploymentId=deployment.ToString("D"),TeamId=0}};
        Context.BountyRedemption Award(Guid match,Guid token,Guid deployment)=>new Context.BountyRedemption {MatchId=match.ToString("D"),TokenId=token.ToString("D"),
            ExtractionEventId=Guid.NewGuid().ToString("D"),CarrierDeploymentId=deployment.ToString("D"),ExtractedDeploymentIds=new[]{deployment.ToString("D")}};
        Context.Settlement Receipt(Context.Profile live,Context.Deployment deployment,string outcome,InventoryGraph graph,Context.BountyRedemption award=null)=>new Context.Settlement
        {PlayerId=live.PlayerId,SettlementId=deployment.DeploymentId,DeploymentId=deployment.DeploymentId,Mode=deployment.Mode,MatchId=deployment.MatchId,
            RulesVersion=deployment.RulesVersion,TeamId=deployment.TeamId,Outcome=outcome,InventoryJson=RaidInventoryTransport.Encode(graph),
            Dust=graph.Count("dust",true),Alloy=graph.Count("alloy",true),Cells=graph.Count("cells",true),BountyEventId=award?.ExtractionEventId,Redemption=award};
        try
        {
            string path=Path.Combine(directory,"profile.json"),player,pistolId;
            var match=Guid.NewGuid();var token=Guid.NewGuid();var deployment=Guid.NewGuid();
            Context.Deployment command;Context.Settlement receipt;Context.BountyRedemption award=Award(match,token,deployment);string saved;
            using(var store=new OfflineProfileStore(path))
            {
                player=store.Read().PlayerId;Check(Guid.TryParse(player,out _),"Offline human has no real GUID");
                Check(JObject.Parse(File.ReadAllText(path)).Value<int>("Format")==2,"New ordinary profile changed format");
                using(var disabled=new Context(store))Check(!disabled.ClashSupported,"Plain offline mode enabled Clash without marker");
                Reject(()=>store.RegisterBountyMatch(match.ToString("D"),1,token.ToString("D"),new[]{new Context.BountyParticipant{PlayerId=Guid.NewGuid().ToString("D"),DeploymentId=deployment.ToString("D"),TeamId=0}}),"bot/fake account registration");
                Reject(()=>store.RegisterBountyMatch(match.ToString("D"),1,token.ToString("D"),new[]{Human(player,deployment)[0],new Context.BountyParticipant{PlayerId=null,DeploymentId=Guid.NewGuid().ToString("D"),TeamId=1}}),"bot roster persisted as a profile");
                var graph=Graph(store);var pistol=graph.Items.First(i=>i.Code=="pistol");pistolId=pistol.Id;
                store.Move(new InventoryCommand{ExpectedVersion=graph.Version,ItemId=pistolId,Parent="equipment",Region="Pistol",X=0,Y=0});
                store.RegisterBountyMatch(match.ToString("D"),1,token.ToString("D"),Human(player,deployment));
                Check(JObject.Parse(File.ReadAllText(path)).Value<int>("Format")==3,"Clash ledger did not exclude old readers");
                store.RegisterBountyMatch(match.ToString("D"),1,token.ToString("D"),Human(player,deployment));
                command=Deploy(player,match,deployment,2);var live=store.Deploy(command);var raid=RaidInventoryTransport.Decode(live.RaidInventoryJson);
                Check(Graph(store).Find(pistolId)==null && raid.Find(pistolId)!=null,"Real self-equipped gun was not debited");
                Check(live.Mode=="BountyClash"&&live.MatchId==match.ToString("D")&&live.DeploymentId==deployment.ToString("D"),"Local deployment lost mode binding");
                Check(store.Deploy(command).RaidInventoryJson==live.RaidInventoryJson,"Retry debited another kit");
                Reject(()=>store.Deploy(new Context.Deployment{PlayerId=player,DeploymentId=deployment.ToString("D").ToUpperInvariant(),Cells=2}),"stripped mode/uppercase identity bypass");
                Reject(()=>store.RegisterBountyMatch(Guid.NewGuid().ToString("D"),1,Guid.NewGuid().ToString("D"),Human(player,Guid.NewGuid())),"new match during open deployment");
                var forged=Award(match,token,Guid.NewGuid());Reject(()=>store.RedeemBounty(forged),"bot or other carrier awarded to human");
                store.RedeemBounty(award);store.RedeemBounty(award);
                Reject(()=>store.RedeemBounty(Award(match,token,deployment)),"second event for same token");
                Reject(()=>store.Settle(Receipt(live,command,"Dead",raid,award)),"death awarded extraction bounty");
                Check(raid.AddSupply("dust",1,foundInRaid:true)==InventoryError.None,"Fixture cargo placement failed");
                receipt=Receipt(live,command,"Extracted",raid,award);var result=store.Settle(receipt);saved=result.InventoryJson;
                Check(result.Dust==101+80 && Graph(store).Find(pistolId)!=null && !store.RaidActive,"Extraction lost loot/gear or did not pay exactly solo80");
                Check(Graph(store).MissionFirsts==0,"Solo objective triggered MC00 first recovery");
                Check(store.Settle(receipt).InventoryJson==saved,"Settlement retry duplicated bounty");
            }
            using(var restarted=new OfflineProfileStore(path))
            {
                Check(restarted.Read().InventoryJson==saved && restarted.Settle(receipt).InventoryJson==saved,"Restart duplicated or lost local award");
                restarted.RedeemBounty(award);Check(restarted.Read().Dust==181,"Redeem replay minted money");
                Reject(()=>restarted.Deploy(command),"closed epoch debited gear again");
                Reject(()=>restarted.RegisterBountyMatch(Guid.NewGuid().ToString("D"),1,token.ToString("D"),Human(player,Guid.NewGuid())),"old token reused by next match");
                // A bot carrying the token has no local redemption. Human cargo may
                // still extract without receiving that token's bounty.
                var m=Guid.NewGuid();var d=Guid.NewGuid();var t=Guid.NewGuid();restarted.RegisterBountyMatch(m.ToString("D"),1,t.ToString("D"),Human(player,d));
                var cmd=Deploy(player,m,d);var live=restarted.Deploy(cmd);var raid=RaidInventoryTransport.Decode(live.RaidInventoryJson);
                var noBounty=restarted.Settle(Receipt(live,cmd,"Extracted",raid));Check(noBounty.Dust==181,"Bot token extraction rewarded human");
                Reject(()=>restarted.RedeemBounty(Award(m,t,d)),"late award after cargo-only closed epoch");
                // A charged deployment that never starts combat returns the exact
                // original gun/cells, without a first-recovery or bounty reward.
                m=Guid.NewGuid();d=Guid.NewGuid();restarted.RegisterBountyMatch(m.ToString("D"),1,Guid.NewGuid().ToString("D"),Human(player,d));
                cmd=Deploy(player,m,d,2);restarted.Deploy(cmd);var refund=restarted.Request("internal/bounty-clash/deployments/refund",cmd);
                Check(!restarted.RaidActive && Graph(restarted).Find(pistolId)!=null && refund.Cells==12 && refund.Dust==181,"Charged abort did not refund the exact kit");
                Check(restarted.Request("internal/bounty-clash/deployments/refund",cmd).InventoryJson==refund.InventoryJson && Graph(restarted).MissionFirsts==0,"Charged refund replay minted gear/rewards");
                // Terminal death consumes the original carried kit exactly once.
                m=Guid.NewGuid();d=Guid.NewGuid();restarted.RegisterBountyMatch(m.ToString("D"),1,Guid.NewGuid().ToString("D"),Human(player,d));
                cmd=Deploy(player,m,d);live=restarted.Deploy(cmd);raid=RaidInventoryTransport.Decode(live.RaidInventoryJson);
                var dead=Receipt(live,cmd,"Dead",raid);restarted.Settle(dead);restarted.Settle(dead);
                Check(Graph(restarted).Find(pistolId)==null && restarted.Read().Dust==181-raid.Count("dust",true),"Death refunded carried gun or paid bounty");
                // Refund before a debit leaves a tombstone; late successful retry is forbidden.
                m=Guid.NewGuid();d=Guid.NewGuid();restarted.RegisterBountyMatch(m.ToString("D"),1,Guid.NewGuid().ToString("D"),Human(player,d));
                cmd=Deploy(player,m,d);restarted.Request("internal/bounty-clash/deployments/refund",cmd);
                Reject(()=>restarted.Deploy(cmd),"late debit after abort refund");
            }
            // Exercise the actual offline context API bridge. Only the explicit
            // local enable flag may register/deploy; there is no HTTP/fake account.
            using(var store=new OfflineProfileStore(Path.Combine(directory,"bridge","profile.json")))
            {
                var context=new Context(store);var m=Guid.NewGuid();var d=Guid.NewGuid();var t=Guid.NewGuid();player=store.Read().PlayerId;
                try{await context.RegisterBountyMatchAsync(m,1,t,Human(player,d));throw new Exception("Unmarked local context registered Clash");}
                catch(Context.LoadoutRejectedException){checks++;}
                context.SetClashSoloEnabled(true);Check(context.ClashSupported,"Explicit local context did not advertise Clash");
                await context.RegisterBountyMatchAsync(m,1,t,Human(player,d));var connection=new Entity{Index=77};context.Profiles[connection]=store.Read();
                context.BeginBountyDeploy(connection,m,1,0,d,0);var live=await context.Deployments[connection].Task;context.Deployments.Remove(connection);
                context.Profiles[connection]=live;context.Inventories[connection]=new Unity.MP_FPS.RaidInventoryState{Graph=RaidInventoryTransport.Decode(live.RaidInventoryJson),RaidId=1};
                context.BeginBountyRedemption(m,t,Guid.NewGuid(),d,new[]{d});await Task.WhenAll(context.Saves.Values.Select(s=>s.Task));context.CleanBountyJournals();
                Check(!store.RaidActive&&store.Read().Dust==180&&Directory.GetFiles(store.OutboxDirectory).Length==0,"Offline context bridge did not settle actual solo80");
                context.SetClashSoloEnabled(false);Check(!context.ClashSupported,"Removing local enable left Clash capability active");
                Reject(()=>context.BeginBountyDeploy(connection,Guid.NewGuid(),1,0,Guid.NewGuid(),0),"disabled local context submitted new debit");
                context.Dispose();await context.ShutdownTask;
            }
            // Crash recovery from an immutable .bounty group must work even in the
            // ordinary offline loader, before the player selects a mode/marker.
            string crash=Path.Combine(directory,"crash.json");Context.Settlement recoveryReceipt;
            using(var store=new OfflineProfileStore(crash))
            {
                var m=Guid.NewGuid();var d=Guid.NewGuid();var t=Guid.NewGuid();player=store.Read().PlayerId;
                store.RegisterBountyMatch(m.ToString("D"),1,t.ToString("D"),Human(player,d));var cmd=Deploy(player,m,d);var live=store.Deploy(cmd);
                var cargo=RaidInventoryTransport.Decode(live.RaidInventoryJson);var payout=Award(m,t,d);recoveryReceipt=Receipt(live,cmd,"Extracted",cargo,payout);
                Directory.CreateDirectory(store.OutboxDirectory);
                File.WriteAllText(Path.Combine(store.OutboxDirectory, m.ToString("N")+".bounty"),JsonConvert.SerializeObject(new{Redemption=payout,Settlements=new[]{recoveryReceipt}}));
            }
            using(var store=new OfflineProfileStore(crash))
            {
                var context=new Context(store);Check(!context.ClashSupported,"Recovery silently enables new offline Clash play");
                await Task.WhenAll(context.Saves.Values.Select(s=>s.Task));context.CleanBountyJournals();
                Check(!store.RaidActive && store.Read().Dust==180 && Directory.GetFiles(store.OutboxDirectory).Length==0,"Crash replay lost payout or retained receipt");
                context.Dispose();await context.ShutdownTask;Check(store.Settle(recoveryReceipt).Dust==180,"Crash replay duplicated payout");
            }
            // No successful extraction receipt means the interrupted carried kit
            // remains lost, and reopening cannot resurrect it.
            string abandoned=Path.Combine(directory,"abandoned.json");
            using(var store=new OfflineProfileStore(abandoned))
            {
                var m=Guid.NewGuid();var d=Guid.NewGuid();player=store.Read().PlayerId;store.RegisterBountyMatch(m.ToString("D"),1,Guid.NewGuid().ToString("D"),Human(player,d));store.Deploy(Deploy(player,m,d,1));
            }
            using(var store=new OfflineProfileStore(abandoned)){store.RecoverAbandonedRaid();Check(!store.RaidActive&&store.Read().Cells==11,"Abandoned solo raid refunded used kit");}
            using(var store=new OfflineProfileStore(abandoned))Check(!store.RaidActive&&store.Read().Cells==11,"Restart resurrected abandoned deployment");
            // Future/corrupt formats preserve the exact file and do not create/reset a profile.
            string future=Path.Combine(directory,"future.json");var raw=JObject.Parse(File.ReadAllText(path));raw["Format"]=99;string bytes=raw.ToString();File.WriteAllText(future,bytes);
            Reject(()=>{using var ignored=new OfflineProfileStore(future);},"future format accepted");Check(File.ReadAllText(future)==bytes,"Unknown format was rewritten");
            Console.WriteLine($"PASS {checks} isolated Library offline solo persistence checks: human-only identity, Format3, debit/loss/extraction/solo80, idempotence/old epochs, bot-no-award, abort and crash recovery. No real profiles/Prefs touched.");
        }
        finally{Directory.Delete(directory,true);}
    }
}
