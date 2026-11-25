using Npgsql;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

internal static class AccountEndpoints
{
    internal static void Map(RouteGroupBuilder auth, RouteGroupBuilder internalApi)
    {
        auth.MapPost("/register", async (Credentials request, AccountRepository accounts, CancellationToken ct) =>
        {
            if (!AccountRepository.ValidCredentials(request))
                return Results.BadRequest(new { error = "Use 3-32 letters, digits or underscores for username and an 8-128 character password." });
            try { return Results.Ok(await accounts.RegisterAsync(request, ct)); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            { return Results.Conflict(new { error = "Username or guest profile already registered." }); }
        }).RequireRateLimiting("auth");
        auth.MapPost("/login", async (Credentials request, AccountRepository accounts, CancellationToken ct) =>
        {
            if (!AccountRepository.ValidCredentials(request)) return Results.Unauthorized();
            var result = await accounts.LoginAsync(request, ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireRateLimiting("auth");
        auth.MapGet("/me", async (HttpRequest request, AccountRepository accounts, CancellationToken ct) =>
        {
            var result = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
        auth.MapPost("/logout", async (HttpRequest request, AccountRepository accounts, CancellationToken ct) =>
        {
            await accounts.LogoutAsync(request.BearerToken(), ct);
            return Results.NoContent();
        });

        auth.MapPost("/inventory/move", (HttpRequest request, InventoryCommand command, AccountRepository accounts,
            InventoryRepository inventory, CancellationToken ct) =>
            RunInventoryCommand(request, accounts, ct, player => inventory.MoveAsync(player, command, ct)));
        auth.MapPost("/research/advance", (HttpRequest request, ResearchQuestCommand command, AccountRepository accounts,
            InventoryRepository inventory, CancellationToken ct) =>
            RunInventoryCommand(request, accounts, ct, player => inventory.ApplyResearchCommandAsync(player, command, ct)));

        auth.MapGet("/shop", async (HttpRequest request, AccountRepository accounts, CancellationToken ct) =>
        {
            var profile = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
            return profile is null ? Results.Unauthorized() : Results.Ok(new { offers = ShopCatalog.Offers, profile });
        });
        auth.MapPost("/shop/trade", (HttpRequest request, ShopCommand command, AccountRepository accounts,
            InventoryRepository inventory, CancellationToken ct) =>
            RunInventoryCommand(request, accounts, ct, player => inventory.TradeAsync(player, command, ct)));
        auth.MapPost("/inventory/recovery/claim", (HttpRequest request, RecoveryClaim command, AccountRepository accounts,
            InventoryRepository inventory, CancellationToken ct) =>
            RunInventoryCommand(request, accounts, ct, player => inventory.ClaimRecoveryAsync(player, command, ct)));

        auth.MapPost("/game-ticket", async (HttpRequest request, AccountRepository accounts, GameJoinTickets tickets, CancellationToken ct) =>
        {
            var profile = await accounts.ResolveSessionAsync(request.Headers.Authorization.ToString().Replace("Bearer ", ""), ct);
            return profile is null ? Results.Unauthorized() : Results.Ok(new { Ticket = tickets.Issue(profile.PlayerId, DateTime.UtcNow) });
        }).RequireRateLimiting("auth");

        internalApi.MapGet("/accounts/{playerId:guid}", async (Guid playerId, AccountRepository accounts, CancellationToken ct) =>
        {
            return Results.Ok(await accounts.ReadProfileAsync(playerId, ct));
        });
        // Trusted administration only; the game join path consumes single-use tickets.
        internalApi.MapPost("/sessions/resolve", async (SessionCredential request, AccountRepository accounts, CancellationToken ct) =>
        {
            var profile = await accounts.ResolveSessionAsync(request.Token, ct);
            return profile is null ? Results.Unauthorized() : Results.Ok(profile);
        });
        internalApi.MapPost("/profiles/resolve", async (ResolveProfile request, StashRepository store, IConfiguration config, CancellationToken ct) =>
        {
            if (!config.GetValue<bool>("AllowGuestProfiles")) return Results.StatusCode(403);
            if (request.GuestToken is null || request.GuestToken.Length != 64 || !request.GuestToken.All(Uri.IsHexDigit)
                || string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 64)
                return Results.BadRequest(new { error = "Invalid guest credential or display name." });
            return Results.Ok(await store.ResolveAsync(request, ct));
        });
    }

    private static async Task<IResult> RunInventoryCommand<T>(HttpRequest request, AccountRepository accounts,
        CancellationToken ct, Func<Guid, Task<T>> execute)
    {
        var profile = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
        if (profile == null) return Results.Unauthorized();
        try { return Results.Ok(await execute(profile.PlayerId)); }
        catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    internal static string? BearerToken(this HttpRequest request)
    {
        string header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : null;
    }
}
