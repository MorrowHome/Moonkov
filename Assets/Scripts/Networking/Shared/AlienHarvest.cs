#nullable disable
using System;

namespace Unity.MP_FPS.Inventory
{
    public static class AlienHarvest
    {
        public const float DissectionSeconds = 6;
        public const int NeuralOrganChancePercent = 12;

        public static InventoryGraph Create(bool boss, Random random,string species=null)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var graph = InventoryGraph.Create(false, false);
            graph.LootRows=24;graph.Items.Add(new InventoryItem {Id=LootInventoryExchange.Root,Code=LootInventoryExchange.Root});
            Add(graph, "alien_tissue", boss ? 5 : 1);
            Add(graph, "alien_raw_meat", boss ? 4 : random.Next(1, 3));
            if (boss) Add(graph, "alien_core", 1);
            if (boss || random.Next(100) < NeuralOrganChancePercent) Add(graph, "alien_neural_core", 1);
            if(species!=null)
            {
                var organ=AlienBiology.Species(species) ?? throw new ArgumentException("Unknown alien species: "+species,nameof(species));
                Add(graph,organ.Code,1);
            }
            return graph;
        }

        private static void Add(InventoryGraph graph, string code, int quantity)
        {
            if (graph.AddSupply(code, quantity,LootInventoryExchange.Root,foundInRaid: true) != InventoryError.None)
                throw new InvalidOperationException("Alien harvest does not fit: " + code);
        }
    }
}
