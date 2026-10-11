using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Explicit authoring actions for successive building exports. Operates on
    /// the selected scene/Prefab Mode hierarchy, with Undo and manual save. Never
    /// silently reimports FBX files, rebuilds terrain, or overwrites source assets.
    /// </summary>
    public static class BuildingOptimizationMenus
    {
        private const string Menu = "Tools/Moon Environment/Buildings/";

        [MenuItem(Menu + "Optimize Decorative Shadows in Selection")]
        public static void OptimizeShadows() => EditSelection(root =>
        {
            var result = BuildingShadowTools.Optimize(root);
            Debug.Log($"Building shadows [{root.name}]: {result.castersBefore} -> {result.castersAfter} casters; visible geometry and colliders retained. Undo is available.", root);
        });

        [MenuItem(Menu + "Reuse Identical Meshes in Selection")]
        public static void ReuseMeshes() => EditSelection(root =>
        {
            var result = MC00MeshReuseTools.OptimizeSubtree(root);
            Debug.Log($"Building mesh reuse [{root.name}]: {result.uniqueBefore} -> {result.uniqueAfter} unique meshes; {result.referencesReused} references changed. Save the scene/prefab when ready.", root);
        });

        [MenuItem(Menu + "Prepare Selected Building")]
        public static void PrepareBuilding() => EditSelection(root =>
        {
            // Avoid two budgets writing to the same Light components. When a
            // replaced child is selected, refresh the existing owner's full list.
            var owner = root.GetComponentInParent<LocalLightBudget>(true);
            foreach (var nested in root.GetComponentsInChildren<LocalLightBudget>(true))
                if (nested != owner) throw new InvalidOperationException("Select the building's existing LocalLightBudget root, or a child within it; nested budgets must not overlap.");
            int cameras = 0, suns = 0;
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (!IsImportedModelComponent(camera) || !camera.enabled) continue;
                Undo.RecordObject(camera, "Disable imported presentation camera");
                camera.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
                cameras++;
            }
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Directional || !IsImportedModelComponent(light) || !light.enabled) continue;
                Undo.RecordObject(light, "Disable imported presentation sun");
                light.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                suns++;
            }
            int areas = MoonBaseApollo12Tools.ConvertImportedAreaLights(root);
            MoonBaseApollo12Tools.ConfigureLightBudget(owner ? owner.gameObject : root);
            BuildingLightingTools.Calibrate(root);
            var result = MC00MeshReuseTools.OptimizeSubtree(root);
            var shadows = BuildingShadowTools.Optimize(root);
            Debug.Log($"Building prepared [{root.name}]: {cameras} imported cameras and {suns} imported suns disabled; {areas} area lights converted; meshes {result.uniqueBefore} -> {result.uniqueAfter}; shadow casters {shadows.castersBefore} -> {shadows.castersAfter}. Undo is available; save the scene/prefab when ready.", root);
        });

        [MenuItem(Menu + "Restore Imported Mesh References in Selection")]
        public static void RestoreImportedMeshes() => EditSelection(root =>
        {
            int changed = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var imported = PrefabUtility.GetCorrespondingObjectFromOriginalSource(filter);
                if (!imported || PrefabUtility.GetPrefabAssetType(imported) != PrefabAssetType.Model || filter.sharedMesh == imported.sharedMesh) continue;
                // This explicit command also resets intentional manual mesh swaps.
                // It is kept separate from Prepare: a rerun must not silently erase them.
                Undo.RecordObject(filter, "Restore imported building mesh reference");
                filter.sharedMesh = imported.sharedMesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                changed++;
            }
            Debug.Log($"Restored {changed} MeshFilter references under [{root.name}] to the current imported model. Intentional mesh swaps in this selection were also reset; Undo can restore them.", root);
        });

        [MenuItem(Menu + "Reuse Identical Meshes in Selection", true)]
        [MenuItem(Menu + "Prepare Selected Building", true)]
        [MenuItem(Menu + "Optimize Decorative Shadows in Selection", true)]
        [MenuItem(Menu + "Restore Imported Mesh References in Selection", true)]
        private static bool CanEditSelection() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.gameObjects.Length == 1 && Selection.activeGameObject &&
            !EditorUtility.IsPersistent(Selection.activeGameObject) && Selection.activeGameObject.scene.IsValid();

        private static bool IsImportedModelComponent(Component component)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(component);
            return source && PrefabUtility.GetPrefabAssetType(source) == PrefabAssetType.Model;
        }

        private static void EditSelection(Action<GameObject> edit)
        {
            if (!CanEditSelection()) throw new InvalidOperationException("Stop Play and select one building root in the Hierarchy or Prefab Mode, not the FBX asset.");
            var root = Selection.activeGameObject;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Optimize selected building");
            try
            {
                edit(root);
                EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }
    }
}
