using System;
using Unity.MP_FPS.Moon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Editor
{
    public static class MoonRaidSetup
    {
        [MenuItem("Tools/Moon Environment/Set Up Minimum Raid")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before editing raid points.");
            var scene = SceneManager.GetSceneByPath(MoonNetworkSetup.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(MoonNetworkSetup.ScenePath, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("Save MoonGameScene before setting up raid points.");
                foreach (var root in scene.GetRootGameObjects())
                    if (root.GetComponent<MoonRaidMap>() != null) return; // Preserve authored points on subsequent runs.
                MoonEnvironment environment = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out MoonEnvironment found)) environment = found;
                if (environment == null) throw new InvalidOperationException("Moon environment is missing.");
                UnityEngine.Physics.SyncTransforms();
                Vector3 centre = environment.SpawnPoint.position;
                var positions = new Vector3[12];
                for (int i = 0; i < positions.Length; i++)
                    positions[i] = Ground(centre + new Vector3((i % 3 - 1) * 14, 0, 8 + i / 3 * 13)) + Vector3.up * 0.55f;
                Vector3 extraction = Ground(centre + new Vector3(0, 0, 75));
                var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
                if (panel == null) throw new InvalidOperationException("HUD PanelSettings is missing.");
                var go = new GameObject("Moon Raid");
                SceneManager.MoveGameObjectToScene(go, scene);
                var map = go.AddComponent<MoonRaidMap>();
                var data = new SerializedObject(map);
                var points = data.FindProperty("m_LootPositions");
                points.arraySize = positions.Length;
                for (int i = 0; i < positions.Length; i++) points.GetArrayElementAtIndex(i).vector3Value = positions[i];
                data.FindProperty("m_ExtractionPosition").vector3Value = extraction;
                data.FindProperty("m_PanelSettings").objectReferenceValue = panel;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save raid setup.");
                Debug.Log("Moon raid ready: 12 caches, 8-second extraction, 10-minute raid. Existing character combat is retained.");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Vector3 Ground(Vector3 desired)
        {
            // A small deterministic search keeps authored points clear of rocks and steep faces.
            for (int ring = 0; ring <= 4; ring++)
            for (int z = -ring; z <= ring; z++)
            for (int x = -ring; x <= ring; x++)
            {
                if (ring > 0 && Math.Max(Math.Abs(x), Math.Abs(z)) != ring) continue;
                Vector3 probe = desired + new Vector3(x * 2, 0, z * 2);
                if (!UnityEngine.Physics.Raycast(new Vector3(probe.x, 1200, probe.z), Vector3.down,
                    out var hit, 1500, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore) ||
                    !(hit.collider is TerrainCollider) || Vector3.Angle(hit.normal, Vector3.up) > 25) continue;
                if (UnityEngine.Physics.CheckCapsule(hit.point + Vector3.up * 0.4f, hit.point + Vector3.up * 1.5f,
                    0.35f, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                return hit.point;
            }
            throw new InvalidOperationException("No safe raid point near " + desired + ". Author it on clear terrain.");
        }
    }
}
