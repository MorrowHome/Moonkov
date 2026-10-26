using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

public static class RaidResultManifestChecks
{
    [MenuItem("Tools/Moonkov/Check Result Manifest")]
    public static string Run()
    {
        int count = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); count++; }
        var graph = InventoryGraph.Create(false, false);
        var box = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "secure_case", Parent = "equipment", Region = "SecureContainer" };
        graph.Items.Add(box);
        Check(graph.AddSupply("dust", 3, box.Id, true) == InventoryError.None, "Protected fixture");
        Check(graph.AddSupply("medkit", 2, "pockets") == InventoryError.None, "Unprotected fixture");
        var rifle = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "rifle", Parent = "equipment", Region = "Primary" };
        graph.Items.Add(rifle);
        graph.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "halo_capacitor", Parent = rifle.Id, Region = HaloModules.Slot(HaloModule.Capacitor) });
        Check(graph.Validate() == InventoryError.None, "Valid equipped weapon and module tree");
        string before = RaidInventoryTransport.Encode(graph);
        var success = RaidResultManifest.Capture(graph, 7, RaidPhase.Extracted);
        Check(success.Valid(7) && success.LostCount == 0 && success.ReturnedCount == 8, "Every carried item returns on extraction");
        Check(success.Items.Any(i => i.Code == "halo_capacitor" && i.Returned == 1), "Installed modules are included once");
        Check(RaidInventoryTransport.Encode(graph) == before, "Manifest capture never changes inventory");
        var mixed = graph.Clone(); mixed.AddSupply("dust", 1, "pockets");
        var mixedRow = RaidResultManifest.Capture(mixed, 7, RaidPhase.Dead).Items.Single(i => i.Code == "dust");
        Check(mixedRow.Returned == 3 && mixedRow.Lost == 1, "Same item type can be partly protected and partly lost");
        var failed = RaidResultManifest.Capture(graph, 7, RaidPhase.Dead);
        var protectedGraph = SecureInventory.FailureReturn(graph);
        Check(failed.ReturnedCount == 4 && failed.LostCount == 4, "Secure case and contents return; other equipment is lost");
        Check(failed.Items.All(r => r.Returned == protectedGraph.Count(r.Code)), "Manifest agrees with actual failure-return policy");
        var timedOut = RaidResultManifest.Capture(graph, 7, RaidPhase.TimedOut);
        Check(timedOut.ReturnedCount == failed.ReturnedCount && timedOut.LostCount == failed.LostCount, "Timeout applies the same protection policy");
        var corpse = LootInventoryExchange.DropOnDeath(graph);
        Check(failed.LostCount == 4 && corpse.Count("rifle") == 1 && graph.Count("rifle") == 0, "Final manifest survives real death ownership transfer");
        Check(success.ReturnedCount == 8, "Earlier frozen manifest does not follow mutable inventory");
        var openCorpse = LootInventoryExchange.Snapshot(graph, corpse);
        Check(RaidResultManifest.Capture(openCorpse, 7, RaidPhase.Extracted).ReturnedCount == 4, "An open loot window is not owned cargo");
        var stash = InventoryGraph.Create(true, false); stash.AddSupply("dust", 5, "stash");
        Check(RaidResultManifest.Capture(stash, 1, RaidPhase.Extracted).Items.Count == 0, "Storage items are never raid cargo");
        Check(!failed.Valid(8), "Another raid cannot use this manifest");
        var invalid = new RaidResultManifest { RaidId = 7, Outcome = RaidPhase.Active };
        Check(!invalid.Valid(7), "Active state cannot masquerade as a result");
        invalid.Outcome = RaidPhase.Extracted; invalid.Items.Add(new RaidResultItem { Code = "dust", Returned = -1 });
        Check(!invalid.Valid(7), "Negative quantity rejected");
        invalid.Items[0].Returned = 1; invalid.Items[0].Lost = 1;
        Check(!invalid.Valid(7), "Successful extraction cannot report a loss");
        invalid.Items[0].Lost = 0; invalid.Items.Add(new RaidResultItem { Code = "dust", Returned = 1 });
        Check(!invalid.Valid(7), "Duplicate rows rejected");

        var client = new RaidInventoryClientState();
        bool Receive(string json, int raid, uint sequence)
        {
            int parts = (json.Length + RaidInventoryTransport.ChunkCharacters - 1) / RaidInventoryTransport.ChunkCharacters;
            bool complete = false;
            for (int i = parts - 1; i >= 0; i--)
            {
                var chunk = new RaidInventoryChunkV2Rpc { RaidId = raid, Sequence = sequence, Count = parts, Index = i,
                    Json = json.Substring(i * RaidInventoryTransport.ChunkCharacters, Math.Min(RaidInventoryTransport.ChunkCharacters, json.Length - i * RaidInventoryTransport.ChunkCharacters)) };
                complete |= client.Receive(chunk, RaidInventoryTransport.Decode);
            }
            return complete;
        }
        string payload = RaidInventoryTransport.EncodeSnapshot(graph, -1, 0, result: failed);
        Check(Receive(payload, 7, 1) && client.Result?.LostCount == 4, "Out-of-order chunks publish final graph and manifest atomically");
        Check(!Receive(payload, 7, 1), "Duplicate sequence does not republish result");
        Check(Receive(RaidInventoryTransport.EncodeSnapshot(graph, -1, 0), 8, 2) && client.Result == null, "New raid clears previous result");
        Check(!Receive(payload, 7, 1) && client.Result == null, "Late old fragments cannot resurrect previous cargo");
        Check(Receive(RaidInventoryTransport.EncodeSnapshot(graph, -1, 0, result: invalid), 8, 3) && client.Result == null, "Invalid optional result does not discard valid inventory");
        Check(Receive(RaidInventoryTransport.Encode(graph), 9, 4) && client.Result == null, "Legacy graph payload remains supported");

        var host = new ScrollView(); var view = new Unity.MP_FPS.Client.RaidResultCargoView(host);
        view.Present(null); Check(host.childCount == 1, "Pending manifest has an explicit state");
        view.Present(failed);
        Check(host.Query<VisualElement>(className: "raid-cargo-row").ToList().Count == 5, "Failure lists protected and lost groups");
        var first = host.ElementAt(0); view.Present(failed);
        Check(ReferenceEquals(first, host.ElementAt(0)), "Unchanged result does not rebuild rows");
        view.Present(new RaidResultManifest { RaidId = 7, Outcome = RaidPhase.Extracted });
        Check(host.childCount == 1, "Empty cargo is distinct from syncing");
        host.Clear();
        foreach (string locale in new[] { "en", "zh-Hans" })
        {
            var table = AssetDatabase.LoadAssetAtPath<StringTable>($"Assets/Localization/Tables/MoonkovUI_{locale}.asset");
            Check(table.GetEntry("CARGO MANIFEST") != null && table.GetEntry("PROTECTED ITEMS") != null &&
                table.GetEntry("LOST ITEMS") != null, "Result groups localized: " + locale);
        }
        return $"PASS: {count} manifest, protection, chunk transport and view checks; no profiles or network peers.";
    }
}
