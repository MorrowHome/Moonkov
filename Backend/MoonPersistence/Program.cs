using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

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
var instanceOptions = new OnlineInstanceOptions();
builder.Configuration.GetSection("OnlineInstances").Bind(instanceOptions);
if (string.IsNullOrWhiteSpace(instanceOptions.ProjectRoot)) instanceOptions.ProjectRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../.."));
builder.Services.AddSingleton(instanceOptions);
builder.Services.AddSingleton(new OnlineLobbies(Path.Combine(instanceOptions.ProjectRoot, "LocalData/online-lobbies.json")));
builder.Services.AddSingleton<OnlineIceSignaling>();
builder.Services.AddHostedService<OnlineInstanceWorker>();
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
    if (context.Request.Path == "/auth/lobby/ice" || context.Request.Path == "/internal/instances/ice")
    {
        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = context.Request.Path.StartsWithSegments("/auth") ? 8192 : 300000;
    }
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

var auth = app.MapGroup("/auth");
var internalApi = app.MapGroup("/internal");
AccountEndpoints.Map(auth, internalApi);
LobbyEndpoints.Map(auth, internalApi, instanceOptions);
RaidEndpoints.Map(internalApi);
BountyClashEndpoints.Map(internalApi.MapGroup("/bounty-clash"));

internalApi.MapGet("/health", async (NpgsqlDataSource db, OnlineLobbies lobbies, CancellationToken ct) =>
{
    await using var command = db.CreateCommand("SELECT 1");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready", features = new[] { "alien-harvest-v1", "halo-research-v1", "species-harvest-v2", "missions-v1", "recovery-inbox-v1", "bounty-clash-v1", "game-join-tickets-v1", "online-lobbies-v1", "embedded-ice-v2", "lobby-chest-badge-v1", "lobby-entry-actions-v1", "lobby-room-directory-v1", "research-quests-v1" },
        onlineInstances = true, instancePhase = lobbies.Current()?.Phase ?? "Idle", build = typeof(InventoryRepository).Assembly.GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion });
});

await app.RunAsync();
