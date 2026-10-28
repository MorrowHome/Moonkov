using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One scene-owned service point. Does not rebuild buildings, navigation or lighting.
public static class TheophilusOxygenStationAuthoring
{
    private const string Name = "MC00 L22 Oxygen Supply";
    private const string Report = "Library/BuildValidation/TheophilusOxygenStation.json";
    private static int s_Checks;
    private static int Mask => LayerMask.GetMask("Default", "Ground");
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }
    private readonly struct Placement
    {
        public readonly Vector3 Position, Stand;
        public readonly Quaternion Rotation;
        public Placement(Vector3 position, Quaternion rotation)
        { Position = position; Rotation = rotation; Stand = position + rotation * new Vector3(0, .06f, 1.05f); }
    }

    [MenuItem("Tools/Moonkov/Oxygen/Install formal MC00 supply station")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stable Edit Mode required.");
        var scene = SceneManager.GetSceneByPath(TheophilusOxygenTools.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
            throw new InvalidOperationException("Open and save the formal Theophilus map first.");
        var existing = scene.GetRootGameObjects().SingleOrDefault(g => g.name == Name);
        if (existing) { Validate(scene, existing); return; }
        var site = UnityEngine.Object.FindObjectsByType<RaidMissionSite>(FindObjectsSortMode.None).Single(s => s.gameObject.scene == scene);
        var anchor = site.Anchor("sampler") ?? throw new InvalidOperationException("Formal sampler anchor is missing.");
        Physics.SyncTransforms();
        var placement = FindPlacement(anchor, site);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoonkovOxygenTools.StationPath);
        if (!prefab) throw new InvalidOperationException("The existing oxygen station prefab is missing.");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        try
        {
            Undo.RegisterCreatedObjectUndo(root, "Install MC00 oxygen supply");
            root.name = Name; root.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
            var station = root.GetComponent<OxygenRefillZone>(); station.Configure(10, true); station.Powered = true;
            station.ConfigureConnection(new Vector3(.48f, 1.12f, .38f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(station);
            Physics.SyncTransforms(); Validate(scene, root);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save formal oxygen station.");
        }
        catch { UnityEngine.Object.DestroyImmediate(root); throw; }
    }

    private static Placement FindPlacement(RaidMissionAnchor anchor, RaidMissionSite site)
    {
        // Search only a small ring around existing authored gameplay. Prefer the room edge.
        foreach (bool requireWall in new[] { true, false })
        foreach (float radius in new[] { 4f, 5f, 6f, 7f })
        for (int i = 0; i < 16; i++)
        {
            var offset = Quaternion.Euler(0, anchor.Yaw + i * 22.5f, 0) * Vector3.forward * radius;
            var point = anchor.Approach + offset;
            if (!Floor(point, anchor.Approach.y, out var floor)) continue;
            point.y = floor.y;
            var direction = Vector3.ProjectOnPlane(anchor.Approach - point, Vector3.up).normalized;
            var rotation = Quaternion.LookRotation(direction); var p = new Placement(point, rotation);
            if (site.Anchors.Any(a => Vector3.Distance(a.Approach, p.Position) < 2 || Vector3.Distance(a.Approach, p.Stand) < 1.2f)) continue;
            if (requireWall && !Physics.Raycast(point + Vector3.up, -direction, 1.2f, Mask, QueryTriggerInteraction.Ignore)) continue;
            if (!BreathableZone.TrySample(p.Stand + Vector3.up, out var air) || !air.Breathable || air.AllowsSuitRefill) continue;
            if (Physics.CheckBox(point + rotation * new Vector3(0, .88f, -.06f), new Vector3(.57f, .82f, .4f), rotation, Mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.CheckCapsule(p.Stand + Vector3.up * .28f, p.Stand + Vector3.up * 1.45f, .27f, Mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.Linecast(p.Stand + Vector3.up, point + rotation * new Vector3(.48f, 1.12f, .38f), Mask, QueryTriggerInteraction.Ignore)) continue;
            bool flat = true;
            foreach (var corner in new[] { new Vector3(-.5f, 0, -.3f), new Vector3(.5f, 0, -.3f), new Vector3(-.5f, 0, .3f), new Vector3(.5f, 0, .3f) })
                if (!Floor(point + rotation * corner, point.y, out var support) || Mathf.Abs(support.y - point.y) > .04f) { flat = false; break; }
            if (!flat || !Floor(p.Stand, point.y, out var standFloor) || Mathf.Abs(standFloor.y - point.y) > .08f) continue;
            return p;
        }
        throw new InvalidOperationException("No clear, pressurized, floor-supported service point near the existing sampler. Map was not changed.");
    }

    private static bool Floor(Vector3 point, float expectedY, out Vector3 floor)
    {
        var hits = Physics.RaycastAll(new Vector3(point.x, expectedY + .8f, point.z), Vector3.down, 1.4f, Mask, QueryTriggerInteraction.Ignore)
            .Where(h => h.normal.y > .95f && Mathf.Abs(h.point.y - (expectedY - .06f)) < .3f).OrderBy(h => Mathf.Abs(h.point.y - expectedY)).ToArray();
        floor = hits.Length > 0 ? hits[0].point : default;
        return hits.Length > 0;
    }

    private static void Validate(Scene scene, GameObject root)
    {
        var station = root.GetComponent<OxygenRefillZone>();
        Check(station && station.Powered && station.RequiresClearConnection, "Formal station has powered, clear-connection supply");
        Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == MoonkovOxygenTools.StationPath, "Station uses existing reusable prefab");
        var stand = root.transform.TransformPoint(new Vector3(0, .06f, 1.05f)); var sample = stand + Vector3.up;
        Check(Floor(stand, root.transform.position.y, out var floor) && Mathf.Abs(floor.y - root.transform.position.y) < .08f, "Service standing area is floor-supported");
        Check(!Physics.CheckCapsule(stand + Vector3.up * .28f, stand + Vector3.up * 1.45f, .27f, Mask, QueryTriggerInteraction.Ignore), "Actor standing capsule is clear");
        Check(BreathableZone.TrySample(sample, out var air) && air.Breathable && !air.AllowsSuitRefill, "Station sits in an existing breathable room without ambient tank refill");
        Check(Mathf.Abs(OxygenRefillZone.SampleSupply(sample, air.Breathable) - 10) < .001f, "Actual station zone delivers ten points per second");
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); health.ProtectionInitialized = health.HeadDomeEquipped = true; health.Oxygen = 20;
        RaidOxygen.Tick(ref health, air, OxygenParameters.Default, 2, 1, OxygenRefillZone.SampleSupply(sample, air.Breathable));
        Check(Mathf.Abs(health.Oxygen - 40) < .001f && RaidOxygen.ReceivingExternalSupply(health), "Real room and station refill equipped actor and enable existing feedback");
        station.Powered = false;
        try { Check(OxygenRefillZone.SampleSupply(sample, true) == 0, "Power loss stops supply"); }
        finally { station.Powered = true; }
        Check(OxygenRefillZone.SampleSupply(root.transform.TransformPoint(new Vector3(0, 1, 4)), true) == 0, "Leaving service area stops supply");
        Check(OxygenRefillZone.SampleSupply(sample, false) == 0, "Station cannot grant breathable air in vacuum");
        GameObject obstruction = null;
        try
        {
            obstruction = new GameObject("Temporary oxygen connection obstruction"); SceneManager.MoveGameObjectToScene(obstruction, scene);
            obstruction.transform.position = (sample + station.ConnectionPoint) * .5f;
            var block = obstruction.AddComponent<BoxCollider>(); block.size = Vector3.one * .15f;
            Physics.SyncTransforms();
            Check(OxygenRefillZone.SampleSupply(sample, true) == 0, "An opaque obstruction prevents through-wall supply");
        }
        finally { if (obstruction) UnityEngine.Object.DestroyImmediate(obstruction); Physics.SyncTransforms(); }
        Check(OxygenRefillZone.SampleSupply(sample, true) == 10, "Removing temporary obstruction restores supply");
        var report = new ReportData { Position = root.transform.position, Stand = stand, Connection = station.ConnectionPoint, Yaw = root.transform.eulerAngles.y, Checks = s_Checks };
        Directory.CreateDirectory("Library/BuildValidation");
        File.WriteAllText(Report, JsonUtility.ToJson(report, true));
    }

    [Serializable] private sealed class ReportData
    { public Vector3 Position, Stand, Connection; public float Yaw; public int Checks; }

    public static void RunBatch()
    {
        Scene scene = default;
        try
        {
            s_Checks = 0; scene = EditorSceneManager.OpenScene(TheophilusOxygenTools.ScenePath, OpenSceneMode.Single);
            Install(); scene = EditorSceneManager.OpenScene(TheophilusOxygenTools.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms(); Validate(scene, scene.GetRootGameObjects().Single(g => g.name == Name));
            MoonkovOxygenChecks.Run();
            Debug.Log($"PASS: {s_Checks} formal oxygen station placement, air, obstruction, power, refill and saved-scene checks.");
            File.WriteAllText("Library/BuildValidation/TheophilusOxygenStation-result.txt", "PASS");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            EditorApplication.Exit(1);
        }
    }
}
