using System.Text.Json;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Online;

int checks = 0;
void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; }
var graph = InventoryGraph.Create(false, false);
InventoryItem Equip(string id, string code, string slot)
{
    var item = new InventoryItem { Id = id, Code = code, Parent = InventoryCatalog.Equipment, Region = slot };
    graph.Items.Add(item); return item;
}
var rifle = Equip("private-weapon-id", "rifle", "Primary");
Equip("private-secondary-id", "shotgun", "Secondary");
Equip("private-head-id", "energy_head_dome", "Helmet");
Equip("private-bag-id", "cargo_pack", "Backpack");
Equip("private-badge-id", "field_rig", "ChestRig");
Equip("private-melee-id", "energy_sword", "Melee");
graph.Items.Add(new InventoryItem { Id = "private-module-id", Code = "halo_pulse_controller", Parent = rifle.Id, Region = "Shaping" });
graph.Items.Add(new InventoryItem { Id = "private-content-id", Code = "water", Parent = "private-bag-id", Region = "main" });
string original = JsonSerializer.Serialize(graph, new JsonSerializerOptions { IncludeFields = true });
var appearance = LobbyAppearanceInventory.FromInventory(graph, 4, 2);
Check(appearance.CharacterIndex == 4 && appearance.PoseIndex == 2, "Character/pose selector lost");
Check(appearance.Weapon == "rifle" && appearance.Modules != 0, "Equipped halo/module not projected");
Check(appearance.Headgear == "energy_head_dome" && appearance.Backpack == "cargo_pack" && appearance.Melee == "energy_sword", "Visible gear missing");
Check(appearance.ChestBadge == "field_rig", "Equipped badge missing from public appearance");
string json = JsonSerializer.Serialize(appearance, new JsonSerializerOptions { IncludeFields = true });
Check(!json.Contains("private-") && !json.Contains("water") && !json.Contains("shotgun") && !json.Contains("LoadedAmmo"), "Private inventory leaked into roster");
var preview = LobbyAppearanceInventory.PreviewInventory(appearance);
Check(preview.Equipped("Primary")?.Code == "rifle" && preview.Equipped("Helmet")?.Code == "energy_head_dome", "Preview did not reconstruct visible equipment");
Check(preview.Equipped("ChestRig")?.Code == "field_rig", "Squad portrait lost the badge");
var roundtrip = JsonSerializer.Deserialize<LobbyAppearance>(json, new JsonSerializerOptions { IncludeFields = true });
Check(roundtrip?.ChestBadge == "field_rig" && roundtrip.SameAs(appearance), "Badge did not survive JSON round-trip");
var legacy = JsonSerializer.Deserialize<LobbyAppearance>("{\"CharacterIndex\":4,\"Backpack\":\"cargo_pack\"}", new JsonSerializerOptions { IncludeFields = true });
Check(legacy?.ChestBadge == "" && LobbyAppearanceInventory.PreviewInventory(legacy).Equipped("ChestRig") == null, "Legacy roster invented a badge");
var changedBadge = appearance.Copy(); changedBadge.ChestBadge = "rig";
Check(!changedBadge.SameAs(appearance) && appearance.ChestBadge == "field_rig", "Badge changes ignored or copied by reference");
Check((ushort)Unity.MP_FPS.Inventory.HaloModules.Mask(preview, preview.Equipped("Primary")) == appearance.Modules, "Module projection did not round-trip");
Check(original == JsonSerializer.Serialize(graph, new JsonSerializerOptions { IncludeFields = true }), "Projection modified account inventory");
var sanitized = LobbyAppearanceInventory.FromInventory(graph, int.MaxValue, int.MinValue);
Check(sanitized.CharacterIndex == 2 && sanitized.PoseIndex == -1, "Invalid selectors reached renderer");
var forged = LobbyAppearanceInventory.PreviewInventory(new LobbyAppearance { Weapon = "water", Headgear = "rifle", ChestBadge = "rifle", Backpack = "dust", Melee = "helmet" });
Check(forged.Items.All(i => i.Parent != InventoryCatalog.Equipment), "Invalid gear kind reached preview");
var lobby = new OnlineLobbies();
var id = Guid.NewGuid(); var now = DateTime.UtcNow;
lobby.Act(id, "A", false, new LobbyAction { Action = "create" }, now);
lobby.UpdateAppearance(id, appearance);
lobby.Act(id, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
lobby.UpdateAppearance(id, appearance.Copy());
LobbySnapshot Read() => lobby.Snapshot(id, now, "host", "host", 7979);
Check(Read().Members[0].Ready, "Unchanged snapshot cancelled ready");
appearance.ChestBadge = "rig"; lobby.UpdateAppearance(id, appearance);
Check(!Read().Members[0].Ready && Read().Members[0].Appearance.ChestBadge == "rig", "Badge switch kept stale readiness or appearance");
lobby.Act(id, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
var copy = Read(); copy.Members[0].Appearance.Weapon = "pistol";
Check(Read().Members[0].Appearance.Weapon == "rifle", "Snapshot mutated authoritative appearance");
appearance.PoseIndex = 1; lobby.UpdateAppearance(id, appearance);
Check(!Read().Members[0].Ready, "Changed pose kept ready");
lobby.Act(id, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
lobby.Act(id, "A", false, new LobbyAction { Action = "start" }, now);
appearance.CharacterIndex = 3; lobby.UpdateAppearance(id, appearance);
Check(Read().Phase == "Lobby" && lobby.Allocate(now) == null, "Changed loadout kept a queued allocation");
lobby.Act(id, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
lobby.Act(id, "A", false, new LobbyAction { Action = "start" }, now);
lobby.Allocate(now); appearance.CharacterIndex = 2; lobby.UpdateAppearance(id, appearance);
Check(Read().Members[0].Appearance.CharacterIndex == 3, "Active raid appearance was changed by lobby poll");
string stateFile = Path.Combine(Path.GetTempPath(), "moon-appearance-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var disk = new OnlineLobbies(stateFile);
    disk.Act(id, "A", false, new LobbyAction { Action = "create" }, now);
    disk.UpdateAppearance(id, appearance);
    var restored = new OnlineLobbies(stateFile).Snapshot(id, now, "host", "host", 7979);
    Check(restored.Members[0].Appearance.SameAs(appearance), "Restart lost public appearance fields");
}
finally { if (File.Exists(stateFile)) File.Delete(stateFile); }
Console.WriteLine($"Online public appearance: {checks} checks passed; no account or network client used.");
