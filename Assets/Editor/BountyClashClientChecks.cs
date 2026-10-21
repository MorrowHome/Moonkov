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
            Phase = ClashPhase.Active, Remaining = 900, Connected = true, AcknowledgedRequest = 3 };
        ClashSenseRpc Sense(uint sequence, bool active = true) => new ClashSenseRpc
        { MatchId = "test-match", Epoch = 1, Sequence = sequence, Active = active, Remaining = 4, ContactCount = active ? 1 : 0 };
        ClashSenseContactRpc Contact(uint sequence) => new ClashSenseContactRpc
        { MatchId = "test-match", Epoch = 1, Sequence = sequence, Index = 0, Count = 1,
            Position = new float3(4, 1, 20), UncertaintyRadius = 3, ValidFor = .35f };
        var data = new BountyClashClientState { Approved = true };
        data.Receive(Snapshot(1, 1), 10);
        data.Receive(Contact(1), 10); data.Receive(Sense(1), 10);
        Check(data.ContactVisible(0, 10.1), "Contact before sense header was lost");
        Check(!data.ContactVisible(0, 10.36), "Expired contact remained visible");
        data.Receive(Sense(2), 11); data.Receive(Contact(2), 11);
        Check(data.ContactVisible(0, 11.1), "Contact after sense header was lost");
        data.Receive(Sense(3, false), 11.2); data.Receive(Contact(2), 11.3);
        Check(!data.ContactVisible(0, 11.3), "Late contact revived a stopped boost");
        data.Receive(Sense(4), 12); data.Receive(Contact(4), 12); data.ClearContacts();
        Check(!data.ContactVisible(0, 12), "Release retained a cloud");
        data.Receive(Snapshot(1, 5, false), 13); data.Receive(Sense(5), 13); data.Receive(Contact(5), 13);
        Check(!data.ContactVisible(0, 13), "Non-carrier received a visible contact");
        data.Receive(Snapshot(1, 6), 14); data.Receive(Sense(6), 14);
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
