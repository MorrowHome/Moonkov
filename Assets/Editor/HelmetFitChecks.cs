using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HelmetFitChecks
{
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play.");
        var evidence = new List<string>();
        foreach (var path in new[] { "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab", "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab" })
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                for (var p = animator.transform; p; p = p.parent) p.gameObject.SetActive(true);
                animator.runtimeAnimatorController = null; animator.Rebind(); animator.Update(0);
                var protection = root.GetComponentInChildren<EnergyShieldPresentation>(true);
                if (!protection) protection = animator.gameObject.AddComponent<EnergyShieldPresentation>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                if (!(bool)typeof(EnergyShieldPresentation).GetMethod("EnsureReady", flags).Invoke(protection, null)) throw new InvalidOperationException("Protection initialization failed.");
                var size = (Vector3)typeof(EnergyShieldPresentation).GetField("m_VisorSize", flags).GetValue(protection);
                if (size.x < .08f || size.x > .26f) throw new InvalidOperationException("Head fit outside expected metre range: " + size);
                if (path.Contains("HatsuneMiku") && size.y > .34f) throw new InvalidOperationException("Miku dome still has excessive crown height: " + size.y);
                evidence.Add("PASS: fitted skull radius=" + size.x.ToString("F4") + "m, height=" + size.y.ToString("F4") + "m / " + path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        Directory.CreateDirectory("Temp/BuildFixReview/final"); File.WriteAllLines("Temp/BuildFixReview/final/helmet-fit.txt", evidence);
    }
}
