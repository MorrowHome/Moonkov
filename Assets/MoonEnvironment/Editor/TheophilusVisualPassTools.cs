using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Formal-map-only finish pass. Shared source materials and lightmap bases stay intact.</summary>
    public static class TheophilusVisualPassTools
    {
        public const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        public const string Folder = "Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01";
        private static readonly HashSet<string> Finishes = new HashSet<string>(StringComparer.Ordinal)
        {
            "LD2_Painted_Metal_Grey", "LD2_Hull_CeramicPaint", "LD2_Structure_GraphiteCoat",
            "LD2_Raw_StainlessSteel", "LD2_Brushed_Aluminium", "LD2_Anodized_DarkAlloy",
            "M05_ENTRY_ContactAlloy", "M05_ENTRY_DustedBase", "M05_ENTRY_CleanCeramic"
        };

        [MenuItem("Tools/Moon Environment/Theophilus/Apply visual pass 01")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply visual authoring in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try { ApplyToScene(scene); }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        private static void ApplyToScene(Scene scene)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var frame = all.Single(t => t.name == "MC00_ROOT_Standard_YUp_Frame");
            var entry = all.Single(t => t.name == "ORG_East_EVA_Pressure_Entry_01a046");
            var room = all.Single(t => t.name == "ORG_ROOM_G03_0eae18");
            var probe = all.Single(t => t.name == "ORG_ROOM_G03_0eae18 reflection").GetComponent<ReflectionProbe>();
            var volume = all.Select(t => t.GetComponent<Volume>()).Single(v => v && v.isGlobal && v.priority == 10);
            var shader = Shader.Find("MoonEnvironment/Industrial Dust");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Industrial Dust shader unavailable.");
            Directory.CreateDirectory(Folder + "/Materials");
            Directory.CreateDirectory(Folder + "/Meshes");
            Directory.CreateDirectory(Folder + "/Textures");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var dust = MakeDust();
            var profile = MakeProfile();
            Undo.RecordObject(volume, "Theophilus formal color grade");
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
            PrefabUtility.RecordPrefabInstancePropertyModifications(volume);

            var copies = new Dictionary<Material, Material>();
            bool InTarget(MeshRenderer renderer) => renderer &&
                (renderer.transform.IsChildOf(entry) || renderer.transform.IsChildOf(room));
            int changed = 0, combinedChanged = 0;
            Material Finish(Material original)
            {
                if (!original || !Finishes.Contains(original.name)) return original;
                if (copies.TryGetValue(original, out var cached)) return cached;
                var path = Folder + "/Materials/" + original.name + "_Dust.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = new Material(original) { name = original.name + "_Dust" };
                    material.shader = shader;
                    // Drop unrelated Lit variants; preserve the supported original texture channels.
                    material.shaderKeywords = Array.Empty<string>();
                    if (original.GetTexture("_BumpMap")) material.EnableKeyword("_NORMALMAP");
                    if (original.GetTexture("_MetallicGlossMap")) material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    if (original.GetTexture("_OcclusionMap")) material.EnableKeyword("_OCCLUSIONMAP");
                    material.SetTexture("_DustMap", dust);
                    material.SetColor("_DustColor", new Color(.40f, .385f, .35f));
                    material.SetFloat("_DustAmount", .62f);
                    material.SetFloat("_DustFloorY", frame.position.y + .60f);
                    material.SetFloat("_DustTiling", .70f);
                    if (original.name.Contains("Stainless") || original.name.Contains("Aluminium"))
                    {
                        // A broad brushed reflection, never a mirror finish.
                        material.SetFloat("_Smoothness", .48f);
                        if (!original.GetTexture("_MetallicGlossMap")) material.SetFloat("_Metallic", .8f);
                    }
                    material.enableInstancing = true;
                    AssetDatabase.CreateAsset(material, path);
                }
                copies.Add(original, material);
                return material;
            }
            bool Swap(MeshRenderer renderer)
            {
                var before = renderer.sharedMaterials;
                var after = before.Select(Finish).ToArray();
                if (before.SequenceEqual(after)) return false;
                Undo.RecordObject(renderer, "Scene-owned industrial finish");
                renderer.sharedMaterials = after;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
                return true;
            }
            foreach (var renderer in entry.GetComponentsInChildren<MeshRenderer>(true)
                         .Concat(room.GetComponentsInChildren<MeshRenderer>(true)))
                if (Swap(renderer)) changed++;
            // Static display clusters are the visible representation; finish their materials too.
            // Mixed-room clusters deliberately remain unchanged to keep the scope local.
            var clusters = frame.GetComponentInChildren<BuildingRenderClusters>();
            if (clusters)
            {
                var groups = new SerializedObject(clusters).FindProperty("groups");
                for (int i = 0; i < groups.arraySize; i++)
                {
                    var group = groups.GetArrayElementAtIndex(i);
                    var sources = group.FindPropertyRelative("sources");
                    bool eligible = sources.arraySize > 0;
                    for (int j = 0; j < sources.arraySize && eligible; j++)
                        eligible &= InTarget(sources.GetArrayElementAtIndex(j).objectReferenceValue as MeshRenderer);
                    if (!eligible) continue;
                    var combined = group.FindPropertyRelative("combined").objectReferenceValue as MeshRenderer;
                    if (combined && Swap(combined)) combinedChanged++;
                }
            }

            var existingRoot = all.FirstOrDefault(t => t.name == "Theophilus Visual Pass 01");
            if (existingRoot && !existingRoot.Find("E01 surface prints"))
            {
                UnityEngine.Object.DestroyImmediate(existingRoot.gameObject);
                existingRoot = null;
            }
            if (!existingRoot)
            {
                var root = new GameObject("Theophilus Visual Pass 01");
                root.transform.SetPositionAndRotation(frame.position, frame.rotation);
                MakeMarkings(root.transform, frame);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Folder + "/EntryMarkings.prefab", InteractionMode.AutomatedAction);
            }
            else MakeMarkings(existingRoot, frame);
            // Reuse the existing room/circuit probe; no new runtime capture or light is added.
            Undo.RecordObject(probe, "Service bay reflection finish");
            probe.intensity = 1.15f;
            probe.blendDistance = .65f;
            probe.importance = 110;
            probe.resolution = 128;
            EditorUtility.SetDirty(probe);
            var reference = AssetDatabase.LoadAssetAtPath<Texture>(Folder + "/Textures/G03Reflection.exr");
            if (reference) probe.customBakedTexture = reference;
            PrefabUtility.RecordPrefabInstancePropertyModifications(probe);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save formal scene.");
            Debug.Log($"Theophilus visual pass: {changed} source renderers, {combinedChanged} display clusters, {copies.Count} material copies; static entry markings and G03 reflection.");
        }

        private static VolumeProfile MakeProfile()
        {
            var path = Folder + "/TheophilusLook.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Theophilus formal look";
            AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.ACES;
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = .75f;
            color.contrast.value = 7;
            color.saturation.value = -7;
            var split = profile.Add<SplitToning>(true);
            split.shadows.value = new Color(.482f,.493f,.512f);
            split.highlights.value = new Color(.518f,.507f,.486f);
            split.balance.value = 8;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 1.5f;
            bloom.intensity.value = .18f;
            bloom.scatter.value = .5f;
            bloom.highQualityFiltering.value = false;
            bloom.downscale.value = BloomDownscaleMode.Quarter;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = .085f;
            vignette.smoothness.value = .75f;
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        [MenuItem("Tools/Moon Environment/Theophilus/Apply soft light atmosphere")]
        public static void ApplyAtmosphere()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Tune the formal look in Edit Mode.");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Folder + "/TheophilusLook.asset");
            if (!profile || !profile.TryGet<Bloom>(out var bloom) ||
                !profile.TryGet<ColorAdjustments>(out var color) || !profile.TryGet<SplitToning>(out var split))
                throw new InvalidOperationException("Apply visual pass 01 before tuning atmosphere.");
            Undo.RecordObjects(new UnityEngine.Object[] { bloom, color, split }, "Theophilus soft light atmosphere");
            bloom.intensity.Override(3);
            bloom.threshold.Override(.55f);
            bloom.scatter.Override(.92f);
            bloom.filter.Override(BloomFilterMode.Dual);
            bloom.clamp.Override(32);
            bloom.tint.Override(new Color(1,.965f,.92f));
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            color.contrast.Override(4);
            color.saturation.Override(0);
            split.shadows.Override(new Color(.47f,.488f,.522f));
            split.highlights.Override(new Color(.54f,.514f,.476f));
            split.balance.Override(12);
            EditorUtility.SetDirty(bloom);EditorUtility.SetDirty(color);EditorUtility.SetDirty(split);
            foreach (string name in new[] { "LD2_Painted_Metal_Grey", "LD2_Hull_CeramicPaint", "LD2_Anodized_DarkAlloy",
                         "LD2_Brushed_Aluminium", "LD2_Raw_StainlessSteel" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + name + "_Dust.mat");
                if (!material) throw new InvalidOperationException("Missing formal finish: " + name);
                Undo.RecordObject(material, "Clean paint sheen");
                bool paint = name.Contains("Paint") || name.Contains("DarkAlloy");
                material.SetFloat("_CoatStrength", paint ? .3f : 0);
                material.SetFloat("_CoatSmoothness", .82f);
                if (paint) material.EnableKeyword("_CLEARCOAT");
                else { material.DisableKeyword("_CLEARCOAT"); material.SetFloat("_Smoothness",.62f); }
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Moon Environment/Theophilus/Apply atmosphere lamp emission")]
        public static void ApplyAtmosphereLamps()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Author atmosphere lamps in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the formal Theophilus map first.");
            var controls = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LightCircuitControls>(true)).Single();
            var serialized = new SerializedObject(controls);
            var bindings = serialized.FindProperty("lights");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                var fixture = binding.FindPropertyRelative("fixture").objectReferenceValue as Light;
                if (!fixture || !fixture.transform.parent) continue;
                string name = fixture.transform.parent.name;
                if (!name.Contains("G03") && !name.Contains("East EVA")) continue;
                var targets = binding.FindPropertyRelative("emissiveTargets");
                for (int j = 0; j < targets.arraySize; j++)
                {
                    var target = targets.GetArrayElementAtIndex(j);
                    var original = target.FindPropertyRelative("onMaterial").objectReferenceValue as Material;
                    var renderer = target.FindPropertyRelative("renderer").objectReferenceValue as Renderer;
                    int slot = target.FindPropertyRelative("materialSlot").intValue;
                    if (!original || !renderer) throw new InvalidOperationException("Missing lamp binding.");
                    bool amber = original.name.Contains("Amber") || original.name.Contains("Warm");
                    string path = Folder + "/Materials/" + (amber ? "AtmosphereAmberOn.mat" : "AtmosphereTaskOn.mat");
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!material)
                    {
                        material = new Material(original) { name = amber ? "Theophilus amber lamp on" : "Theophilus warm white lamp on" };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    // More optical bloom energy, without additional realtime illumination or shadows.
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", amber ? new Color(18,7,1.8f) : new Color(18,13,8));
                    EditorUtility.SetDirty(material);
                    var slots = renderer.sharedMaterials;
                    // Preserve a currently switched-off fixture. Its unchanged off material remains bound.
                    if (slots[slot] == original)
                    {
                        Undo.RecordObject(renderer, "Atmosphere lamp emission");
                        slots[slot] = material; renderer.sharedMaterials = slots;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        EditorUtility.SetDirty(renderer);
                    }
                    target.FindPropertyRelative("onMaterial").objectReferenceValue = material;
                }
            }
            serialized.ApplyModifiedProperties();
            // Rebind the existing adapter so its cached slot arrays reference the new on-materials.
            // A fresh scene already starts this way; reapplying the tool must behave identically.
            var owner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BuildingBakedLighting>(true)).Single();
            controls.ConfigureBaked(owner);
            EditorUtility.SetDirty(controls);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controls);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save atmosphere lamp bindings.");
        }

        private static Texture2D MakeDust()
        {
            var path = Folder + "/Textures/IndustrialDustMask.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(256,256,TextureFormat.RGB24,false,true);
                var pixels = new Color[256*256];
                for (int y=0;y<256;y++) for (int x=0;x<256;x++)
                {
                    // Periodic noise: the repeated world-space mask has no border seam.
                    float Periodic(float scale, float ox, float oy)
                    {
                        float u=x/256f,v=y/256f;
                        return Mathf.Lerp(Mathf.Lerp(Mathf.PerlinNoise(ox+u*scale,oy+v*scale),Mathf.PerlinNoise(ox+(u-1)*scale,oy+v*scale),u),
                            Mathf.Lerp(Mathf.PerlinNoise(ox+u*scale,oy+(v-1)*scale),Mathf.PerlinNoise(ox+(u-1)*scale,oy+(v-1)*scale),u),v);
                    }
                    float broad=Periodic(7,13,27),fine=Periodic(48,51,9);
                    pixels[y*256+x]=new Color(Mathf.Clamp01((broad-.35f)*2+fine*.18f),fine,Periodic(24,87,32));
                }
                texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            }
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=false;importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void MakeMarkings(Transform root, Transform frame)
        {
            var atlasPath=Folder+"/Textures/EntryMarkings.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);
            if (!importer) throw new InvalidOperationException("EntryMarkings atlas missing.");
            importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;
            importer.anisoLevel=4;importer.SaveAndReimport();
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/EntryPrint.mat");
            if(!material)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Theophilus worn entry print"};
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
                material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);
                material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_Smoothness",.15f);material.SetFloat("_Cull",2);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType","Transparent");material.renderQueue=3000;
                AssetDatabase.CreateAsset(material,Folder+"/Materials/EntryPrint.mat");
            }
            var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Rect uv)
            {
                int i=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
                uvs.AddRange(new[]{new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMax,uv.yMin),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMin,uv.yMax)});
                triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            }
            Vector3 Ground(float x,float z)
            {
                var origin=frame.TransformPoint(new Vector3(x,3.3f,z));
                var hits=Physics.RaycastAll(origin,Vector3.down,8,~0,QueryTriggerInteraction.Ignore);
                var hit=hits.Where(h=>h.normal.y>.7f).OrderBy(h=>h.distance).FirstOrDefault();
                if(!hit.collider)throw new InvalidOperationException($"No walk surface for entry marking {x}/{z}.");
                return root.InverseTransformPoint(hit.point+Vector3.up*.004f);
            }
            // Floor strip and four boot treads lie on the actual authored walkway.
            Quad(Ground(18.8f,-34.2f),Ground(21.2f,-34.2f),Ground(21.2f,-34.52f),Ground(18.8f,-34.52f),new Rect(0,.383f,1,.109f));
            for(int i=0;i<4;i++)
            {
                float x=20+(i%2==0?-.17f:.17f),z=-35.25f-i*.65f;
                Quad(Ground(x-.11f,z+.20f),Ground(x+.11f,z+.20f),Ground(x+.11f,z-.20f),Ground(x-.11f,z-.20f),new Rect(.055f,.07f,.125f,.28f));
            }
            var mesh=new Mesh{name="Theophilus entry surface prints"};mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);
            mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var path=Folder+"/Meshes/EntryPrints.asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;EditorUtility.SetDirty(mesh);}
            else AssetDatabase.CreateAsset(mesh,path);
            mesh.UploadMeshData(false);
            var child=root.Find("E01 surface prints");
            var go=child?child.gameObject:new GameObject("E01 surface prints");
            if(!child)go.transform.SetParent(root,false);
            var filter=go.GetComponent<MeshFilter>();if(!filter)filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            var renderer=go.GetComponent<MeshRenderer>();if(!renderer)renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
        }
    }
}
