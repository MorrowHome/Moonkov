using Unity.MP_FPS;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
static ClashArenaDefinition Arena()=>new(){Center=new(0,0,0),Target=new(0,0,0),Radius=130,Spawns=Enumerable.Range(0,12).Select(i=>new ClashPoint(i,0,0)).ToArray(),Exits=new[]{new ClashPoint(-70,0,0),new ClashPoint(70,0,0)}};
static (BountyClashMatch,ClashParticipant,ClashParticipant) Started(int teamSize=1)
{
 var m=new BountyClashMatch(Arena(),1,0);var a=m.Join("a","A",0);var b=m.Join("b","B",0);
 Check(m.CreateTeam(a,teamSize)==ClashResult.Ok&&m.CreateTeam(b,1)==ClashResult.Ok,"create teams");
 if(teamSize>1){var ally=m.Join("c","C",0);Check(m.JoinTeam(ally,m.Teams[a.TeamId].Invite)==ClashResult.Ok,"authorized invite");}
 foreach(var p in m.Players.Values)m.Ready(p,true,0);
 m.Tick(10);Check(m.Phase==ClashPhase.Deploying,"shared ready countdown");
 foreach(var p in m.Players.Values)m.MarkDeployed(p.Id);
 m.Tick(11);Check(m.Phase==ClashPhase.Active&&m.Deadline==911,"shared match start");return(m,a,b);
}
{
 var (m,a,b)=Started();Check(m.Join("late","Late",12)==null,"running match admitted stranger");
 Check(!m.Request(a,1,0,m.MatchId)&&m.Request(a,1,1,m.MatchId)&&!m.Request(a,1,1,m.MatchId),"request/epoch dedup");
 m.Disconnect(a,12);Check(m.Join("a","A",13)==a&&a.DeploymentId!=b.DeploymentId,"reconnect duplicated participant/deployment");
}
{
 var (m,a,b)=Started(2);var ally=m.Account("c");m.Down(ally,2);Check(ally.MaxFraction==.75f&&ally.Life==ClashLife.Downed,"down transition");
 Check(m.Banish(a,true,12)==ClashResult.Ok&&m.BanishHealed&&ally.Downs==0&&ally.Life==ClashLife.Downed,"banish must restore at begin without resurrection");
 Check(m.Banish(a,true,13)==ClashResult.WrongPhase,"banish replay healed again");
 Check(m.BeginRevive(a,ally.Id,true,13)==ClashResult.Ok,"revive start");
 m.Tick(14);Check(ally.Life==ClashLife.Downed,"expired hold completed revive");
 Check(m.BeginRevive(a,ally.Id,true,15)==ClashResult.Ok,"restart revive");
 for(double t=15.25;t<=20;t+=.25){m.RenewRevive(a,true,t);m.Tick(t);}
 Check(ally.Life==ClashLife.Alive,"continuous revive did not complete");
 m.Down(ally,2);m.Observe(a.Id,new(-70,0,0),1,0);m.Observe(ally.Id,new(20,0,0),0,0);m.Observe(b.Id,new(40,0,0),1,0);
 m.Tick(21);Check(m.ExitBlock(a.TeamId,0)==ClashExitBlock.None,"remote downed teammate blocked unrelated exit");
 m.Observe(ally.Id,new(-65,0,0),0,0);m.Tick(22);Check(m.ExitBlock(a.TeamId,0)==ClashExitBlock.DownedTeammate,"nearby downed ally did not block exit");
}
{
 var (m,a,b)=Started();m.Banish(a,true,12);m.Tick(72);Check(m.Bounty==ClashBountyState.Ground,"banish did not finish");
 Check(m.Pickup(a,true)==ClashResult.Ok&&m.Pickup(b,true)==ClashResult.WrongPhase,"single token ownership");
 m.Sense(a,true,72);m.Tick(72.5);float left=a.SenseRemaining;m.Down(a,2);Check(m.Bounty==ClashBountyState.Ground,"down did not drop bounty");
 Check(m.Pickup(b,true)==ClashResult.Ok&&b.SenseRemaining==5,"new carrier did not receive personal sense budget");
 m.Observe(b.Id,new(70,0,0),1,0);m.Tick(73);m.Tick(103);Check(b.Life==ClashLife.Extracted&&m.Award!=null&&m.Deadline<=193,"bounty extraction/tail deadline");
 Check(m.Award.DeploymentIds.SequenceEqual(new[]{b.DeploymentId}),"award roster includes wrong participant");
}
{
 var (m,a,b)=Started();m.Observe(a.Id,new(200,0,0),1,0);m.Tick(12);m.Tick(32);Check(a.Life==ClashLife.Dead,"boundary grace failed");
 m.Disconnect(b,33);m.Tick(123);Check(b.IsFinished&&m.Phase==ClashPhase.Finished,"disconnect retention unbounded");
}
{
 var m=new BountyClashMatch(Arena(),1,0);var a=m.Join("a","A",0);var b=m.Join("b","B",0);m.CreateTeam(a,1);m.CreateTeam(b,1);m.Ready(a,true,0);m.Ready(b,true,0);m.Tick(10);m.MarkDeployed(a.Id);m.Tick(130);
 Check(m.DeploymentAborted&&m.Phase==ClashPhase.Finished,"partial deployment locked room forever");
}
Check(BountyClashRules.Reward(1)==80&&BountyClashRules.Reward(2)==35&&BountyClashRules.Reward(3)==20,"economy v1 mismatch");
Console.WriteLine("Bounty Clash pure rules passed: lobby/roster/epoch, shared time, deploy abort, reconnect, banish heal, revive hold, local exit block, unique bounty/sense, extraction/tail, boundary, disconnect, reward v1.");
{
 var (m,a,b)=Started(2);var ally=m.Account("c");
 m.Observe(a.Id,new(-70,0,0),1,0);m.Observe(ally.Id,new(0,0,0),1,0);m.Observe(b.Id,new(50,0,0),1,0);
 m.Tick(12);m.Tick(22);Check(m.ExitRemaining(a.TeamId,0,22)==20,"extraction accumulated progress");
 m.Observe(b.Id,new(-68,0,0),1,0);m.Tick(23);m.Tick(40);Check(m.ExitRemaining(a.TeamId,0,40)==20,"enemy must freeze not reset progress");
 m.Observe(b.Id,new(50,0,0),1,0);m.Tick(41);m.Tick(46);Check(m.ExitRemaining(a.TeamId,0,46)==15,"unblock must resume existing progress");
 m.Observe(a.Id,new(0,0,0),1,0);m.Tick(47);Check(m.ExitRemaining(a.TeamId,0,47)<0,"whole team leaving resets exit");
}
{
 var (m,a,b)=Started(2);var ally=m.Account("c");m.Banish(a,true,12);m.Tick(72);m.Pickup(a,true);
 for(double t=72;t<78;t+=.25){m.Sense(a,3,t);m.Tick(t+.25);}
 Check(a.SenseRemaining==0,"five-second boost cumulative budget");m.Down(a,2);m.Pickup(b,true);Check(b.SenseRemaining==5,"first enemy pickup restores its own unused budget");
 m.Sense(b,1,78);m.Tick(78.5);Check(b.SenseRemaining==5,"base view must not consume boost");
 m.Sense(b,3,78.5);m.Tick(79);float left=b.SenseRemaining;
 m.Down(b,1);m.Pickup(ally,true);m.BeginRevive(ally,a.Id,true,79);
 // Reconnect does not create a fresh budget even if the token has changed hands.
 m.Disconnect(b,79);Check(m.Join("b","B",79.1)==b&&b.SenseRemaining==left,"reconnect reset personal boost quota");
}
Console.WriteLine("Additional official-rule regressions passed: extraction pause/resume/leave reset, personal token quota, base/boost split, reconnect quota.");

SoloRulesChecks.Run();
