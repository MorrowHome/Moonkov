using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class PmcFurnitureLootChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static InventoryGraph EmptyCache()
    {
        var graph = InventoryGraph.Create(false, false);
        graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
        return graph;
    }

    public static string Run()
    {
        s_Checks = 0;
        var caches = new RaidLootContainers(); caches.Initialize(1);
        var fixedPositions = new[] { new Vector3(8, 0, 4) };
        int first = RaidFurnitureLoot.FirstId;
        for (int i = 0; i < 24; i++)
        {
            caches.FurnitureCaches.Add(first + i, new float3(i + 1, 0, 0));
            caches.Containers.Add(first + i, EmptyCache());
        }
        Check(PmcLootRules.TryPosition(fixedPositions, null, 0, out var fixedPoint) && fixedPoint == fixedPositions[0],
            "fixed cache positions remain available without furniture");
        Check(PmcLootRules.TryPosition(fixedPositions, caches, first, out var drawerPoint) && drawerPoint == Vector3.right,
            "registered drawer resolves its authored interaction point");
        Check(!PmcLootRules.TryPosition(fixedPositions, caches, RaidLootContainers.FirstDeathBagId, out _) &&
            !PmcLootRules.TryPosition(fixedPositions, caches, RaidLootContainers.FirstMissionCacheId, out _),
            "corpses and mission outputs cannot become furniture objectives");
        var visited = new HashSet<int> { first + 2 };
        var nearby = PmcLootRules.NearbyDrawers(caches, Vector3.zero, visited);
        Check(nearby.Length == 12 && nearby[0] == first && nearby[1] == first + 1 && !nearby.Contains(first + 2),
            "nearest unvisited drawers have a bounded pathfinding budget");
        Check(nearby.SequenceEqual(nearby.OrderBy(id => id)) && nearby.All(id => id < first + 20),
            "candidate order is stable and excludes distant drawers");
        Check(nearby.All(id => caches.Containers[id].Count("data_drive") == 0),
            "empty drawers remain candidates; an enemy does not know their contents in advance");
        caches.Containers.Remove(first + 1);
        Check(!PmcLootRules.TryPosition(fixedPositions, caches, first + 1, out _) &&
            !PmcLootRules.NearbyDrawers(caches, Vector3.zero, visited).Contains(first + 1),
            "removed containers cannot remain actionable targets");
        caches.FurnitureCaches.Add(first + 25, new float3(float.NaN, 0, 0));
        caches.Containers.Add(first + 25, EmptyCache());
        Check(!PmcLootRules.NearbyDrawers(caches, Vector3.zero, null).Contains(first + 25),
            "invalid positions cannot enter pathfinding");
        Check(PmcLootRules.NearbyDrawers(null, Vector3.zero, null).Length == 0 &&
            !PmcLootRules.TryPosition(fixedPositions, null, first, out _),
            "missing furniture state preserves the ordinary cache fallback");
        return "PASS " + s_Checks + " PMC furniture target checks";
    }
}
