using System;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class TacticalTeamEffectsChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    { s_Checks++; if(!condition)throw new InvalidOperationException(message); }

    public static string Run()
    {
        s_Checks=0;
        var owner=new Entity {Index=12,Version=3};var friend=new Entity {Index=13,Version=1};
        var reused=new Entity {Index=12,Version=4};
        var source=new ClashDamageSource {OnlineHuman=true,OnlineTeam=2,OnlineActor=owner,NetworkId=9};
        foreach(var phase in new[] {TacticalWorldPhase.Fire,TacticalWorldPhase.Poison,TacticalWorldPhase.Wire})
        {
            Check(ServerGameSystem.TacticalAreaBlockedByTeam(phase,BountyClashDamage.TeamProtected(source,friend,2)),
                "Friendly harmful status is blocked: "+phase);
            Check(!ServerGameSystem.TacticalAreaBlockedByTeam(phase,BountyClashDamage.TeamProtected(source,owner,2)),
                "Owner remains exposed to their own status: "+phase);
            Check(!ServerGameSystem.TacticalAreaBlockedByTeam(phase,BountyClashDamage.TeamProtected(source,friend,3)),
                "Enemy remains exposed: "+phase);
            Check(ServerGameSystem.TacticalAreaBlockedByTeam(phase,BountyClashDamage.TeamProtected(source,reused,2)),
                "Reused actor identity remains a protected teammate: "+phase);
        }
        Check(!ServerGameSystem.TacticalAreaBlockedByTeam(TacticalWorldPhase.Choke,true),"Choke still extinguishes and affects everyone");
        Check(!ServerGameSystem.TacticalAreaBlockedByTeam(TacticalWorldPhase.Alarm,true),"Alarm remains usable on friendly contact");
        Check(!ServerGameSystem.TacticalAreaBlockedByTeam(TacticalWorldPhase.Armed,true),"Friendly contact can still trigger an armed trap");
        source.OnlineHuman=false;
        Check(!ServerGameSystem.TacticalAreaBlockedByTeam(TacticalWorldPhase.Poison,BountyClashDamage.TeamProtected(source,friend,2)),
            "AI or environmental areas still affect players");
        return "PASS "+s_Checks+" tactical team effect checks";
    }
}
