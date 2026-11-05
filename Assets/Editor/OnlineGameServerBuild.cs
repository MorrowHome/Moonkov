using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OnlineGameServerBuild
{
    const string Report = "Library/BuildValidation/OnlineServer/build-report.json";
    const string QueueKey = "Moonkov.OnlineServerBuildQueued";
    static double s_IdleSince = -1;
    [Serializable] sealed class Result
    {
        public string Status, Output, Message;
        public ulong Bytes;
        public int Errors;
        public double Seconds, AddressablesSeconds, ArchiveSeconds;
    }
    [Serializable] sealed class BuildTrace { public TraceEvent[] traceEvents; }
    [Serializable] sealed class TraceEvent { public string name; public double dur; }

    // Addressables' "LZ4" uses LZ4HC. Local iteration does not need that expensive
    // compression pass. Keep the normal SBP cache and all source group settings.
    public sealed class LocalBundleCompression : IDisposable
    {
        readonly BuildCallbacks m_Callbacks = ContentPipeline.BuildCallbacks;
        readonly Func<IBuildParameters, IDependencyData, IWriteData, ReturnCode> m_PreviousPacking;
        readonly Dictionary<BundledAssetGroupSchema, CompressionState> m_Saved = new Dictionary<BundledAssetGroupSchema, CompressionState>();
        struct CompressionState { public int Value; public bool Dirty; }

        public LocalBundleCompression()
        {
            m_PreviousPacking = m_Callbacks.PostPackingCallback;
            m_Callbacks.PostPackingCallback = PrepareBundles;
        }

        ReturnCode PrepareBundles(IBuildParameters parameters, IDependencyData dependencies, IWriteData writeData)
        {
            var code = m_PreviousPacking?.Invoke(parameters, dependencies, writeData) ?? ReturnCode.Success;
            if (code < ReturnCode.Success || !(parameters is AddressableAssetsBundleBuildParameters bundles)) return code;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return code;
            foreach (var group in settings.groups)
            {
                var schema = group != null ? group.GetSchema<BundledAssetGroupSchema>() : null;
                // Custom schemas retain their own compression policy.
                if (schema == null || schema.GetType() != typeof(BundledAssetGroupSchema) || m_Saved.ContainsKey(schema)) continue;
                using (var serialized = new SerializedObject(schema))
                {
                    var compression = serialized.FindProperty("m_Compression");
                    if (compression == null) throw new InvalidOperationException("Addressables compression schema has changed.");
                    m_Saved.Add(schema, new CompressionState { Value = compression.intValue, Dirty = EditorUtility.IsDirty(schema) });
                }
                // Do not use the property setter: it dirties groups/settings and
                // can save temporary server settings into the client's assets.
                JsonUtility.FromJsonOverwrite("{\"m_Compression\":0}", schema);
                EditorUtility.ClearDirty(schema);
            }
            bundles.BundleCompression = BuildCompression.Uncompressed;
            return code;
        }

        public void Dispose()
        {
            m_Callbacks.PostPackingCallback = m_PreviousPacking;
            foreach (var saved in m_Saved)
            {
                if (saved.Key == null) continue;
                JsonUtility.FromJsonOverwrite("{\"m_Compression\":" + saved.Value.Value + "}", saved.Key);
                if (saved.Value.Dirty) EditorUtility.SetDirty(saved.Key);
                else EditorUtility.ClearDirty(saved.Key);
            }
            m_Saved.Clear();
        }
    }

    static OnlineGameServerBuild()
    { if (SessionState.GetBool(QueueKey, false)) EditorApplication.update += WhenIdle; }

    [MenuItem("Tools/Moonkov/Build Local Windows Server")]
    public static void Build()
    {
        s_IdleSince = -1;
        SessionState.SetBool(QueueKey, true);
        // SBP/Addressables compiles separately and does not inherit the player's
        // extraScriptingDefines. Select the same local entry for the Server target.
        var serverTarget = UnityEditor.Build.NamedBuildTarget.Server;
        string defines = PlayerSettings.GetScriptingDefineSymbols(serverTarget);
        if (Array.IndexOf(defines.Split(';'), "MOON_LOCAL_SERVER") < 0)
            PlayerSettings.SetScriptingDefineSymbols(serverTarget, defines + ";MOON_LOCAL_SERVER");
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, JsonUtility.ToJson(new Result { Status = "WaitingForEditor", Output = "Build/MoonServer/MoonServer.exe" }, true));
        EditorApplication.update -= WhenIdle;
        EditorApplication.update += WhenIdle;
    }

    static void WhenIdle()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
        { s_IdleSince = -1; return; }
        if (s_IdleSince < 0) { s_IdleSince = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup - s_IdleSince < 2) return;
        EditorApplication.update -= WhenIdle;
        SessionState.SetBool(QueueKey, false);
        try { BuildNow(); } catch (Exception ex) { Debug.LogException(ex); }
    }

    static void BuildNow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Wait for the current Editor operation to finish.");
        var result = new Result { Status = "Building", Output = "Build/MoonServer/MoonServer.exe" };
        var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, JsonUtility.ToJson(result, true));
        try
        {
            Directory.CreateDirectory("Build/MoonServer");
            var scenes = new[] {
                "Assets/Scenes/ServerScene.unity", "Assets/Scenes/Persistents.unity", "Assets/Scenes/GameResourcesSubScene.unity",
                "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity",
                "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusSpawnPointsSubScene.unity"
            };
            // SaveAsCopy keeps another task's original scene open and dirty. The
            // build uses its exact current contents without saving over that source.
            for (int i = 0; i < scenes.Length; i++)
            {
                var loaded = SceneManager.GetSceneByPath(scenes[i]);
                if (!loaded.IsValid() || !loaded.isLoaded || !loaded.isDirty) continue;
                Directory.CreateDirectory("Assets/OnlineServerBuildSnapshot");
                string copy = "Assets/OnlineServerBuildSnapshot/" + Path.GetFileName(scenes[i]);
                if (!EditorSceneManager.SaveScene(loaded, copy, true)) throw new InvalidOperationException("Could not preserve a build copy of the unsaved scene.");
                scenes[i] = copy;
            }
            var started = DateTime.UtcNow;
            BuildReport report;
            using (new LocalBundleCompression())
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = result.Output,
                    target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Server, options = BuildOptions.None,
                    extraScriptingDefines = new[] { "MOON_LOCAL_SERVER" }
                });
            }
            result.Status = report.summary.result.ToString(); result.Errors = report.summary.totalErrors; result.Bytes = report.summary.totalSize;
            result.Seconds = report.summary.totalTime.TotalSeconds;
            ReadAddressablesTimings(result, started);
            if (report.summary.result == BuildResult.Cancelled)
            {
                result.Message = "Build cancelled; save modified scenes only after their owner confirms.";
                return;
            }
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Server build failed; inspect the build report and Editor log.");
            File.WriteAllText("Build/MoonServer/online-instance-protocol.txt", "1");
            Debug.Log("[Online Server] Dedicated Windows server built: " + Path.GetFullPath(result.Output));
        }
        catch (Exception ex) { result.Status = "Failed"; result.Message = ex.Message; throw; }
        finally
        {
            File.WriteAllText(Report, JsonUtility.ToJson(result, true));
            if (EditorUserBuildSettings.standaloneBuildSubtarget != previousSubtarget)
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
        }
    }

    static void ReadAddressablesTimings(Result result, DateTime started)
    {
        const string tracePath = "Library/com.unity.addressables/AddressablesBuildTEP.json";
        if (!File.Exists(tracePath) || File.GetLastWriteTimeUtc(tracePath) < started) return;
        try
        {
            var trace = JsonUtility.FromJson<BuildTrace>(File.ReadAllText(tracePath));
            if (trace?.traceEvents == null) return;
            foreach (var step in trace.traceEvents)
            {
                if (step.name == "ContentPipeline.BuildAssetBundles") result.AddressablesSeconds += step.dur / 1000000d;
                if (step.name == "ArchiveAndCompressBundles") result.ArchiveSeconds += step.dur / 1000000d;
            }
        }
        catch (Exception ex) { Debug.LogWarning("[Online Server] Could not read build timings: " + ex.Message); }
    }
}
