using System.Security.Cryptography;
using System.Text;
using Npgsql;

public sealed class StashRepository(NpgsqlDataSource db)
{
    public async Task InitializeAsync()
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(741825610)", connection, transaction);
        await gate.ExecuteNonQueryAsync();
        await using var schema = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql")), connection, transaction);
        await schema.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<Profile> ResolveAsync(ResolveProfile request, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.GuestToken.ToLowerInvariant())));
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO players (id, guest_token_hash, display_name) VALUES ($1, $2, $3)
            ON CONFLICT (guest_token_hash) DO UPDATE SET display_name = EXCLUDED.display_name
            RETURNING id
            """, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(request.DisplayName);
        var playerId = (Guid)(await command.ExecuteScalarAsync(ct))!;
        await InventoryRepository.EnsureStacksAsync(connection, transaction, playerId, ct);
        var result = await InventoryRepository.ReadAsync(connection, transaction, playerId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Profile> SettleAsync(Settlement request, CancellationToken ct, bool clash = false)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize rewards for this player. Atomic increments never overwrite another raid's rewards.
        await InventoryRepository.LockPlayerAsync(connection, transaction, request.PlayerId, ct);
        await BountyClashRepository.CheckBindingAsync(connection,transaction,InventoryRepository.AsDeployment(request),clash,ct);
        if (clash && (!request.DeploymentId.HasValue || request.InventoryJson==null || (request.Outcome!="Extracted" && request.BountyEventId.HasValue))) throw new ReceiptConflictException();
        if (!clash && request.BountyEventId.HasValue) throw new ReceiptConflictException();
        if (request.DeploymentId.HasValue)
            await InventoryRepository.CheckDeploymentAsync(connection, transaction, request, ct);
        await using var receipt = new NpgsqlCommand("""
            INSERT INTO raid_settlements (id, player_id, outcome, dust, alloy, cells, deployment_id,inventory,mode,match_id,rules_version,team_id,bounty_event_id)
            VALUES ($1, $2, $3, $4, $5, $6, $7,$8::jsonb,$9,$10,$11,$12,$13) ON CONFLICT (id) DO NOTHING
            """, connection, transaction);
        receipt.Parameters.AddWithValue(request.SettlementId);
        receipt.Parameters.AddWithValue(request.PlayerId);
        receipt.Parameters.AddWithValue(request.Outcome);
        receipt.Parameters.AddWithValue(request.Dust);
        receipt.Parameters.AddWithValue(request.Alloy);
        receipt.Parameters.AddWithValue(request.Cells);
        receipt.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid, Value = (object?)request.DeploymentId ?? DBNull.Value });
        receipt.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)request.InventoryJson ?? DBNull.Value });
        InventoryRepository.AddBindingParameters(receipt,InventoryRepository.AsDeployment(request));
        receipt.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Uuid, Value=(object?)request.BountyEventId ?? DBNull.Value });
        var inserted = await receipt.ExecuteNonQueryAsync(ct) == 1;
        if (!inserted)
        {
            await using var previous = new NpgsqlCommand("SELECT player_id, outcome, dust, alloy, cells, deployment_id, inventory::text,mode,match_id,rules_version,team_id,bounty_event_id FROM raid_settlements WHERE id = $1", connection, transaction);
            previous.Parameters.AddWithValue(request.SettlementId);
            await using var reader = await previous.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != request.PlayerId
                || reader.GetString(1) != request.Outcome || reader.GetInt32(2) != request.Dust
                || reader.GetInt32(3) != request.Alloy || reader.GetInt32(4) != request.Cells
                || (reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5)) != request.DeploymentId || !InventoryRepository.BindingMatches(reader,7,InventoryRepository.AsDeployment(request))
                || (reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11))!=request.BountyEventId)
                throw new ReceiptConflictException();
            if ((reader.IsDBNull(6) ? null : reader.GetString(6)) is string saved)
            { if (request.InventoryJson == null || InventoryRepository.Encode(InventoryRepository.Decode(saved)) != InventoryRepository.Encode(InventoryRepository.Decode(request.InventoryJson))) throw new ReceiptConflictException(); }
            else if (request.InventoryJson != null) throw new ReceiptConflictException();
        }
        int bountyDust = inserted && clash ? await BountyClashRepository.ClaimAwardAsync(connection,transaction,request,ct) : 0;
        if (inserted)
        {
            if (request.InventoryJson != null) await InventoryRepository.ReturnInventoryAsync(connection, transaction, request, ct, clash, bountyDust);
            else if (request.Outcome == "Extracted") {
            await InventoryRepository.AddAsync(connection, transaction, request.PlayerId, "dust", request.Dust, ct);
            await InventoryRepository.AddAsync(connection, transaction, request.PlayerId, "alloy", request.Alloy, ct);
            await InventoryRepository.AddAsync(connection, transaction, request.PlayerId, "cells", request.Cells, ct);
            }
        }
        if (request.DeploymentId.HasValue)
        {
            await using var close = new NpgsqlCommand("UPDATE raid_deployments SET status='Closed' WHERE id=$1", connection, transaction);
            close.Parameters.AddWithValue(request.DeploymentId.Value);
            await close.ExecuteNonQueryAsync(ct);
        }
        var result = await InventoryRepository.ReadAsync(connection, transaction, request.PlayerId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

}
