// Read the independent display scene and capture its unchanged R6 building.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LR06InspectSharedPBR
{
    const string Folder = "Assets/MoonEnvironment/Variants/LR06FullPreview";
    const string ScenePath = Folder + "/Scenes/LR06_Full_Display.unity";
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new Exception("Editor busy");
        if (SceneManager.GetSceneByPath(ScenePath).isLoaded) throw new Exception("Display scene already open");
        var oldScene = SceneManager.GetActiveScene();
        var oldPath = oldScene.path; var oldDirty = oldScene.isDirty;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var prior = RenderTexture.active;
        RenderTexture target = null; Texture2D pixels = null;
        try
        {
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var camera = roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Single();
            var lights = roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).ToArray();
            var filters = roots.SelectMany(x => x.GetComponentsInChildren<MeshFilter>(true)).Where(x => x.sharedMesh).ToArray();
            var targetFilter = filters.Single(x => AssetDatabase.GetAssetPath(x.sharedMesh).EndsWith("/Map_07.fbx"));
            var mesh = targetFilter.sharedMesh;
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh));
            string readResult;
            try { readResult = "vertices=" + mesh.vertices.Length + ", uv=" + mesh.uv.Length + ", tangents=" + mesh.tangents.Length; }
            catch (Exception error) { readResult = error.GetType().Name + ": " + error.Message; }
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(1840, 255, 2255);
            camera.transform.LookAt(new Vector3(1873, 240, 2298));
            target = new RenderTexture(960, 600, 24, RenderTextureFormat.ARGB32); target.Create();
            pixels = new Texture2D(960, 600, TextureFormat.RGB24, false);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); pixels.Apply();
            System.IO.Directory.CreateDirectory("Library/BuildValidation/LR06PBR");
            System.IO.File.WriteAllBytes("Library/BuildValidation/LR06PBR/UnityR6T06Before.png", pixels.EncodeToPNG());
            var additional = camera.GetUniversalAdditionalCameraData();
            var report = new { status = "unchanged_display_scene_inspection", unityVersion = Application.unityVersion,
                colorSpace = QualitySettings.activeColorSpace.ToString(), renderPipeline = GraphicsSettings.currentRenderPipeline.name,
                cameraFov = camera.fieldOfView, cameraPostProcessing = additional.renderPostProcessing,
                lights = lights.Length, pointLights = lights.Count(l => l.type == LightType.Point),
                shadowLights = lights.Count(l => l.shadows != LightShadows.None), mesh = mesh.name,
                meshPath = AssetDatabase.GetAssetPath(mesh), meshReadable = mesh.isReadable,
                hasUv0 = mesh.HasVertexAttribute(VertexAttribute.TexCoord0), hasTangents = mesh.HasVertexAttribute(VertexAttribute.Tangent),
                importerTangents = importer.importTangents.ToString(), directMeshRead = readResult,
                overviewSceneCount = filters.Length, originalScene = oldPath, originalSceneDirty = oldDirty };
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06PBR/UnityBefore.json", serialized);
            return report;
        }
        finally
        {
            RenderTexture.active = prior;
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (oldScene.IsValid() && SceneManager.GetActiveScene() != oldScene) SceneManager.SetActiveScene(oldScene);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (oldScene.path != oldPath || oldScene.isDirty != oldDirty) throw new Exception("Original scene changed");
        }
    }
}
