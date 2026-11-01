using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Preserve baked payloads, but let Players discard their unused CPU copies.</summary>
    [InitializeOnLoad]
    public static class SourceWorldRuntimeAssets
    {
        private const string StatePath = "LocalData/SourceWorldStartupAudit/RuntimeAssets.state.json";
        [Serializable] private sealed class Migration
        {
            public string[] paths;
            public int index, changedAssets, changedObjects;
            public bool running;
            public string error;
        }
        private static Migration migration;

        static SourceWorldRuntimeAssets()
        {
            if (!File.Exists(StatePath)) return;
            migration = JsonUtility.FromJson<Migration>(File.ReadAllText(StatePath));
            if (migration != null && migration.running) EditorApplication.update += Tick;
        }

        // Do not call Apply(makeNoLongerReadable) or UploadMeshData(true) before
        // saving an authored asset: retain its serialized payload for the build.
        // This is the same persisted flag used by Unity's Read/Write setting.
        internal static bool SetReadable(Object asset, bool readable)
        {
            if (!(asset is Mesh) && !(asset is Cubemap) && !(asset is Texture2DArray))
                throw new ArgumentException("Expected a source mesh, cubemap or statistics array.", nameof(asset));
            using var serialized = new SerializedObject(asset);
            var flag = serialized.FindProperty("m_IsReadable");
            if (flag == null) throw new InvalidOperationException("Missing Read/Write flag on " + asset.name);
            if (flag.boolValue == readable) return false;
            flag.boolValue = readable;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return true;
        }

        [MenuItem("Tools/Moon Environment/Source World/Optimize baked runtime assets")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Optimize source assets in Edit Mode.");
            if (migration != null && migration.running) return;
            string root = SourceWorldAssetImport.Root;
            migration = new Migration
            {
                paths = Directory.GetFiles(root + "Meshes", "Atlas_*.asset")
                    .Concat(Directory.GetFiles(root + "Reflections", "Probe_*.asset"))
                    .Append(SourceWorldAssetImport.DataPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                running = true
            };
            Save();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (migration == null || !migration.running) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                string path = migration.paths[migration.index];
                int changed = 0;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if ((asset is Mesh || asset is Cubemap || asset is Texture2DArray) && SetReadable(asset, false)) changed++;
                if (changed > 0)
                {
                    var main = AssetDatabase.LoadMainAssetAtPath(path);
                    EditorUtility.SetDirty(main);
                    AssetDatabase.SaveAssetIfDirty(main);
                    migration.changedAssets++;
                    migration.changedObjects += changed;
                }
                migration.index++;
                migration.running = migration.index < migration.paths.Length;
                Save();
                if (!migration.running)
                {
                    EditorApplication.update -= Tick;
                    Debug.Log($"Source runtime assets optimized: {migration.changedObjects} objects in {migration.changedAssets} assets. Baked payloads preserved.");
                }
            }
            catch (Exception exception)
            {
                migration.running = false;
                migration.error = exception.ToString();
                Save();
                EditorApplication.update -= Tick;
                Debug.LogException(exception);
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            File.WriteAllText(StatePath, JsonUtility.ToJson(migration, true));
        }
    }
}
