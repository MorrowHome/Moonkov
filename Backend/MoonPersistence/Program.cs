using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Unity.MP_FPS.Inventory;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true).AddEnvironmentVariables().AddCommandLine(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://127.0.0.1:5080");
var connectionString = builder.Configuration["ConnectionStrings:Postgres"]
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres in appsettings.local.json or environment.");
var serverKey = builder.Configuration["ServerKey"];
if (string.IsNullOrWhiteSpace(serverKey) || serverKey.Length < 32)
    throw new InvalidOperationException("Configure a random ServerKey of at least 32 characters.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<StashRepository>();
builder.Services.AddSingleton<InventoryRepository>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<GameJoinTickets>();
var clashEconomy = new BountyEconomy(builder.Configuration.GetValue("BountyClash:BaseDust",20),
    builder.Configuration.GetValue("BountyClash:SoloBonusDust",60),builder.Configuration.GetValue("BountyClash:DuoBonusDust",15));
clashEconomy.Validate();
builder.Services.AddSingleton(clashEconomy);
builder.Services.AddSingleton<BountyClashRepository>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.IncludeFields = true);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Only the VPS reverse proxy is trusted, so a client cannot spoof its own address.
    options.KnownProxies.Add(IPAddress.Parse("10.8.0.1"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
// The relay masquerades traffic, so without this every request looks like it came from the
// tunnel address and the per-IP auth rate limit becomes a single bucket shared by all players.
app.UseForwardedHeaders();
await app.Services.GetRequiredService<StashRepository>().InitializeAsync();
var expectedKey = SHA256.HashData(Encoding.UTF8.GetBytes(serverKey));
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/auth")) { await next(context); return; }
    var supplied = context.Request.Headers["X-Moon-Server-Key"].ToString();
    if (supplied.Length > 256 || !CryptographicOperations.FixedTimeEquals(expectedKey,
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
    {
        context.Response.StatusCode = 401;
        return;
    }
    await next(context);
});
app.UseRateLimiter();
// Login throttling must not count inventory moves and background refreshes.
var auth = app.MapGroup("/auth");
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
    var result = await accounts.ResolveSessionAsync(BearerToken(request), ct);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
auth.MapPost("/logout", async (HttpRequest request, AccountRepository accounts, CancellationToken ct) =>
{
    await accounts.LogoutAsync(BearerToken(request), ct);
    return Results.NoContent();
});
auth.MapPost("/inventory/move", async (HttpRequest request, InventoryCommand command, AccountRepository accounts, InventoryRepository inventory, CancellationToken ct) =>
{
    var profile = await accounts.ResolveSessionAsync(BearerToken(request), ct);
    if (profile is null) return Results.Unauthorized();
    try { return Results.Ok(await inventory.MoveAsync(profile.PlayerId, command, ct)); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
});
auth.MapGet("/shop", async (HttpRequest request, AccountRepository accounts, CancellationToken ct) =>
{
    var profile = await accounts.ResolveSessionAsync(BearerToken(request), ct);
    return profile is null ? Results.Unauthorized() : Results.Ok(new { offers = ShopCatalog.Offers, profile });
});
auth.MapPost("/shop/trade", async (HttpRequest request, ShopCommand command, AccountRepository accounts, InventoryRepository inventory, CancellationToken ct) =>
{
    var profile = await accounts.ResolveSessionAsync(BearerToken(request), ct);
    if (profile is null) return Results.Unauthorized();
    try { return Results.Ok(await inventory.TradeAsync(profile.PlayerId, command, ct)); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error = ex.Message }); }
});
auth.MapPost("/inventory/recovery/claim", async (HttpRequest request, RecoveryClaim command, AccountRepository accounts, InventoryRepository inventory, CancellationToken ct) =>
{
    var profile=await accounts.ResolveSessionAsync(BearerToken(request),ct);
    if(profile is null)return Results.Unauthorized();
    try {return Results.Ok(await inventory.ClaimRecoveryAsync(profile.PlayerId,command,ct));}
    catch(DeploymentRejectedException ex) {return Results.Conflict(new {error=ex.Message});}
});
auth.MapPost("/game-ticket", async (HttpRequest request, AccountRepository accounts, GameJoinTickets tickets, CancellationToken ct) =>
{
    var profile = await accounts.ResolveSessionAsync(request.Headers.Authorization.ToString().Replace("Bearer ", ""), ct);
    return profile is null ? Results.Unauthorized() : Results.Ok(new { Ticket = tickets.Issue(profile.PlayerId, DateTime.UtcNow) });
}).RequireRateLimiting("auth");
app.MapPost("/internal/game-tickets/consume", async (SessionCredential request, AccountRepository accounts, GameJoinTickets tickets, CancellationToken ct) =>
{
    var player = tickets.Consume(request.Token, DateTime.UtcNow);
    return player is null ? Results.Unauthorized() : Results.Ok(await accounts.ReadProfileAsync(player.Value, ct));
});
app.MapGet("/internal/accounts/{playerId:guid}", async (Guid playerId, AccountRepository accounts, CancellationToken ct) =>
{
    return Results.Ok(await accounts.ReadProfileAsync(playerId, ct));
});
// Trusted administration only; the game join path consumes single-use tickets.
app.MapPost("/internal/sessions/resolve", async (SessionCredential request, AccountRepository accounts, CancellationToken ct) =>
{
    var profile = await accounts.ResolveSessionAsync(request.Token, ct);
    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
});
app.MapGet("/internal/health", async (NpgsqlDataSource db, CancellationToken ct) =>
{
    await using var command = db.CreateCommand("SELECT 1");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready", features = new[] { "alien-harvest-v1", "halo-research-v1", "species-harvest-v2", "missions-v1", "recovery-inbox-v1", "bounty-clash-v1", "game-join-tickets-v1" },
        build = typeof(InventoryRepository).Assembly.GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion });
});
app.MapPost("/internal/profiles/resolve", async (ResolveProfile request, StashRepository store, CancellationToken ct) =>
{
    if (!builder.Configuration.GetValue<bool>("AllowGuestProfiles")) return Results.StatusCode(403);
    if (request.GuestToken is null || request.GuestToken.Length != 64 || !request.GuestToken.All(Uri.IsHexDigit)
        || string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 64)
        return Results.BadRequest(new { error = "Invalid guest credential or display name." });
    return Results.Ok(await store.ResolveAsync(request, ct));
});
app.MapPost("/internal/settlements", async (Settlement request, StashRepository store, CancellationToken ct) =>
{
    if (request.PlayerId == Guid.Empty || request.SettlementId == Guid.Empty
        || request.Outcome is not ("Extracted" or "Dead" or "TimedOut")
        || request.Dust < 0 || request.Alloy < 0 || request.Cells < 0
        || (request.InventoryJson == null && (long)request.Dust + request.Alloy + request.Cells > 12)
        || request.InventoryJson?.Length > 500000)
        return Results.BadRequest(new { error = "Invalid settlement." });
    try { return Results.Ok(await store.SettleAsync(request, ct)); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error = "Settlement ID already has another payload." }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error = "Unknown profile." }); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
});
app.MapPost("/internal/deployments", async (Deployment request, InventoryRepository inventory, CancellationToken ct) =>
{
    if (!ValidDeployment(request)) return Results.BadRequest(new { error="invalid_loadout" });
    try { return Results.Ok(await inventory.DeployAsync(request, ct)); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="deployment_conflict" }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error="unknown_profile" }); }
});
app.MapPost("/internal/deployments/abandon", async (Deployment request, InventoryRepository inventory, CancellationToken ct) =>
{
    if (!ValidDeployment(request)) return Results.BadRequest(new { error="invalid_loadout" });
    try { return Results.Ok(await inventory.AbandonAsync(request, ct)); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="deployment_conflict" }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error="unknown_profile" }); }
});
// Separate routes ensure an older backend fails closed (404), rather than silently
// ignoring new JSON fields and charging an Expedition loadout.
var clash = app.MapGroup("/internal/bounty-clash");
clash.MapPost("/matches", async (BountyMatchRegistration request, BountyClashRepository repository, CancellationToken ct) =>
{
    try { await repository.RegisterAsync(request,ct); return Results.Ok(new { status="registered" }); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="clash_match_conflict" }); }
    catch (PostgresException ex) when (ex.SqlState is "23505" or "23503") { return Results.Conflict(new { error="clash_roster_conflict" }); }
});
clash.MapPost("/redemptions", async (BountyRedemption request, BountyClashRepository repository, CancellationToken ct) =>
{
    try { await repository.RedeemAsync(request,ct); return Results.Ok(new { status="redeemed" }); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="clash_redemption_conflict" }); }
    catch (PostgresException ex) when (ex.SqlState=="23505") { return Results.Conflict(new { error="clash_redemption_conflict" }); }
});
clash.MapPost("/deployments", async (Deployment request, InventoryRepository inventory, CancellationToken ct) =>
{
    if (!ValidDeployment(request)) return Results.BadRequest(new { error="invalid_loadout" });
    try { return Results.Ok(await inventory.DeployAsync(request,ct,clash:true)); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="deployment_conflict" }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error="unknown_profile" }); }
});
clash.MapPost("/deployments/abandon", async (Deployment request, InventoryRepository inventory, CancellationToken ct) =>
{
    if (!ValidDeployment(request)) return Results.BadRequest(new { error="invalid_loadout" });
    try { return Results.Ok(await inventory.AbandonAsync(request,ct,clash:true)); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="deployment_conflict" }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error="unknown_profile" }); }
});
clash.MapPost("/deployments/refund", async (Deployment request, InventoryRepository inventory, StashRepository store, CancellationToken ct) =>
{
    if (!ValidDeployment(request)) return Results.BadRequest(new { error="invalid_loadout" });
    try { return Results.Ok(await inventory.RefundBountyDeploymentAsync(request,store,ct)); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="deployment_conflict" }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error="unknown_profile" }); }
});
clash.MapPost("/settlements", async (Settlement request, StashRepository store, CancellationToken ct) =>
{
    if (request.PlayerId==Guid.Empty || request.SettlementId==Guid.Empty || request.Outcome is not ("Extracted" or "Dead" or "TimedOut") ||
        request.Dust<0 || request.Alloy<0 || request.Cells<0 || request.InventoryJson==null || request.InventoryJson.Length>500000)
        return Results.BadRequest(new { error="invalid_clash_settlement" });
    try { return Results.Ok(await store.SettleAsync(request,ct,clash:true)); }
    catch (ReceiptConflictException) { return Results.Conflict(new { error="settlement_conflict" }); }
    catch (ProfileNotFoundException) { return Results.NotFound(new { error="unknown_profile" }); }
    catch (DeploymentRejectedException ex) { return Results.Conflict(new { error=ex.Message }); }
});
await app.RunAsync();

static bool ValidDeployment(Deployment request) => request.PlayerId != Guid.Empty && request.DeploymentId != Guid.Empty && request.Cells is >= 0 and <= 12;

static string? BearerToken(HttpRequest request)
{
    string header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : null;
}

public sealed record ResolveProfile(string GuestToken, string DisplayName);
public sealed record Settlement(Guid PlayerId, Guid SettlementId, string Outcome, int Dust, int Alloy, int Cells, Guid? DeploymentId=null, string? InventoryJson=null, string? Mode=null, Guid? MatchId=null, int RulesVersion=0, int TeamId=0, Guid? BountyEventId=null);
public sealed record Profile(Guid PlayerId, int Dust, int Alloy, int Cells, string DisplayName,
    Guid? DeploymentId=null, int CarriedCells=0, Guid? CellStackId=null, IReadOnlyList<InventoryStack>? Items=null,
    string? InventoryJson=null, string? RaidInventoryJson=null, Guid? ActiveDeploymentId=null, int MissionSchemaVersion=1, string? RecoveryJson=null, string? Mode=null, Guid? MatchId=null, int RulesVersion=0, int TeamId=0);
public sealed class ReceiptConflictException : Exception;
public sealed class ProfileNotFoundException : Exception;
