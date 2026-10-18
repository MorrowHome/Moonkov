using System;

namespace Unity.MP_FPS.Inventory
{
    public static class AlienHarvest
    {
        public const float DissectionSeconds = 6;
        public const int NeuralOrganChancePercent = 12;

        public static InventoryGraph Create(bool boss, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var graph = InventoryGraph.Create(false, false);
            Add(graph, "alien_tissue", boss ? 5 : 1);
            Add(graph, "alien_raw_meat", boss ? 4 : random.Next(1, 3));
            if (boss) Add(graph, "alien_core", 1);
            if (boss || random.Next(100) < NeuralOrganChancePercent) Add(graph, "alien_neural_core", 1);
            return graph;
        }

        private static void Add(InventoryGraph graph, string code, int quantity)
        {
            if (graph.AddSupply(code, quantity, foundInRaid: true) != InventoryError.None)
                throw new InvalidOperationException("Alien harvest does not fit: " + code);
        }
    }
}
