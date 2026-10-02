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
        await using var stash = new NpgsqlCommand("INSERT INTO stashes (player_id) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction);
        stash.Parameters.AddWithValue(playerId);
        await stash.ExecuteNonQueryAsync(ct);
        var result = await ReadAsync(connection, transaction, playerId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Profile> SettleAsync(Settlement request, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize rewards for this player. Atomic increments never overwrite another raid's rewards.
        await using var gate = new NpgsqlCommand("SELECT player_id FROM stashes WHERE player_id = $1 FOR UPDATE", connection, transaction);
        gate.Parameters.AddWithValue(request.PlayerId);
        if (await gate.ExecuteScalarAsync(ct) is null) throw new ProfileNotFoundException();
        await using var receipt = new NpgsqlCommand("""
            INSERT INTO raid_settlements (id, player_id, outcome, dust, alloy, cells)
            VALUES ($1, $2, $3, $4, $5, $6) ON CONFLICT (id) DO NOTHING
            """, connection, transaction);
        receipt.Parameters.AddWithValue(request.SettlementId);
        receipt.Parameters.AddWithValue(request.PlayerId);
        receipt.Parameters.AddWithValue(request.Outcome);
        receipt.Parameters.AddWithValue(request.Dust);
        receipt.Parameters.AddWithValue(request.Alloy);
        receipt.Parameters.AddWithValue(request.Cells);
        var inserted = await receipt.ExecuteNonQueryAsync(ct) == 1;
        if (!inserted)
        {
            await using var previous = new NpgsqlCommand("SELECT player_id, outcome, dust, alloy, cells FROM raid_settlements WHERE id = $1", connection, transaction);
            previous.Parameters.AddWithValue(request.SettlementId);
            await using var reader = await previous.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != request.PlayerId
                || reader.GetString(1) != request.Outcome || reader.GetInt32(2) != request.Dust
                || reader.GetInt32(3) != request.Alloy || reader.GetInt32(4) != request.Cells)
                throw new ReceiptConflictException();
        }
        if (inserted && request.Outcome == "Extracted")
        {
            await using var reward = new NpgsqlCommand("UPDATE stashes SET dust = dust + $2, alloy = alloy + $3, cells = cells + $4 WHERE player_id = $1", connection, transaction);
            reward.Parameters.AddWithValue(request.PlayerId);
            reward.Parameters.AddWithValue(request.Dust);
            reward.Parameters.AddWithValue(request.Alloy);
            reward.Parameters.AddWithValue(request.Cells);
            await reward.ExecuteNonQueryAsync(ct);
        }
        var result = await ReadAsync(connection, transaction, request.PlayerId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<Profile> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT s.dust, s.alloy, s.cells, p.display_name FROM stashes s JOIN players p ON p.id = s.player_id WHERE player_id = $1", connection, transaction);
        command.Parameters.AddWithValue(playerId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ProfileNotFoundException();
        return new Profile(playerId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3));
    }
}
