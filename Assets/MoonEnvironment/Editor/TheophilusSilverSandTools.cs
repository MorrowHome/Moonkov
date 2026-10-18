using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Opt in the formal map without changing shared TerrainData or source materials.</summary>
    public static class TheophilusSilverSandTools
    {
        public const string Folder = TheophilusVisualPassTools.Folder + "/SilverSand";
        private const string GroundShader = "Moon/R29 Local Ground Display";

        [MenuItem("Tools/Moon Environment/Theophilus/Apply silver sand")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply silver sand in Edit Mode.");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(TheophilusVisualPassTools.ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(TheophilusVisualPassTools.ScenePath, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("Formal scene has unsaved edits; preserve them before applying.");
                SceneManager.SetActiveScene(scene);
                ApplyToScene(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void ApplyToScene(Scene scene)
        {
            var shader = Shader.Find("Moon/Silver Sand Terrain");
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Silver sand terrain shader is unavailable or has errors.");
            var localShader = Shader.Find(GroundShader);
            if (!localShader || ShaderUtil.ShaderHasError(localShader))
                throw new InvalidOperationException("Local ground shader has errors.");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var clones = new Dictionary<Material, Material>();
            var terrains = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true))
                .Where(t => t.drawHeightmap).ToArray();
            foreach (var terrain in terrains)
            {
                var source = terrain.materialTemplate;
                if (!source || (source.shader.name != "Universal Render Pipeline/Terrain/Lit" && source.shader != shader))
                    throw new InvalidOperationException("Unexpected visible terrain material: " + terrain.name);
                // Current map uses at most four layers; the standard add pass deliberately
                // remains unchanged. Refuse silent partial coverage if that contract changes.
                if (terrain.terrainData.terrainLayers.Length > 4)
                    throw new InvalidOperationException("Silver sand requires a dedicated add pass for >4 terrain layers.");
                Undo.RecordObject(terrain, "Apply silver sand terrain");
                terrain.materialTemplate = Clone(source, shader, clones);
                EditorUtility.SetDirty(terrain);
            }
            int renderers = 0;
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i] || materials[i].shader.name != GroundShader) continue;
                    materials[i] = Clone(materials[i], localShader, clones);
                    changed = true;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Apply silver sand ground");
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                renderers++;
            }
            foreach (var material in clones.Values) AssetDatabase.SaveAssetIfDirty(material);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Silver sand: {terrains.Length} terrains, {renderers} local ground renderers, {clones.Count} materials.");
        }

        private static Material Clone(Material source, Shader shader, Dictionary<Material, Material> clones)
        {
            if (clones.TryGetValue(source, out var existing)) return existing;
            string name = source.name.StartsWith("SilverSand_", StringComparison.Ordinal)
                ? source.name : "SilverSand_" + source.name;
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(source) { name = name, shader = shader };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            bool compacted = name.Contains("Compacted");
            bool clasts = name.Contains("Clasts");
            material.SetVector("_SilverSand", new Vector4(compacted ? 14 : clasts ? 24 : 18, 8,
                compacted ? .4f : .48f, 45));
            material.SetVector("_SilverSheen", new Vector4(compacted ? 1.8f : clasts ? 1.1f : .8f,
                compacted ? .38f : clasts ? .28f : .2f, 1.6f, 0));
            EditorUtility.SetDirty(material);
            clones.Add(source, material);
            return material;
        }
    }
}
