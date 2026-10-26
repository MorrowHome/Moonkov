using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

public static class DepartureEquipmentChecks
{
    [MenuItem("Tools/Moonkov/Check Departure Equipment")]
    public static string Run()
    {
        int count = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); count++; }
        InventoryItem Add(InventoryGraph graph, string code, string parent, string region)
        {
            var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code, Parent = parent, Region = region };
            graph.Items.Add(item); return item;
        }
        var loading = DepartureEquipmentCheck.Read(null);
        Check(loading.Issue == DepartureEquipmentIssue.Loading && !loading.EmergencyAvailable, "Missing inventory is loading, not eligibility");
        var graph = InventoryGraph.Create(true, false);
        var empty = DepartureEquipmentCheck.Read(graph);
        Check(empty.Issue == DepartureEquipmentIssue.Halo && empty.EmergencyAvailable, "Empty account points to existing emergency supply");
        string before = RaidInventoryTransport.Encode(graph);
        var command = new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = graph.Version, Operation = ShopOperation.Emergency };
        Check(ShopRules.Apply(graph, command, out var supplied) == null, "Existing emergency transaction succeeds");
        Check(RaidInventoryTransport.Encode(graph) == before, "Preview and transaction preparation do not mutate the source");
        Check(supplied.Equipped("Pistol")?.LoadedAmmo == 6 && supplied.Count("cells", true) == 3, "Existing kit equips revolver and carries cells");
        Check(supplied.Items.Where(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root).All(i => i.EmergencySupply), "Existing emergency sale protection retained");
        Check(DepartureEquipmentCheck.Read(supplied).Issue == DepartureEquipmentIssue.Medicine &&
            !DepartureEquipmentCheck.Read(supplied).EmergencyAvailable, "After reclaim, advice moves to medical preparation");
        command.ExpectedVersion = supplied.Version;
        Check(ShopRules.Apply(supplied, command, out _) == "shop_has_weapon", "New entry does not bypass duplicate-kit eligibility");
        supplied.AddSupply("medkit", 1, "pockets");
        Check(DepartureEquipmentCheck.Read(supplied).Issue == DepartureEquipmentIssue.None, "Equipped halo and accessible medicine pass");
        supplied.Equipped("Pistol").LoadedAmmo = 0;
        Check(DepartureEquipmentCheck.Read(supplied).Copy == "Halo equipped. Medicine accessible.", "Equipment check does not promise ammunition readiness");

        var stored = InventoryGraph.Create(true, false); stored.AddSupply("rifle", 1, "stash");
        Check(DepartureEquipmentCheck.Read(stored).Issue == DepartureEquipmentIssue.Halo &&
            !DepartureEquipmentCheck.Read(stored).EmergencyAvailable, "Stored weapon routes to equipment instead of a free kit");
        var nested = InventoryGraph.Create(true, false);
        var pack = Add(nested, "small_pack", "stash", "main");
        Add(nested, "pistol", pack.Id, "main");
        Check(nested.Validate() == InventoryError.None && !DepartureEquipmentCheck.Read(nested).EmergencyAvailable,
            "Weapon in a stored container still blocks emergency eligibility");
        Check(DepartureEquipmentCheck.Read(InventoryGraph.Create(false, false)).EmergencyAvailable == false,
            "In-raid graph cannot qualify for base supply");
        foreach (string medical in new[] { "medkit", "med_gel", "trauma_kit" })
        {
            var kit = InventoryGraph.Create(true, false); Add(kit, "pistol", "equipment", "Pistol");
            kit.AddSupply(medical, 1, "stash");
            Check(DepartureEquipmentCheck.Read(kit).Issue == DepartureEquipmentIssue.Medicine, "Stored medicine is not accessible: " + medical);
            var rig = Add(kit, "rig", "equipment", "ChestRig");
            var item = kit.Items.Single(i => i.Code == medical);
            if (!kit.FindSpace(item, rig.Id, out var region, out int x, out int y)) throw new InvalidOperationException("No fixture rig space");
            item.Parent = rig.Id; item.Region = region; item.X = x; item.Y = y;
            Check(kit.Validate() == InventoryError.None && DepartureEquipmentCheck.Read(kit).Issue == DepartureEquipmentIssue.None,
                "Equipped rig medicine is accessible: " + medical);
            if (!kit.FindSpace(rig, "stash", out region, out x, out y)) throw new InvalidOperationException("No fixture stash space");
            rig.Parent = "stash"; rig.Region = region; rig.X = x; rig.Y = y;
            Check(kit.Validate() == InventoryError.None && DepartureEquipmentCheck.Read(kit).Issue == DepartureEquipmentIssue.Medicine,
                "Unequipped rig is not accessible: " + medical);
        }
        var backpackOnly = InventoryGraph.Create(true, false); Add(backpackOnly, "pistol", "equipment", "Pistol");
        var bag = Add(backpackOnly, "backpack", "equipment", "Backpack"); Add(backpackOnly, "medkit", bag.Id, "main");
        Check(DepartureEquipmentCheck.Read(backpackOnly).Issue == DepartureEquipmentIssue.Medicine, "Backpack medicine remains unavailable for immediate treatment");
        string unchanged = RaidInventoryTransport.Encode(backpackOnly); DepartureEquipmentCheck.Read(backpackOnly);
        Check(unchanged == RaidInventoryTransport.Encode(backpackOnly), "Readiness never reorders or moves supplies");
        var layout = Resources.Load<VisualTreeAsset>("Moonkov/UI/OperationReady").CloneTree();
        Check(layout.Q<Button>("readyEquipmentAction") != null && layout.Q<Label>("readyEquipmentNote") != null,
            "Native readiness bindings exist");
        foreach (string locale in new[] { "en", "zh-Hans" })
        {
            var table = AssetDatabase.LoadAssetAtPath<StringTable>($"Assets/Localization/Tables/MoonkovUI_{locale}.asset");
            foreach (var state in new[] { loading, empty, DepartureEquipmentCheck.Read(stored), DepartureEquipmentCheck.Read(backpackOnly), DepartureEquipmentCheck.Read(supplied) })
                Check(table.GetEntry(state.Copy) != null && table.GetEntry(state.Action) != null, "Localized readiness: " + locale + "/" + state.Issue);
        }
        return $"PASS: {count} departure equipment and emergency-rule checks; isolated inventories only.";
    }
}
