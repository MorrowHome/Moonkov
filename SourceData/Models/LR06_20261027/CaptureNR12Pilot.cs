// Unity CLI eval_file snippet. Only uses a disposable Editor preview scene.
void Need(bool value, string message) { if (!value) throw new Exception(message); }
Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer,
    "Unity Editor is busy");
string pilot = "Assets/MoonEnvironment/Variants/LR06Pilot";
string prefabPath = pilot + "/Prefabs/NR12_Corridor_LR06_Pilot.prefab";
string output = "Library/BuildValidation/LR06Pilot";
System.IO.Directory.CreateDirectory(output);
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
Need(prefab, "Missing isolated pilot prefab");
var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
bool sceneDirtyBefore = activeScene.isDirty;
string sceneNameBefore = activeScene.name;
var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
RenderTexture target = null;
Texture2D pixels = null;
var oldActive = RenderTexture.active;
try
{
    var instance = PrefabUtility.InstantiatePrefab(prefab, preview) as GameObject;
    Need(instance, "Failed to instantiate pilot in preview scene");
    var meshes = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
    Need(meshes.Length == 3, "Expected three pilot meshes");
    var materialChecks = meshes.Select(f => {
        var r = f.GetComponent<MeshRenderer>();
        Need(r && r.sharedMaterials.Length == 1 && r.sharedMaterial, "Missing pilot material: " + f.name);
        var m = r.sharedMaterial;
        Need(m.shader && m.shader.name == "Universal Render Pipeline/Lit", "Wrong shader: " + f.name);
        bool light = f.name.Contains("Diffuser");
        if (light) Need(m.IsKeywordEnabled("_EMISSION") && m.GetColor("_EmissionColor").maxColorComponent > 1,
                        "Warm diffuser emission is missing");
        else Need(m.GetTexture("_BaseMap") && m.GetTexture("_MetallicGlossMap") && m.GetTexture("_BumpMap") &&
                  m.IsKeywordEnabled("_NORMALMAP") && m.IsKeywordEnabled("_METALLICSPECGLOSSMAP"),
                  "Baked material textures are missing: " + f.name);
        return new { f.name, mesh = AssetDatabase.GetAssetPath(f.sharedMesh), material = AssetDatabase.GetAssetPath(m),
            shader = m.shader.name, emission = m.GetColor("_EmissionColor").ToString(),
            baseMap = m.GetTexture("_BaseMap") ? AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")) : null,
            mask = m.GetTexture("_MetallicGlossMap") ? AssetDatabase.GetAssetPath(m.GetTexture("_MetallicGlossMap")) : null,
            normal = m.GetTexture("_BumpMap") ? AssetDatabase.GetAssetPath(m.GetTexture("_BumpMap")) : null,
            uvCount = f.sharedMesh.uv.Length, normalCount = f.sharedMesh.normals.Length, tangentCount = f.sharedMesh.tangents.Length };
    }).ToArray();

    var cameraObject = new GameObject("Temporary pilot capture camera");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, preview);
    var camera = cameraObject.AddComponent<Camera>();
    camera.scene = preview; camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(preview);
    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .055f);
    camera.fieldOfView = 48; camera.nearClipPlane = .05f; camera.farClipPlane = 100; camera.allowHDR = true;

    var sunObject = new GameObject("Temporary neutral preview fill");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sunObject, preview);
    var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = .65f;
    sun.transform.rotation = Quaternion.Euler(48, -32, 0); sun.shadows = LightShadows.Soft;
    var warmObject = new GameObject("Temporary illustrative warm preview light");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(warmObject, preview);
    var warm = warmObject.AddComponent<Light>(); warm.type = LightType.Point;
    warm.transform.position = new Vector3(1998.175f, 255.9f, 2462.6f);
    warm.color = new Color(1, .68f, .38f); warm.intensity = 9; warm.range = 8; warm.shadows = LightShadows.Soft;
    target = new RenderTexture(1200, 800, 24, RenderTextureFormat.ARGB32); Need(target.Create(), "Could not create capture texture");
    pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
    var shots = new List<object>();
    void Capture(string label, Vector3 position, Vector3 look)
    {
        camera.transform.position = position; camera.transform.LookAt(look);
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
        string path = output + "/" + label + ".png";
        System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
        shots.Add(new { path, position = new[] { position.x, position.y, position.z },
            look = new[] { look.x, look.y, look.z }, illustrativeWarmPointIntensity = warm.intensity });
    }
    Capture("fixture", new Vector3(1993.8f, 254.2f, 2457.6f), new Vector3(1998.4f, 255.3f, 2463.0f));
    Capture("wall_front", new Vector3(1992.5f, 254.6f, 2467.2f), new Vector3(2000.5f, 254.2f, 2468.0f));
    Capture("wall_back", new Vector3(2008.5f, 254.6f, 2467.2f), new Vector3(2000.5f, 254.2f, 2468.0f));
    Need(activeScene.name == sceneNameBefore && activeScene.isDirty == sceneDirtyBefore,
        "Original active scene changed during preview");
    var report = new { scope = "isolated_editor_preview_only", formalSceneChanged = false, sourceLightsConverted = false,
        note = "Preview uses temporary illustrative lighting; it is not a physical match to Blender or a gameplay test.",
        materialChecks, shots, originalScene = sceneNameBefore, originalSceneDirty = sceneDirtyBefore };
    var jsonType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
    string serialized = (string)jsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
    System.IO.File.WriteAllText(output + "/UnityPreview.json", serialized);
    return report;
}
finally
{
    RenderTexture.active = oldActive;
    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
    if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
}
