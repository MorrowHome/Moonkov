using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks saved GL125 loot/encounters against current collision and the server navigation builder.</summary>
public static class GL125RaidLayoutReview
{
    const string MapPath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task<object> Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetSceneByPath(MapPath).isLoaded)
            throw new InvalidOperationException("Use the existing idle Editor with the formal map closed.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(MapPath, OpenSceneMode.Additive);
        World world = null;
        DollSingerEnemySystem system = null;
        int checks = 0;
        var failures = new List<string>();
        try
        {
            var roots = scene.GetRootGameObjects();
            var map = roots.SelectMany(r => r.GetComponentsInChildren<MoonRaidMap>()).Single();
            var furniture = roots.SelectMany(r => r.GetComponentsInChildren<RaidFurnitureLoot>()).Single();
            var aliens = roots.SelectMany(r => r.GetComponentsInChildren<AlienEncounterSettings>()).Single();
            Physics.SyncTransforms();
            Require(map.LootPositions.Length == 64, "64 ordinary caches");
            Require(furniture.Drawers.Count(d => d.SupplyCache) == 32, "32 route caches");
            Require(furniture.Drawers.Count(d => !d.SupplyCache) == 30, "30 retained drawers");
            Require(map.EnemySpawnPositions.Length == 128, "128 PMC anchors");
            Require(aliens.Count == 23 && aliens.MaximumAliens == 12 && aliens.MaximumTotalSpawns == 48,
                "Encounter slots and population budgets retained");
            var cachePositions = new List<Vector3>();
            var cacheRotations = new List<Quaternion>();
            for (int i = 0; i < map.LootPositions.Length; i++)
            {
                cachePositions.Add(map.LootPositions[i]);
                cacheRotations.Add(Quaternion.FromToRotation(Vector3.up, map.GetLootGroundNormal(i)) * Quaternion.Euler(0, map.GetLootYaw(i), 0));
            }
            foreach (var drawer in furniture.Drawers.Where(d => d.SupplyCache))
            {
                Require(drawer.Target, "Route cache reference");
                cachePositions.Add(drawer.Position);
                cacheRotations.Add(drawer.Target.rotation);
            }
            for (int i = 0; i < cachePositions.Count; i++)
            {
                Vector3 normal = cacheRotations[i] * Vector3.up;
                Vector3 floor = cachePositions[i] - normal * .35f;
                float yaw = (Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up, normal)) * cacheRotations[i]).eulerAngles.y;
                Require(RaidSpawnSafety.TryPlaceCache(floor, yaw, out var supported, out _) && Vector3.Distance(supported, cachePositions[i]) < .06f,
                    "Cache " + i + " has full support and standing access");
                Require(InBuildingArea(cachePositions[i]), "Cache " + i + " inside GL125 building area");
                for (int j = 0; j < i; j++) Require(Vector3.Distance(cachePositions[i], cachePositions[j]) >= 4.9f, "Cache separation " + i + "/" + j);
            }
            foreach (var point in map.EnemySpawnPositions)
            {
                Require(InBuildingArea(point), "PMC inside building area");
                Require(RaidSpawnSafety.TryGroundPosition(point, .35f, 1.8f, 45, out var floor, out _) && Mathf.Abs(point.y - floor.y) < .1f,
                    "PMC support and capsule clearance");
            }
            for (int i = 0; i < aliens.Count; i++)
            {
                Require(InBuildingArea(aliens[i].Position), "Alien primary anchor " + i);
                Require(aliens[i].CandidatePositions.Length >= 2 && aliens[i].CandidatePositions.Length <= 32, "Bounded candidates " + i);
                foreach (var point in aliens[i].CandidatePositions) Require(InBuildingArea(point), "Alien alternative inside building area " + i);
            }
            string placement = AlienSpawnScheduleChecks.RunPlacementChecks();
            Require(placement.StartsWith("Alien physical placement: 23/23"), placement);

            world = new World("GL125 saved-layout navigation only", WorldFlags.GameServer);
            system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            var ready = typeof(DollSingerEnemySystem).GetMethod("NavigationReady", Hidden);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!(bool)ready.Invoke(system, new object[] { map, timer.Elapsed.TotalSeconds }))
            {
                if (timer.Elapsed.TotalSeconds > 180) throw new TimeoutException("Navigation build exceeded 180 seconds.");
                await Task.Delay(50);
            }
            var starts = new List<Vector3>();
            for (int i = 0; i < map.ExtractionCount; i++) if (map.TryExtractionPosition(i, out var exit)) starts.Add(exit);
            starts.AddRange(new[] { new Vector3(1639,211.7f,2150), new Vector3(1611,211.7f,2146), new Vector3(1599,211.7f,2170), new Vector3(1587,211.7f,2150) });
            starts = starts.Select(p => NavMesh.SamplePosition(p, out var h, 3, NavMesh.AllAreas) ? (Vector3?)h.position : null)
                .Where(p => p.HasValue).Select(p => p.Value).ToList();
            Require(starts.Count >= 4, "Player spawn/exit navigation seeds available");
            var path = new NavMeshPath();
            for (int i = 0; i < cachePositions.Count; i++)
            {
                bool reachable = false;
                for (int side = -1; side <= 1; side += 2)
                {
                    var stand = cachePositions[i] + cacheRotations[i] * new Vector3(0,0,side * 1.25f);
                    if (!RaidSpawnSafety.TryGroundPosition(stand,.35f,1.8f,40,out var floor,out _)) continue;
                    reachable |= Reachable(floor);
                }
                Require(reachable,"Navigable cache standing position " + i);
                if (i % 16 == 0) await Task.Yield();
            }
            for (int i = 0; i < map.EnemySpawnPositions.Length; i++)
                Require(Reachable(map.EnemySpawnPositions[i]), "PMC " + i + " connected to player/exit navigation");
            return new { passed = failures.Count == 0, checks, failures = failures.ToArray(), placement,
                ordinary = map.LootPositions.Length, route = 32, pmc = map.EnemySpawnPositions.Length,
                alienCandidates = Enumerable.Range(0,aliens.Count).Sum(i=>aliens[i].CandidatePositions.Length),
                scope = "Saved scene, physics and actual server navigation builder; no Play mode, transport or multiplayer." };

            bool Reachable(Vector3 position)
            {
                if (!NavMesh.SamplePosition(position,out var sample,1,NavMesh.AllAreas) || Mathf.Abs(position.y-sample.position.y)>.4f) return false;
                return starts.OrderBy(p=>(p-position).sqrMagnitude).Any(p => NavMesh.CalculatePath(p,sample.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete);
            }
        }
        finally
        {
            if (system != null)
            {
                foreach (string name in new[] { "m_NavInstance", "m_MissionNavInstance" })
                {
                    var field = typeof(DollSingerEnemySystem).GetField(name,Hidden);
                    var instance = (NavMeshDataInstance)field.GetValue(system);
                    if (instance.valid) instance.Remove();
                    field.SetValue(system,default(NavMeshDataInstance));
                }
                var links = (List<NavMeshLinkInstance>)typeof(DollSingerEnemySystem).GetField("m_MissionNavLinks",Hidden).GetValue(system);
                foreach (var link in links) if (NavMesh.IsLinkValid(link)) NavMesh.RemoveLink(link);
                links.Clear();
                foreach (string name in new[] { "m_NavData", "m_MissionNavData" })
                {
                    var field = typeof(DollSingerEnemySystem).GetField(name,Hidden);
                    var data = field.GetValue(system) as NavMeshData;
                    if (data) { NavMeshBuilder.Cancel(data); Object.DestroyImmediate(data); }
                    field.SetValue(system,null);
                }
            }
            world?.Dispose();
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(previous);
        }
        void Require(bool value,string label) { checks++; if (!value) failures.Add(label); }
    }

    // Excludes the retired full-DEM spawn grid; allows bounded searches around authored structures.
    static bool InBuildingArea(Vector3 point) => point.x >= 1550 && point.x <= 2480 && point.z >= 1670 && point.z <= 2640;
}
