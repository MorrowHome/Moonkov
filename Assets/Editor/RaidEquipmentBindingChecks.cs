using System;
using System.Linq;
using System.IO;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class RaidEquipmentBindingChecks
{
    private sealed class Memory : IQuickSlotStore
    {
        public QuickSlotSave Load(string scope) => null;
        public void Save(string scope, QuickSlotSave value) { }
    }
    public static void Run(bool repaired = true)
    {
        var graph = InventoryGraph.Create(false); graph.AddSupply("medkit", 1);
        var pressed = graph.Items.First(i => i.Code == "medkit");
        var latest = graph.Clone();
        var bindings = new QuickSlotBindings(new Memory()); bindings.UseProfile("isolated-binding-check");
        bool accepted = bindings.Bind(8, pressed, latest);
        if (!repaired)
        {
            if (accepted) throw new InvalidOperationException("Expected old object-identity rejection was not reproduced.");
            File.WriteAllText("Temp/raid-binding-baseline.txt", "REPRODUCED: unchanged item from previous snapshot rejected; same ID, same code, same version."); return;
        }
        if (!accepted || bindings[8] != pressed.Id || !bindings.Available(8, latest))
            throw new InvalidOperationException("Current snapshot did not resolve the dragged item identity.");
        var removed = latest.Clone(); removed.Items.RemoveAll(i => i.Id == pressed.Id);
        if (bindings.Bind(7, pressed, removed)) throw new InvalidOperationException("Removed item was bound.");
        var replaced = latest.Clone(); replaced.Find(pressed.Id).Code = "dust";
        if (bindings.Bind(7, pressed, replaced)) throw new InvalidOperationException("Changed item kind was bound.");
        if (bindings.Bind(7, latest.Equipped("Backpack"), latest)) throw new InvalidOperationException("Non-usable gear was bound as a consumable.");
        if (latest.Version != graph.Version || latest.Count("medkit") != graph.Count("medkit"))
            throw new InvalidOperationException("Binding mutated inventory.");
        File.WriteAllText("Temp/raid-binding-check.txt", "PASS: refreshed identity, removed/changed/unsupported rejection, no inventory mutation.");
    }
}
