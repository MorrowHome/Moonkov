using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum RaidDamageKind : byte { Bullet, Explosion, Fire, Poison, Bleed, Environment, Melee }
    // Server-only attribution captured when an attack is released. NetworkIds
    // can be reused; participant+epoch identify the original combatant.
    public struct ClashDamageSource
    {
        public int NetworkId, ParticipantId, Epoch;
    }
    public static class BountyClashDamage
    {
        public static bool CanAct(EntityManager manager,Entity player)=>BountyClashRuntime.CanAct(manager,player);
        public static ClashDamageSource Capture(World world,Entity actor)
        {
            if(world==null||!world.IsCreated||!world.EntityManager.Exists(actor))return default;
            var manager=world.EntityManager;
            var source=new ClashDamageSource{NetworkId=manager.HasComponent<GhostOwner>(actor)?manager.GetComponentData<GhostOwner>(actor).NetworkId:0};
            if(manager.HasComponent<ClashPlayer>(actor))
            {var player=manager.GetComponentData<ClashPlayer>(actor);source.ParticipantId=player.ParticipantId;source.Epoch=player.Epoch;}
            return source;
        }
        public static ClashDamageSource Capture(World world,int networkId)
        {
            var source=new ClashDamageSource{NetworkId=networkId};
            if(world==null||!world.IsCreated||networkId==0)return source;
            using var query=world.EntityManager.CreateEntityQuery(typeof(BountyClashWorld));
            if(query.CalculateEntityCount()!=1)return source;
            var match=world.EntityManager.GetComponentObject<BountyClashWorld>(query.GetSingletonEntity()).Match;
            if(match!=null)foreach(var p in match.Players.Values)
                if(p.Connected&&p.NetworkId==networkId){source.ParticipantId=p.Id;source.Epoch=match.Epoch;break;}
            return source;
        }
        public static bool IsSelf(World world,Entity victim,ClashDamageSource source)
        {
            var manager=world.EntityManager;
            if(source.ParticipantId>0&&manager.HasComponent<ClashPlayer>(victim))
            {var p=manager.GetComponentData<ClashPlayer>(victim);return p.ParticipantId==source.ParticipantId&&p.Epoch==source.Epoch;}
            return source.NetworkId!=0&&manager.HasComponent<GhostOwner>(victim)&&manager.GetComponentData<GhostOwner>(victim).NetworkId==source.NetworkId;
        }
        // Compatibility for immediate damage. Delayed attacks must capture at release.
        public static float Apply(World world,Entity victim,int sourceNetworkId,BodyPart part,float amount,uint tick,RaidDamageKind kind,Unity.Mathematics.float3 impactPoint=default)
            =>Apply(world,victim,Capture(world,sourceNetworkId),part,amount,tick,kind,impactPoint);
        public static float Apply(World world,Entity victim,ClashDamageSource source,BodyPart part,float amount,uint tick,RaidDamageKind kind,Unity.Mathematics.float3 impactPoint=default)
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
                if(source.Epoch!=0&&source.Epoch!=match.Epoch)return 0;
                ClashParticipant attacker=null;
                if(source.ParticipantId>0&&!match.Players.TryGetValue(source.ParticipantId,out attacker))return 0;
                if(attacker!=null&&attacker.TeamId==health.ClashTeam&&(kind==RaidDamageKind.Bullet||kind==RaidDamageKind.Melee))amount*=.1f;
                if(manager.HasComponent<ClashPlayer>(victim))
                {var player=manager.GetComponentData<ClashPlayer>(victim);player.LastAttacker=attacker?.Id??0;manager.SetComponentData(victim,player);}
            }
            float healthBefore=health.CurrentHealth;
            float damaged=RaidHealth.CombatDamage(ref health,part,amount,tick,kind,impactPoint);
            manager.SetComponentData(victim,health);
            if(!health.ClashEnabled&&healthBefore>0&&health.CurrentHealth<=0&&manager.HasComponent<GhostOwner>(victim))
            {
                int target=manager.GetComponentData<GhostOwner>(victim).NetworkId;
                if(target!=source.NetworkId)Gameplay.Leaderboard.LeaderboardManager.Instance?.AddKill(source.NetworkId,target);
            }
            return damaged;
        }
    }
}
