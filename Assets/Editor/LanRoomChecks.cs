using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

// Data/protocol checks only. Does not create a Netcode World or a gameplay client.
public static class LanRoomChecks
{
    public static async Task<string> Run(string parent)
    {
        string directory = Path.Combine(Path.GetFullPath(parent), "lan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        { try { action(); } catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException || ex is ArgumentException) { checks++; return; } throw new Exception("Accepted: " + name); }
        string hostCredential = new string('a', 64), guestCredential = new string('b', 64);
        try
        {
            using (var frame = new MemoryStream())
            {
                await LanProtocol.WriteAsync(frame, new LanRequest { Action = "join", Name = "猫娘干员" }, CancellationToken.None).ConfigureAwait(false);
                frame.Position = 0;
                Check((await LanProtocol.ReadAsync<LanRequest>(frame, CancellationToken.None).ConfigureAwait(false)).Name == "猫娘干员", "UTF-8 frame round trip");
            }
            using (var malformed = new MemoryStream(new byte[] { 127, 255, 255, 255 }))
            {
                try { await LanProtocol.ReadAsync<LanRequest>(malformed, CancellationToken.None).ConfigureAwait(false); throw new Exception("Oversized frame accepted"); }
                catch (InvalidDataException) { checks++; }
            }
            foreach (var mode in new[] { RaidGameMode.Expedition, RaidGameMode.BountyClash })
            {
                using var backend = new LanPersistence(Path.Combine(directory, mode.ToString()));
                await backend.RecoverAsync().ConfigureAwait(false);
                using var server = new LanRoomServer(backend, "LAN test", mode, 2, 0, 7979, false);
                var host = server.AddHost(hostCredential, "Host");
                var guest = server.Handle(new LanRequest { Action = "join", Credential = guestCredential, Name = "Guest" });
                LanRequest Request(string action, bool hostPlayer = false, bool ready = false) => new LanRequest
                { Action = action, Credential = hostPlayer ? hostCredential : guestCredential, RoomId = host.Room.Id, Ready = ready };
                Check(guest.Room.Mode == mode && guest.Room.Members.Length == 2 && host.MemberId != guest.MemberId, mode + " roster and distinct profiles");
                Reject(() => server.Handle(new LanRequest { Action = "join", Credential = new string('c', 64) }), "full room");
                Reject(() => server.Handle(new LanRequest { Action = "poll", Protocol = 999 }), "version mismatch");
                Reject(() => server.Handle(Request("start", true)), "unready start");
                Reject(() => server.Handle(Request("start")), "guest start");
                server.Handle(Request("ready", ready: true));
                Reject(() => server.Handle(Request("trade")), "ready loadout mutation");
                var start = server.Handle(Request("start", true));
                Check(start.Room.Phase == LanRoomPhase.Preparing, "host scene loading barrier");
                var ticket = server.Handle(Request("poll")).Ticket;
                Check(backend.Request("internal/sessions/resolve", new { Token = ticket }).PlayerId == guest.MemberId, "room ticket owns correct host profile");
                Reject(() => backend.Request("internal/sessions/resolve", new { Token = guestCredential }), "persistent credential cannot bypass room admission");
                Reject(() => server.Handle(new LanRequest { Action = "join", Credential = new string('c', 64) }), "late join");
                Reject(() => server.Handle(Request("listening")), "guest launch barrier");
                Check(server.Handle(Request("listening", true)).Room.Phase == LanRoomPhase.Playing, "host releases waiting clients");
                Check(server.Handle(Request("returned", true)).Room.Phase == LanRoomPhase.Returning, "host return drains round");
                Check(server.Handle(Request("returned")).Room.Phase == LanRoomPhase.Lobby, "next round lobby");
                Reject(() => backend.Request("internal/sessions/resolve", new { Token = ticket }), "old round ticket revoked");
                Check(server.Handle(Request("poll")).Room.Members.All(m => !m.Ready && !m.InGame), "next round resets readiness");
                server.Handle(Request("leave"));
                Check(server.Handle(Request("poll", true)).Room.Members.Length == 1, "guest leaves room");
            }
            string stash = Path.Combine(directory, "multiplayer-stash");
            RaidPersistenceContext.Settlement[] receipts;
            string[] players;
            int[] dust;
            using (var backend = new LanPersistence(stash))
            {
                players = new[] { backend.OpenPlayer(hostCredential, "A"), backend.OpenPlayer(guestCredential, "B"), backend.OpenPlayer(new string('c', 64), "Enemy") };
                dust = players.Select(p => backend.Read(p).Profile.Dust).ToArray();
                string match = Guid.NewGuid().ToString("D"), token = Guid.NewGuid().ToString("D");
                var roster = players.Select((p, i) => new RaidPersistenceContext.BountyParticipant { PlayerId = p, DeploymentId = Guid.NewGuid().ToString("D"), TeamId = i < 2 ? 0 : 1 }).ToArray();
                backend.RegisterBounty(match, 1, token, roster); backend.RegisterBounty(match, 1, token, roster);
                var deployments = roster.Select(p => new RaidPersistenceContext.Deployment { PlayerId = p.PlayerId, DeploymentId = p.DeploymentId, Mode = "BountyClash", MatchId = match, RulesVersion = 1, TeamId = p.TeamId }).ToArray();
                var deployed = deployments.Select(p => backend.Request("internal/bounty-clash/deployments", p)).ToArray();
                Check(players.All(p => backend.Read(p).RaidActive), "all human profiles debit their equipment");
                Reject(() => backend.Mutate(players[0], "trade", new Newtonsoft.Json.Linq.JObject()), "in-raid inventory locked");
                var award = new RaidPersistenceContext.BountyRedemption { MatchId = match, TokenId = token, ExtractionEventId = Guid.NewGuid().ToString("D"),
                    CarrierDeploymentId = roster[0].DeploymentId, ExtractedDeploymentIds = roster.Take(2).Select(p => p.DeploymentId).ToArray() };
                backend.RedeemBounty(award); backend.RedeemBounty(award);
                var wrong = new RaidPersistenceContext.BountyRedemption { MatchId = match, TokenId = token, ExtractionEventId = Guid.NewGuid().ToString("D"), CarrierDeploymentId = roster[0].DeploymentId,
                    ExtractedDeploymentIds = new[] { roster[0].DeploymentId, roster[2].DeploymentId } };
                Reject(() => backend.RedeemBounty(wrong), "cross-team bounty");
                receipts = deployed.Select((p, i) =>
                {
                    var graph = RaidInventoryTransport.Decode(p.RaidInventoryJson);
                    return new RaidPersistenceContext.Settlement { PlayerId = p.PlayerId, DeploymentId = p.DeploymentId, SettlementId = p.DeploymentId, Mode = p.Mode,
                        MatchId = p.MatchId, RulesVersion = 1, TeamId = p.TeamId, Outcome = i < 2 ? "Extracted" : "Dead",
                        InventoryJson = i < 2 ? RaidInventoryTransport.Encode(graph) : null,
                        Dust = graph.Count("dust", true), Alloy = graph.Count("alloy", true), Cells = graph.Count("cells", true),
                        Redemption = i < 2 ? award : null, BountyEventId = i < 2 ? award.ExtractionEventId : null };
                }).ToArray();
                foreach (var receipt in receipts) backend.Request("internal/bounty-clash/settlements", receipt);
                for (int i = 0; i < 3; i++) Check(backend.Read(players[i]).Profile.Dust == dust[i] + (i < 2 ? BountyClashRules.Reward(2) : 0), "team-size reward " + i);
                Check(players.All(p => !backend.Read(p).RaidActive), "all profiles release raid ownership");
            }
            using (var restarted = new LanPersistence(stash))
            {
                await restarted.RecoverAsync().ConfigureAwait(false);
                Check(restarted.OpenPlayer(hostCredential, "A renamed") == players[0], "same client identity survives host restart");
                foreach (var receipt in receipts) restarted.Request("internal/bounty-clash/settlements", receipt);
                Check(restarted.Read(players[0]).Profile.Dust == dust[0] + BountyClashRules.Reward(2), "settlement retry after restart cannot duplicate bounty");
            }
            string crashRoot = Path.Combine(directory, "crash-recovery"), crashPlayer;
            int crashDust;
            using (var backend = new LanPersistence(crashRoot))
            {
                crashPlayer = backend.OpenPlayer(hostCredential, "Recovery"); crashDust = backend.Read(crashPlayer).Profile.Dust;
                string match = Guid.NewGuid().ToString("D"), token = Guid.NewGuid().ToString("D"), deployment = Guid.NewGuid().ToString("D");
                backend.RegisterBounty(match, 1, token, new[] { new RaidPersistenceContext.BountyParticipant { PlayerId = crashPlayer, DeploymentId = deployment, TeamId = 0 } });
                var live = backend.Request("internal/bounty-clash/deployments", new RaidPersistenceContext.Deployment
                { PlayerId = crashPlayer, DeploymentId = deployment, Mode = "BountyClash", MatchId = match, RulesVersion = 1, TeamId = 0 });
                var award = new RaidPersistenceContext.BountyRedemption { MatchId = match, TokenId = token, ExtractionEventId = Guid.NewGuid().ToString("D"),
                    CarrierDeploymentId = deployment, ExtractedDeploymentIds = new[] { deployment } };
                var receipt = new RaidPersistenceContext.Settlement { PlayerId = crashPlayer, DeploymentId = deployment, SettlementId = deployment,
                    Mode = "BountyClash", MatchId = match, RulesVersion = 1, Outcome = "Extracted", InventoryJson = live.RaidInventoryJson,
                    Redemption = award, BountyEventId = award.ExtractionEventId };
                // Crash after the authoritative extraction snapshot, before any recipient is credited.
                File.WriteAllText(Path.Combine(backend.OutboxDirectory, Guid.Parse(match).ToString("N") + ".bounty"),
                    Newtonsoft.Json.JsonConvert.SerializeObject(new { Redemption = award, Settlements = new[] { receipt } }));
            }
            using (var recovered = new LanPersistence(crashRoot))
            {
                await recovered.RecoverAsync().ConfigureAwait(false);
                Check(recovered.Read(crashPlayer).Profile.Dust == crashDust + BountyClashRules.Reward(1), "crash outbox replays bounty before abandoned-raid recovery");
                Check(!recovered.Read(crashPlayer).RaidActive && Directory.GetFiles(recovered.OutboxDirectory).Length == 0, "crash recovery releases kit and clears acknowledged journals");
                var deploy = new RaidPersistenceContext.Deployment { PlayerId = crashPlayer, DeploymentId = Guid.NewGuid().ToString("D") };
                recovered.Request("internal/deployments", deploy);
                Check(recovered.Read(crashPlayer).RaidActive, "expedition owns its LAN profile kit");
            }
            using (var abandoned = new LanPersistence(crashRoot))
            {
                await abandoned.RecoverAsync().ConfigureAwait(false);
                Check(!abandoned.Read(crashPlayer).RaidActive, "unfinished expedition is recovered as lost after host restart");
            }
            return "LAN room/protocol/persistence: " + checks + " checks passed; no gameplay clients launched.";
        }
        finally { Directory.Delete(directory, true); }
    }
}
