// Unity CLI eval_file snippet. Do not run while another task owns the Editor.
// Imports only the hash-bound three-object pilot into a new independent folder.
// It does not open, modify or save the formal map scene.
string root = System.IO.Path.GetFullPath("SourceData/Models/LR06_20261027");
string outFolder = "Assets/MoonEnvironment/Variants/LR06Pilot";
void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
string Hash(string path) { using (var h = System.Security.Cryptography.SHA256.Create())
    using (var f = System.IO.File.OpenRead(path)) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
var jsonType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
object Field(object o, string k) => o.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(o, new object[] { k });
T Read<T>(object o) => (T)o.GetType().GetMethod("ToObject", new[] { typeof(Type) }).Invoke(o, new object[] { typeof(T) });
object[] Rows(object o) => ((System.Collections.IEnumerable)o).Cast<object>().ToArray();
var plan = jsonType.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
    new object[] { System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PilotImportPlan.json")), typeof(object) });
var allAssets = Rows(Field(plan, "assets"));
var materialRows = Rows(Field(plan, "materials"));
Need(Read<string>(Field(plan, "status")) == "awaiting_Unity_import_and_visual_validation", "Unexpected pilot plan status");
Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer,
    "Unity Editor is busy; leave the pilot for a serial window");
Need(!AssetDatabase.IsValidFolder(outFolder) && !System.IO.Directory.Exists(outFolder), "Pilot destination already exists; inspect it first");
foreach (var row in allAssets)
{
    string p = System.IO.Path.Combine(root, Read<string>(Field(row, "path")));
    Need(System.IO.File.Exists(p) && new System.IO.FileInfo(p).Length == Read<long>(Field(row, "bytes")) &&
         Hash(p) == Read<string>(Field(row, "sha256")), "Pilot source file missing or changed: " + p);
}

string guid = AssetDatabase.CreateFolder("Assets/MoonEnvironment/Variants", "LR06Pilot");
Need(!string.IsNullOrEmpty(guid), "Could not create isolated pilot folder");
AssetDatabase.CreateFolder(outFolder, "Models");
AssetDatabase.CreateFolder(outFolder, "Textures");
AssetDatabase.CreateFolder(outFolder, "Materials");
AssetDatabase.CreateFolder(outFolder, "Prefabs");
var copied = new Dictionary<string, string>();
foreach (var row in allAssets)
{
    string role = Read<string>(Field(row, "role"));
    if (role == "roughness") continue; // Packed into the metallic/smoothness map.
    string from = System.IO.Path.Combine(root, Read<string>(Field(row, "path")));
    string dest = outFolder + (role == "model" ? "/Models/" : "/Textures/") + System.IO.Path.GetFileName(from);
    System.IO.File.Copy(from, dest, false);
    Need(Hash(dest) == Read<string>(Field(row, "sha256")), "Copied file hash mismatch: " + dest);
    copied.Add(System.IO.Path.GetFileName(from), dest);
    AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
}
string modelPath = copied["NR12_corridor_pilot.fbx"];
var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
Need(modelImporter, "No FBX model importer");
modelImporter.globalScale = 1; modelImporter.useFileScale = true; modelImporter.preserveHierarchy = true;
modelImporter.bakeAxisConversion = false; modelImporter.importCameras = false; modelImporter.importLights = false;
modelImporter.importAnimation = false; modelImporter.addCollider = false; modelImporter.generateSecondaryUV = false;
modelImporter.swapUVChannels = false; modelImporter.meshCompression = ModelImporterMeshCompression.Off;
modelImporter.importNormals = ModelImporterNormals.Import; modelImporter.importTangents = ModelImporterTangents.CalculateMikk;
modelImporter.isReadable = true; modelImporter.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
modelImporter.SaveAndReimport();
foreach (var path in copied.Values.Where(p => p.Contains("/Textures/")))
{
    var t = AssetImporter.GetAtPath(path) as TextureImporter;
    Need(t, "No texture importer: " + path);
    bool normal = path.Contains("_normal.exr"), mask = path.Contains("_metallic_smoothness.png");
    t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
    t.sRGBTexture = !normal && !mask; t.alphaIsTransparency = false; t.maxTextureSize = 512;
    t.SaveAndReimport();
}
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Need(model, "Pilot FBX could not be loaded");
var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var instance = PrefabUtility.InstantiatePrefab(model, preview) as GameObject;
    Need(instance, "Pilot FBX could not be instantiated in preview scene");
    instance.name = "NR12_Corridor_LR06_Pilot";
    var filters = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
    Need(filters.Length == 3 && materialRows.Length == 3, "Expected exactly three pilot meshes");
    float Error(float yaw)
    {
        float worst = 0;
        var correction = Matrix4x4.Rotate(Quaternion.Euler(0, yaw, 0));
        foreach (var row in materialRows)
        {
            string name = Read<string>(Field(row, "object"));
            var candidates = filters.Where(f => f.name == name).ToArray();
            Need(candidates.Length == 1, "Pilot mesh name is missing or ambiguous: " + name);
            var bb = Read<float[][]>(Field(row, "source_world_bounds"));
            var min = new Vector3(bb[0][0], bb[0][2], bb[0][1]);
            var max = new Vector3(bb[1][0], bb[1][2], bb[1][1]);
            var actual = GeometryUtility.CalculateBounds(candidates[0].sharedMesh.vertices,
                correction * candidates[0].transform.localToWorldMatrix);
            worst = Mathf.Max(worst, Vector3.Distance(actual.min, min), Vector3.Distance(actual.max, max));
        }
        return worst;
    }
    float identityError = Error(0), halfTurnError = Error(180);
    float yaw = identityError <= halfTurnError ? 0 : 180;
    float boundError = Mathf.Min(identityError, halfTurnError);
    Need(boundError < .003f, "Pilot unit/axis bounds differ by " + boundError + " m; no prefab saved");
    instance.transform.rotation = Quaternion.Euler(0, yaw, 0);

    Texture2D Texture(string filename) => AssetDatabase.LoadAssetAtPath<Texture2D>(copied[filename]);
    var shader = Shader.Find("Universal Render Pipeline/Lit");
    Need(shader, "URP Lit shader is unavailable");
    var materialPaths = new List<string>();
    for (int i = 0; i < materialRows.Length; i++)
    {
        var row = materialRows[i];
        string name = Read<string>(Field(row, "object"));
        var filter = filters.Single(f => f.name == name);
        var renderer = filter.GetComponent<MeshRenderer>();
        Need(renderer && filter.sharedMesh.subMeshCount == 1, "Unexpected renderer/submesh layout: " + name);
        var mat = new Material(shader) { name = "LR06Pilot_" + i, enableInstancing = true };
        float metallic = Read<float>(Field(row, "metallic")), roughness = Read<float>(Field(row, "roughness"));
        mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", 1 - roughness);
        if (i < 2)
        {
            var baseMap = Texture("pilot_" + i + "_basecolor.png");
            var mask = Texture("pilot_" + i + "_metallic_smoothness.png");
            var normal = Texture("pilot_" + i + "_normal.exr");
            Need(baseMap && mask && normal, "Missing baked texture for " + name);
            mat.SetColor("_BaseColor", Color.white); mat.SetTexture("_BaseMap", baseMap);
            mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Smoothness", 1);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            var c = Read<float[]>(Field(row, "base_color"));
            mat.SetColor("_BaseColor", new Color(c[0], c[1], c[2], c[3]).gamma);
            var e = Read<float[]>(Field(row, "emission_color"));
            float intensity = Read<float>(Field(row, "emission_strength"));
            mat.SetColor("_EmissionColor", new Color(e[0], e[1], e[2], e[3]) * intensity);
            // URP's material postprocessor derives _EMISSION from AnyEmissive.
            // None would silently disable the authored glowing diffuser on import.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            mat.EnableKeyword("_EMISSION");
        }
        string path = outFolder + "/Materials/LR06Pilot_" + i + ".mat";
        AssetDatabase.CreateAsset(mat, path); AssetDatabase.SaveAssetIfDirty(mat);
        renderer.sharedMaterials = new[] { mat }; materialPaths.Add(path);
    }
    string prefabPath = outFolder + "/Prefabs/NR12_Corridor_LR06_Pilot.prefab";
    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool saved);
    Need(saved && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), "Could not save pilot prefab");
    var report = new { status = "isolated_pilot_only", model = modelPath, prefab = prefabPath,
        identityError, halfTurnError, correctionYaw = yaw, maxBoundsError = boundError,
        objects = filters.Select(f => new { f.name, vertices = f.sharedMesh.vertexCount,
            triangles = f.sharedMesh.triangles.Length / 3, materials = f.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m ? m.name : null).ToArray() }).ToArray(),
        materials = materialPaths, sourceAreaLightsConverted = false, formalSceneChanged = false, visualAndLightingValidated = false };
    string serialized = (string)jsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
    System.IO.File.WriteAllText(System.IO.Path.Combine(root, "NR12Pilot_Corrected/UnityImport.json"), serialized);
    return report;
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
