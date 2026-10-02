using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

public sealed class AccountRepository(NpgsqlDataSource db)
{
    public const int Iterations = 600_000;
    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(16);
    public static bool ValidCredentials(Credentials request) => request.Username is not null
        && Regex.IsMatch(request.Username, "\\A[A-Za-z0-9_]{3,32}\\z")
        && request.Password is not null && request.Password.Length is >= 8 and <= 128;
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.ToLowerInvariant())));

    public async Task<LoginResult> RegisterAsync(Credentials request, CancellationToken ct)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] passwordHash = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        Guid playerId = Guid.NewGuid();
        if (request.GuestToken is { Length: 64 } && request.GuestToken.All(Uri.IsHexDigit))
        {
            await using var guest = new NpgsqlCommand("SELECT id FROM players WHERE guest_token_hash = $1 FOR UPDATE", connection, transaction);
            guest.Parameters.AddWithValue(HashToken(request.GuestToken));
            if (await guest.ExecuteScalarAsync(ct) is Guid existing) playerId = existing;
        }
        await using var player = new NpgsqlCommand("""
            INSERT INTO players (id, display_name) VALUES ($1, $2)
            ON CONFLICT (id) DO UPDATE SET display_name = EXCLUDED.display_name
            """, connection, transaction);
        player.Parameters.AddWithValue(playerId);
        player.Parameters.AddWithValue(request.Username);
        await player.ExecuteNonQueryAsync(ct);
        await using var account = new NpgsqlCommand("""
            INSERT INTO accounts (username_key, player_id, password_salt, password_hash, password_iterations)
            VALUES ($1, $2, $3, $4, $5)
            """, connection, transaction);
        account.Parameters.AddWithValue(request.Username.ToLowerInvariant());
        account.Parameters.AddWithValue(playerId);
        account.Parameters.AddWithValue(salt);
        account.Parameters.AddWithValue(passwordHash);
        account.Parameters.AddWithValue(Iterations);
        await account.ExecuteNonQueryAsync(ct);
        // Retire the old guest credential after linking the account.
        await using var retire = new NpgsqlCommand("UPDATE players SET guest_token_hash = NULL WHERE id = $1", connection, transaction);
        retire.Parameters.AddWithValue(playerId);
        await retire.ExecuteNonQueryAsync(ct);
        await InventoryRepository.EnsureStacksAsync(connection, transaction, playerId, ct);
        var session = await CreateSessionAsync(connection, transaction, playerId, request.Username, ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    public async Task<LoginResult?> LoginAsync(Credentials request, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        Guid playerId = default;
        byte[] salt = DummySalt, expected = new byte[32];
        string displayName = request.Username;
        int iterations = Iterations;
        await using (var command = new NpgsqlCommand("""
            SELECT a.player_id, a.password_salt, a.password_hash, a.password_iterations, p.display_name
            FROM accounts a JOIN players p ON p.id = a.player_id WHERE a.username_key = $1
            """, connection))
        {
            command.Parameters.AddWithValue(request.Username.ToLowerInvariant());
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                playerId = reader.GetGuid(0); salt = reader.GetFieldValue<byte[]>(1); expected = reader.GetFieldValue<byte[]>(2);
                iterations = reader.GetInt32(3); displayName = reader.GetString(4);
            }
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, iterations, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual) || playerId == Guid.Empty) return null;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var result = await CreateSessionAsync(connection, transaction, playerId, displayName, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<LoginResult> CreateSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid playerId, string displayName, CancellationToken ct)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expires = DateTime.UtcNow.AddDays(7);
        await using var cleanup = new NpgsqlCommand("DELETE FROM login_sessions WHERE player_id = $1 AND expires_at <= now()", connection, transaction);
        cleanup.Parameters.AddWithValue(playerId);
        await cleanup.ExecuteNonQueryAsync(ct);
        await using var command = new NpgsqlCommand("INSERT INTO login_sessions (token_hash, player_id, expires_at) VALUES ($1, $2, $3)", connection, transaction);
        command.Parameters.AddWithValue(HashToken(token));
        command.Parameters.AddWithValue(playerId);
        command.Parameters.AddWithValue(expires);
        await command.ExecuteNonQueryAsync(ct);
        return new LoginResult(token, expires, displayName);
    }

    public async Task<Profile?> ResolveSessionAsync(string? token, CancellationToken ct)
    {
        if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit)) return null;
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT player_id FROM login_sessions WHERE token_hash=$1 AND expires_at>now()", connection);
        command.Parameters.AddWithValue(HashToken(token));
        var id = await command.ExecuteScalarAsync(ct);
        if (id is not Guid playerId) return null;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var profile = await InventoryRepository.ReadAsync(connection, transaction, playerId, ct);
        await transaction.CommitAsync(ct);
        return profile;
    }

    public async Task LogoutAsync(string? token, CancellationToken ct)
    {
        if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit)) return;
        await using var command = db.CreateCommand("DELETE FROM login_sessions WHERE token_hash = $1");
        command.Parameters.AddWithValue(HashToken(token));
        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed record Credentials(string Username, string Password, string? GuestToken = null);
public sealed record LoginResult(string Token, DateTime ExpiresAt, string DisplayName);
public sealed record SessionCredential(string Token);
