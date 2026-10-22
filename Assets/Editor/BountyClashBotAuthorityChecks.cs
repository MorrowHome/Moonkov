using System;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using BodyPart = Unity.MP_FPS.BodyPart;

// Isolated ECS authority checks: no scene, PlayerPrefs, profile, network or live World changes.
public static class BountyClashBotAuthorityChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use idle EditMode.");
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message);checks++; }
        using var world = new World("Solo bot authority fixture", WorldFlags.GameServer);
        var em = world.EntityManager;
        var mode = em.CreateEntity(typeof(RaidModeState));
        em.SetComponentData(mode, new RaidModeState { Mode = RaidGameMode.BountyClash });
        Check(!ClashSoloRuntime.Enabled(em), "Online mode acquired solo privileges");
        em.CreateEntity(typeof(OfflineRaidMode));
        Check(!ClashSoloRuntime.Enabled(em), "Offline alone acquired solo privileges");
        em.CreateEntity(typeof(ClashSoloMode));
        Check(ClashSoloRuntime.Enabled(em), "Explicit offline solo gate stayed closed");
        var arena = new ClashArenaDefinition { Center = new(0, 0, 0), Target = new(0, 0, 0), Radius = 130,
            Spawns = Enumerable.Range(0, 12).Select(i => new ClashPoint(i, 0, 0)).ToArray(),
            Exits = new[] { new ClashPoint(-70, 0, 0), new ClashPoint(70, 0, 0) } };
        var match = new BountyClashMatch(arena, 1, 0, solo: true);
        var human = match.Join(Guid.NewGuid().ToString("D"), "Human", 0);match.CreateTeam(human, 1);
        var bot = match.JoinBot("Bot");match.CreateTeam(bot, 2);
        var ally = match.JoinBot("Wing");match.JoinTeam(ally, match.Teams[bot.TeamId].Invite);
        foreach (var p in match.Players.Values) match.Ready(p, true, 0);
        match.Tick(10);foreach (var p in match.Players.Values) match.MarkDeployed(p.Id);match.Tick(11);
        var host = em.CreateEntity();em.AddComponentObject(host, new BountyClashWorld { Match = match, Epoch = 1 });
        Entity Actor(ClashParticipant p, int owner)
        {
            p.NetworkId = owner;
            var entity = em.CreateEntity(typeof(GhostOwner), typeof(ClashPlayer), typeof(PredictedPlayerGhost));
            em.SetComponentData(entity, new GhostOwner { NetworkId = owner });
            em.SetComponentData(entity, new ClashPlayer { ParticipantId = p.Id, TeamId = p.TeamId, Epoch = 1, CanAct = true });
            var health = new PredictedPlayerGhost { ClashEnabled = true, ClashCanAct = true, ClashLife = ClashLife.Alive,
                ClashTeam = p.TeamId, ClashMaxHealthFraction = 1 };
            RaidHealth.Initialize(ref health);em.SetComponentData(entity, health);
            return entity;
        }
        var humanActor = Actor(human, 1);var botActor = Actor(bot, -100000);var allyActor = Actor(ally, -100001);
        var released = BountyClashDamage.Capture(world, botActor);
        Check(released.ParticipantId == bot.Id && released.Epoch == 1 && released.NetworkId < 0, "Bot attack lost stable attribution");
        Check(BountyClashDamage.IsSelf(world, botActor, released) && !BountyClashDamage.IsSelf(world, allyActor, released), "Bot owner self-filter confused squad members");
        float damage = BountyClashDamage.Apply(world, allyActor, released, BodyPart.Chest, 10, 1, RaidDamageKind.Bullet);
        Check(Math.Abs(damage - 1) < .01f, "Bot friendly bullet bypassed 10 percent rule");
        var input = em.CreateEntity(typeof(PredictedClientInput));
        var registry = new ClashBotRegistry();
        typeof(ClashBotRegistry).GetProperty("Epoch").SetValue(registry, 1);
        var spawn = new ClashBotSpawnRequest { ParticipantId = bot.Id, TeamId = bot.TeamId, Slot = bot.Slot, Epoch = 1, OwnerNetworkId = bot.NetworkId };
        registry.PendingSpawns.Add(spawn);
        Check(registry.Register(spawn, botActor, input, InventoryGraph.Create(false)), "Valid actor registration failed");
        Check(!registry.Register(spawn, botActor, input, InventoryGraph.Create(false)) && registry.PendingSpawns.Count == 0, "Actor registered twice or pending request leaked");
        var stale = spawn;stale.Epoch = 2;
        Check(!registry.Register(stale, allyActor, input, InventoryGraph.Create(false)), "Wrong-epoch actor registered");
        match.Down(bot, human.Id);
        var downed = em.GetComponentData<PredictedPlayerGhost>(botActor);downed.ClashLife = ClashLife.Downed;downed.ClashCanAct = false;
        em.SetComponentData(botActor, downed);
        var tag = em.GetComponentData<ClashPlayer>(botActor);tag.CanAct = false;em.SetComponentData(botActor, tag);
        Check(!BountyClashDamage.CanAct(em, botActor) && em.Exists(botActor) && em.Exists(input), "Downed bot lost its original body or remained actionable");
        Check(BountyClashDamage.Apply(world, botActor, BountyClashDamage.Capture(world, humanActor), BodyPart.Chest, 10, 2, RaidDamageKind.Bullet) == 0, "Downed bot accepted ordinary hit damage");
        em.DestroyEntity(botActor);bot.Connected = false;
        damage = BountyClashDamage.Apply(world, humanActor, released, BodyPart.Chest, 10, 3, RaidDamageKind.Bullet);
        Check(Math.Abs(damage - 10) < .01f && em.GetComponentData<ClashPlayer>(humanActor).LastAttacker == bot.Id, "Released bot attack lost credit after actor destruction/disconnect");
        var old = released;old.Epoch = 2;
        Check(BountyClashDamage.Apply(world, humanActor, old, BodyPart.Chest, 10, 4, RaidDamageKind.Bullet) == 0, "Old-epoch attack damaged this match");
        return "PASS " + checks + " isolated solo marker, bot registry, damage/owner/epoch and downed-body authority checks";
    }
}
