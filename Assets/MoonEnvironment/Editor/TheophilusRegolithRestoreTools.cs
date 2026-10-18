using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Restore d432063's surface assets on the formal map, while retaining the
    /// current low/high alphamaps. Does not rewrite any TerrainData or geometry.
    /// Per-Terrain materials reference persistent SplatAlpha subassets directly.
    /// </summary>
    public static class TheophilusRegolithRestoreTools
    {
        private const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        private const string SourcePath = "Assets/MoonEnvironment/Materials/LunarRegolith.mat";
        private const string MaterialRoot = "Assets/MoonEnvironment/Variants/TheophilusBase/Materials/OriginalRegolith";

        [MenuItem("Tools/Moon Environment/Theophilus/Restore Original Regolith Detail")]
        public static void RestoreFromMenu() => Debug.Log(Restore());

        public static string Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before authoring lunar materials.");
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the formal TheophilusGameScene first.");
            if (scene.isDirty)
                throw new InvalidOperationException("Save or resolve existing scene edits before restoring materials.");
            Material source = AssetDatabase.LoadAssetAtPath<Material>(SourcePath);
            if (!source || source.shader.name != "MoonEnvironment/Regolith")
                throw new InvalidOperationException("The original lunar surface material is unavailable.");
            Terrain[] terrains = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>(true))
                .Where(t=>t.terrainData && t.materialTemplate &&
                    (t.materialTemplate.name.StartsWith("R8_") ||
                     AssetDatabase.GetAssetPath(t.materialTemplate).StartsWith(MaterialRoot+"/",StringComparison.Ordinal)))
                .OrderBy(t=>t.transform.position.x).ThenBy(t=>t.transform.position.z).ThenBy(t=>t.terrainData.size.x).ToArray();
            if (terrains.Length != 10)
                throw new InvalidOperationException($"Expected the nine detail tiles and far terrain, found {terrains.Length}.");

            // Validate all targets before creating assets or touching scene bindings.
            foreach (Terrain terrain in terrains)
            {
                TerrainData data=terrain.terrainData;
                if (data.terrainLayers.Length!=2 || data.alphamapTextures.Length!=1)
                    throw new InvalidOperationException($"{terrain.name}: expected the existing two-layer control map.");
                Texture2D control=data.alphamapTextures[0];
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(control,out string guid,out long id) || string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException($"{terrain.name}: the control texture is not persistent.");
            }
            if (!AssetDatabase.IsValidFolder(MaterialRoot))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(MaterialRoot).Replace('\\','/'),"OriginalRegolith");
            var averages=new Dictionary<Texture2D,Color>();
            var changes=new List<string>();
            foreach (Terrain terrain in terrains)
            {
                TerrainData data=terrain.terrainData;
                string stem=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(data));
                string path=MaterialRoot+"/"+stem+"_OriginalRegolith.mat";
                Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material=new Material(source);
                    AssetDatabase.CreateAsset(material,path);
                }
                else { material.shader=source.shader;material.CopyPropertiesFromMaterial(source); }
                material.name=stem+"_OriginalRegolith";
                Color low=Average(data.terrainLayers[0].diffuseTexture,averages);
                Color high=Average(data.terrainLayers[1].diffuseTexture,averages);
                float reference=Mathf.Max(.001f,(Luminance(low)+Luminance(high))*.5f);
                material.SetFloat("_PreserveTerrainPattern",1);
                material.SetTexture("_PreservedTerrainControl",data.alphamapTextures[0]);
                Vector3 p=terrain.transform.position,size=data.size;
                material.SetVector("_PreservedTerrainRect",new Vector4(p.x,p.z,size.x,size.z));
                material.SetVector("_PreservedLowTint",new Vector4(low.r/reference,low.g/reference,low.b/reference,0));
                material.SetVector("_PreservedHighTint",new Vector4(high.r/reference,high.g/reference,high.b/reference,0));
                EditorUtility.SetDirty(material);
                Undo.RecordObject(terrain,"Restore original lunar surface detail");
                terrain.materialTemplate=material;
                // The original explicit shader has no native Terrain/Lit basemap
                // dependency. Keep its mip-filtered world-space detail over the DEM.
                terrain.basemapDistance=8000;
                PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
                changes.Add($"{terrain.name}: {path}");
            }
            // Save only our new material assets, leaving unrelated dirty assets alone.
            foreach (Terrain terrain in terrains) AssetDatabase.SaveAssetIfDirty(terrain.materialTemplate);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save formal terrain material bindings.");
            return "Restored original grain, normals, parallax and lunar lighting on "+terrains.Length+
                " Terrains. Kept existing TerrainData, controls, layers, colliders and base objects.\n"+string.Join("\n",changes);
        }

        private static float Luminance(Color c) => c.r*.2126f+c.g*.7152f+c.b*.0722f;

        private static Color Average(Texture2D texture,Dictionary<Texture2D,Color> cache)
        {
            if (!texture) throw new InvalidOperationException("A current terrain layer has no base-colour texture.");
            if (cache.TryGetValue(texture,out Color value)) return value;
            // Capture only the colour mean; R8's micro texture is not layered over
            // the restored grain. No importer readability or source pixels change.
            RenderTexture previous=RenderTexture.active;
            RenderTexture target=RenderTexture.GetTemporary(32,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            Texture2D sample=null;
            try
            {
                Graphics.Blit(texture,target);
                RenderTexture.active=target;
                sample=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
                sample.ReadPixels(new Rect(0,0,32,32),0,0);sample.Apply();
                Color total=Color.clear;
                foreach (Color pixel in sample.GetPixels()) total+=pixel;
                value=total/(32*32);cache.Add(texture,value);return value;
            }
            finally
            {
                RenderTexture.active=previous;
                RenderTexture.ReleaseTemporary(target);
                if (sample) Object.DestroyImmediate(sample);
            }
        }
    }
}
