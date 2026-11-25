internal static class RaidEndpoints
{
    internal static void Map(RouteGroupBuilder internalApi)
    {
        internalApi.MapPost("/settlements", async (Settlement request, StashRepository store, OnlineLobbies lobbies, CancellationToken ct) =>
        {
            if (request.PlayerId == Guid.Empty || request.SettlementId == Guid.Empty
                || request.Outcome is not ("Extracted" or "Dead" or "TimedOut")
                || request.Dust < 0 || request.Alloy < 0 || request.Cells < 0
                || (request.InventoryJson == null && (long)request.Dust + request.Alloy + request.Cells > 12)
                || request.InventoryJson?.Length > 500000)
                return Results.BadRequest(new { error = "Invalid settlement." });
            try
            {
                var profile = await store.SettleAsync(request, ct);
                if (profile.ActiveDeploymentId == null) lobbies.Settled(request.PlayerId);
                return Results.Ok(profile);
            }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "Settlement ID already has another payload." }); }
            catch (ProfileNotFoundException) { return Results.NotFound(new { error = "Unknown profile." }); }
            catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        MapDeployments(internalApi, clash: false);
    }

    internal static void MapDeployments(RouteGroupBuilder routes, bool clash)
    {
        routes.MapPost("/deployments", async (Deployment request, InventoryRepository inventory, CancellationToken ct) =>
        {
            if (!ValidDeployment(request)) return Results.BadRequest(new { error = "invalid_loadout" });
            try { return Results.Ok(await inventory.DeployAsync(request, ct, clash)); }
            catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "deployment_conflict" }); }
            catch (ProfileNotFoundException) { return Results.NotFound(new { error = "unknown_profile" }); }
        });
        routes.MapPost("/deployments/abandon", async (Deployment request, InventoryRepository inventory, CancellationToken ct) =>
        {
            if (!ValidDeployment(request)) return Results.BadRequest(new { error = "invalid_loadout" });
            try { return Results.Ok(await inventory.AbandonAsync(request, ct, clash)); }
            catch (DeploymentRejectedException ex) when (clash) { return Results.Conflict(new { error = ex.Message }); }
            catch (ReceiptConflictException) { return Results.Conflict(new { error = "deployment_conflict" }); }
            catch (ProfileNotFoundException) { return Results.NotFound(new { error = "unknown_profile" }); }
        });
    }

    internal static bool ValidDeployment(Deployment request) => request.PlayerId != Guid.Empty && request.DeploymentId != Guid.Empty && request.Cells is >= 0 and <= 12;
}
