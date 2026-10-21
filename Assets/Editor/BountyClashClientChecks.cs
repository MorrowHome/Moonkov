using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using UnityEngine.UIElements;

// Ordering/authorization tests for the real client cache, without a live account or Prefs.
public static class BountyClashClientChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        ClashSnapshotRpc Snapshot(int epoch, uint sequence, bool carrier = true) => new ClashSnapshotRpc
        { MatchId = "test-match", Epoch = epoch, Sequence = sequence, IsCarrier = carrier, Life = ClashLife.Alive,
            Phase = ClashPhase.Active, Remaining = 900, SenseRemaining = 4, Connected = true, AcknowledgedRequest = 3 };
        ClashSenseRpc Sense(uint sequence, bool active = true) => new ClashSenseRpc
        { MatchId = "test-match", Epoch = 1, Sequence = sequence, Active = active, Remaining = 4, Presence = true, SectorMask = active ? 1u : 0u, ContactCount = active ? 1 : 0 };
        ClashSenseContactRpc Contact(uint sequence) => new ClashSenseContactRpc
        { MatchId = "test-match", Epoch = 1, Sequence = sequence, Index = 0, Count = 1,
            Position = new float3(4, 1, 20), UncertaintyRadius = 3, ValidFor = .35f };
        var data = new BountyClashClientState { Approved = true };
        data.Receive(Snapshot(1, 1), 10); data.SetSenseInput(true, true);
        data.Receive(Contact(1), 10); data.Receive(Sense(1), 10);
        Check(data.ContactVisible(0, 10.1), "Contact before sense header was lost");
        Check(!data.ContactVisible(0, 10.36), "Expired contact remained visible");
        data.Receive(Sense(2), 11); data.Receive(Contact(2), 11);
        Check(data.ContactVisible(0, 11.1), "Contact after sense header was lost");
        data.Receive(Sense(3, false), 11.2); data.Receive(Contact(2), 11.3);
        Check(!data.ContactVisible(0, 11.3), "Late contact revived a stopped boost");
        data.Receive(Sense(4), 12); data.Receive(Contact(4), 12); data.StopBoost();
        Check(!data.ContactVisible(0, 12), "Release retained a cloud");
        data.Receive(Snapshot(1, 5, false), 13); data.Receive(Sense(5), 13); data.Receive(Contact(5), 13);
        Check(!data.ContactVisible(0, 13), "Non-carrier received a visible contact");
        data.Receive(Snapshot(1, 6), 14); data.SetSenseInput(true, true); data.Receive(Sense(6), 14);
        var invalid = Contact(6); invalid.Position.x = float.NaN; data.Receive(invalid, 14);
        Check(!data.ContactVisible(0, 14), "Non-finite contact accepted");
        data.Receive(Contact(6), 14); data.Receive(Snapshot(2, 7), 14.1);
        Check(!data.ContactVisible(0, 14.1), "Epoch change retained cloud");
        data.Receive(Snapshot(1, 100), 14.2);
        Check(data.Snapshot.Epoch == 2, "Late old epoch replaced current match");
        for (int i = 1; i <= 12; i++) data.Receive(new ClashParticipantRpc { MatchId = "test-match", Epoch = 2, Sequence = 8, ParticipantId = i });
        data.Receive(Snapshot(2, 8), 15);
        Check(data.Participants.Count == 12, "Full roster was truncated");
        data.Receive(new ClashParticipantRpc { MatchId = "test-match", Epoch = 3, Sequence = 9, ParticipantId = 40 });
        data.Receive(Snapshot(3, 9), 16);
        Check(data.Participants.Count == 1 && data.Participants.ContainsKey(40), "Old full roster blocked a new epoch participant");
        data.Receive(new ClashParticipantRpc { MatchId = "test-match", Epoch = 2, Sequence = 100, ParticipantId = 41 });
        Check(data.Participants.Count == 1, "Late roster crossed epoch");
        data.PendingRequest = 4; data.RequestedAt = 16;
        Check(data.ActionPending(17) && !data.ActionPending(21.1), "Pending action did not time out");
        var acknowledged = Snapshot(3, 10); acknowledged.AcknowledgedRequest = 4; acknowledged.Result = ClashResult.Blocked;
        data.Receive(acknowledged, 17);
        Check(data.PendingRequest == 0 && data.LastFailure == ClashResult.Blocked, "Acknowledgement or failure feedback lost");
        data.Receive(Snapshot(3, 11), 17.2);
        Check(data.LastFailure == ClashResult.Blocked, "Heartbeat erased action failure before presentation");
        // Local stops invalidate old headers as well as payload arrays. No World/network.
        var stopped = new BountyClashClientState { Approved = true };
        stopped.Receive(Snapshot(1, 20), 30); stopped.SetSenseInput(true, true);
        stopped.Receive(Sense(20), 30); stopped.Receive(Contact(20), 30);
        Check(stopped.ContactVisible(0, 30.1), "Stop regression fixture never acquired a contact");
        stopped.SetSenseInput(true, false);
        Check(stopped.Sense.Presence && !stopped.Sense.Active && stopped.Sense.SectorMask == 0 && stopped.Sense.ContactCount == 0,
            "Releasing boost erased base Presence or retained directional output");
        stopped.Receive(Contact(20), 30.11); stopped.Receive(Sense(20), 30.11);
        Check(!stopped.ContactVisible(0, 30.12) && !stopped.Sense.Active, "Same-sequence late packet revived stopped boost");
        stopped.SetSenseInput(true, false); stopped.SetSenseInput(true, false);
        stopped.Receive(Sense(21, false), 30.13);
        Check(stopped.Sense.Presence, "Base view lost legitimate fresh Presence after boost stopped");
        stopped.SetSenseInput(true, true); stopped.Receive(Sense(21), 30.14); stopped.Receive(Contact(21), 30.14);
        Check(!stopped.SenseVisible(30.15), "Re-entering boost reused a header received while stopped");
        stopped.Receive(Contact(22), 30.16); stopped.Receive(Sense(22), 30.16);
        Check(stopped.ContactVisible(0, 30.17), "Repeated stopped frames blocked a new authorized header");
        stopped.SetSenseInput(false, false);
        Check(!stopped.Sense.Presence && !stopped.Sense.Active && stopped.Sense.SectorMask == 0, "Leaving view/menu retained Presence");
        stopped.Receive(Sense(22), 30.18); stopped.Receive(Contact(22), 30.18);
        stopped.Receive(Sense(23), 30.19); stopped.Receive(Contact(23), 30.19);
        Check(!stopped.Sense.Presence && !stopped.Sense.Active, "Header arriving while view was off restored output");
        stopped.SetSenseInput(true, true); stopped.Receive(Sense(23), 30.20); stopped.Receive(Contact(23), 30.20);
        Check(!stopped.SenseVisible(30.21) && !stopped.Sense.Presence, "Reopening view reused stopped-period packets");
        stopped.Receive(Sense(24), 30.22); stopped.Receive(Contact(24), 30.22);
        Check(stopped.ContactVisible(0, 30.23) && stopped.Sense.Presence, "Fresh post-stop authorization did not restore view");
        var exhausted = Snapshot(1, 25); exhausted.SenseRemaining = 0; stopped.Receive(exhausted, 30.24);
        Check(!stopped.SenseVisible(30.25) && stopped.Sense.SectorMask == 0, "Budget exhaustion retained boost output");
        stopped.SetSenseInput(true, false); var instinct = Sense(25, false); instinct.Remaining = 0; stopped.Receive(instinct, 30.25);
        Check(stopped.Sense.Presence, "Exhausted boost removed legal base-view Instinct");
        var dead = Snapshot(1, 26); dead.Life = ClashLife.Downed; stopped.Receive(dead, 30.26);
        Check(!stopped.Sense.Presence && !stopped.SenseVisible(30.27), "Downed state retained dark-sight output");
        stopped.Receive(Snapshot(2, 1), 30.28); stopped.SetSenseInput(true, true);
        stopped.Receive(Sense(100), 30.29); stopped.Receive(Contact(100), 30.29);
        Check(!stopped.SenseVisible(30.30) && !stopped.Sense.Presence, "Previous match packet crossed stop/epoch boundary");
        var root = new VisualElement();
        using (var view = new BountyClashView(root, (_, _, _) => { }, () => { }))
        {
            view.Present(data, default, false, null, false, true);
            Check(root.Q("clashLobby") != null && root.Q("clashMap") != null, "Native HUD bindings missing");
        }
        Check(root.childCount == 0, "HUD disposal left overlay attached");
        return "PASS " + checks + " real client ordering, contact authorization/expiry and native binding checks";
    }
}
