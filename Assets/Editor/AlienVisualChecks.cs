using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class AlienVisualChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Capture face attachment preview")]
    public static void CaptureFace()
    {
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var root = new GameObject("Temporary face attachment preview"); SceneManager.MoveGameObjectToScene(root, scene);
        var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/FaceHugger.asset");
        AlienCreatureBody body = null; RenderTexture target = null; Texture2D pixels = null; Material headMaterial = null; Camera camera = null;
        try
        {
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere); head.transform.SetParent(root.transform, false);
            head.transform.localPosition = Vector3.up * 1.6f; head.transform.localScale = new Vector3(.37f, .45f, .38f);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            headMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); headMaterial.SetColor("_BaseColor", new Color(.65f, .65f, .65f));
            head.GetComponent<Renderer>().sharedMaterial = headMaterial;
            var creature = new GameObject("Attached face parasite"); creature.transform.SetParent(root.transform, false);
            creature.transform.position = new Vector3(0, 1.6f, .12f);
            body = new AlienCreatureBody(creature.transform, profile, profile.Scale, true, LayerMask.NameToLayer("ClientPlayer"));
            body.SetAttachedPose(creature.transform.position, Quaternion.identity);
            body.SetCombatPresentation(AlienAction.Attached, .5f, 1, 100);
            for(int frame=0;frame<16;frame++) body.Tick(Vector3.zero, 0, 0, 1f/60, 1, true);
            var lamp = new GameObject("Preview light"); lamp.transform.SetParent(root.transform, false);
            var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            lamp.transform.rotation = Quaternion.Euler(35, -25, 0);
            var cameraObject = new GameObject("Preview camera"); cameraObject.transform.SetParent(root.transform, false);
            camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.1f, .12f, .15f); camera.fieldOfView = 34; camera.nearClipPlane = .02f;
            camera.transform.position = new Vector3(.55f, 1.92f, 1.45f); camera.transform.LookAt(new Vector3(0, 1.6f, .08f));
            camera.farClipPlane = 20;
            root.transform.position += new Vector3(-9500, 1000, -9500);
            target = new RenderTexture(640, 640, 24);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            try { RenderTexture.active = target; pixels = new Texture2D(640, 640, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); pixels.Apply(); }
            finally { RenderTexture.active = previous; }
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllBytes("Library/BuildValidation/AlienIntegration/face-attachment.png", pixels.EncodeToPNG());
            Debug.Log("Face attachment preview saved; cosmetic render only, not first-person or network acceptance.");
        }
        finally
        {
            body?.Dispose(); if (camera) camera.targetTexture = null;
            if (target) Object.DestroyImmediate(target); if (pixels) Object.DestroyImmediate(pixels);
            if (headMaterial) Object.DestroyImmediate(headMaterial); Object.DestroyImmediate(root);
            SceneManager.SetActiveScene(previousScene); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
