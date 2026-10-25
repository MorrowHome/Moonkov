using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class OfflineProfileStore
    {
        private const string ClashMode = "BountyClash";
        private const int ClashVersion = 1, SoloBountyDust = 80;
        private sealed class OfflineClashState
        {
            public int Version = 1;
            public string CurrentMatchId, ActiveMatchId;
            public Dictionary<string, OfflineClashMatch> Matches = new Dictionary<string, OfflineClashMatch>();
        }
        // Only the real human profile is persisted. Bots have no account records.
        private sealed class OfflineClashMatch
        {
            public string MatchId, TokenId, PlayerId, DeploymentId, RegistrationHash;
            public int RulesVersion, TeamId;
            public RaidPersistenceContext.BountyParticipant[] Participants;
            public int? Cells;
            public string State = "Registered";
            public RaidPersistenceContext.BountyRedemption Redemption;
            public string RedemptionHash;
            public bool AwardClaimed;
        }
        private sealed class LocalRegistration
        {
            public string MatchId, TokenId;
            public int RulesVersion;
            public RaidPersistenceContext.BountyParticipant[] Participants;
        }
        private static string Id(string value)
        {
            if(!Guid.TryParse(value,out var id) || id==Guid.Empty)throw new InvalidDataException("Invalid local Clash identity.");
            return id.ToString("D");
        }
        private static bool IsClashDeployment(SaveData save,string deploymentId) => Guid.TryParse(deploymentId,out var id) && save.Clash!=null &&
            save.Clash.Matches.Values.Any(m=>m.DeploymentId==id.ToString("D"));
        private static string RegistrationHash(OfflineClashMatch match) => Receipt(new LocalRegistration
        {
            MatchId=match.MatchId,TokenId=match.TokenId,RulesVersion=match.RulesVersion,
            Participants=match.Participants ?? new[]{new RaidPersistenceContext.BountyParticipant {PlayerId=match.PlayerId,DeploymentId=match.DeploymentId,TeamId=match.TeamId}}
        });
        private static RaidPersistenceContext.BountyRedemption CanonicalRedemption(RaidPersistenceContext.BountyRedemption request)
        {
            if(request==null || request.ExtractedDeploymentIds==null || request.ExtractedDeploymentIds.Length<1 || request.ExtractedDeploymentIds.Length>3)
                throw new InvalidDataException("A local Clash award can contain only its human deployment.");
            return new RaidPersistenceContext.BountyRedemption {MatchId=Id(request.MatchId),TokenId=Id(request.TokenId),
                ExtractionEventId=Id(request.ExtractionEventId),CarrierDeploymentId=Id(request.CarrierDeploymentId),
                ExtractedDeploymentIds=request.ExtractedDeploymentIds.Select(Id).ToArray()};
        }
        private static void ValidateAward(OfflineClashMatch match,RaidPersistenceContext.BountyRedemption award)
        {
            if(award.MatchId!=match.MatchId || award.TokenId!=match.TokenId ||
                !award.ExtractedDeploymentIds.Contains(match.DeploymentId) ||
                award.ExtractedDeploymentIds.Distinct().Count()!=award.ExtractedDeploymentIds.Length)
                throw new InvalidDataException("Clash award does not belong to the registered player.");
            if(match.Participants==null)
            {
                if(award.CarrierDeploymentId!=match.DeploymentId || award.ExtractedDeploymentIds.Length!=1)
                    throw new InvalidDataException("Solo Clash award has another participant.");
            }
            else if(!award.ExtractedDeploymentIds.Contains(award.CarrierDeploymentId) ||
                award.ExtractedDeploymentIds.Any(id=>!match.Participants.Any(p=>p.DeploymentId==id && p.TeamId==match.TeamId)))
                throw new InvalidDataException("LAN Clash award must belong to the carrier's team.");
        }

        private static void ValidateClash(SaveData save)
        {
            if(save.Clash==null)
            {if(save.Format==3)throw new InvalidDataException("Missing local Clash ledger.");return;}
            var ledger=save.Clash;
            if((save.Format!=3 && save.Format!=4) || ledger.Version!=1 || ledger.Matches==null ||
                (ledger.CurrentMatchId!=null && !ledger.Matches.ContainsKey(ledger.CurrentMatchId)) ||
                (ledger.ActiveMatchId!=null && ledger.ActiveMatchId!=ledger.CurrentMatchId))
                throw new InvalidDataException("Unsupported local Clash ledger.");
            var tokens=new HashSet<string>();var deployments=new HashSet<string>();var events=new HashSet<string>();
            foreach(var pair in ledger.Matches)
            {
                var match=pair.Value;
                if(match==null || Id(match.MatchId)!=pair.Key || match.MatchId!=pair.Key || Id(match.TokenId)!=match.TokenId ||
                    !tokens.Add(match.TokenId) || Id(match.DeploymentId)!=match.DeploymentId || !deployments.Add(match.DeploymentId) ||
                    match.PlayerId!=save.PlayerId || match.RulesVersion!=ClashVersion || match.TeamId<0 || match.TeamId>3 ||
                    match.RegistrationHash!=RegistrationHash(match) ||
                    !(match.State=="Registered" || match.State=="Open" || match.State=="Closed" || match.State=="Cancelled") ||
                    (match.Cells.HasValue && !RaidRules.ValidLoadout(match.Cells.Value)))
                    throw new InvalidDataException("Invalid local Clash match binding.");
                if(match.Participants!=null && (match.Participants.Length<1 || match.Participants.Length>12 ||
                    match.Participants.Any(p=>p==null || Id(p.PlayerId)!=p.PlayerId || Id(p.DeploymentId)!=p.DeploymentId || p.TeamId<0 || p.TeamId>3) ||
                    match.Participants.Select(p=>p.PlayerId).Distinct().Count()!=match.Participants.Length ||
                    match.Participants.Select(p=>p.DeploymentId).Distinct().Count()!=match.Participants.Length ||
                    match.Participants.GroupBy(p=>p.TeamId).Any(g=>g.Count()>3) ||
                    !match.Participants.Any(p=>p.PlayerId==match.PlayerId && p.DeploymentId==match.DeploymentId && p.TeamId==match.TeamId)))
                    throw new InvalidDataException("Invalid LAN Clash roster.");
                if(match.State=="Open")
                {
                    if(ledger.ActiveMatchId!=match.MatchId || save.DeploymentId!=match.DeploymentId || match.Cells!=save.CarriedCells)
                        throw new InvalidDataException("Local Clash deployment lost its owner.");
                    var raid=RaidInventoryTransport.Decode(save.RaidInventoryJson);
                    if(raid==null || raid.Validate()!=InventoryError.None || raid.Find("stash")!=null || raid.Find("loot")!=null || raid.Count("cells",true)!=save.CarriedCells)
                        throw new InvalidDataException("Invalid original local Clash loadout.");
                }
                else if(ledger.ActiveMatchId==match.MatchId)throw new InvalidDataException("Closed local Clash deployment is still active.");
                if(match.State=="Closed" && !save.Receipts.ContainsKey(match.DeploymentId))throw new InvalidDataException("Missing local Clash settlement receipt.");
                if(match.Redemption!=null)
                {
                    var award=CanonicalRedemption(match.Redemption);ValidateAward(match,award);
                    if(match.RedemptionHash!=Receipt(award) || !events.Add(award.ExtractionEventId))throw new InvalidDataException("Invalid local Clash award receipt.");
                }
                else if(match.RedemptionHash!=null || match.AwardClaimed)throw new InvalidDataException("Missing local Clash award event.");
                if(match.AwardClaimed && match.State!="Closed" || match.Redemption!=null && match.State!="Open" && !match.AwardClaimed)
                    throw new InvalidDataException("Local Clash award was not atomically settled.");
            }
        }
        private static void SupersedeClashRegistration(SaveData save)
        {
            if(save.Clash?.CurrentMatchId!=null && save.Clash.Matches.TryGetValue(save.Clash.CurrentMatchId,out var previous) && previous.State=="Registered")
                previous.State="Cancelled";
        }
        internal void RegisterBountyMatch(string matchId,int rulesVersion,string tokenId,RaidPersistenceContext.BountyParticipant[] participants,bool lan=false)
        {
            lock(m_Lock)
            {
                if(participants==null || participants.Length<1 || participants.Any(p=>p==null) || (!lan && participants.Length!=1))throw new InvalidDataException("Local Clash must register exactly its human profile.");
                var human=participants.FirstOrDefault(p=>p.PlayerId==m_Save.PlayerId);
                if(human==null)throw new InvalidDataException("Clash roster does not contain the profile owner.");
                CheckPlayer(human.PlayerId);
                var match=new OfflineClashMatch {MatchId=Id(matchId),TokenId=Id(tokenId),PlayerId=human.PlayerId,DeploymentId=Id(human.DeploymentId),RulesVersion=rulesVersion,TeamId=human.TeamId,Participants=lan?participants:null};
                if(rulesVersion!=ClashVersion || match.TeamId<0 || match.TeamId>3)throw new InvalidDataException("Unsupported local Clash rules.");
                match.RegistrationHash=RegistrationHash(match);
                if(m_Save.Clash!=null && m_Save.Clash.Matches.TryGetValue(match.MatchId,out var previous))
                {if(previous.RegistrationHash!=match.RegistrationHash)throw new InvalidDataException("Local Clash registration conflict.");return;}
                if(RaidActive || m_Save.Receipts.ContainsKey(match.DeploymentId) || m_Save.Clash!=null &&
                    m_Save.Clash.Matches.Values.Any(m=>m.TokenId==match.TokenId || m.DeploymentId==match.DeploymentId))
                    throw new InvalidDataException("Local Clash match reuses an owned or closed deployment/token.");
                var next=Copy();if(next.Clash==null)next.Clash=new OfflineClashState();
                SupersedeClashRegistration(next);next.Clash.Matches.Add(match.MatchId,match);next.Clash.CurrentMatchId=match.MatchId;
                Commit(next);
            }
        }
        private OfflineClashMatch Binding(RaidPersistenceContext.Deployment payload)
        {
            CheckPlayer(payload.PlayerId);
            if(payload.Mode!=ClashMode || payload.RulesVersion!=ClashVersion || payload.MatchId==null || m_Save.Clash==null ||
                !m_Save.Clash.Matches.TryGetValue(payload.MatchId,out var match) || match.DeploymentId!=payload.DeploymentId ||
                match.PlayerId!=payload.PlayerId || match.TeamId!=payload.TeamId || match.RulesVersion!=payload.RulesVersion)
                throw new InvalidDataException("Local Clash request does not own the registered deployment.");
            return match;
        }
        private RaidPersistenceContext.Deployment CurrentDeployment()
        {
            var payload=new RaidPersistenceContext.Deployment {PlayerId=m_Save.PlayerId,DeploymentId=m_Save.DeploymentId,Cells=m_Save.CarriedCells};
            if(m_Save.Clash?.ActiveMatchId!=null)
            {
                var match=m_Save.Clash.Matches[m_Save.Clash.ActiveMatchId];
                payload.Mode=ClashMode;payload.MatchId=match.MatchId;payload.RulesVersion=match.RulesVersion;payload.TeamId=match.TeamId;
            }
            return payload;
        }
        private RaidPersistenceContext.Profile DeployedClashProfile(OfflineClashMatch match)
        {
            var profile=Read();profile.DeploymentId=m_Save.DeploymentId;profile.CarriedCells=m_Save.CarriedCells;profile.RaidInventoryJson=m_Save.RaidInventoryJson;
            profile.CellStackId=RaidInventoryTransport.Decode(profile.RaidInventoryJson).Items.FirstOrDefault(i=>i.Code=="cells")?.Id;
            profile.Mode=ClashMode;profile.MatchId=match.MatchId;profile.RulesVersion=match.RulesVersion;profile.TeamId=match.TeamId;return profile;
        }
        private RaidPersistenceContext.Profile DeployClash(RaidPersistenceContext.Deployment payload)
        {
            OfflineClashMatch match;
            try {match=Binding(payload);}
            catch(InvalidDataException ex) {throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,ex.Message);}
            if(!RaidRules.ValidLoadout(payload.Cells) || m_Save.Receipts.ContainsKey(payload.DeploymentId) ||
                m_Save.Clash.CurrentMatchId!=match.MatchId || !(match.State=="Registered" || match.State=="Open") ||
                RaidActive && (m_Save.DeploymentId!=payload.DeploymentId || m_Save.Clash.ActiveMatchId!=match.MatchId || m_Save.CarriedCells!=payload.Cells))
                throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,"Local Clash deployment is closed or conflicts with another raid.");
            if(!RaidActive)
            {
                if(m_Save.Recovery.ItemCount()>0)throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,"recovery_pending");
                var next=Copy();PrepareCells(next.Inventory,payload.Cells);var raid=next.Inventory.ExtractLoadout();
                if(raid.Validate()!=InventoryError.None || raid.CarriedWeight>InventoryCatalog.CarryWeightLimit)throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                next.DeploymentId=payload.DeploymentId;next.CarriedCells=payload.Cells;next.RaidInventoryJson=RaidInventoryTransport.Encode(raid);
                var opened=next.Clash.Matches[match.MatchId];opened.State="Open";opened.Cells=payload.Cells;next.Clash.ActiveMatchId=match.MatchId;Commit(next);
            }
            return DeployedClashProfile(m_Save.Clash.Matches[match.MatchId]);
        }
        internal void RedeemBounty(RaidPersistenceContext.BountyRedemption request)
        {
            lock(m_Lock)
            {
                var award=CanonicalRedemption(request);
                if(m_Save.Clash==null || !m_Save.Clash.Matches.TryGetValue(award.MatchId,out var match))throw new InvalidDataException("Unknown local Clash award match.");
                ValidateAward(match,award);string hash=Receipt(award);
                if(match.Redemption!=null)
                {if(match.RedemptionHash!=hash)throw new InvalidDataException("Local Clash token was already assigned to a different event.");return;}
                if(match.State!="Open" || m_Save.Clash.ActiveMatchId!=match.MatchId || m_Save.DeploymentId!=match.DeploymentId ||
                    m_Save.Clash.Matches.Values.Any(m=>m.Redemption?.ExtractionEventId==award.ExtractionEventId))
                    throw new InvalidDataException("Local Clash token cannot be awarded to a closed or different deployment.");
                var next=Copy();var entry=next.Clash.Matches[match.MatchId];entry.Redemption=award;entry.RedemptionHash=hash;Commit(next);
            }
        }
        private RaidPersistenceContext.Profile SettleClash(RaidPersistenceContext.Settlement payload)
        {
            var deployment=new RaidPersistenceContext.Deployment {PlayerId=payload.PlayerId,DeploymentId=payload.DeploymentId,Mode=payload.Mode,
                MatchId=payload.MatchId,RulesVersion=payload.RulesVersion,TeamId=payload.TeamId};
            var match=Binding(deployment);string receipt=Receipt(payload);
            if(payload.SettlementId!=match.DeploymentId)throw new InvalidDataException("Local Clash settlement identity mismatch.");
            if(m_Save.Receipts.TryGetValue(payload.SettlementId,out var previous))
            {if(previous!=receipt)throw new InvalidDataException("Local Clash settlement receipt conflict.");return Read();}
            if(match.State!="Open" || m_Save.Clash.ActiveMatchId!=match.MatchId || m_Save.DeploymentId!=payload.DeploymentId ||
                !(payload.Outcome=="Extracted" || payload.Outcome=="Dead" || payload.Outcome=="TimedOut"))throw new InvalidDataException("Local Clash settlement has no open deployment.");
            int bonus=0;
            if(match.Redemption!=null)
            {
                if(payload.Outcome!="Extracted" || payload.BountyEventId!=match.Redemption.ExtractionEventId || match.AwardClaimed)
                    throw new InvalidDataException("Local Clash award requires its actual successful extraction event.");
                bonus=match.Participants==null ? SoloBountyDust : BountyClashRules.Reward(match.Participants.Count(p=>p.TeamId==match.TeamId));
            }
            else if(payload.BountyEventId!=null || payload.Redemption!=null)throw new InvalidDataException("Unregistered local Clash award.");
            if(payload.Redemption!=null && Receipt(CanonicalRedemption(payload.Redemption))!=match.RedemptionHash)
                throw new InvalidDataException("Local Clash settlement changed its award event.");
            var next=Copy();
            if(payload.Outcome=="Extracted")
            {
                if(string.IsNullOrEmpty(payload.InventoryJson))throw new InvalidDataException("Local Clash extraction inventory is missing.");
                var raid=RaidInventoryTransport.Decode(payload.InventoryJson);
                if(raid==null || raid.Validate()!=InventoryError.None || raid.Find("stash")!=null || raid.Find("loot")!=null ||
                    raid.Count("dust",true)!=payload.Dust || raid.Count("alloy",true)!=payload.Alloy || raid.Count("cells",true)!=payload.Cells ||
                    raid.Items.Any(i=>MissionInventory.IsArtifact(i.Code) && !i.MissionRecovered))throw new InvalidDataException("Invalid local Clash extraction inventory.");
                var result=RecoveryInventory.Settle(next.Inventory,next.Recovery,raid,out var stored,out var pending,missionRewards:false,bonusDust:bonus);
                if(result!=InventoryError.None)throw new InvalidDataException("Unable to return local Clash equipment: "+result);
                next.Inventory=stored;next.Recovery=pending;
            }
            else ReturnProtected(next, RaidInventoryTransport.Decode(payload.InventoryJson ?? next.RaidInventoryJson));
            var closed=next.Clash.Matches[match.MatchId];closed.State="Closed";closed.AwardClaimed=bonus>0;
            next.Clash.ActiveMatchId=null;next.DeploymentId=next.RaidInventoryJson=null;next.CarriedCells=0;
            next.Receipts.Add(payload.SettlementId,receipt);Commit(next);return Read();
        }
        private RaidPersistenceContext.Profile AbandonClash(RaidPersistenceContext.Deployment payload)
        {
            var match=Binding(payload);
            if(m_Save.Receipts.ContainsKey(payload.DeploymentId) || match.State=="Cancelled")return Read();
            if(match.State=="Open" && (m_Save.Clash.ActiveMatchId!=match.MatchId || match.Cells!=payload.Cells))throw new InvalidDataException("Local Clash abandonment mismatch.");
            if(match.Redemption!=null && !match.AwardClaimed)throw new InvalidDataException("Local Clash extraction receipt recovery must complete before abandonment.");
            var next=Copy();next.Clash.Matches[match.MatchId].State=match.State=="Open" ? "Closed" : "Cancelled";
            if(next.Clash.ActiveMatchId==match.MatchId)
            {
                if (next.RaidInventoryJson != null) ReturnProtected(next, RaidInventoryTransport.Decode(next.RaidInventoryJson));
                next.Clash.ActiveMatchId=null;next.DeploymentId=next.RaidInventoryJson=null;next.CarriedCells=0;
            }
            next.Receipts.Add(payload.DeploymentId,"abandoned");Commit(next);return Read();
        }
        private RaidPersistenceContext.Profile RefundClash(RaidPersistenceContext.Deployment payload)
        {
            lock(m_Lock)
            {
                var match=Binding(payload);
                if(m_Save.Receipts.ContainsKey(payload.DeploymentId) || match.State=="Cancelled")return Read();
                if(match.State!="Open")return AbandonClash(payload);
                if(m_Save.Clash.ActiveMatchId!=match.MatchId || match.Cells!=payload.Cells || match.Redemption!=null)throw new InvalidDataException("Local Clash refund does not own an unawarded deployment.");
                var raid=RaidInventoryTransport.Decode(m_Save.RaidInventoryJson);
                return SettleClash(new RaidPersistenceContext.Settlement {PlayerId=m_Save.PlayerId,SettlementId=match.DeploymentId,DeploymentId=match.DeploymentId,Mode=ClashMode,
                    MatchId=match.MatchId,RulesVersion=match.RulesVersion,TeamId=match.TeamId,Outcome="Extracted",InventoryJson=m_Save.RaidInventoryJson,
                    Dust=raid.Count("dust",true),Alloy=raid.Count("alloy",true),Cells=raid.Count("cells",true)});
            }
        }
    }
}
