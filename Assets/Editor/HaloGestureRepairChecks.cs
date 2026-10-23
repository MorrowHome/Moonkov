using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class HaloGestureRepairChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static void CaptureHands(string path)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        DollSingerFirstPersonHands hands = null;
        var target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
        var sheet = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        Mesh poseMesh = null;
        var previous = RenderTexture.active;
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"), scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var t = animator.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            animator.runtimeAnimatorController = null; animator.Rebind(); animator.Update(0);
            var source = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).First();
            for (var t = source.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            var cameraObject = new GameObject("Repair pose camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var owner = cameraObject.AddComponent<Camera>(); owner.enabled = false; owner.aspect = 16f / 9;
            var hierarchy = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            owner.transform.position = (hierarchy["目.L"].position + hierarchy["目.R"].position) * .5f + Vector3.forward * .015f;
            owner.transform.rotation = Quaternion.identity;
            hands = new DollSingerFirstPersonHands(source, source.sharedMesh, owner, animator) { StableFraming = true };
            Require(hands.Camera, "Owner arm camera was not constructed.");
            hands.Camera.scene = scene; hands.Camera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
            hands.Camera.clearFlags = CameraClearFlags.SolidColor; hands.Camera.backgroundColor = new Color(.025f, .04f, .07f);
            // Several poses are rendered in one Editor tick. Bake each current skeleton
            // explicitly, so GPU skinning cannot reuse the first camera's frame cache.
            var armRenderer = (SkinnedMeshRenderer)typeof(DollSingerFirstPersonHands).GetField("renderer", Private).GetValue(hands);
            var poseObject = new GameObject("Current skeleton surface"); poseObject.transform.SetParent(armRenderer.transform, false);
            var skinScale = armRenderer.transform.lossyScale;
            poseObject.transform.localScale = new Vector3(1f / skinScale.x, 1f / skinScale.y, 1f / skinScale.z);
            poseObject.layer = armRenderer.gameObject.layer;
            poseMesh = new Mesh(); poseObject.AddComponent<MeshFilter>().sharedMesh = poseMesh;
            poseObject.AddComponent<MeshRenderer>().sharedMaterials = armRenderer.sharedMaterials;
            var keyObject = new GameObject("Repair pose key"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyObject, scene);
            var key = keyObject.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.5f; key.transform.rotation = Quaternion.Euler(25, 155, 0);
            var bake = new DollSingerBakedGestures(animator, AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DollSinger/Animation/Blender/WeaponGestures.json"));
            target.Create();
            int count = 0;
            foreach (var entry in HaloArsenal.Entries)
            {
                bake.RestoreBeforeAnimation(); bake.Apply((int)entry.Gesture, 1, 10, false, 0, .01f, hierarchy["上半身2"], Vector3.forward, 1);
                hands.Present(true, 65, owner, false, 1); hands.FrameHands(true);
                var projection = hands.Camera.projectionMatrix; var position = hands.Camera.transform.localPosition;
                foreach (float shotAge in new[] { 0f, .04f })
                {
                    bake.RestoreBeforeAnimation(); bake.Apply((int)entry.Gesture, 1, shotAge, false, 0, .01f, hierarchy["上半身2"], Vector3.forward, 1);
                    hands.FrameHands(true);
                    Require(hands.Camera.projectionMatrix == projection && hands.Camera.transform.localPosition == position, "Shot animation re-fitted hand perspective: " + entry.Code);
                }
                bake.RestoreBeforeAnimation(); bake.Apply((int)entry.Gesture, 1, 10, false, 0, .01f, hierarchy["上半身2"], Vector3.forward, 1);
                armRenderer.BakeMesh(poseMesh);
                poseMesh.RecalculateBounds();
                poseObject.GetComponent<MeshFilter>().sharedMesh = null;
                poseObject.GetComponent<MeshFilter>().sharedMesh = poseMesh;
                RenderPipeline.SubmitRenderRequest(hands.Camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                sheet.ReadPixels(new Rect(0, 0, 640, 360), count % 3 * 640, (2 - count / 3) * 360, false); count++;
            }
            sheet.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log("Nine expansion signs captured from production skeleton; projection remains fixed during shots: " + path);
        }
        finally
        {
            RenderTexture.active = previous; hands?.Dispose(); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(sheet);
            if (poseMesh) Object.DestroyImmediate(poseMesh);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
