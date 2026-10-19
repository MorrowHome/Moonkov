using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Missions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Uses the actual formal colliders and the existing runtime AI navigation builder once.
// It does not start a raid, save the scene, move user input or write a player profile.
[InitializeOnLoad]
public static class MissionFormalChecks
{
    private static Scene s_Formal, s_Original;
    private static World s_World;
    private static DollSingerEnemySystem s_Navigation;
    private static RaidMissionSite s_Site;
    private static MoonRaidMap s_Map;
    private static double s_Started;
    private static bool s_Opened;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Status { get; private set; } = "Not started";
    private static string Output => Path.GetFullPath("Library/BuildValidation/Missions");
    static MissionFormalChecks() { AssemblyReloadEvents.beforeAssemblyReload += Cleanup; }

    public static void Start()
    {
        if (Status == "Running" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            RaidMissionAuthoring.HasLiveWorlds())
            throw new InvalidOperationException("Require idle Edit Mode and no game Worlds.");
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scene.");
        s_Original = SceneManager.GetActiveScene(); s_Formal = SceneManager.GetSceneByPath(RaidMissionAuthoring.ScenePath);
        if (s_Formal.IsValid() && s_Formal.isLoaded) throw new InvalidOperationException("Do not take over an already open formal map.");
        Status = "Running"; s_Started = EditorApplication.timeSinceStartup;
        try
        {
            s_Formal = EditorSceneManager.OpenScene(RaidMissionAuthoring.ScenePath, OpenSceneMode.Additive); s_Opened = true;
            s_Site = s_Formal.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RaidMissionSite>(true)).Single();
            s_Map = s_Formal.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MoonRaidMap>(true)).Single();
            if (s_Site.Validate() != null) throw new InvalidOperationException(s_Site.Validate());
            s_World = new World("Formal mission navigation / isolated validation", WorldFlags.GameServer);
            s_Navigation = s_World.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            typeof(DollSingerEnemySystem).GetMethod("NavigationReady", Private).Invoke(s_Navigation, new object[] { s_Map, 0d });
            EditorApplication.update += Tick;
        }
        catch (Exception ex) { Status = "FAIL: " + ex.GetBaseException().Message; Cleanup(); throw; }
    }

    private static void Tick()
    {
        try
        {
            var build = (AsyncOperation)typeof(DollSingerEnemySystem).GetField("m_Build", Private).GetValue(s_Navigation);
            if (build != null && !build.isDone)
            {
                if (EditorApplication.timeSinceStartup - s_Started > 180) throw new TimeoutException("Formal navigation build exceeded 180 seconds.");
                return;
            }
            bool ready = (bool)typeof(DollSingerEnemySystem).GetMethod("NavigationReady", Private).Invoke(s_Navigation, new object[] { s_Map, 1d });
            if (!ready) throw new InvalidOperationException("Existing server navigation did not become ready.");
            var lines = new List<string>();
            var entry = s_Site.Anchor("entry");
            bool sampleStart = NavMesh.SamplePosition(entry.Approach, out var start, 3, NavMesh.AllAreas);
            bool all = sampleStart;
            foreach (var anchor in s_Site.Anchors)
            {
                bool ground = Physics.Raycast(anchor.Approach + Vector3.up, Vector3.down, out _, 2.5f,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
                bool clear = !Physics.CheckCapsule(anchor.Approach + Vector3.up * .3f, anchor.Approach + Vector3.up * 1.5f, .25f,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
                bool sight = !Physics.Linecast(anchor.Approach + Vector3.up * 1.4f, anchor.Position,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
                bool sample = NavMesh.SamplePosition(anchor.Approach, out var end, 3, NavMesh.AllAreas);
                var path = new NavMeshPath();
                bool route = sampleStart && sample && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                float distance = 0; for (int i = 1; i < path.corners.Length; i++) distance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                lines.Add(anchor.Id + ": ground=" + ground + " capsule=" + clear + " sight=" + sight + " nav=" + sample + " route=" + route + " routeMeters=" + distance.ToString("F1"));
                all &= ground && clear && sight && sample && route;
            }
            Directory.CreateDirectory(Output);
            File.WriteAllLines(Path.Combine(Output, "formal-navigation.txt"), lines);
            Capture("sampler", 0, Path.Combine(Output, "sampler-authoring.png"));
            Capture("archive", 1, Path.Combine(Output, "archive-authoring.png"));
            Status = (all ? "PASS" : "FAIL") + ": " + s_Site.Anchors.Length + " formal mission stations; existing runtime NavMesh builder; ground/capsule/sight/route. See " + Output;
            if (all) Debug.Log("[Missions] " + Status); else Debug.LogError("[Missions] " + Status + "\n" + string.Join("\n", lines));
            Cleanup();
        }
        catch (Exception ex) { Status = "FAIL: " + ex.GetBaseException().Message; Debug.LogError(Status); Cleanup(); }
    }

    private static void Capture(string id, int definition, string path)
    {
        var anchor = s_Site.Anchor(id); var cameraObject = new GameObject("Mission authored device inspection / temporary");
        SceneManager.MoveGameObjectToScene(cameraObject, s_Formal);
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); target.Create();
        Texture2D image = null;
        try
        {
            var snapshot = new RaidMissionSnapshotRpc { DefinitionIndex = definition, OperationId = Guid.NewGuid().ToString("D"),
                Phase = MissionPhase.OutputReady, CompletedMask = definition == 0 ? 15u : 31u, OutputRemaining = definition == 0 ? 2 : 1 };
            s_Site.Present(snapshot, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .03f; camera.farClipPlane = 1000;
            camera.transform.position = anchor.Approach + Vector3.up * 1.4f + Vector3.ProjectOnPlane(anchor.Approach - anchor.Position, Vector3.up).normalized * .65f;
            camera.transform.rotation = Quaternion.LookRotation(anchor.Position - camera.transform.position);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var oldActive = SceneManager.GetActiveScene(); SceneManager.SetActiveScene(s_Formal);
            try { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target }); }
            finally { if (oldActive.IsValid()) SceneManager.SetActiveScene(oldActive); }
            var previous = RenderTexture.active;
            try { RenderTexture.active = target; image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { RenderTexture.active = previous; }
        }
        finally
        { if (image) UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject); }
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (s_Navigation != null)
        {
            var data = (NavMeshData)typeof(DollSingerEnemySystem).GetField("m_NavData", Private).GetValue(s_Navigation);
            var build = (AsyncOperation)typeof(DollSingerEnemySystem).GetField("m_Build", Private).GetValue(s_Navigation);
            if (data && build != null && !build.isDone) NavMeshBuilder.Cancel(data);
            var instance = (NavMeshDataInstance)typeof(DollSingerEnemySystem).GetField("m_NavInstance", Private).GetValue(s_Navigation);
            if (instance.valid) instance.Remove();
            typeof(DollSingerEnemySystem).GetField("m_NavData", Private).SetValue(s_Navigation, null);
            typeof(DollSingerEnemySystem).GetField("m_Build", Private).SetValue(s_Navigation, null);
            if (data) UnityEngine.Object.DestroyImmediate(data);
        }
        s_Navigation = null; s_World?.Dispose(); s_World = null;
        if (s_Original.IsValid() && s_Original.isLoaded) SceneManager.SetActiveScene(s_Original);
        if (s_Opened && s_Formal.IsValid() && s_Formal.isLoaded) EditorSceneManager.CloseScene(s_Formal, true);
        s_Opened = false; s_Site = null; s_Map = null;
    }
}
