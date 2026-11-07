using Unity.MP_FPS.Online;

// Short-lived, authenticated signaling only. No game datagrams or persistent SDP.
public sealed class OnlineIceSignaling(OnlineLobbies lobbies)
{
    private sealed class Slot
    {
        public required string Instance, Attempt, Description;
        public Guid Player;
        public bool PublicOnly, Cancelled;
        public DateTime Created, Expires;
        public readonly TaskCompletionSource<IceReply> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object gate = new();
    private readonly Dictionary<Guid, Slot> slots = new();
    private void Prune(DateTime now, string instance)
    {
        foreach (var pair in slots.Where(x => x.Value.Expires <= now || x.Value.Instance != instance).ToArray())
        {
            pair.Value.Answer.TrySetResult(new IceReply { Status = "closed", Error = "P2P 协商已结束，请重试。" });
            slots.Remove(pair.Key);
        }
    }
    public async Task<IceReply> ExchangeAsync(Guid player, IceClientMessage message, CancellationToken ct)
    {
        if (message.Protocol != OnlineNetworkProtocol.Version || !Guid.TryParseExact(message.AttemptId, "N", out _))
            throw new InvalidOperationException("连接协议不匹配，请更新客户端。");
        var now = DateTime.UtcNow;
        lobbies.Admission(player, message.InstanceId, now);
        var instance = lobbies.Current();
        if (instance?.Id != message.InstanceId || instance.ConnectionPath != OnlineNetworkProtocol.P2P || !instance.IceAvailable)
            throw new InvalidOperationException("本局未启用 P2P 直连。");
        Task<IceReply> answer;
        lock (gate)
        {
            Prune(now, message.InstanceId);
            slots.TryGetValue(player, out var slot);
            if (message.Action == "cancel")
            {
                if (slot?.Attempt == message.AttemptId)
                {
                    slot.Cancelled = true;
                    slot.Answer.TrySetResult(new IceReply { Status = "closed" });
                }
                return new IceReply { Status = "closed" };
            }
            if (message.Action == "offer")
            {
                ValidateDescription(message.Description);
                if (slot?.Attempt != message.AttemptId)
                {
                    if (slot != null && now - slot.Created < TimeSpan.FromSeconds(2))
                        throw new InvalidOperationException("正在结束上次 P2P 连接，请稍后重试。");
                    if (slot == null && slots.Count >= 64) throw new InvalidOperationException("P2P 协商数量已达上限。");
                    slot?.Answer.TrySetResult(new IceReply { Status = "closed" });
                    slot = new Slot { Player = player, Instance = message.InstanceId, Attempt = message.AttemptId,
                        Description = message.Description, PublicOnly = message.PublicOnly, Created = now, Expires = now.AddSeconds(45) };
                    slots[player] = slot;
                }
                else if (slot.Description != message.Description || slot.PublicOnly != message.PublicOnly)
                    throw new InvalidOperationException("请为新的 P2P 协商创建新的连接编号。");
            }
            else if (message.Action != "poll") throw new InvalidOperationException("未知 P2P 操作。");
            if (slot == null || slot.Attempt != message.AttemptId || slot.Cancelled)
                return new IceReply { Status = "closed", Error = "P2P 协商已结束，请重试。" };
            answer = slot.Answer.Task;
        }
        try { return await answer.WaitAsync(TimeSpan.FromSeconds(7), ct); }
        catch (TimeoutException) { return new IceReply { Status = "pending" }; }
    }
    public IceServerInbox Poll(IceServerPoll message)
    {
        var instance = lobbies.Current();
        if (instance?.Id != message.InstanceId || instance.Phase != "Ready" || instance.ConnectionPath != OnlineNetworkProtocol.P2P)
            throw new InvalidOperationException("对局实例已结束或未启用 P2P。");
        if (message.Answers == null || message.Answers.Length > 64) throw new InvalidOperationException("P2P 响应数量无效。");
        lock (gate)
        {
            Prune(DateTime.UtcNow, message.InstanceId);
            foreach (var reply in message.Answers)
            {
                if (!Guid.TryParse(reply.PlayerId, out var player) || !slots.TryGetValue(player, out var slot) ||
                    slot.Attempt != reply.AttemptId || slot.Cancelled) continue;
                if (!string.IsNullOrEmpty(reply.Error))
                    slot.Answer.TrySetResult(new IceReply { Status = "failed", Error = "服务器 P2P 协商失败，请重试或在下局选择 VPS 中转。" });
                else
                {
                    ValidateDescription(reply.Description);
                    slot.Answer.TrySetResult(new IceReply { Status = "ready", Description = reply.Description });
                }
            }
            return new IceServerInbox { Offers = slots.Values.Select(s => new IceOffer
            {
                PlayerId = s.Player.ToString(), AttemptId = s.Attempt, Description = s.Description,
                PublicOnly = s.PublicOnly, Cancelled = s.Cancelled || !instance.Seats.Any(x => x.Player == s.Player && !x.Returned && !x.Settled)
            }).ToArray() };
        }
    }
    private static void ValidateDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4095 || value.Any(c => c == 0 || c > 127) ||
            !value.Contains("a=ice-ufrag:", StringComparison.Ordinal) || !value.Contains("a=ice-pwd:", StringComparison.Ordinal))
            throw new InvalidOperationException("P2P 连接描述无效。");
    }
}
