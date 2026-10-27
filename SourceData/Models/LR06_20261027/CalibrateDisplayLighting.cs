// Calibrate only the existing lights in the independent LR06 display scene.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LR06CalibrateDisplayLighting
{
    const string ScenePath = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
    const string Output = "Library/BuildValidation/LR06PBR";
    [Serializable] public class SunRow { public float[] direction_unity, color_linear; public float unity_intensity; public bool soft_shadows; }
    [Serializable] public class PointRow { public string name; public float[] position_unity, source_color_linear; public float intensity, range_metres; public bool shadows; }
    [Serializable] public class Plan { public SunRow source_sun; public PointRow[] point_lights; }
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Trial() => Run(false);
    public static object Apply() => Run(true);
    static object Run(bool apply)
    {
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer, "Editor busy");
        Need(!SceneManager.GetSceneByPath(ScenePath).isLoaded, "Display scene is already open");
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = (Plan)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText("SourceData/Models/LR06_20261027/DisplayLightCalibrationPlan.json"), typeof(Plan) });
        Need(plan != null && plan.point_lights.Length == 31 && plan.point_lights.All(l => !l.shadows), "Unexpected calibration plan");
        var old = SceneManager.GetActiveScene(); string oldPath = old.path; bool oldDirty = old.isDirty;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var previous = RenderTexture.active;
        try
        {
            Need(scene.IsValid() && scene.isLoaded, "Display scene did not open");
            if (SceneManager.GetActiveScene() != scene) Need(SceneManager.SetActiveScene(scene), "Could not activate display scene");
            var roots = scene.GetRootGameObjects();
            var lights = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
            var originallyWithoutExtraData = lights.Where(light => !light.GetComponent<UniversalAdditionalLightData>()).ToArray();
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single();
            var parent = roots.Single(root => root.name == "LR06_Illustrative_Lighting_31_Proxies_No_Shadows" ||
                                              root.name == "LR06_Illustrative_Lighting_31_WarmProxies");
            Need(lights.Length == 32 && lights.Count(l => l.type == LightType.Point) == 31 &&
                 lights.Count(l => l.type == LightType.Directional) == 1 &&
                 lights.Where(l => l.type == LightType.Point).All(l => l.shadows == LightShadows.None), "Unexpected initial lights");
            var points = lights.Where(l => l.type == LightType.Point).ToDictionary(l => l.name, StringComparer.Ordinal);
            Need(plan.point_lights.All(row => points.ContainsKey(row.name)), "Existing proxy missing");
            var originalPosition = camera.transform.position; var originalRotation = camera.transform.rotation;
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            System.IO.Directory.CreateDirectory(Output);
            void Capture(string name, int width, int height, Vector3 position, Vector3 look)
            {
                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                Texture2D pixels = null;
                try
                {
                    Need(target.Create(), "Could not create render target");
                    pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                    camera.transform.position = position; camera.transform.LookAt(look);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
                    System.IO.File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                    target.Release(); UnityEngine.Object.DestroyImmediate(target);
                }
            }
            var r6Position = new Vector3(1840,255,2255); var r6Target = new Vector3(1873,240,2298);
            var overviewPosition = new Vector3(1250,950,1400); var overviewTarget = new Vector3(2000,220,2180);
            var buildingPosition = new Vector3(1920,265,2430); var buildingTarget = new Vector3(1946,255,2470);
            if (!apply)
            {
                Capture("UnityR6T06LightBefore",960,600,r6Position,r6Target);
                Capture("UnityOverviewLightBefore",1600,1000,overviewPosition,overviewTarget);
                Capture("UnityWarmBuildingBefore",1600,1000,buildingPosition,buildingTarget);
            }
            var sun = lights.Single(light => light.type == LightType.Directional);
            var sd = plan.source_sun.direction_unity; var sc = plan.source_sun.color_linear;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(sd[0],sd[1],sd[2]));
            sun.color = new Color(sc[0],sc[1],sc[2]).gamma;
            sun.intensity = plan.source_sun.unity_intensity;
            sun.shadows = plan.source_sun.soft_shadows ? LightShadows.Soft : LightShadows.None;
            sun.name = "Source preview sun (direction approximated)";
            foreach (var row in plan.point_lights)
            {
                var light = points[row.name]; var p = row.position_unity; var c = row.source_color_linear;
                light.transform.position = new Vector3(p[0],p[1],p[2]);
                light.color = new Color(c[0],c[1],c[2]).gamma;
                light.intensity = row.intensity; light.range = row.range_metres; light.shadows = LightShadows.None;
            }
            parent.name = "LR06_Illustrative_Lighting_31_WarmProxies";
            Capture(apply ? "UnityR6T06Final" : "UnityR6T06LightTrial",960,600,r6Position,r6Target);
            Capture(apply ? "UnityOverviewFinal" : "UnityOverviewLightTrial",1600,1000,overviewPosition,overviewTarget);
            Capture(apply ? "UnityWarmBuildingFinal" : "UnityWarmBuildingTrial",1600,1000,buildingPosition,buildingTarget);
            Capture(apply ? "UnityCorridorFinal" : "UnityCorridorTrial",1600,1000,new Vector3(1998,253.3f,2470),new Vector3(1998,254.3f,2461));
            Capture(apply ? "UnityCentralFinal" : "UnityCentralTrial",1600,1000,new Vector3(1790,420,1830),new Vector3(2000,275,2110));
            camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            Need(lights.Where(light => light.type == LightType.Point).All(light => light.shadows == LightShadows.None), "A point proxy unexpectedly casts shadows");
            if (apply)
            {
                // Render requests can attach default URP data. Do not persist data
                // that was not present on these lights before the capture.
                foreach (var light in originallyWithoutExtraData)
                {
                    var extra = light.GetComponent<UniversalAdditionalLightData>();
                    if (extra) UnityEngine.Object.DestroyImmediate(extra);
                }
                Need(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save display scene");
            }
            var report = new { status = apply ? "display_lighting_saved" : "display_lighting_trial_not_saved",
                lights = lights.Length, warmPointProxies = points.Count, pointShadowLights = points.Values.Count(l => l.shadows != LightShadows.None),
                directionalShadowLights = lights.Count(l => l.type == LightType.Directional && l.shadows != LightShadows.None),
                sunDirection = new[] { sun.transform.forward.x, sun.transform.forward.y, sun.transform.forward.z },
                ambientUnchanged = true, originalScene = oldPath, originalSceneDirty = oldDirty };
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText(Output + (apply ? "/UnityLightingApplied.json" : "/UnityLightingTrial.json"), serialized);
            return report;
        }
        finally
        {
            RenderTexture.active = previous;
            if (old.IsValid() && SceneManager.GetActiveScene() != old) SceneManager.SetActiveScene(old);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            Need(old.path == oldPath && old.isDirty == oldDirty, "Original scene changed during lighting work");
        }
    }

}
