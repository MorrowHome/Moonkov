using System.Text.Json;
using Npgsql;
using Unity.MP_FPS.Inventory;

public sealed partial class InventoryRepository
{
    private static readonly JsonSerializerOptions InventoryJsonOptions = new() { IncludeFields = true };
    public static string Encode(InventoryGraph graph) => JsonSerializer.Serialize(graph, InventoryJsonOptions);
    public static InventoryGraph Decode(string json) => JsonSerializer.Deserialize<InventoryGraph>(json, InventoryJsonOptions) ?? throw new DeploymentRejectedException("invalid_inventory");

    public static async Task<InventoryGraph> ReadGraphAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, CancellationToken ct)
    {
        // All readers that can lazily migrate share the same player lock as deploy/settlement.
        await LockPlayerAsync(connection, transaction, playerId, ct);
        await using (var query = new NpgsqlCommand("SELECT inventory FROM inventory_profiles WHERE player_id=$1", connection, transaction))
        {
            query.Parameters.AddWithValue(playerId);
            if (await query.ExecuteScalarAsync(ct) is string json) return Decode(json);
        }
        var graph = InventoryGraph.Create();
        var legacy = new List<(string Code, int Count, string Id)>();
        await using (var query = new NpgsqlCommand("SELECT item_code,quantity,id FROM inventory_stacks WHERE player_id=$1 ORDER BY item_code", connection, transaction))
        {
            query.Parameters.AddWithValue(playerId); await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) legacy.Add((reader.GetString(0), reader.GetInt32(1), reader.GetGuid(2).ToString("D")));
        }
        foreach (var item in legacy)
        {
            while (graph.AddSupply(item.Code, item.Count, "stash", id: item.Id) != InventoryError.None)
            {
                if (graph.StashRows >= 4096) throw new DeploymentRejectedException("legacy_inventory_too_large");
                graph.StashRows = Math.Min(4096, graph.StashRows * 2);
            }
        }
        await WriteGraphAsync(connection, transaction, playerId, graph, ct);
        return graph;
    }
    public static async Task WriteGraphAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid playerId, InventoryGraph graph, CancellationToken ct)
    {
        if (graph.Validate() != InventoryError.None) throw new DeploymentRejectedException("invalid_inventory");
        await using (var write = new NpgsqlCommand("""
            INSERT INTO inventory_profiles (player_id,revision,inventory) VALUES ($1,$2,$3::jsonb)
            ON CONFLICT (player_id) DO UPDATE SET revision=EXCLUDED.revision,inventory=EXCLUDED.inventory
            """, connection, transaction))
        {
            write.Parameters.AddWithValue(playerId); write.Parameters.AddWithValue(graph.Version); write.Parameters.AddWithValue(Encode(graph));
            await write.ExecuteNonQueryAsync(ct);
        }
        // Derived V3 compatibility projection only; no independent resource mutation remains.
        foreach (string code in new[] { "dust", "alloy", "cells" })
        {
            await using var totals = new NpgsqlCommand("UPDATE inventory_stacks SET quantity=$3 WHERE player_id=$1 AND item_code=$2", connection, transaction);
            totals.Parameters.AddWithValue(playerId); totals.Parameters.AddWithValue(code); totals.Parameters.AddWithValue(graph.Count(code));
            await totals.ExecuteNonQueryAsync(ct);
        }
    }
    private static void PrepareCells(InventoryGraph graph, int desired)
    {
        int current = graph.Count("cells", true);
        if (current > desired)
        {
            int remove = current - desired;
            foreach (var item in graph.Items.Where(i => i.Code == "cells" && graph.Carried(i)).ToArray())
            {
                if (remove == 0) break;
                int amount = Math.Min(item.Quantity, remove); var stored = item.Clone(); stored.Quantity = amount;
                if (amount < item.Quantity) { item.Quantity -= amount; stored.Id = Guid.NewGuid().ToString("D"); }
                else graph.Items.Remove(item);
                if (!graph.FindSpace(stored, "stash", out var region, out var x, out var y)) throw new DeploymentRejectedException("stash_full");
                stored.Parent = "stash"; stored.Region = region; stored.X = x; stored.Y = y; graph.Items.Add(stored); remove -= amount;
            }
        }
        else if (current < desired)
        {
            int take = desired - current;
            var stock = graph.Items.Where(i => i.Code == "cells" && !graph.Carried(i)).ToArray();
            if (stock.Sum(i => i.Quantity) < take) throw new DeploymentRejectedException("insufficient_cells");
            int remaining = take;
            foreach (var item in stock)
            { int used = Math.Min(item.Quantity, remaining); item.Quantity -= used; remaining -= used; if (item.Quantity == 0) graph.Items.Remove(item); if (remaining == 0) break; }
            if (graph.AddSupply("cells", take) != InventoryError.None) throw new DeploymentRejectedException("loadout_full");
        }
    }
    public async Task<Profile> MoveAsync(Guid playerId, InventoryCommand command, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var graph = await ReadGraphAsync(connection, transaction, playerId, ct);
        // Inventory cannot be re-equipped remotely while a raid owns the carried kit.
        await using (var active = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM raid_deployments WHERE player_id=$1 AND status='Open')", connection, transaction))
        { active.Parameters.AddWithValue(playerId); if ((bool)(await active.ExecuteScalarAsync(ct))!) throw new DeploymentRejectedException("raid_active"); }
        var result = graph.TryApply(command);
        if (result != InventoryError.None) throw new DeploymentRejectedException("inventory_" + result);
        await WriteGraphAsync(connection, transaction, playerId, graph, ct);
        var profile = await ReadAsync(connection, transaction, playerId, ct); await transaction.CommitAsync(ct); return profile;
    }
    public static async Task ReturnInventoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Settlement request, CancellationToken ct)
    {
        if (request.InventoryJson == null) return;
        var raid = Decode(request.InventoryJson);
        if (raid.Find("stash") != null || raid.Count("dust", true) != request.Dust || raid.Count("alloy", true) != request.Alloy || raid.Count("cells", true) != request.Cells
            || raid.Validate() != InventoryError.None) throw new ReceiptConflictException();
        if (request.Outcome != "Extracted") return;
        var graph = await ReadGraphAsync(connection, transaction, request.PlayerId, ct);
        var result = graph.ReturnLoadout(raid);
        if (result != InventoryError.None) throw new DeploymentRejectedException("return_" + result);
        await WriteGraphAsync(connection, transaction, request.PlayerId, graph, ct);
    }
}
