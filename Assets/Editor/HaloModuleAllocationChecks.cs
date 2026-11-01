using System;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class HaloModuleAllocationChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Performance/Check halo module allocation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the isolated checks in Edit Mode.");
        foreach (string path in new[] {
            "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab",
            "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab",
            "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab" }) Check(path);
        Debug.Log("Halo module allocation passed: three rigs, four weapons, lazy creation, reuse, removal and unequip.");
    }

    private static void Check(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject actor = null;
        try
        {
            actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            SceneManager.MoveGameObjectToScene(actor, scene);
            foreach (var behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var camera in actor.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            var halo = actor.GetComponentInChildren<DollSingerHaloAim>(true);
            halo.gameObject.SetActive(true);
            halo.GetComponent<Animator>().Rebind(); halo.GetComponent<Animator>().Update(0);
            typeof(DollSingerHaloAim).GetMethod("Awake", Private).Invoke(halo, null);
            var weapons = new[] { DollSingerHaloAim.HaloWeapon.Rifle, DollSingerHaloAim.HaloWeapon.Revolver,
                DollSingerHaloAim.HaloWeapon.Shotgun, DollSingerHaloAim.HaloWeapon.Sniper };
            foreach (var weapon in weapons) halo.SetNetworkWeapon(weapon, 4, false, 0, 0, 0);
            Require(halo.GetComponentsInChildren<HaloModuleVisual>(true).Length == 0,
                "Unmodified weapons allocated modification layers.");
            int count = 0;
            foreach (var weapon in weapons)
            {
                halo.SetNetworkWeapon(weapon, 2, false, 0, 0, 0, modules: (ushort)HaloModule.Capacitor);
                var layers = halo.GetComponentsInChildren<HaloModuleVisual>(true);
                Require(layers.Length == ++count, "A weapon created more than its own layer.");
                var layer = layers.Single(v => v.gameObject.activeInHierarchy);
                Require(layer.GetComponentsInChildren<SpriteRenderer>(true).Count(s => s.gameObject.activeInHierarchy) == 2,
                    "The selected module's mirrored artwork is missing.");
                halo.SetNetworkWeapon(weapon, 1, true, .5f, 1, 1, modules: (ushort)HaloModule.Capacitor);
                Require(halo.GetComponentsInChildren<HaloModuleVisual>(true).Length == count &&
                    layers.Single(v => v.gameObject.activeInHierarchy) == layer, "Repeated snapshots recreated the layer.");
                halo.SetNetworkWeapon(weapon, 4, false, 0, 1, 1);
                Require((HaloModule)typeof(HaloModuleVisual).GetField("modules", Private).GetValue(layer) == HaloModule.None &&
                    !layer.GetComponentsInChildren<SpriteRenderer>(true).Any(s => s.gameObject.activeInHierarchy) &&
                    layer.GetComponent<MeshFilter>().sharedMesh.vertexCount == 0, "Removed modules retained visible geometry.");
                halo.SetNetworkWeapon(weapon, 4, false, 0, 1, 1, modules: (ushort)HaloModule.Capacitor);
                Require(halo.GetComponentsInChildren<HaloModuleVisual>(true).Single(v => v.gameObject.activeInHierarchy) == layer,
                    "Re-equipping modifications did not reuse the layer.");
                halo.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.None, 0, false, 0, 1, 1);
                Require(!layers.Any(v => v.gameObject.activeInHierarchy), "Unequipped module layers stayed visible.");
            }
        }
        finally
        {
            if (actor) Object.DestroyImmediate(actor);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
