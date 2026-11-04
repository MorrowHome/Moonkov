using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class HaloTransferReview
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Preview Teammate Halo Transfer")]
    public static void Menu() => Debug.Log(Capture());
    public static string Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        Directory.CreateDirectory("LocalData/HaloTransfer");
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        GameObject recipientRoot = null;
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            var stage = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"), scene);
            var donor = stage.GetComponentInChildren<DollSingerHaloAim>(true);
            recipientRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab"), scene);
            var recipient = recipientRoot.GetComponentInChildren<DollSingerHaloAim>(true);
            foreach (var halo in new[] { donor, recipient })
            {
                foreach (var behaviour in halo.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var camera in halo.transform.root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                foreach (var item in halo.transform.root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 5;
                halo.gameObject.SetActive(true);
                halo.GetComponent<Animator>().Rebind(); halo.GetComponent<Animator>().Update(0);
                typeof(DollSingerHaloAim).GetMethod("Awake", Private).Invoke(halo, null);
                typeof(DollSingerHaloAim).GetField("bakedGestures", Private).SetValue(halo, null);
                halo.SetNetworkPresentation(false, Vector3.forward, Vector3.zero);
            }
            donor.transform.position = new Vector3(-1.35f, 0, 0);
            recipient.transform.position = new Vector3(1.35f, 0, 0);
            donor.transform.rotation = recipient.transform.rotation = Quaternion.identity;
            recipient.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.Rifle, 4, false, 0, 0, 0);
            var haloTick = typeof(DollSingerHaloAim).GetMethod("LateUpdate", Private);
            haloTick.Invoke(recipient, null);
            var effect = donor.gameObject.AddComponent<HaloTransferPresentation>(); effect.Configure(donor);
            var tick = typeof(HaloTransferPresentation).GetMethod("SimulatePresentation", Private);
            int lights = donor.GetComponentsInChildren<Light>(true).Length;
            int colliders = donor.GetComponentsInChildren<Collider>(true).Length;
            Transform original = donor.haloVisual;
            var state = new PredictedPlayerGhost { CurrentHealth = 440, HaloEquipped = true, HaloEnergyInitialized = true,
                HaloEnergy = 60, HaloCapacity = 100, HaloTransferPhase = HaloTransferPhase.Sending,
                HaloTransferPoint = recipient.RestingAnchor - Vector3.up * .75f };
            for (uint weapon = DollSingerWeapons.Halo; weapon <= DollSingerWeapons.Nocturne; weapon++)
            {
                state.EquippedWeaponID = weapon;
                donor.SetNetworkWeapon((DollSingerHaloAim.HaloWeapon)(weapon - 1), 4, false, 0, 0, 0);
                haloTick.Invoke(donor, null);
                effect.Present(state, recipient);
                for (int frame = 0; frame < 20; frame++) tick.Invoke(effect, new object[] { 1f / 60 });
                Check(donor.haloVisual == original && Vector3.Distance(original.position, recipient.RestingAnchor) < .7f, "Actual equipped halo must travel next to recipient");
                var lines = (LineRenderer[])typeof(HaloTransferPresentation).GetField("m_Lines", Private).GetValue(effect);
                Color expected = donor.ColorForWeapon((DollSingerHaloAim.HaloWeapon)(weapon - 1));
                Check(lines.All(line => line) && (new Vector3(lines[0].startColor.r, lines[0].startColor.g, lines[0].startColor.b) -
                    new Vector3(expected.r, expected.g, expected.b)).sqrMagnitude < .0001f, "Transfer follows each equipped halo's authored color");
                Check(donor.GetComponentsInChildren<Light>(true).Length == lights && donor.GetComponentsInChildren<Collider>(true).Length == colliders,
                    "Transfer cannot add lights or colliders");
                effect.ResetPresentation();
                Check(lines.All(line => !line.enabled) && Vector3.Distance(original.position, donor.RestingAnchor) < .001f, "Reset returns halo and hides every strand");
            }
            state.EquippedWeaponID = DollSingerWeapons.Shotgun;
            donor.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.Shotgun, 4, false, 0, 0, 0);
            haloTick.Invoke(donor, null);
            effect.Present(state, recipient);
            for (int frame = 0; frame < 20; frame++) tick.Invoke(effect, new object[] { 1f / 60 });
            var cameraNode = new GameObject("Transfer native review camera"); cameraNode.transform.SetParent(stage.transform, false);
            var cameraView = cameraNode.AddComponent<Camera>(); cameraView.enabled = false; cameraView.scene = scene;
            cameraView.cullingMask = 1 << 5; cameraView.clearFlags = CameraClearFlags.SolidColor;
            cameraView.backgroundColor = new Color(.035f, .04f, .055f);
            cameraView.nearClipPlane = .02f; cameraView.farClipPlane = 30;
            cameraView.orthographic = true; cameraView.orthographicSize = 1.65f;
            cameraView.transform.position = new Vector3(.5f, 3.7f, 6.5f); cameraView.transform.LookAt(new Vector3(0, 1.3f, 0));
            var data = cameraView.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            var lightNode = new GameObject("Transfer preview light"); lightNode.transform.SetParent(stage.transform, false);
            var light = lightNode.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.cullingMask = 1 << 5;
            lightNode.transform.rotation = cameraView.transform.rotation;
            target = new RenderTexture(1440, 810, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            target.Create(); cameraView.aspect = 1440f / 810;
            Render(cameraView, target, "LocalData/HaloTransfer/native-transfer.png");
            Render(cameraView, target, "LocalData/HaloTransfer/native-transfer.png");
            string result = $"Halo transfer native art: PASS ({checks} checks, all 13 equipped halos).";
            File.WriteAllText("LocalData/HaloTransfer/appearance.txt", result);
            return result;
        }
        finally
        {
            if (recipientRoot) UnityEngine.Object.DestroyImmediate(recipientRoot);
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Render(Camera camera, RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); }
    }
}
