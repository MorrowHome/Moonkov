using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS;

public static class PmcCharacterRosterChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        var prefabs = new PlayerEntityPrefabs
        {
            DollSingerEntityPrefab = new Entity { Index = 10, Version = 1 },
            HatsuneMikuEntityPrefab = new Entity { Index = 11, Version = 1 },
            ShianEntityPrefab = new Entity { Index = 12, Version = 1 }
        };
        var roster = new PmcCharacterRoster();
        var random = new Unity.Mathematics.Random(47891);
        var orders = new HashSet<int>();
        for (int batch = 0; batch < 8; batch++)
        {
            var seen = new HashSet<int>();
            int order = 0;
            for (int i = 0; i < 3; i++)
            {
                Check(roster.TryChoose(prefabs, ref random, out var prefab, out int character), "Available character was skipped");
                Check(seen.Add(character) && prefab == PmcCharacterRoster.Prefab(prefabs, character), "Repeated character or wrong prefab in a bag");
                order = order * 10 + character;
            }
            Check(seen.SetEquals(new[] { 2, 3, 4 }), "All three character models must appear once per bag");
            orders.Add(order);
        }
        Check(orders.Count > 1, "Character must not remain tied to spawn order and AI personality");
        for (int mask = 0; mask < 8; mask++)
        {
            var available = prefabs;
            if ((mask & 1) == 0) available.DollSingerEntityPrefab = Entity.Null;
            if ((mask & 2) == 0) available.HatsuneMikuEntityPrefab = Entity.Null;
            if ((mask & 4) == 0) available.ShianEntityPrefab = Entity.Null;
            roster.Reset();
            for (int i = 0; i < 6; i++)
            {
                bool chosen = roster.TryChoose(available, ref random, out var prefab, out int character);
                Check(mask == 0 ? !chosen && prefab == Entity.Null : chosen && prefab != Entity.Null &&
                    prefab == PmcCharacterRoster.Prefab(available, character), "Missing prefab fallback failed for mask " + mask);
            }
        }
        return "PASS " + checks + " PMC character roster checks: balanced shuffled bags, matching prefabs, map reset and all missing-prefab combinations.";
    }
}
