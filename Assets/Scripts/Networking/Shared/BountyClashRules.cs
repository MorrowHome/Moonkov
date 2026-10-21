using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS
{
    public enum ClashPhase : byte { Lobby, Preparing, Deploying, Active, Finished }
    public enum ClashLife : byte { Waiting, Alive, Downed, Dead, Extracted, TimedOut }
    public enum ClashBountyState : byte { Unbanished, Banishing, Ground, Carried, Extracted }
    public enum ClashResult : byte { Ok, Stale, Invalid, WrongPhase, Full, Forbidden, Busy, Blocked, Unsupported }
    public enum ClashExitBlock : byte { None, Enemy, DownedTeammate, NotActive }
    [Serializable] public struct ClashPoint
    {
        public float X, Y, Z;
        public ClashPoint(float x, float y, float z) { X=x; Y=y; Z=z; }
        public static double DistanceSquared(ClashPoint a, ClashPoint b) => (a.X-b.X)*(double)(a.X-b.X)+(a.Y-b.Y)*(double)(a.Y-b.Y)+(a.Z-b.Z)*(double)(a.Z-b.Z);
        public static double HorizontalSquared(ClashPoint a, ClashPoint b) => (a.X-b.X)*(double)(a.X-b.X)+(a.Z-b.Z)*(double)(a.Z-b.Z);
        public bool Finite => !float.IsNaN(X) && !float.IsInfinity(X) && !float.IsNaN(Y) && !float.IsInfinity(Y) && !float.IsNaN(Z) && !float.IsInfinity(Z);
    }
    [Serializable] public sealed class ClashArenaDefinition
    {
        public ClashPoint Center, Target;
        public float Radius, ExitRadius=8, BlockRadius=15, BanishSeconds=60, ReadySeconds=10, ReconnectSeconds=90;
        public ClashPoint[] Spawns = Array.Empty<ClashPoint>(), Exits = Array.Empty<ClashPoint>();
        public string Validate()
        {
            if (!Center.Finite || !Target.Finite || Radius < 20 || Radius > 1500 || !float.IsFinite(Radius) ||
                Spawns == null || Spawns.Length != 12 || Exits == null || Exits.Length != 2 ||
                ExitRadius < 2 || ExitRadius > 30 || BlockRadius < ExitRadius || BlockRadius > 60 ||
                !float.IsFinite(ExitRadius) || !float.IsFinite(BlockRadius) ||
                BanishSeconds < 1 || BanishSeconds > 300 || ReadySeconds < 1 || ReadySeconds > 60 || ReconnectSeconds < 1 || ReconnectSeconds > 300 ||
                !float.IsFinite(BanishSeconds) || !float.IsFinite(ReadySeconds) || !float.IsFinite(ReconnectSeconds)) return "arena_parameters";
            foreach (var p in Spawns.Concat(Exits).Append(Target)) if (!p.Finite || ClashPoint.HorizontalSquared(p,Center)>Radius*(double)Radius) return "arena_anchor";
            return null;
        }
    }
    public static class BountyClashRules
    {
        public const int Version=1, MaxPlayers=12, MaxTeams=4;
        public const float Duration=900, BoundaryGrace=20, ExtractionSeconds=30, ExitTail=90, ReviveSeconds=5, SenseSeconds=5, BoostRange=125, InstinctRange=75;
        public static bool Ended(ClashLife life) => life == ClashLife.Dead || life == ClashLife.Extracted || life == ClashLife.TimedOut;
        public static int Reward(int startingTeamSize) => 20+(startingTeamSize==1?60:startingTeamSize==2?15:0);
    }
    public sealed class ClashParticipant
    {
        public readonly int Id;
        public readonly string Account;
        public string Name, DeploymentId;
        public int TeamId=-1, Slot, NetworkId, Downs, StartingTeamSize, LastAttacker;
        public bool Ready, Connected=true, Deployed;
        public ClashLife Life=ClashLife.Waiting;
        public ClashPoint Position;
        public double DisconnectDeadline, OutsideSince=-1, SenseLease, ViewLease;
        public float SenseRemaining=5;
        public int Exit=-1;
        public float Health;
        public uint DamageRevision, LastRequest;
        public ClashResult Result;
        public float MaxFraction => Math.Max(0,1-Downs*.25f);
        public bool IsFinished => BountyClashRules.Ended(Life);
        internal ClashParticipant(int id,string account,string name) { Id=id;Account=account;Name=name; }
    }
    public sealed class ClashTeam
    {
        public int Id, Leader, Capacity=3;
        public string Invite;
        public readonly List<int> Members=new List<int>();
    }
    public sealed class ClashExtraction
    {
        public readonly string MatchId, TokenId, EventId, CarrierDeploymentId;
        public readonly string[] DeploymentIds;
        public ClashExtraction(string match,string token,string carrier,string[] deployments)
        { MatchId=match;TokenId=token;CarrierDeploymentId=carrier;DeploymentIds=(string[])deployments.Clone();EventId=Guid.NewGuid().ToString("D"); }
    }
    public sealed class BountyClashMatch
    {
        public readonly string MatchId=Guid.NewGuid().ToString("D"), TokenId=Guid.NewGuid().ToString("D");
        public readonly int Epoch;
        public readonly ClashArenaDefinition Arena;
        public readonly Dictionary<int,ClashParticipant> Players=new Dictionary<int,ClashParticipant>();
        public readonly Dictionary<int,ClashTeam> Teams=new Dictionary<int,ClashTeam>();
        private readonly Dictionary<string,int> m_Accounts=new Dictionary<string,int>(StringComparer.Ordinal);
        private readonly Dictionary<int,Revive> m_Revives=new Dictionary<int,Revive>();
        private readonly double[,] m_ExitStarted=new double[4,2];
        private readonly ClashExitBlock[,] m_ExitBlocked=new ClashExitBlock[4,2];
        private int m_NextPlayer=1;
        private double m_LastTime, m_BanishAt;
        private int m_BanishTeam=-1;
        private readonly double[,] m_ExitElapsed=new double[4,2];
        public ClashPhase Phase {get;private set;}
        public ClashBountyState Bounty {get;private set;}
        public int CarrierId {get;private set;}=-1;
        public ClashPoint BountyPosition {get;private set;}
        public double Deadline {get;private set;}
        public double PrepareDeadline {get;private set;}
        public bool DeploymentAborted {get;private set;}
        public double DeploymentDeadline {get;private set;}
        public uint Revision {get;private set;}=1;
        public ClashExtraction Award {get;private set;}
        public bool BanishHealed {get;private set;}
        public int BanishTeam => m_BanishTeam;
        private sealed class Revive { public int Actor,Target; public double Started,Lease; public uint ActorDamage,TargetDamage; }
        public BountyClashMatch(ClashArenaDefinition arena,int epoch,double now)
        {
            if(arena==null || arena.Validate()!=null || epoch<1 || !TimeValid(now)) throw new ArgumentException("Invalid clash arena.");
            Arena=arena;Epoch=epoch;m_LastTime=now;BountyPosition=arena.Target;
            for(int t=0;t<4;t++)for(int e=0;e<2;e++)m_ExitStarted[t,e]=-1;
        }
        private static bool TimeValid(double now)=>!double.IsNaN(now)&&!double.IsInfinity(now)&&now>=0;
        public ClashParticipant Account(string account)=>account!=null&&m_Accounts.TryGetValue(account,out int id)?Players[id]:null;
        public ClashParticipant Join(string account,string name,double now)
        {
            if(!TimeValid(now)||now<m_LastTime||string.IsNullOrWhiteSpace(account)) return null;
            var old=Account(account);
            if(old!=null)
            {
                if(old.Connected || (!old.IsFinished && now>old.DisconnectDeadline)) return null;
                old.Connected=true;old.DisconnectDeadline=0;Revision++;return old;
            }
            if(Phase!=ClashPhase.Lobby&&Phase!=ClashPhase.Preparing || Players.Count>=12 || Teams.Count>=4&&Teams.Values.All(t=>t.Members.Count>=t.Capacity))return null;
            var p=new ClashParticipant(m_NextPlayer++,account,name);Players.Add(p.Id,p);m_Accounts.Add(account,p.Id);Revision++;return p;
        }
        public bool Request(ClashParticipant p,uint request,int epoch,string match)
        {
            if(p==null||!p.Connected||epoch!=Epoch||match!=MatchId||request==0||request<=p.LastRequest) return false;
            p.LastRequest=request;p.Result=ClashResult.Ok;return true;
        }
        public ClashResult CreateTeam(ClashParticipant p,int size)
        {
            if(!Lobby())return ClashResult.WrongPhase;
            if(size<1||size>3)return ClashResult.Invalid;
            if(p.TeamId>=0)return ClashResult.Forbidden;
            int id=Enumerable.Range(0,4).Where(t=>!Teams.ContainsKey(t)).DefaultIfEmpty(-1).First();
            if(id<0)return ClashResult.Full;
            var team=new ClashTeam{Id=id,Leader=p.Id,Capacity=size,Invite=Guid.NewGuid().ToString("N").Substring(0,10).ToUpperInvariant()};
            team.Members.Add(p.Id);Teams.Add(id,team);p.TeamId=id;p.Slot=0;ResetReady();return ClashResult.Ok;
        }
        public ClashResult JoinTeam(ClashParticipant p,string invite)
        {
            if(!Lobby())return ClashResult.WrongPhase;
            if(p.TeamId>=0)return ClashResult.Forbidden;
            var team=Teams.Values.FirstOrDefault(t=>string.Equals(t.Invite,invite,StringComparison.Ordinal));
            if(team==null)return ClashResult.Forbidden;if(team.Members.Count>=team.Capacity)return ClashResult.Full;
            team.Members.Add(p.Id);p.TeamId=team.Id;p.Slot=team.Members.Count-1;ResetReady();return ClashResult.Ok;
        }
        public ClashResult LeaveTeam(ClashParticipant p)
        {
            if(!Lobby())return ClashResult.WrongPhase;
            if(p.TeamId>=0&&Teams.TryGetValue(p.TeamId,out var t))
            {t.Members.Remove(p.Id);if(t.Members.Count==0)Teams.Remove(t.Id);else{t.Leader=t.Members[0];for(int i=0;i<t.Members.Count;i++)Players[t.Members[i]].Slot=i;}}
            p.TeamId=-1;ResetReady();return ClashResult.Ok;
        }
        public ClashResult Capacity(ClashParticipant p,int size)
        {
            if(!Lobby()||p.TeamId<0)return ClashResult.WrongPhase;
            var t=Teams[p.TeamId];if(t.Leader!=p.Id||size<t.Members.Count||size<1||size>3)return ClashResult.Forbidden;
            t.Capacity=size;ResetReady();return ClashResult.Ok;
        }
        public ClashResult Ready(ClashParticipant p,bool ready,double now)
        {
            if(!Lobby()||p.TeamId<0)return ClashResult.WrongPhase;
            p.Ready=ready;Revision++;
            if(!ready){Phase=ClashPhase.Lobby;PrepareDeadline=0;}
            if(Phase==ClashPhase.Lobby&&AllReady()){Phase=ClashPhase.Preparing;PrepareDeadline=now+Arena.ReadySeconds;}
            return ClashResult.Ok;
        }
        private bool Lobby()=>Phase==ClashPhase.Lobby||Phase==ClashPhase.Preparing;
        private void ResetReady(){foreach(var p in Players.Values)p.Ready=false;Phase=ClashPhase.Lobby;PrepareDeadline=0;Revision++;}
        private bool AllReady()=>Teams.Count>=2&&Players.Count>=2&&Players.Values.All(p=>p.TeamId>=0&&p.Ready&&p.Connected);
        public void Disconnect(ClashParticipant p,double now)
        {
            p.Connected=false;p.SenseLease=p.ViewLease=0;p.DisconnectDeadline=now+Arena.ReconnectSeconds;
            if(Lobby()){p.Ready=false;Phase=ClashPhase.Lobby;PrepareDeadline=0;}Revision++;
        }
        public void MarkDeployed(int id){if(Phase==ClashPhase.Deploying&&Players.TryGetValue(id,out var p)){p.Deployed=true;Revision++;}}
        public void Observe(int id,ClashPoint position,float health,uint damage)
        {if(Players.TryGetValue(id,out var p)){p.Position=position;p.Health=health;p.DamageRevision=damage;}}
        public ClashResult Banish(ClashParticipant p,bool accessible,double now)
        {
            if(!Active(p)||Bounty!=ClashBountyState.Unbanished)return ClashResult.WrongPhase;
            if(!accessible)return ClashResult.Blocked;
            Bounty=ClashBountyState.Banishing;m_BanishAt=now;m_BanishTeam=p.TeamId;BanishHealed=true;
            foreach(var ally in Players.Values)if(ally.TeamId==p.TeamId&&!ally.IsFinished)ally.Downs=0;
            Revision++;return ClashResult.Ok;
        }
        public ClashResult Pickup(ClashParticipant p,bool accessible)
        {
            if(!Active(p)||Bounty!=ClashBountyState.Ground)return ClashResult.WrongPhase;
            if(!accessible)return ClashResult.Blocked;
            CarrierId=p.Id;Bounty=ClashBountyState.Carried;Revision++;return ClashResult.Ok;
        }
        public void Sense(ClashParticipant p,bool held,double now)=>Sense(p,held?3:0,now);
        public void Sense(ClashParticipant p,int flags,double now)
        {
            p.ViewLease=Active(p)&&(flags&1)!=0?now+.6:0;
            p.SenseLease=p.ViewLease>now&&p.Id==CarrierId&&(flags&2)!=0&&p.SenseRemaining>0?now+.6:0;
        }
        public ClashResult BeginRevive(ClashParticipant p,int target,bool accessible,double now)
        {
            if(!Active(p)||!Players.TryGetValue(target,out var victim)||victim.TeamId!=p.TeamId||victim.Life!=ClashLife.Downed)return ClashResult.Forbidden;
            if(!accessible)return ClashResult.Blocked;
            if(m_Revives.Values.Any(r=>r.Target==target&&r.Actor!=p.Id))return ClashResult.Busy;
            m_Revives[p.Id]=new Revive{Actor=p.Id,Target=target,Started=now,Lease=now+.6,ActorDamage=p.DamageRevision,TargetDamage=victim.DamageRevision};
            Revision++;return ClashResult.Ok;
        }
        public void RenewRevive(ClashParticipant p,bool accessible,double now)
        {if(m_Revives.TryGetValue(p.Id,out var r)){if(!accessible)m_Revives.Remove(p.Id);else r.Lease=now+.6;}}
        public void CancelRevive(ClashParticipant p){m_Revives.Remove(p.Id);}
        public float ReviveProgress(int id,double now)=>m_Revives.Values.Where(r=>r.Target==id||r.Actor==id).Select(r=>(float)Math.Min(1,(now-r.Started)/5)).FirstOrDefault();
        public int ReviveTarget(int actor)=>m_Revives.TryGetValue(actor,out var r)?r.Target:-1;
        public bool Active(ClashParticipant p)=>Phase==ClashPhase.Active&&p!=null&&p.Life==ClashLife.Alive;
        public void Down(ClashParticipant p,int attacker)
        {
            if(!Active(p))return;
            p.Downs++;p.LastAttacker=attacker;p.Life=p.MaxFraction>0?ClashLife.Downed:ClashLife.Dead;Drop(p);m_Revives.Remove(p.Id);Revision++;
        }
        public void Kill(ClashParticipant p,ClashLife outcome=ClashLife.Dead)
        {if(p.IsFinished)return;p.Life=outcome;Drop(p);m_Revives.Remove(p.Id);Revision++;}
        private void Drop(ClashParticipant p){if(CarrierId!=p.Id)return;CarrierId=-1;Bounty=ClashBountyState.Ground;BountyPosition=p.Position;}
        public float BanishProgress(double now)=>Bounty==ClashBountyState.Banishing?(float)Math.Min(1,(now-m_BanishAt)/Arena.BanishSeconds):Bounty>=ClashBountyState.Ground?1:0;
        public float BoundaryRemaining(ClashParticipant p,double now)=>p.OutsideSince<0?20:(float)Math.Max(0,20-(now-p.OutsideSince));
        public ClashExitBlock ExitBlock(int team,int exit)=>team>=0&&team<4?m_ExitBlocked[team,exit]:ClashExitBlock.NotActive;
        public float ExitRemaining(int team,int exit,double now)=>team>=0&&team<4&&m_ExitStarted[team,exit]>=0?(float)Math.Max(0,30-m_ExitElapsed[team,exit]):-1;
        public void Tick(double now)
        {
            if(!TimeValid(now)||now<m_LastTime)return;double dt=now-m_LastTime;m_LastTime=now;
            if(Lobby()) foreach(var p in Players.Values.ToArray())
                if(!p.Connected&&now>=p.DisconnectDeadline){LeaveTeam(p);Players.Remove(p.Id);m_Accounts.Remove(p.Account);}
            if(Phase==ClashPhase.Preparing)
            {
                if(!AllReady()){Phase=ClashPhase.Lobby;PrepareDeadline=0;return;}
                if(now>=PrepareDeadline){Phase=ClashPhase.Deploying;DeploymentDeadline=now+120;foreach(var p in Players.Values){p.DeploymentId=Guid.NewGuid().ToString("D");p.StartingTeamSize=Teams[p.TeamId].Members.Count;}Revision++;}
            }
            if(Phase==ClashPhase.Deploying && (now>=DeploymentDeadline || Players.Values.Any(p=>!p.Connected&&now>=p.DisconnectDeadline)))
            {DeploymentAborted=true;Phase=ClashPhase.Finished;foreach(var p in Players.Values)p.Life=ClashLife.TimedOut;Revision++;return;}
            if(Phase==ClashPhase.Deploying&&Players.Values.All(p=>p.Deployed))
            {Phase=ClashPhase.Active;Deadline=now+900;foreach(var p in Players.Values)p.Life=ClashLife.Alive;Revision++;}
            if(Phase!=ClashPhase.Active)return;
            if(now>=Deadline){foreach(var p in Players.Values)if(!p.IsFinished)Kill(p,ClashLife.TimedOut);Phase=ClashPhase.Finished;Revision++;return;}
            foreach(var p in Players.Values)
            {
                if(p.IsFinished)continue;
                if(!p.Connected&&now>=p.DisconnectDeadline){Kill(p);continue;}
                bool outside=ClashPoint.HorizontalSquared(p.Position,Arena.Center)>Arena.Radius*(double)Arena.Radius;
                if(!outside)p.OutsideSince=-1;else if(p.OutsideSince<0)p.OutsideSince=now;
                else if(now-p.OutsideSince>=20){Kill(p);continue;}
                if(p.Id==CarrierId&&p.Connected&&p.Life==ClashLife.Alive)
                {float used=(float)Math.Max(0,Math.Min(now,p.SenseLease)-(now-dt));p.SenseRemaining=Math.Max(0,p.SenseRemaining-used);}
            }
            if(Bounty==ClashBountyState.Banishing&&now-m_BanishAt>=Arena.BanishSeconds)
            {Bounty=ClashBountyState.Ground;BountyPosition=Arena.Target;Revision++;}
            foreach(var pair in m_Revives.ToArray())
            {
                var r=pair.Value;var a=Players[r.Actor];var v=Players[r.Target];
                if(!Active(a)||!a.Connected||v.Life!=ClashLife.Downed||now>r.Lease||a.DamageRevision!=r.ActorDamage||v.DamageRevision!=r.TargetDamage){m_Revives.Remove(pair.Key);continue;}
                if(now-r.Started>=5){v.Life=ClashLife.Alive;v.Health=1;m_Revives.Remove(pair.Key);Revision++;}
            }
            foreach(var team in Teams.Values)
            {
                var members=team.Members.Select(id=>Players[id]).ToArray();
                if(!members.Any(p=>p.Life==ClashLife.Alive))foreach(var p in members)if(p.Life==ClashLife.Downed)Kill(p);
                for(int exit=0;exit<2;exit++)
                {
                    var point=Arena.Exits[exit];
                    bool enemy=Players.Values.Any(p=>p.TeamId!=team.Id&&p.Life==ClashLife.Alive&&ClashPoint.DistanceSquared(p.Position,point)<=Arena.BlockRadius*(double)Arena.BlockRadius);
                    bool downed=members.Any(p=>p.Life==ClashLife.Downed&&ClashPoint.DistanceSquared(p.Position,point)<=Arena.BlockRadius*(double)Arena.BlockRadius);
                    var escaping=members.Where(p=>p.Life==ClashLife.Alive&&ClashPoint.DistanceSquared(p.Position,point)<=Arena.ExitRadius*(double)Arena.ExitRadius).ToArray();
                    bool wasBlocked=m_ExitBlocked[team.Id,exit]!=ClashExitBlock.None;
                    m_ExitBlocked[team.Id,exit]=enemy?ClashExitBlock.Enemy:downed?ClashExitBlock.DownedTeammate:ClashExitBlock.None;
                    if(escaping.Length==0){m_ExitStarted[team.Id,exit]=-1;m_ExitElapsed[team.Id,exit]=0;continue;}
                    if(m_ExitStarted[team.Id,exit]<0)m_ExitStarted[team.Id,exit]=now;
                    else if(!enemy&&!downed&&!wasBlocked)m_ExitElapsed[team.Id,exit]+=dt;
                    if(enemy||downed||m_ExitElapsed[team.Id,exit]<30)continue;
                    var carrier=escaping.FirstOrDefault(p=>p.Id==CarrierId);
                    if(carrier!=null)
                    {Award=new ClashExtraction(MatchId,TokenId,carrier.DeploymentId,escaping.Select(p=>p.DeploymentId).ToArray());Bounty=ClashBountyState.Extracted;CarrierId=-1;Deadline=Math.Min(Deadline,now+90);}
                    foreach(var p in escaping){p.Life=ClashLife.Extracted;p.Exit=exit;Revision++;}m_ExitStarted[team.Id,exit]=-1;
                }
            }
            if(Players.Values.All(p=>p.IsFinished)){Phase=ClashPhase.Finished;Revision++;}
        }
    }
}
