using System.Text.Json;
using Npgsql;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

internal static class ResearchQuestDatabaseChecks
{
    internal static async Task<int> RunAsync(NpgsqlDataSource db)
    {
        var store = new StashRepository(db);
        var repository = new InventoryRepository(db);
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks++; }
        async Task Reject(Func<Task> operation, string name)
        {
            try { await operation(); }
            catch (Exception ex) when (ex is ReceiptConflictException or DeploymentRejectedException) { checks++; return; }
            throw new InvalidOperationException("Accepted " + name);
        }
        Task<Profile> Player(string name) =>
            store.ResolveAsync(new ResolveProfile(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), name), default);
        Task<Profile> Act(Profile player, ResearchQuestAction action, ResearchQuest quest = default, ResearchEnding ending = default) =>
            repository.ApplyResearchCommandAsync(player.PlayerId, new ResearchQuestCommand { Action = action, Quest = quest, Ending = ending }, default);
        Task<Profile> Review(Profile player, ResearchStoryChapter chapter) =>
            repository.ApplyResearchCommandAsync(player.PlayerId, new ResearchQuestCommand { Action = ResearchQuestAction.ReviewStory, Chapter = chapter }, default);
        Deployment Deploy(Profile player) => new(player.PlayerId, Guid.NewGuid(), 0, ResearchVersion: ResearchQuestRules.DeploymentVersion);
        Settlement Receipt(Profile profile, InventoryGraph raid, ResearchQuestResult? evidence, string outcome = "Extracted") =>
            new(profile.PlayerId, profile.DeploymentId!.Value, outcome, raid.Count("dust", true), raid.Count("alloy", true), raid.Count("cells", true),
                profile.DeploymentId, InventoryRepository.Encode(raid), ResearchQuests: evidence);

        var a = await Player("research_fixture_a");
        var b = await Player("research_fixture_b");
        Check(ResearchQuestRules.Valid(a.ResearchQuests) && a.ResearchQuests!.Revision == 0, "Default migrated account progress");
        await Reject(() => Review(b, ResearchStoryChapter.Originals), "unavailable story chapter");
        var intro = await Task.WhenAll(Review(b, ResearchStoryChapter.Recruitment), Review(b, ResearchStoryChapter.Recruitment));
        Check(intro.All(p => p.ResearchQuests!.Revision == 1 && p.ResearchQuests.Accepted == 0 &&
            ResearchQuestRules.StoryReviewed(p.ResearchQuests, ResearchStoryChapter.Recruitment)), "Concurrent story review is persisted once");
        await Reject(() => Act(a, ResearchQuestAction.Accept, ResearchQuest.EndOfTransmission), "locked terminal quest");
        var same = await Task.WhenAll(Act(a, ResearchQuestAction.Accept), Act(a, ResearchQuestAction.Accept));
        Check(same.All(p => p.ResearchQuests!.Revision == 1 && p.ResearchQuests.Active == 1 && p.ResearchQuests.StorySeen == 0),
            "Concurrent duplicate accept applied once; the other account's story is private");
        var d = Deploy(a);
        var live = await repository.DeployAsync(d, default);
        Check(live.ResearchQuests!.Active == 1, "Deployment owns a research snapshot");
        await Reject(() => Act(a, ResearchQuestAction.Claim), "mutation while a raid is open");
        var raid = InventoryRepository.Decode(live.RaidInventoryJson!);
        var ready = new ResearchQuestResult { Active = 1, ExposureSeconds = 300 };
        await Reject(() => store.SettleAsync(Receipt(live, raid, new ResearchQuestResult { Active = 2, HaloKills = 5 }), default),
            "evidence from a different contract");
        var dead = await store.SettleAsync(Receipt(live, raid, ready, "Dead"), default);
        Check(dead.ResearchQuests!.Revision == 1 && dead.ResearchQuests.Ready == 0, "Death does not advance research");
        live = await repository.DeployAsync(Deploy(a), default);
        var extraction = Receipt(live, InventoryRepository.Decode(live.RaidInventoryJson!), ready);
        var saved = await store.SettleAsync(extraction, default);
        var replay = await store.SettleAsync(extraction, default);
        Check(saved.ResearchQuests!.Ready == 1 && replay.ResearchQuests!.Revision == 2, "Extraction and retry advance once");
        await Reject(() => store.SettleAsync(extraction with
            { ResearchQuests = new ResearchQuestResult { Active = 1, ExposureSeconds = 299 } }, default), "modified research receipt");
        a = await Act(a, ResearchQuestAction.Claim);
        Check(a.ResearchQuests!.Completed == 1 && b.ResearchQuests!.Accepted == 0, "Independent player progress");

        // Seed the same valid chapter state for two players. Their issued evidence
        // must still be bound to the deployment that produced it.
        var flashActive = new ResearchQuestProgress { Accepted = 31, Completed = 15, HaloKills = ResearchQuestRules.HaloKillGoal, Revision = 8 };
        ResearchQuestRules.Migrate(flashActive);
        await WriteProgress(a.PlayerId, flashActive);
        await WriteProgress(b.PlayerId, flashActive);
        var legacy = await repository.DeployAsync(new Deployment(b.PlayerId, Guid.NewGuid(), 0), default);
        Check(legacy.ResearchQuests == null &&
            ResearchQuestRules.IssuedId(InventoryRepository.Decode(legacy.RaidInventoryJson!), ResearchQuestRules.Flash) == null,
            "Legacy server does not receive quest supplies");
        await store.SettleAsync(Receipt(legacy, InventoryRepository.Decode(legacy.RaidInventoryJson!), null), default);
        var first = await repository.DeployAsync(Deploy(a), default);
        var second = await repository.DeployAsync(Deploy(b), default);
        var firstRaid = InventoryRepository.Decode(first.RaidInventoryJson!);
        var secondRaid = InventoryRepository.Decode(second.RaidInventoryJson!);
        string firstDrive = ResearchQuestRules.IssuedId(firstRaid, ResearchQuestRules.Flash);
        string secondDrive = ResearchQuestRules.IssuedId(secondRaid, ResearchQuestRules.Flash);
        Check(firstDrive != null && secondDrive != null && firstDrive != secondDrive, "Each deployment receives a different drive");
        var wrong = await store.SettleAsync(Receipt(second, secondRaid,
            new ResearchQuestResult { Active = flashActive.Active, FlashId = firstDrive }), default);
        Check(wrong.ResearchQuests!.Ready == 0, "Other player's issued drive cannot count");
        var right = await store.SettleAsync(Receipt(first, firstRaid,
            new ResearchQuestResult { Active = flashActive.Active, FlashId = firstDrive }), default);
        Check(ResearchQuestRules.Status(right.ResearchQuests, ResearchQuest.FlashDrive) == ResearchQuestStatus.Ready &&
            !InventoryRepository.Decode(right.InventoryJson!).Items.Any(item => InventoryCatalog.IsResearchSupply(item.Code)),
            "Valid extraction credits the owner and removes mission-only gear");

        int all = (1 << ResearchQuestRules.Count) - 1;
        var finale = new ResearchQuestProgress { SequenceVersion = 1, Accepted = all, Completed = all, HaloKills = 5, SentryKills = 3,
            Revision = 30, CredentialsId = Guid.NewGuid().ToString("D"), EvidenceId = Guid.NewGuid().ToString("D") };
        await WriteProgress(a.PlayerId, finale);
        await WriteProgress(b.PlayerId, finale);
        var corporate = await Task.WhenAll(Act(a, ResearchQuestAction.Decide, ending: ResearchEnding.Corporate),
            Act(a, ResearchQuestAction.Decide, ending: ResearchEnding.Corporate));
        Check(corporate.All(p => p.ResearchQuests!.Ending == ResearchEnding.Corporate && p.ResearchQuests.Revision == 31 &&
            p.Dust == right.Dust + ResearchQuestRules.CorporateDust), "Concurrent duplicate decision pays once");
        var stash = InventoryRepository.Decode(corporate[0].InventoryJson!);
        var recycler = stash.Items.Single(item => item.Code == ResearchQuestRules.CorporateModule);
        Check(stash.Find(recycler.Parent)?.Code == ResearchQuestRules.CorporateHalo, "Actual reward Halo contains its upgrade");
        await Reject(() => Act(a, ResearchQuestAction.Decide, ending: ResearchEnding.Expose), "rewritten ending");
        var erased = await Act(b, ResearchQuestAction.Decide, ending: ResearchEnding.Erase);
        Check(erased.ResearchQuests!.Ending == ResearchEnding.Erase && erased.ResearchQuests.EvidenceId == null &&
            erased.Dust == wrong.Dust, "Different teammates can choose different endings");
        return checks;

        async Task WriteProgress(Guid player, ResearchQuestProgress progress)
        {
            await using var command = db.CreateCommand("UPDATE players SET research_quests=$2::jsonb WHERE id=$1");
            command.Parameters.AddWithValue(player);
            command.Parameters.AddWithValue(JsonSerializer.Serialize(progress, new JsonSerializerOptions { IncludeFields = true }));
            await command.ExecuteNonQueryAsync();
        }
    }
}
