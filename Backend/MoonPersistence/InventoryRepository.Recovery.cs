using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

public sealed partial class InventoryRepository
{
    public static async Task<RecoveryInbox> ReadRecoveryAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid playerId,CancellationToken ct)
    {
        await using var read=new NpgsqlCommand("SELECT inbox FROM inventory_recovery WHERE player_id=$1",connection,transaction);
        read.Parameters.AddWithValue(playerId);
        return await read.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<RecoveryInbox>(json,InventoryJsonOptions)
            ?? throw new DeploymentRejectedException("invalid_recovery") : new RecoveryInbox();
    }
    private static async Task WriteRecoveryAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid playerId,RecoveryInbox inbox,CancellationToken ct)
    {
        await using var write=new NpgsqlCommand("""
            INSERT INTO inventory_recovery (player_id,revision,inbox) VALUES ($1,$2,$3::jsonb)
            ON CONFLICT (player_id) DO UPDATE SET revision=EXCLUDED.revision,inbox=EXCLUDED.inbox
            """,connection,transaction);
        write.Parameters.AddWithValue(playerId);write.Parameters.AddWithValue(inbox.Version);
        write.Parameters.AddWithValue(JsonSerializer.Serialize(inbox,InventoryJsonOptions));await write.ExecuteNonQueryAsync(ct);
    }
    public async Task<Profile> ClaimRecoveryAsync(Guid playerId,RecoveryClaim request,CancellationToken ct)
    {
        if(request==null || !Guid.TryParse(request.RequestId,out var requestId))throw new DeploymentRejectedException("recovery_invalid");
        string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("recovery/"+JsonSerializer.Serialize(request,InventoryJsonOptions))));
        await using var connection=await db.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        await LockPlayerAsync(connection,transaction,playerId,ct);
        await using(var receipt=new NpgsqlCommand("SELECT request_hash FROM inventory_trades WHERE player_id=$1 AND request_id=$2",connection,transaction))
        {
            receipt.Parameters.AddWithValue(playerId);receipt.Parameters.AddWithValue(requestId);
            if(await receipt.ExecuteScalarAsync(ct) is string previous)
            {
                if(previous!=fingerprint)throw new DeploymentRejectedException("recovery_request_conflict");
                var replay=await ReadAsync(connection,transaction,playerId,ct);await transaction.CommitAsync(ct);return replay;
            }
        }
        await using(var active=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM raid_deployments WHERE player_id=$1 AND status='Open')",connection,transaction))
        {active.Parameters.AddWithValue(playerId);if(await active.ExecuteScalarAsync(ct) is true)throw new DeploymentRejectedException("raid_active");}
        var graph=await ReadGraphAsync(connection,transaction,playerId,ct);var inbox=await ReadRecoveryAsync(connection,transaction,playerId,ct);
        var error=RecoveryInventory.Claim(graph,inbox,request,out var stored,out var pending);
        if(error!=InventoryError.None)throw new DeploymentRejectedException("recovery_"+error);
        await WriteGraphAsync(connection,transaction,playerId,stored,ct);await WriteRecoveryAsync(connection,transaction,playerId,pending,ct);
        await using(var receipt=new NpgsqlCommand("INSERT INTO inventory_trades (player_id,request_id,request_hash) VALUES ($1,$2,$3)",connection,transaction))
        {receipt.Parameters.AddWithValue(playerId);receipt.Parameters.AddWithValue(requestId);receipt.Parameters.AddWithValue(fingerprint);await receipt.ExecuteNonQueryAsync(ct);}
        var profile=await ReadAsync(connection,transaction,playerId,ct);await transaction.CommitAsync(ct);return profile;
    }
}
