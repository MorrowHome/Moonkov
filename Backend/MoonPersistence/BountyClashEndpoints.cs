using Npgsql;

internal static class BountyClashEndpoints
{
    internal static void Map(RouteGroupBuilder clash)
    {
        clash.MapPost("/matches", async (BountyMatchRegistration request, BountyClashRepository repository, CancellationToken ct) =>
        {
            try { await repository.RegisterAsync(request, ct); return Results.Ok(new { status = "registered" }); }
            catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "clash_match_conflict" }); }
            catch (PostgresException ex) when (ex.SqlState is "23505" or "23503") { return Results.Conflict(new { error = "clash_roster_conflict" }); }
        });
        clash.MapPost("/redemptions", async (BountyRedemption request, BountyClashRepository repository, CancellationToken ct) =>
        {
            try { await repository.RedeemAsync(request, ct); return Results.Ok(new { status = "redeemed" }); }
            catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "clash_redemption_conflict" }); }
            catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "clash_redemption_conflict" }); }
        });
        RaidEndpoints.MapDeployments(clash, clash: true);
        clash.MapPost("/deployments/refund", async (Deployment request, InventoryRepository inventory, StashRepository store, CancellationToken ct) =>
        {
            if (!RaidEndpoints.ValidDeployment(request)) return Results.BadRequest(new { error = "invalid_loadout" });
            try { return Results.Ok(await inventory.RefundBountyDeploymentAsync(request, store, ct)); }
            catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "deployment_conflict" }); }
            catch (ProfileNotFoundException) { return Results.NotFound(new { error = "unknown_profile" }); }
        });
        clash.MapPost("/settlements", async (Settlement request, StashRepository store, CancellationToken ct) =>
        {
            if (request.PlayerId == Guid.Empty || request.SettlementId == Guid.Empty || request.Outcome is not ("Extracted" or "Dead" or "TimedOut") ||
                request.Dust < 0 || request.Alloy < 0 || request.Cells < 0 || request.InventoryJson == null || request.InventoryJson.Length > 500000)
                return Results.BadRequest(new { error = "invalid_clash_settlement" });
            try { return Results.Ok(await store.SettleAsync(request, ct, clash: true)); }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "settlement_conflict" }); }
            catch (ProfileNotFoundException) { return Results.NotFound(new { error = "unknown_profile" }); }
            catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
    }
}
