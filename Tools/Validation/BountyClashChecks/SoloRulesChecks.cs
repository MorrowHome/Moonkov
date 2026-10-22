using Unity.MP_FPS;

internal static class SoloRulesChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Solo: " + message); checks++; }
    private static (BountyClashMatch Match, ClashParticipant Human, ClashParticipant[] Bots) Lobby()
    {
        var arena = new ClashArenaDefinition { Center = new(0, 0, 0), Target = new(0, 0, 0), Radius = 130,
            ReadySeconds = 1, BanishSeconds = 1,
            Spawns = Enumerable.Range(0, 12).Select(i => new ClashPoint(i, 0, 0)).ToArray(),
            Exits = new[] { new ClashPoint(-70, 0, 0), new ClashPoint(70, 0, 0) } };
        var match = new BountyClashMatch(arena, 1, 0, solo: true);
        var human = match.Join(Guid.NewGuid().ToString("D"), "Human", 0);
        match.CreateTeam(human, 1);
        for (int t = 0; t < 3; t++)
        {
            var lead = match.JoinBot("Lead" + t);
            match.CreateTeam(lead, 2);
            var wing = match.JoinBot("Wing" + t);
            match.JoinTeam(wing, match.Teams[lead.TeamId].Invite);
        }
        var bots = match.Players.Values.Where(p => p.IsBot).ToArray();
        foreach (var bot in bots) match.Ready(bot, true, 0);
        return (match, human, bots);
    }
    private static (BountyClashMatch Match, ClashParticipant Human, ClashParticipant[] Bots) Started()
    {
        var run = Lobby();
        run.Match.Ready(run.Human, true, 0);run.Match.Tick(1);
        foreach (var p in run.Match.Players.Values) run.Match.MarkDeployed(p.Id);
        run.Match.Tick(2);
        return run;
    }
    public static void Run()
    {
        var online = new BountyClashMatch(new ClashArenaDefinition { Center = new(0, 0, 0), Target = new(0, 0, 0), Radius = 130,
            Spawns = Enumerable.Range(0, 12).Select(i => new ClashPoint(i, 0, 0)).ToArray(), Exits = new[] { new ClashPoint(-70, 0, 0), new ClashPoint(70, 0, 0) } }, 1, 0);
        Check(online.JoinBot("Not authorized") == null && !online.EndSoloRound(), "online admitted bots or solo finish");
        {
            var (match, human, bots) = Lobby();
            Check(match.Players.Count == 7 && match.Teams.Count == 4 && match.Teams[human.TeamId].Members.Count == 1, "default roster is not 1 + 3x2");
            Check(bots.Length == 6 && bots.All(p => p.Account == null && match.Teams[p.TeamId].Members.Count == 2), "bot has an account or incorrect squad");
            Check(match.Join(Guid.NewGuid().ToString("D"), "Extra human", 0) == null, "solo admitted a second human");
            Check(match.Phase == ClashPhase.Lobby && !human.Ready, "bots started without human consent");
            match.Ready(human, true, 0);match.Tick(1);
            Check(match.Phase == ClashPhase.Deploying && bots.All(p => p.DeploymentId == null) && Guid.TryParse(human.DeploymentId, out _), "deployment created fake bot receipts");
            foreach (var p in match.Players.Values.Where(p => p.Id != bots[0].Id)) match.MarkDeployed(p.Id);
            match.Tick(2);Check(match.Phase == ClashPhase.Deploying, "started before the final bot actor registered");
            match.MarkDeployed(bots[0].Id);match.Tick(3);
            Check(match.Phase == ClashPhase.Active && match.Players.Values.All(p => p.Life == ClashLife.Alive), "all registered actors did not start");
            Check(!match.EndSoloRound(), "live human can terminate the round");
        }
        {
            var (match, human, bots) = Started();var lead = bots[0];var wing = bots[1];
            int originalId = wing.Id;match.Down(wing, human.Id);
            Check(wing.Life == ClashLife.Downed && wing.MaxFraction == .75f, "bot down is not revivable");
            Check(match.BeginRevive(human, wing.Id, true, 3) == ClashResult.Forbidden, "enemy revived a bot");
            match.BeginRevive(lead, wing.Id, true, 3);
            match.Tick(4);Check(wing.Life == ClashLife.Downed, "expired revive lease completed");
            match.BeginRevive(lead, wing.Id, true, 4);
            for (double now = 4.25; now <= 9; now += .25) { match.RenewRevive(lead, true, now);match.Tick(now); }
            Check(wing.Id == originalId && wing.Life == ClashLife.Alive && wing.Downs == 1 && wing.MaxFraction == .75f, "revive replaced bot or erased down penalty");
            match.Down(wing, human.Id);match.Down(lead, human.Id);match.Tick(10);
            Check(wing.Life == ClashLife.Dead && lead.Life == ClashLife.Dead, "bot squad wipe did not finalize downed actors");
            match.Kill(human);Check(match.EndSoloRound() && match.Phase == ClashPhase.Finished && match.Players.Values.All(p => p.IsFinished), "saved-human restart cannot finalize remaining bots");
            Check(match.Award == null, "early solo finish invented an award");
        }
        {
            var (match, human, bots) = Started();var carrier = bots[0];
            match.Banish(carrier, true, 3);match.Tick(4);match.Pickup(carrier, true);
            match.Observe(carrier.Id, new(-70, 0, 0), 440, 0);match.Tick(5);match.Tick(35);
            Check(carrier.Life == ClashLife.Extracted && match.Award.CarrierParticipantId == carrier.Id, "bot did not extract bounty");
            Check(match.Award.CarrierDeploymentId == null && match.Award.DeploymentIds.Length == 0, "bot extraction created a persistent reward recipient");
            Check(human.Life == ClashLife.Alive && match.Deadline == 125, "bot bounty extraction did not apply the normal tail");
        }
        {
            var (match, human, bots) = Started();
            match.Banish(human, true, 3);match.Tick(4);match.Pickup(human, true);
            match.Observe(human.Id, new(-70, 0, 0), 440, 0);match.Tick(5);match.Tick(35);
            Check(match.Award.CarrierParticipantId == human.Id && match.Award.CarrierDeploymentId == human.DeploymentId &&
                match.Award.DeploymentIds.SequenceEqual(new[] { human.DeploymentId }), "human extraction lost its genuine deployment receipt");
            Check(match.EndSoloRound() && bots.All(p => p.Life == ClashLife.TimedOut), "personal extraction still waits for bot match completion");
        }
        Console.WriteLine("Solo rules passed: " + checks + " roster, authorization, actor deployment, same participant revive, wipe, bot/human award and restart checks.");
    }
}
