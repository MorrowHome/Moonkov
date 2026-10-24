using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class WeaponFramingChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static void Run()
    {
        var evidence = new System.Collections.Generic.List<string>();
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play.");
        foreach (var path in new[] { "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab", "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab" })
        {
            var scene = EditorSceneManager.NewPreviewScene(); DollSingerFirstPersonHands hands = null;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                for (var p = animator.transform; p; p = p.parent) p.gameObject.SetActive(true);
                animator.runtimeAnimatorController = null; animator.Rebind(); animator.Update(0);
                var hierarchy = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var source = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).First();
                var cameraNode = new GameObject("Isolated hands check camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraNode, scene);
                var camera = cameraNode.AddComponent<Camera>(); camera.enabled = false; camera.aspect = 16f / 9;
                camera.transform.SetPositionAndRotation(animator.GetBoneTransform(HumanBodyBones.Head).position, Quaternion.identity);
                hands = new DollSingerFirstPersonHands(source, source.sharedMesh, camera, animator);
                var halo = animator.GetComponent<DollSingerHaloAim>();
                var gestures = new DollSingerBakedGestures(animator, (TextAsset)new SerializedObject(halo).FindProperty("gestureBake").objectReferenceValue);
                for (int weapon = 1; weapon <= 4; weapon++)
                {
                    gestures.RestoreBeforeAnimation(); gestures.Apply(weapon, 1, 10, false, 0, .01f, hierarchy["上半身2"], Vector3.forward, 1);
                    hands.Present(true, 65, camera, false, 1); hands.FrameHands(true);
                    var projection = hands.Camera.projectionMatrix; var position = hands.Camera.transform.localPosition;
                    gestures.RestoreBeforeAnimation(); gestures.Apply(weapon, 1, .04f, false, 0, .01f, hierarchy["上半身2"], Vector3.forward, 1);
                    hands.FrameHands(false, true);
                    Require(hands.Camera.projectionMatrix == projection && hands.Camera.transform.localPosition == position, "Firing changed hand framing: " + path + "/" + weapon);
                }
                gestures.RestoreBeforeAnimation();
                evidence.Add("PASS: four firearm projections fixed during recoil / " + path);
            }
            finally { hands?.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }
        Directory.CreateDirectory("Temp/BuildFixReview/final"); File.WriteAllLines("Temp/BuildFixReview/final/firearm-framing.txt", evidence);
    }

}
