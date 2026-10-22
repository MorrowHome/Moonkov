using Npgsql;
using Unity.MP_FPS.Inventory;

public sealed partial class InventoryRepository
{
    // ReadGraphAsync already owns the player row lock. A profile read and any
    // operation which frees space share this same graph/inbox transaction.
    private static async Task<InventoryGraph> MigrateProtectionAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid playerId,InventoryGraph graph,CancellationToken ct)
    {
        if(graph.ProtectionKitVersion>=1)return graph;
        await using(var active=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM raid_deployments WHERE player_id=$1 AND status='Open')",connection,transaction))
        {
            active.Parameters.AddWithValue(playerId);
            if(await active.ExecuteScalarAsync(ct) is true)return graph;
        }
        var inbox=await ReadRecoveryAsync(connection,transaction,playerId,ct);
        if(!ProtectionKitMigration.TryApply(graph,inbox,out var stored,out var pending))return graph;
        await WriteGraphAsync(connection,transaction,playerId,stored,ct);
        await WriteRecoveryAsync(connection,transaction,playerId,pending,ct);
        return stored;
    }
}
