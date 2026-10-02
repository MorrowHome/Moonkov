using Npgsql;

public sealed class InventoryRepository(NpgsqlDataSource db)
{
    public static async Task LockPlayerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, CancellationToken ct)
    {
        await using var gate = new NpgsqlCommand("SELECT id FROM players WHERE id=$1 FOR UPDATE", connection, transaction);
        gate.Parameters.AddWithValue(playerId);
        if (await gate.ExecuteScalarAsync(ct) is null) throw new ProfileNotFoundException();
        await EnsureStacksAsync(connection, transaction, playerId, ct);
    }

    public static async Task EnsureStacksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO inventory_stacks (player_id,item_code,grid_x)
            SELECT $1,code,CASE code WHEN 'dust' THEN 0 WHEN 'alloy' THEN 2 ELSE 4 END
            FROM item_definitions WHERE code IN ('dust','alloy','cells')
            ON CONFLICT (player_id,item_code) DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue(playerId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task AddAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, string code, int quantity, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("UPDATE inventory_stacks SET quantity=quantity+$3 WHERE player_id=$1 AND item_code=$2", connection, transaction);
        command.Parameters.AddWithValue(playerId); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue(quantity);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new ProfileNotFoundException();
    }

    public static async Task<Profile> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, CancellationToken ct)
    {
        var items = new List<InventoryStack>();
        string name;
        await using (var command = new NpgsqlCommand("SELECT display_name FROM players WHERE id=$1", connection, transaction))
        {
            command.Parameters.AddWithValue(playerId);
            name = (string?)await command.ExecuteScalarAsync(ct) ?? throw new ProfileNotFoundException();
        }
        await using (var command = new NpgsqlCommand("""
            SELECT i.id,i.item_code,i.quantity,i.grid_x,i.grid_y,i.rotated,d.width,d.height,d.display_name
            FROM inventory_stacks i JOIN item_definitions d ON d.code=i.item_code WHERE i.player_id=$1 ORDER BY i.item_code
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(playerId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) items.Add(new InventoryStack(reader.GetGuid(0), reader.GetString(1),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8)));
        }
        int Count(string code) => items.Where(item => item.ItemCode == code).Sum(item => item.Quantity);
        return new Profile(playerId, Count("dust"), Count("alloy"), Count("cells"), name, Items: items);
    }

    public async Task<Profile> DeployAsync(Deployment request, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockPlayerAsync(connection, transaction, request.PlayerId, ct);
        Guid? stackId = null;
        bool existing = false;
        await using (var previous = new NpgsqlCommand("SELECT player_id,cells,cell_stack_id,status FROM raid_deployments WHERE id=$1", connection, transaction))
        {
            previous.Parameters.AddWithValue(request.DeploymentId);
            await using var reader = await previous.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                existing = true;
                if (reader.GetGuid(0) != request.PlayerId || reader.GetInt32(1) != request.Cells) throw new ReceiptConflictException();
                if (reader.GetString(3) != "Open") throw new DeploymentRejectedException("deployment_closed");
                stackId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            }
        }
        if (!existing)
        {
            await using var take = new NpgsqlCommand("""
                UPDATE inventory_stacks SET quantity=quantity-$2
                WHERE player_id=$1 AND item_code='cells' AND quantity >= $2 RETURNING id
                """, connection, transaction);
            take.Parameters.AddWithValue(request.PlayerId); take.Parameters.AddWithValue(request.Cells);
            if (await take.ExecuteScalarAsync(ct) is null) throw new DeploymentRejectedException("insufficient_cells");
            stackId = request.Cells > 0 ? Guid.NewGuid() : null;
            await using var record = new NpgsqlCommand("INSERT INTO raid_deployments (id,player_id,cells,cell_stack_id) VALUES ($1,$2,$3,$4)", connection, transaction);
            record.Parameters.AddWithValue(request.DeploymentId); record.Parameters.AddWithValue(request.PlayerId); record.Parameters.AddWithValue(request.Cells);
            record.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Uuid, Value=(object?)stackId ?? DBNull.Value });
            await record.ExecuteNonQueryAsync(ct);
        }
        var profile = await ReadAsync(connection, transaction, request.PlayerId, ct);
        await transaction.CommitAsync(ct);
        return profile with { DeploymentId=request.DeploymentId, CarriedCells=request.Cells, CellStackId=stackId };
    }

    public static async Task CheckDeploymentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Settlement request, CancellationToken ct)
    {
        if (request.DeploymentId != request.SettlementId) throw new ReceiptConflictException();
        await using var command = new NpgsqlCommand("SELECT player_id,status FROM raid_deployments WHERE id=$1", connection, transaction);
        command.Parameters.AddWithValue(request.DeploymentId!.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != request.PlayerId || reader.GetString(1) == "Cancelled") throw new ReceiptConflictException();
    }

    // A missing acknowledgement must not charge a fresh loadout during restart recovery.
    public async Task<Profile> AbandonAsync(Deployment request, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockPlayerAsync(connection, transaction, request.PlayerId, ct);
        string? status = null;
        await using (var previous = new NpgsqlCommand("SELECT player_id,cells,status FROM raid_deployments WHERE id=$1", connection, transaction))
        {
            previous.Parameters.AddWithValue(request.DeploymentId);
            await using var reader = await previous.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetGuid(0) != request.PlayerId || reader.GetInt32(1) != request.Cells) throw new ReceiptConflictException();
                status = reader.GetString(2);
            }
        }
        if (status is null)
        {
            await using var cancel = new NpgsqlCommand("INSERT INTO raid_deployments (id,player_id,cells,status) VALUES ($1,$2,$3,'Cancelled')", connection, transaction);
            cancel.Parameters.AddWithValue(request.DeploymentId); cancel.Parameters.AddWithValue(request.PlayerId); cancel.Parameters.AddWithValue(request.Cells);
            await cancel.ExecuteNonQueryAsync(ct);
        }
        else if (status == "Open")
        {
            await using var lost = new NpgsqlCommand("INSERT INTO raid_settlements (id,player_id,outcome,dust,alloy,cells,deployment_id) VALUES ($1,$2,'Dead',0,0,$3,$1)", connection, transaction);
            lost.Parameters.AddWithValue(request.DeploymentId); lost.Parameters.AddWithValue(request.PlayerId); lost.Parameters.AddWithValue(request.Cells);
            await lost.ExecuteNonQueryAsync(ct);
            await using var close = new NpgsqlCommand("UPDATE raid_deployments SET status='Closed' WHERE id=$1", connection, transaction);
            close.Parameters.AddWithValue(request.DeploymentId);
            await close.ExecuteNonQueryAsync(ct);
        }
        var profile = await ReadAsync(connection, transaction, request.PlayerId, ct);
        await transaction.CommitAsync(ct);
        return profile;
    }
}

public sealed record InventoryStack(Guid Id, string ItemCode, int Quantity, int X, int Y, bool Rotated, int Width, int Height, string DisplayName);
public sealed record Deployment(Guid PlayerId, Guid DeploymentId, int Cells);
public sealed class DeploymentRejectedException(string code) : Exception(code);
