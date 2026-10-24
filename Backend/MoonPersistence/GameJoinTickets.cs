using System.Security.Cryptography;

// Only a short-lived, single-use credential crosses the game transport.
// Account sessions remain in the HTTPS service. Restarting it invalidates pending tickets.
public sealed class GameJoinTickets
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, (string Ticket, DateTime Expires)> pending = new();
    public string Issue(Guid player, DateTime now)
    {
        lock (gate)
        {
            foreach (var expired in pending.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) pending.Remove(expired);
            if (!pending.ContainsKey(player) && pending.Count >= 4096) throw new InvalidOperationException("Join service is busy. Please retry.");
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            pending[player] = (ticket, now.AddSeconds(60));
            return ticket;
        }
    }
    public Guid? Consume(string ticket, DateTime now)
    {
        if (ticket is not { Length: 64 } || !ticket.All(Uri.IsHexDigit)) return null;
        lock (gate)
        {
            foreach (var pair in pending)
            {
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(ticket), Convert.FromHexString(pair.Value.Ticket))) continue;
                pending.Remove(pair.Key);
                return pair.Value.Expires > now ? pair.Key : null;
            }
            return null;
        }
    }
}
