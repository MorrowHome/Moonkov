using System;
using Unity.Entities;
using Unity.MP_FPS;

public static class OnlineSelfDamageChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }

    public static string Run()
    {
        s_Checks=0;
        var attacker=new Entity {Index=10,Version=2};
        var teammate=new Entity {Index=11,Version=1};
        var reused=new Entity {Index=10,Version=3};
        var source=new ClashDamageSource {NetworkId=4,OnlineHuman=true,OnlineTeam=1,OnlineActor=attacker};
        Check(!BountyClashDamage.TeamProtected(source,attacker,1),"Own explosive and status can hurt the owner");
        Check(BountyClashDamage.TeamProtected(source,teammate,1),"A living teammate is protected");
        Check(BountyClashDamage.TeamProtected(source,reused,1),"A recycled entity index is a different teammate");
        Check(!BountyClashDamage.TeamProtected(source,teammate,2),"An opposing team is damageable");
        source.NetworkId=0;
        Check(!BountyClashDamage.TeamProtected(source,attacker,1) && BountyClashDamage.TeamProtected(source,teammate,1),
            "Connection loss does not change captured actor or team");
        source.OnlineActor=Entity.Null;
        Check(BountyClashDamage.TeamProtected(source,teammate,1) && BountyClashDamage.TeamProtected(source,attacker,1),
            "Missing actor identity never removes teammate protection");
        source.OnlineHuman=false;
        Check(!BountyClashDamage.TeamProtected(source,teammate,1),"AI and environmental damage keep their existing rules");
        return "PASS " + s_Checks + " online self-damage policy checks";
    }
}
