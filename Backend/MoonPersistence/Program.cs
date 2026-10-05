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
app.MapPost("/internal/sessions/resolve", async (SessionCredential request, AccountRepository accounts, CancellationToken ct) =>
{
    var result = await accounts.ResolveSessionAsync(request.Token, ct);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/internal/health", async (NpgsqlDataSource db, CancellationToken ct) =>
{
    await using var command = db.CreateCommand("SELECT 1");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready" });
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
await app.RunAsync();

static bool ValidDeployment(Deployment request) => request.PlayerId != Guid.Empty && request.DeploymentId != Guid.Empty && request.Cells is >= 0 and <= 12;

static string? BearerToken(HttpRequest request)
{
    string header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : null;
}

public sealed record ResolveProfile(string GuestToken, string DisplayName);
public sealed record Settlement(Guid PlayerId, Guid SettlementId, string Outcome, int Dust, int Alloy, int Cells, Guid? DeploymentId=null, string? InventoryJson=null);
public sealed record Profile(Guid PlayerId, int Dust, int Alloy, int Cells, string DisplayName,
    Guid? DeploymentId=null, int CarriedCells=0, Guid? CellStackId=null, IReadOnlyList<InventoryStack>? Items=null,
    string? InventoryJson=null, string? RaidInventoryJson=null, Guid? ActiveDeploymentId=null);
public sealed class ReceiptConflictException : Exception;
public sealed class ProfileNotFoundException : Exception;
