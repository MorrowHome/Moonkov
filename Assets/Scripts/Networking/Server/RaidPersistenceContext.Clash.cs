using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class RaidPersistenceContext
    {
        public sealed class BountyParticipant { public string PlayerId, DeploymentId; public int TeamId; }
        private sealed class BountyRegistration { public string MatchId, TokenId; public int RulesVersion; public BountyParticipant[] Participants; }
        public sealed class BountyRedemption
        { public string MatchId, TokenId, ExtractionEventId, CarrierDeploymentId; public string[] ExtractedDeploymentIds; }
        private sealed class BountyFinalization { public BountyRedemption Redemption; public Settlement[] Settlements; }
        private sealed class Capability { public string[] Features { get; set; } }
        private readonly Task<bool> m_ClashCapability = Task.FromResult(false);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task> m_ClashRegistrations = new System.Collections.Concurrent.ConcurrentDictionary<string, Task>();
        private bool m_ClashSoloEnabled;
        public bool ClashSupported => m_Offline!=null ? m_ClashSoloEnabled :
            m_ClashCapability.Status==TaskStatus.RanToCompletion && m_ClashCapability.Result;
        // The server World supplies the explicit OfflineRaidMode + ClashSoloMode
        // + BountyClash gate. This is never deserialized from a client's request.
        public void SetClashSoloEnabled(bool enabled)
        {
            if(enabled && m_Offline==null)throw new InvalidOperationException("Solo Clash requires an isolated offline profile.");
            m_ClashSoloEnabled=enabled;
        }

        private async Task<bool> CheckClashCapabilityAsync()
        {
            try
            {
                using (var response = await m_Http.GetAsync("internal/health", m_Stop.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return false;
                    var health = JsonConvert.DeserializeObject<Capability>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    return health?.Features?.Contains("bounty-clash-v1") == true;
                }
            }
            catch (Exception) { return false; }
        }
        private async Task<Profile> ResolveJoinAsync(string token)
        { await m_ClashCapability.ConfigureAwait(false); return await PostAsync("internal/sessions/resolve", new { Token=token }).ConfigureAwait(false); }
        private async Task PostClashAsync(string path, object payload)
        {
            if(m_Offline!=null)
            {
                if(path=="matches")
                {
                    if(!m_ClashSoloEnabled)throw new LoadoutRejectedException(RaidLoadoutError.Rejected,"Offline Clash was not explicitly enabled by this server World.");
                    var registration=(BountyRegistration)payload;
                    await Task.Run(()=>m_Offline.RegisterBountyMatch(registration.MatchId,registration.RulesVersion,registration.TokenId,registration.Participants)).ConfigureAwait(false);
                }
                else if(path=="redemptions")
                {
                    // Recovery may run before a new mode is selected. The store
                    // accepts only an already registered, owned deployment/event.
                    await Task.Run(()=>m_Offline.RedeemBounty((BountyRedemption)payload)).ConfigureAwait(false);
                }
                else throw new InvalidOperationException("Unsupported local Clash operation.");
                return;
            }
            if (m_Http == null || !await m_ClashCapability.ConfigureAwait(false))
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,"Bounty Clash persistence is unavailable; update the backend.");
            using (var content = new System.Net.Http.StringContent(JsonConvert.SerializeObject(payload),System.Text.Encoding.UTF8,"application/json"))
            using (var response = await m_Http.PostAsync("internal/bounty-clash/"+path,content,m_Stop.Token).ConfigureAwait(false))
                response.EnsureSuccessStatusCode();
        }
        public Task RegisterBountyMatchAsync(Guid matchId, int rulesVersion, Guid tokenId, BountyParticipant[] participants)
        {
            var registration = new BountyRegistration {MatchId=matchId.ToString("D"),RulesVersion=rulesVersion,TokenId=tokenId.ToString("D"),Participants=participants};
            var task = PostClashAsync("matches",registration);
            m_ClashRegistrations[registration.MatchId]=task; return task;
        }
        public void BeginBountyDeploy(Entity connection, Guid matchId, int rulesVersion, int teamId, Guid deploymentId, int cells, Join join=null)
        {
            if(m_Offline!=null && !m_ClashSoloEnabled)throw new LoadoutRejectedException(RaidLoadoutError.Rejected,"Offline Clash requires its explicit local marker.");
            if (Deployments.ContainsKey(connection)) return;
            var payload = new Deployment {PlayerId=Profiles[connection].PlayerId,DeploymentId=deploymentId.ToString("D"),Cells=cells,
                Mode="BountyClash",MatchId=matchId.ToString("D"),RulesVersion=rulesVersion,TeamId=teamId};
            Deployments.Add(connection,new Deploy {Connection=connection,Join=join,Payload=payload,Task=DeployAsync(payload)});
        }
        private async Task AwaitBountyRegistrationAsync(Deployment payload)
        {
            if (payload.Mode==null) return;
            if(m_Offline!=null && !m_ClashSoloEnabled)throw new LoadoutRejectedException(RaidLoadoutError.Rejected,"Offline Clash deployment is not enabled.");
            if (payload.Mode!="BountyClash" || !m_ClashRegistrations.TryGetValue(payload.MatchId,out var registration))
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,"Bounty Clash roster has not been registered.");
            try { await registration.ConfigureAwait(false); }
            catch(Exception) { throw new LoadoutRejectedException(RaidLoadoutError.Rejected,"Bounty Clash roster registration failed before debit."); }
        }
        public void RefundBountyDeployment(Deployment payload, Task<Profile> debitTask=null)
        {
            if(Saves.ContainsKey(payload.DeploymentId))return;
            if(debitTask==null && !File.Exists(DeploymentFile(payload)))return;
            payload.Refund=true;
            var save=new Save {Payload=new Settlement {PlayerId=payload.PlayerId,SettlementId=payload.DeploymentId},AbandonedDeployment=payload};
            Saves.Add(payload.DeploymentId,save);
            save.Task=Task.Run(async()=>
            {
                if(debitTask!=null) {try {await debitTask.ConfigureAwait(false);} catch(Exception) { /* The journal distinguishes a possible debit from a failed registration. */ }}
                if(!File.Exists(DeploymentFile(payload)))return new Profile {PlayerId=payload.PlayerId}; // No debit was submitted.
                WriteJournal(Path.ChangeExtension(DeploymentFile(payload),".refund"),payload);
                return await RetrySave(save).ConfigureAwait(false);
            });
        }
        // Called only after server-side authentication (or when detaching the
        // already authenticated owner). Never changes the immutable receipt/task.
        public bool RebindBountyOwner(Entity oldOwner, Entity newOwner, string authenticatedPlayerId, string settlementId)
        {
            if(oldOwner.Equals(newOwner) || !Guid.TryParse(authenticatedPlayerId,out var playerId) || playerId==Guid.Empty ||
                !Guid.TryParse(settlementId,out var receiptId) || receiptId==Guid.Empty ||
                !Profiles.TryGetValue(oldOwner,out var source) || !Guid.TryParse(source.PlayerId,out var sourceId) || sourceId!=playerId)
                return false;
            if(source.DeploymentId!=null && (!Guid.TryParse(source.DeploymentId,out var deploymentId) || deploymentId!=receiptId))return false;
            Profiles.TryGetValue(newOwner,out var destination);
            if(destination!=null && (!Guid.TryParse(destination.PlayerId,out var destinationId) || destinationId!=playerId))return false;
            Saves.TryGetValue(receiptId.ToString("D"),out var save);
            if(save!=null && (!save.Connection.Equals(oldOwner) || !Guid.TryParse(save.Payload.PlayerId,out var savePlayer) || savePlayer!=playerId ||
                !Guid.TryParse(save.Payload.SettlementId,out var saveReceipt) || saveReceipt!=receiptId))return false;
            if(Saves.Values.Any(pending=>pending.Connection.Equals(oldOwner) && !ReferenceEquals(pending,save)))return false;
            Inventories.TryGetValue(oldOwner,out var inventory);
            if(inventory!=null && Inventories.TryGetValue(newOwner,out var existing) && !ReferenceEquals(inventory,existing))return false;
            // An open deployment needs its original raid binding. A completed one
            // can use the freshly authenticated account graph instead of a cache.
            Profiles[newOwner]=source.DeploymentId==null && destination!=null ? destination : source;
            Profiles.Remove(oldOwner);
            if(inventory!=null){Inventories[newOwner]=inventory;Inventories.Remove(oldOwner);}
            if(save!=null)save.Connection=newOwner;
            return true;
        }
        internal static Deployment DeploymentOf(Profile profile) => new Deployment {PlayerId=profile.PlayerId,DeploymentId=profile.DeploymentId,
            Cells=profile.CarriedCells,Mode=profile.Mode,MatchId=profile.MatchId,RulesVersion=profile.RulesVersion,TeamId=profile.TeamId};
        private static string ProfilePath(string path, string mode) => mode=="BountyClash" ? "internal/bounty-clash/"+path : "internal/"+path;
        private string BountyFile(BountyRedemption redemption) => Path.Combine(m_Outbox,Guid.Parse(redemption.MatchId).ToString("N")+".bounty");

        // Capture the whole extraction atomically before redeeming. A crash at any
        // later point can reconstruct every participant's immutable inventory receipt.
        public void BeginBountyRedemption(Guid matchId, Guid tokenId, Guid extractionEventId, Guid carrierDeploymentId, Guid[] extractedDeploymentIds)
        {
            if(m_Offline!=null && !m_ClashSoloEnabled)throw new InvalidOperationException("New local Clash extraction requires its explicit server marker.");
            var redemption = new BountyRedemption {MatchId=matchId.ToString("D"),TokenId=tokenId.ToString("D"),ExtractionEventId=extractionEventId.ToString("D"),
                CarrierDeploymentId=carrierDeploymentId.ToString("D"),ExtractedDeploymentIds=extractedDeploymentIds.Select(id=>id.ToString("D")).ToArray()};
            var snapshots = new List<Settlement>();
            foreach (var id in redemption.ExtractedDeploymentIds)
            {
                var pair = Profiles.Single(p=>p.Value.DeploymentId==id && p.Value.MatchId==redemption.MatchId && p.Value.Mode=="BountyClash");
                var graph = Inventories[pair.Key].Graph;
                snapshots.Add(new Settlement {PlayerId=pair.Value.PlayerId,DeploymentId=id,SettlementId=id,Outcome="Extracted",
                    Dust=graph.Count("dust",true),Alloy=graph.Count("alloy",true),Cells=graph.Count("cells",true),InventoryJson=JsonConvert.SerializeObject(graph),
                    Mode=pair.Value.Mode,MatchId=pair.Value.MatchId,RulesVersion=pair.Value.RulesVersion,TeamId=pair.Value.TeamId,
                    BountyEventId=redemption.ExtractionEventId,Redemption=redemption});
            }
            var finalization = new BountyFinalization {Redemption=redemption,Settlements=snapshots.ToArray()};
            // Queue all saves before any async request. Shutdown cannot abandon a member.
            var journal = Task.Run(()=>WriteJournal(BountyFile(redemption),finalization));
            foreach (var payload in snapshots)
            {
                if (Saves.ContainsKey(payload.SettlementId)) throw new InvalidOperationException("Bounty recipient is already settling.");
                var connection=Profiles.Single(p=>p.Value.DeploymentId==payload.DeploymentId).Key;
                var save=new Save {Connection=connection,RaidId=Inventories[connection].RaidId,Payload=payload};
                Saves.Add(payload.SettlementId,save);
                save.Task=SaveBountyAsync(payload,journal);
            }
        }
        private void ReplayBountyFinalizations()
        {
            foreach (string file in Directory.GetFiles(m_Outbox,"*.bounty"))
            {
                var group=JsonConvert.DeserializeObject<BountyFinalization>(File.ReadAllText(file));
                if (group?.Redemption==null || group.Settlements==null || group.Settlements.Length is <1 or >3 ||
                    Path.GetFullPath(file)!=BountyFile(group.Redemption) ||
                    !group.Redemption.ExtractedDeploymentIds.OrderBy(id=>id).SequenceEqual(group.Settlements.Select(p=>p.DeploymentId).OrderBy(id=>id)))
                    throw new InvalidDataException("Invalid Bounty Clash finalization journal.");
                foreach (var payload in group.Settlements)
                {
                    if (payload.Mode!="BountyClash" || payload.MatchId!=group.Redemption.MatchId || payload.BountyEventId!=group.Redemption.ExtractionEventId ||
                        !Guid.TryParse(payload.PlayerId,out _) || payload.SettlementId!=payload.DeploymentId || payload.InventoryJson==null)
                        throw new InvalidDataException("Invalid Bounty Clash settlement journal.");
                    payload.Redemption=group.Redemption;
                    if (Saves.ContainsKey(payload.SettlementId)) throw new InvalidDataException("Duplicate Bounty Clash finalization.");
                    var save=new Save {Payload=payload};Saves.Add(payload.SettlementId,save);save.Task=SaveBountyAsync(payload,Task.CompletedTask);
                }
            }
        }
        private async Task<Profile> SaveBountyAsync(Settlement payload, Task journal)
        {
            await journal.ConfigureAwait(false);
            // SaveAsync writes a personal receipt before any network operation too.
            return await SaveAsync(payload).ConfigureAwait(false);
        }
        private async Task RedeemBeforeSaveAsync(Settlement payload)
        {
            if (payload.Redemption!=null) await PostClashAsync("redemptions",payload.Redemption).ConfigureAwait(false);
        }
        public void CleanBountyJournals()
        {
            foreach (string file in Directory.GetFiles(m_Outbox,"*.bounty"))
            {
                var group=JsonConvert.DeserializeObject<BountyFinalization>(File.ReadAllText(file));
                if (group==null) continue;
                bool pending=false;
                foreach (var payload in group.Settlements)
                    if (File.Exists(Path.Combine(m_Outbox,Guid.Parse(payload.SettlementId).ToString("N")+".json")) ||
                        (Saves.TryGetValue(payload.SettlementId,out var save) && (save.Task==null || !save.Task.IsCompletedSuccessfully))) pending=true;
                if (!pending) File.Delete(file);
            }
        }
    }
}
