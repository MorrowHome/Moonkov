using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum RaidDamageKind : byte { Bullet, Explosion, Fire, Poison, Bleed, Environment }
    public static class BountyClashDamage
    {
        public static bool CanAct(EntityManager manager,Entity player)=>BountyClashRuntime.CanAct(manager,player);
        public static float Apply(World world,Entity victim,int sourceNetworkId,BodyPart part,float amount,uint tick,RaidDamageKind kind)
        {
            if(world==null||!world.IsCreated||!world.EntityManager.Exists(victim))return 0;
            var manager=world.EntityManager;
            if(!manager.HasComponent<PredictedPlayerGhost>(victim))return 0;
            var health=manager.GetComponentData<PredictedPlayerGhost>(victim);
            if(health.ClashEnabled)
            {
                if(health.ClashLife!=ClashLife.Alive)return 0;
                using var worlds=manager.CreateEntityQuery(typeof(BountyClashWorld));
                if(worlds.CalculateEntityCount()!=1)return 0;
                var match=manager.GetComponentObject<BountyClashWorld>(worlds.GetSingletonEntity()).Match;
                if(match==null||match.Phase!=ClashPhase.Active)return 0;
                foreach(var attacker in match.Players.Values)
                    if(attacker.NetworkId==sourceNetworkId&&attacker.TeamId==health.ClashTeam&&kind==RaidDamageKind.Bullet)
                    {amount*=.1f;break;}
                if(manager.HasComponent<ClashPlayer>(victim))
                {var player=manager.GetComponentData<ClashPlayer>(victim);player.LastAttacker=sourceNetworkId;manager.SetComponentData(victim,player);}
            }
            float healthBefore=health.CurrentHealth;
            float damaged;
            if(part==BodyPart.Auto)
            {
                float before=health.CurrentHealth;
                if(!health.BodyHealthInitialized)RaidHealth.Damage(ref health,BodyPart.Chest,amount,tick);
                else for(int i=0;i<RaidHealth.PartCount;i++)
                {var bodyPart=(BodyPart)i;float share=amount*RaidHealth.Get(health,bodyPart)/System.Math.Max(.001f,before);RaidHealth.Damage(ref health,bodyPart,share,tick);}
                damaged=before-health.CurrentHealth;health.LastDamageAmount=damaged;health.LastHitPart=BodyPart.Auto;
            }
            else damaged=RaidHealth.Damage(ref health,part,amount,tick);
            manager.SetComponentData(victim,health);
            if(!health.ClashEnabled&&healthBefore>0&&health.CurrentHealth<=0&&manager.HasComponent<GhostOwner>(victim))
            {
                int target=manager.GetComponentData<GhostOwner>(victim).NetworkId;
                if(target!=sourceNetworkId)Gameplay.Leaderboard.LeaderboardManager.Instance?.AddKill(sourceNetworkId,target);
            }
            return damaged;
        }
    }
}
