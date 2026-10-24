using System;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine.UIElements;

public static class OnlineRaidChecks
{
    public static string Run()
    {
        if (UnityEngine.Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); checks++; }
        using var world = new World("IsolatedOnlineTeamChecks", WorldFlags.None);
        var em = world.EntityManager;
        Entity Player(int owner, int team)
        {
            var e = em.CreateEntity(typeof(GhostOwner), typeof(OnlineRaidMember), typeof(PredictedPlayerGhost));
            em.SetComponentData(e, new GhostOwner { NetworkId = owner });
            em.SetComponentData(e, new OnlineRaidMember { Team = team });
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); em.SetComponentData(e, health); return e;
        }
        var actor = Player(1, 0); var ally = Player(2, 0); var enemy = Player(3, 1);
        var source = BountyClashDamage.Capture(world, actor);
        float allyHealth = em.GetComponentData<PredictedPlayerGhost>(ally).CurrentHealth;
        foreach (var kind in new[] { RaidDamageKind.Bullet, RaidDamageKind.Explosion, RaidDamageKind.Melee, RaidDamageKind.Fire, RaidDamageKind.Poison })
            Check(BountyClashDamage.Apply(world, ally, source, BodyPart.Chest, 10, 1, kind) == 0, "Friendly damage leaked: " + kind);
        Check(em.GetComponentData<PredictedPlayerGhost>(ally).CurrentHealth == allyHealth, "Ally lost health");
        Check(BountyClashDamage.Apply(world, enemy, source, BodyPart.Chest, 10, 2, RaidDamageKind.Bullet) > 0, "Versus enemy immune");
        Check(BountyClashDamage.Apply(world, ally, default(ClashDamageSource), BodyPart.Chest, 10, 3, RaidDamageKind.Melee) > 0, "AI damage blocked by cooperation");
        em.DestroyEntity(actor);
        Check(BountyClashDamage.Apply(world, ally, source, BodyPart.Chest, 10, 4, RaidDamageKind.Explosion) == 0, "Delayed attack lost its release-time team");
        var spawns = new OnlineRaidWorld();
        var origin = LocalTransform.FromPosition(new Unity.Mathematics.float3(10, 0, 10));
        var p0 = spawns.Spawn(0, origin); var p1 = spawns.Spawn(0, LocalTransform.FromPosition(100, 0, 100));
        Check(Unity.Mathematics.math.distance(p0.Position, p1.Position) < 3 && !p0.Position.Equals(p1.Position), "Teammates did not spawn together");
        var p2 = spawns.Spawn(1, LocalTransform.FromPosition(100, 0, 100));
        Check(Unity.Mathematics.math.distance(p0.Position, p2.Position) > 100, "Opposing team shared spawn origin");
        var tree = UnityEngine.Resources.Load<VisualTreeAsset>("Moonkov/UI/OnlineLobby");
        var ui = tree.CloneTree();
        Check(ui.Q<Button>("onlineStart") != null && ui.Q<RadioButtonGroup>("onlineTeam") != null && ui.Q<Button>("onlineReturn") != null, "Lobby layout bindings missing");
        Check(UnityEngine.Resources.Load<VisualTreeAsset>("Moonkov/UI/OnlineSquad").CloneTree().Q<Label>("onlineSquadRoster") != null, "Squad HUD layout missing");
        return "Online raid teams/UI: " + checks + " assertions passed; no drivers, clients, Play or live profiles.";
    }
}
