using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

public sealed record BountyParticipant(Guid PlayerId, Guid DeploymentId, int TeamId);
public sealed record BountyMatchRegistration(Guid MatchId, int RulesVersion, Guid TokenId, BountyParticipant[] Participants);
public sealed record BountyRedemption(Guid MatchId, Guid TokenId, Guid ExtractionEventId, Guid CarrierDeploymentId, Guid[] ExtractedDeploymentIds);
public sealed record BountyEconomy(int BaseDust = 20, int SoloBonusDust = 60, int DuoBonusDust = 15)
{
    public void Validate()
    {
        if (BaseDust < 0 || SoloBonusDust < 0 || DuoBonusDust < 0 ||
            (long)BaseDust + Math.Max(SoloBonusDust, DuoBonusDust) > 80)
            throw new InvalidOperationException("Bounty Clash v1 rewards must fit the bounded 80-dust recovery allowance.");
    }
}

// Called only by authenticated game servers. Match registration freezes the roster;
// redemption records one authoritative extraction event, never a client-supplied amount.
public sealed class BountyClashRepository(NpgsqlDataSource db, BountyEconomy economy)
{
    public const string Mode = "BountyClash";
    public const int Version = 1;
    static string Fingerprint<T>(T payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    static void Reject(bool invalid, string code) { if (invalid) throw new DeploymentRejectedException(code); }

    public async Task RegisterAsync(BountyMatchRegistration request, CancellationToken ct)
    {
        Reject(request.MatchId == Guid.Empty || request.TokenId == Guid.Empty || request.RulesVersion != Version || request.Participants == null ||
            request.Participants.Length is < 2 or > 12 || request.Participants.Any(p=>p==null), "clash_invalid_match");
        var roster = request.Participants!.OrderBy(p => p.DeploymentId).ToArray();
        Reject(roster.Any(p => p.PlayerId == Guid.Empty || p.DeploymentId == Guid.Empty || p.TeamId is < 0 or > 3) ||
            roster.Select(p => p.PlayerId).Distinct().Count() != roster.Length || roster.Select(p => p.DeploymentId).Distinct().Count() != roster.Length ||
            roster.GroupBy(p => p.TeamId).Count() is < 2 or > 4 || roster.GroupBy(p => p.TeamId).Any(g => g.Count() > 3), "clash_invalid_roster");
        string hash = Fingerprint(request with { Participants = roster });
        economy.Validate();
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO bounty_clash_matches (id,rules_version,token_id,roster_hash,base_dust,solo_bonus_dust,duo_bonus_dust)
            VALUES ($1,$2,$3,$4,$5,$6,$7) ON CONFLICT DO NOTHING
            """, c, tx))
        {
            insert.Parameters.AddWithValue(request.MatchId); insert.Parameters.AddWithValue(request.RulesVersion); insert.Parameters.AddWithValue(request.TokenId);
            insert.Parameters.AddWithValue(hash); insert.Parameters.AddWithValue(economy.BaseDust); insert.Parameters.AddWithValue(economy.SoloBonusDust); insert.Parameters.AddWithValue(economy.DuoBonusDust);
            if (await insert.ExecuteNonQueryAsync(ct) == 0)
            {
                await using var previous = new NpgsqlCommand("SELECT roster_hash FROM bounty_clash_matches WHERE id=$1", c, tx);
                previous.Parameters.AddWithValue(request.MatchId);
                if (await previous.ExecuteScalarAsync(ct) as string != hash) throw new ReceiptConflictException();
                await tx.CommitAsync(ct); return;
            }
        }
        foreach (var p in roster)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO bounty_clash_participants (deployment_id,match_id,player_id,team_id) VALUES ($1,$2,$3,$4)", c, tx);
            insert.Parameters.AddWithValue(p.DeploymentId); insert.Parameters.AddWithValue(request.MatchId); insert.Parameters.AddWithValue(p.PlayerId); insert.Parameters.AddWithValue(p.TeamId);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public static async Task CheckBindingAsync(NpgsqlConnection c, NpgsqlTransaction tx, Deployment request, bool clash, CancellationToken ct)
    {
        if (!clash)
        {
            Reject(request.Mode != null || request.MatchId.HasValue || request.RulesVersion != 0 || request.TeamId != 0, "clash_requires_mode_endpoint");
            await using var registered = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM bounty_clash_participants WHERE deployment_id=$1)",c,tx);
            registered.Parameters.AddWithValue(request.DeploymentId);
            Reject(await registered.ExecuteScalarAsync(ct) is true,"clash_requires_mode_endpoint");
            return;
        }
        Reject(request.Mode != Mode || !request.MatchId.HasValue || request.MatchId == Guid.Empty || request.RulesVersion != Version, "clash_invalid_binding");
        await using var query = new NpgsqlCommand("""
            SELECT count(*) FROM bounty_clash_participants p JOIN bounty_clash_matches m ON m.id=p.match_id
            WHERE p.deployment_id=$1 AND p.player_id=$2 AND p.match_id=$3 AND p.team_id=$4 AND m.rules_version=$5
            """, c, tx);
        query.Parameters.AddWithValue(request.DeploymentId); query.Parameters.AddWithValue(request.PlayerId); query.Parameters.AddWithValue(request.MatchId!.Value);
        query.Parameters.AddWithValue(request.TeamId); query.Parameters.AddWithValue(request.RulesVersion);
        Reject((long)(await query.ExecuteScalarAsync(ct))! != 1, "clash_unregistered_participant");
    }

    public async Task RedeemAsync(BountyRedemption request, CancellationToken ct)
    {
        Reject(request.MatchId == Guid.Empty || request.TokenId == Guid.Empty || request.ExtractionEventId == Guid.Empty || request.CarrierDeploymentId == Guid.Empty ||
            request.ExtractedDeploymentIds == null || request.ExtractedDeploymentIds.Length is < 1 or > 3, "clash_invalid_redemption");
        var ids = request.ExtractedDeploymentIds!.OrderBy(id => id).ToArray();
        Reject(ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length || !ids.Contains(request.CarrierDeploymentId), "clash_invalid_recipients");
        string hash = Fingerprint(request with { ExtractedDeploymentIds = ids });
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        int baseDust, soloBonus, duoBonus;
        // A match lock serializes claims across players; individual player locks cannot do this.
        await using (var match = new NpgsqlCommand("SELECT token_id,base_dust,solo_bonus_dust,duo_bonus_dust FROM bounty_clash_matches WHERE id=$1 FOR UPDATE", c, tx))
        {
            match.Parameters.AddWithValue(request.MatchId); await using var reader = await match.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != request.TokenId) throw new ReceiptConflictException();
            baseDust = reader.GetInt32(1); soloBonus = reader.GetInt32(2); duoBonus = reader.GetInt32(3);
        }
        await using (var previous = new NpgsqlCommand("SELECT request_hash FROM bounty_clash_redemptions WHERE match_id=$1", c, tx))
        {
            previous.Parameters.AddWithValue(request.MatchId);
            if (await previous.ExecuteScalarAsync(ct) is string saved)
            { if (saved != hash) throw new ReceiptConflictException(); await tx.CommitAsync(ct); return; }
        }
        int team;
        await using (var carrier = new NpgsqlCommand("SELECT team_id FROM bounty_clash_participants WHERE deployment_id=$1 AND match_id=$2", c, tx))
        {
            carrier.Parameters.AddWithValue(request.CarrierDeploymentId); carrier.Parameters.AddWithValue(request.MatchId);
            if (await carrier.ExecuteScalarAsync(ct) is not int value) throw new ReceiptConflictException(); team = value;
        }
        int teamSize;
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM bounty_clash_participants WHERE match_id=$1 AND team_id=$2", c, tx))
        { count.Parameters.AddWithValue(request.MatchId); count.Parameters.AddWithValue(team); teamSize = (int)(long)(await count.ExecuteScalarAsync(ct))!; }
        int dust = baseDust + (teamSize == 1 ? soloBonus : teamSize == 2 ? duoBonus : 0);
        // Lock recipient players in a stable order, like settlement. Their deployments
        // must still be Open; already dead/settled players cannot acquire a later award.
        foreach (var id in ids)
        {
            await using var query = new NpgsqlCommand("""
                SELECT p.id FROM bounty_clash_participants b JOIN players p ON p.id=b.player_id
                JOIN raid_deployments d ON d.id=b.deployment_id
                WHERE b.deployment_id=$1 AND b.match_id=$2 AND b.team_id=$3 AND d.status='Open'
                  AND d.mode='BountyClash' AND d.match_id=b.match_id AND d.player_id=b.player_id FOR UPDATE OF p
                """, c, tx);
            query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(request.MatchId); query.Parameters.AddWithValue(team);
            if (await query.ExecuteScalarAsync(ct) is not Guid) throw new ReceiptConflictException();
        }
        await using (var insert = new NpgsqlCommand("INSERT INTO bounty_clash_redemptions (match_id,token_id,event_id,carrier_deployment_id,request_hash) VALUES ($1,$2,$3,$4,$5)", c, tx))
        {
            insert.Parameters.AddWithValue(request.MatchId); insert.Parameters.AddWithValue(request.TokenId); insert.Parameters.AddWithValue(request.ExtractionEventId);
            insert.Parameters.AddWithValue(request.CarrierDeploymentId); insert.Parameters.AddWithValue(hash); await insert.ExecuteNonQueryAsync(ct);
        }
        foreach (var id in ids)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO bounty_clash_awards (deployment_id,match_id,event_id,dust) VALUES ($1,$2,$3,$4)", c, tx);
            insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(request.MatchId); insert.Parameters.AddWithValue(request.ExtractionEventId); insert.Parameters.AddWithValue(dust);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public static async Task<int> ClaimAwardAsync(NpgsqlConnection c, NpgsqlTransaction tx, Settlement request, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT event_id,dust,claimed FROM bounty_clash_awards WHERE deployment_id=$1 AND match_id=$2", c, tx);
        query.Parameters.AddWithValue(request.DeploymentId!.Value); query.Parameters.AddWithValue(request.MatchId!.Value);
        int dust;
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            { if (request.BountyEventId.HasValue) throw new ReceiptConflictException(); return 0; }
            if (request.Outcome != "Extracted" || request.BountyEventId != reader.GetGuid(0) || reader.GetBoolean(2)) throw new ReceiptConflictException();
            dust = reader.GetInt32(1);
        }
        await using var claim = new NpgsqlCommand("UPDATE bounty_clash_awards SET claimed=true WHERE deployment_id=$1", c, tx);
        claim.Parameters.AddWithValue(request.DeploymentId.Value); await claim.ExecuteNonQueryAsync(ct); return dust;
    }
}
