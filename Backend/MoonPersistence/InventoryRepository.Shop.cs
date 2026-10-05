using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

public sealed partial class InventoryRepository
{
    public async Task<Profile> TradeAsync(Guid playerId, ShopCommand command, CancellationToken ct)
    {
        if (command == null || !Guid.TryParse(command.RequestId, out var requestId)) throw new DeploymentRejectedException("shop_invalid");
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command, InventoryJsonOptions))));
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockPlayerAsync(connection, transaction, playerId, ct);
        await using (var receipt = new NpgsqlCommand("SELECT request_hash FROM inventory_trades WHERE player_id=$1 AND request_id=$2", connection, transaction))
        {
            receipt.Parameters.AddWithValue(playerId); receipt.Parameters.AddWithValue(requestId);
            if (await receipt.ExecuteScalarAsync(ct) is string previous)
            {
                if (previous != fingerprint) throw new DeploymentRejectedException("shop_request_conflict");
                var current = await ReadAsync(connection, transaction, playerId, ct); await transaction.CommitAsync(ct); return current;
            }
        }
        await using (var active = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM raid_deployments WHERE player_id=$1 AND status='Open')", connection, transaction))
        { active.Parameters.AddWithValue(playerId); if (await active.ExecuteScalarAsync(ct) is true) throw new DeploymentRejectedException("raid_active"); }
        var graph = await ReadGraphAsync(connection, transaction, playerId, ct);
        string? error = ShopRules.Apply(graph, command, out var updated);
        if (error != null) throw new DeploymentRejectedException(error);
        await WriteGraphAsync(connection, transaction, playerId, updated, ct);
        await using (var receipt = new NpgsqlCommand("INSERT INTO inventory_trades(player_id,request_id,request_hash) VALUES($1,$2,$3)", connection, transaction))
        { receipt.Parameters.AddWithValue(playerId); receipt.Parameters.AddWithValue(requestId); receipt.Parameters.AddWithValue(fingerprint); await receipt.ExecuteNonQueryAsync(ct); }
        var profile = await ReadAsync(connection, transaction, playerId, ct); await transaction.CommitAsync(ct); return profile;
    }
}
