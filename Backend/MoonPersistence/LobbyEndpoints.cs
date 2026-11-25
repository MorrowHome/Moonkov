using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Online;

internal static class LobbyEndpoints
{
    internal static void Map(RouteGroupBuilder auth, RouteGroupBuilder internalApi, OnlineInstanceOptions instanceOptions)
    {
        auth.MapGet("/lobby", async (HttpRequest request, int? character, int? pose, int? networkProtocol, bool? browseRooms, AccountRepository accounts, OnlineLobbies lobbies, CancellationToken ct) =>
        {
            if (networkProtocol != OnlineNetworkProtocol.Version) return Results.Conflict(new { error = "请更新客户端，当前大厅已启用 P2P/VPS 连接选择。" });
            var profile = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
            if (profile == null) return Results.Unauthorized();
            RefreshLobbyAppearance(lobbies, profile, character, pose);
            return Results.Ok(BuildSnapshot(lobbies, profile, browseRooms == true, instanceOptions));
        });
        auth.MapPost("/lobby/action", async (HttpRequest request, LobbyAction action, int? character, int? pose, int? networkProtocol, bool? browseRooms, AccountRepository accounts, OnlineLobbies lobbies, CancellationToken ct) =>
        {
            if (networkProtocol != OnlineNetworkProtocol.Version) return Results.Conflict(new { error = "请更新客户端，当前大厅已启用 P2P/VPS 连接选择。" });
            var profile = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
            if (profile == null) return Results.Unauthorized();
            try
            {
                RefreshLobbyAppearance(lobbies, profile, character, pose);
                Guid? eligibleApplicant = null;
                if (action.Action == "approve-join")
                {
                    var applicant = lobbies.ApprovalApplicant(profile.PlayerId, action.RequestId, DateTime.UtcNow);
                    if ((await accounts.ReadProfileAsync(applicant, ct)).ActiveDeploymentId == null) eligibleApplicant = applicant;
                }
                lobbies.Act(profile.PlayerId, profile.DisplayName, profile.ActiveDeploymentId != null, action, DateTime.UtcNow, eligibleApplicant);
                RefreshLobbyAppearance(lobbies, profile, character, pose);
                return Results.Ok(BuildSnapshot(lobbies, profile, browseRooms == true, instanceOptions));
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        auth.MapPost("/lobby/game-ticket", async (HttpRequest request, InstanceCredential credential, AccountRepository accounts, OnlineLobbies lobbies, GameJoinTickets tickets, CancellationToken ct) =>
        {
            if (credential.Protocol != OnlineNetworkProtocol.Version) return Results.Conflict(new { error = "请同步更新客户端和独立服务器。" });
            var profile = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
            if (profile == null) return Results.Unauthorized();
            try
            {
                lobbies.Admission(profile.PlayerId, credential.InstanceId, DateTime.UtcNow);
                return Results.Ok(new { Ticket = tickets.Issue(profile.PlayerId, DateTime.UtcNow, credential.InstanceId) });
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        auth.MapPost("/lobby/ice", async (HttpRequest request, IceClientMessage message, AccountRepository accounts, OnlineIceSignaling signaling, CancellationToken ct) =>
        {
            var profile = await accounts.ResolveSessionAsync(request.BearerToken(), ct);
            if (profile == null) return Results.Unauthorized();
            try { return Results.Ok(await signaling.ExchangeAsync(profile.PlayerId, message, ct)); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        internalApi.MapPost("/instances/event", (InstanceEvent update, OnlineLobbies lobbies) =>
        {
            try
            {
                if (update.Kind != "ready") return Results.BadRequest();
                lobbies.Ready(update.InstanceId, DateTime.UtcNow, update.Protocol, update.IceAvailable, update.IceHostId, update.IceHostAddress); return Results.NoContent();
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        internalApi.MapPost("/instances/ice", (IceServerPoll message, OnlineIceSignaling signaling) =>
        {
            try { return Results.Ok(signaling.Poll(message)); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        internalApi.MapPost("/game-tickets/consume", async (SessionCredential request, AccountRepository accounts, GameJoinTickets tickets, OnlineLobbies lobbies, CancellationToken ct) =>
        {
            var player = tickets.Consume(request.Token, DateTime.UtcNow, request.InstanceId);
            if (player == null) return Results.Unauthorized();
            var profile = await accounts.ReadProfileAsync(player.Value, ct);
            if (string.IsNullOrEmpty(request.InstanceId)) return Results.Ok(profile);
            try
            {
                var admission = lobbies.Admission(player.Value, request.InstanceId, DateTime.UtcNow, consume: true);
                var json = System.Text.Json.JsonSerializer.SerializeToNode(profile)!.AsObject();
                json["OnlineInstanceId"] = admission.Instance; json["OnlineTeam"] = admission.Team;
                return Results.Ok(json);
            }
            catch (InvalidOperationException) { return Results.Unauthorized(); }
        });
    }

    private static LobbySnapshot BuildSnapshot(OnlineLobbies lobbies, Profile profile, bool browseRooms,
        OnlineInstanceOptions options)
    {
        var snapshot = lobbies.Snapshot(profile.PlayerId, DateTime.UtcNow, options.Address, options.ServerName,
            options.Port, browseRooms, profile.ActiveDeploymentId != null);
        snapshot.IceStunHost = options.IceStunHost;
        snapshot.IceStunPort = options.IceStunPort;
        return snapshot;
    }

    private static void RefreshLobbyAppearance(OnlineLobbies lobbies, Profile profile, int? character, int? pose)
    {
        if (profile.ActiveDeploymentId != null) return;
        // Selectors are cosmetic and bounded. Equipment always comes from the account.
        var inventory = profile.InventoryJson == null ? InventoryGraph.Create(false, false) : InventoryRepository.Decode(profile.InventoryJson);
        var appearance = LobbyAppearanceInventory.FromInventory(inventory, character ?? 2, pose ?? -1);
        lobbies.UpdateAppearance(profile.PlayerId, appearance);
    }
}
