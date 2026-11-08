using System.Text.Json;
using Npgsql;
using Unity.MP_FPS;

public sealed partial class InventoryRepository
{
    private static ResearchQuestProgress DecodeResearch(string json)
    {
        var progress = JsonSerializer.Deserialize<ResearchQuestProgress>(json, InventoryJsonOptions);
        ResearchQuestRules.Migrate(progress);
        if (progress == null || !ResearchQuestRules.Valid(progress)) throw new DeploymentRejectedException("invalid_research_progress");
        return progress;
    }

    internal static string? EncodeResearchReceipt(ResearchQuestResult? result) =>
        result == null ? null : JsonSerializer.Serialize(result, InventoryJsonOptions);

    internal static string? NormalizeResearchReceipt(string? json) =>
        json == null ? null : EncodeResearchReceipt(JsonSerializer.Deserialize<ResearchQuestResult>(json, InventoryJsonOptions));

    private static async Task<ResearchQuestProgress> ReadResearchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid player, CancellationToken ct)
    {
        await using var read = new NpgsqlCommand("SELECT research_quests::text FROM players WHERE id=$1", connection, transaction);
        read.Parameters.AddWithValue(player);
        return DecodeResearch((string?)await read.ExecuteScalarAsync(ct) ?? throw new ProfileNotFoundException());
    }

    private static async Task WriteResearchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid player,
        ResearchQuestProgress progress, CancellationToken ct)
    {
        await using var write = new NpgsqlCommand("UPDATE players SET research_quests=$2::jsonb WHERE id=$1", connection, transaction);
        write.Parameters.AddWithValue(player);
        write.Parameters.AddWithValue(JsonSerializer.Serialize(progress, InventoryJsonOptions));
        await write.ExecuteNonQueryAsync(ct);
    }

    public async Task<Profile> ApplyResearchCommandAsync(Guid player, ResearchQuestCommand command, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockPlayerAsync(connection, transaction, player, ct);
        await using (var active = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM raid_deployments WHERE player_id=$1 AND status='Open')", connection, transaction))
        {
            active.Parameters.AddWithValue(player);
            if (await active.ExecuteScalarAsync(ct) is true) throw new DeploymentRejectedException("raid_active");
        }

        var progress = await ReadResearchAsync(connection, transaction, player, ct);
        long revision = progress.Revision;
        if (!ResearchQuestRules.ApplyCommand(progress, command)) throw new DeploymentRejectedException("research_unavailable");
        if (progress.Revision != revision)
        {
            if (command.Action == ResearchQuestAction.Decide && command.Ending == ResearchEnding.Corporate)
            {
                var graph = await ReadGraphAsync(connection, transaction, player, ct);
                if (!ResearchQuestRules.GrantCorporateReward(graph)) throw new DeploymentRejectedException("research_reward_full");
                await WriteGraphAsync(connection, transaction, player, graph, ct);
            }
            await WriteResearchAsync(connection, transaction, player, progress, ct);
        }
        var profile = await ReadAsync(connection, transaction, player, ct);
        await transaction.CommitAsync(ct);
        return profile;
    }

    private static async Task ApplyResearchSettlementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Settlement request, Unity.MP_FPS.Inventory.InventoryGraph raid, CancellationToken ct)
    {
        if (request.ResearchQuests == null) return;
        if (!request.DeploymentId.HasValue) throw new ReceiptConflictException();
        string loadout;
        ResearchQuestProgress deployed;
        await using (var source = new NpgsqlCommand(
            "SELECT inventory::text,research_quests::text FROM raid_deployments WHERE id=$1 AND player_id=$2", connection, transaction))
        {
            source.Parameters.AddWithValue(request.DeploymentId.Value);
            source.Parameters.AddWithValue(request.PlayerId);
            await using var reader = await source.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.IsDBNull(1)) throw new ReceiptConflictException();
            loadout = reader.GetString(0);
            deployed = DecodeResearch(reader.GetString(1));
        }
        var progress = await ReadResearchAsync(connection, transaction, request.PlayerId, ct);
        if (deployed.Revision != progress.Revision || deployed.Active != request.ResearchQuests.Active) throw new ReceiptConflictException();
        if (ResearchQuestRules.Record(progress, request.ResearchQuests, Decode(loadout), raid, request.Outcome == "Extracted"))
            await WriteResearchAsync(connection, transaction, request.PlayerId, progress, ct);
    }
}
