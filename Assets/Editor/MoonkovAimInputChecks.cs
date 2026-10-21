using System;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Only this change's new risks; preserves the player's exact preference keys.</summary>
public static class MoonkovAimInputChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    [MenuItem("Tools/Moonkov/Check Aim Input Improvements")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play mode.");
        CheckPreferencesAndCancel();
        CheckAimIntent();
        CheckCover();
        Debug.Log("Aim/input improvements passed: preference reload/defaults and new deployment, shared FOV/ADS scaling at 1.75/6/8x, " +
            "X cancel consumes until release, latest owned ADS intent consumed once, muzzle cover/self/target-surface filtering.");
    }
    private static void CheckPreferencesAndCancel()
    {
        var keys = new[] { DollSingerViewSettings.MouseKey, DollSingerViewSettings.AimKey, DollSingerViewSettings.FovKey };
        var existed = new bool[3]; var saved = new float[3];
        for (int i = 0; i < keys.Length; i++) { existed[i] = PlayerPrefs.HasKey(keys[i]); saved[i] = PlayerPrefs.GetFloat(keys[i]); }
        var root = new GameObject("Inactive aim preference fixture") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false); root.transform.position = Vector3.up * 10;
        DollSingerView view = null;
        try
        {
            DollSingerViewSettings.Set(.23f, .75f, 82f); DollSingerViewSettings.Save(); DollSingerViewSettings.Reload();
            Require(Near(DollSingerViewSettings.MouseSensitivity, .23f) && Near(DollSingerViewSettings.AimSensitivity, .75f) &&
                Near(DollSingerViewSettings.WorldFieldOfView, 82), "Preferences did not survive a cache reload.");
            var model = root.AddComponent<DollSingerMovement>();
            var input = root.AddComponent<DollSingerInput>(); input.ApplyViewSettings();
            Require(Near((float)Get(input, "mouseSensitivity"), .23f), "New deployment did not apply mouse preference.");
            Require(!input.ReadTiptoe(true, true, true), "X cancel also requested tiptoe.");
            Require(!input.ReadTiptoe(false, true, false), "Server acknowledgement leaked held X into tiptoe.");
            Require(!input.ReadTiptoe(false, false, false) && input.ReadTiptoe(true, true, false), "X release did not rearm normal tiptoe.");
            var pivot = new GameObject("View"); pivot.transform.SetParent(root.transform, false);
            var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(pivot.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.localPosition = new Vector3(0, 2, -4);
            view = pivot.AddComponent<DollSingerView>(); view.camera = camera; view.ApplyViewSettings(); view.BindPlayer(root, true);
            foreach (bool first in new[] { true, false })
            foreach (float zoom in new[] { 1.75f, 6f, 8f })
            {
                view.SetFirstPerson(first); Set(view, "viewBlend", first ? 1f : 0f);
                view.SetAimBlend(1); view.SetWeaponAimMagnification(zoom); Set(view, "presentedWeaponMagnification", zoom);
                Call(view, "LateUpdate");
                float measured = Mathf.Tan(82 * .5f * Mathf.Deg2Rad) / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
                Require(Near(measured, zoom) && Near(view.AimLookSensitivity, .75f / zoom), "FOV or ADS preference applied twice/changed across perspectives.");
                Set(input, "<Look>k__BackingField", new Vector2(.008f, .008f));
                float2 look = float2.zero, free = float2.zero; float lean = 0;
                DollSingerLookController.Update(ref look, ref free, ref lean, out _, input, model, view, false, .016f);
                Require(math.distance(look, new float2(.008f, -.008f) * (.75f / zoom)) < .000001f, "Micro aim preference was lost/scaled twice.");
            }
            DollSingerViewSettings.RestoreDefaults(); view.ApplyViewSettings(); input.ApplyViewSettings();
            Require(Near(view.firstPersonFieldOfView, 70) && Near((float)Get(input, "mouseSensitivity"), .15f) &&
                Near(DollSingerViewSettings.AimSensitivity, 1), "Restore view defaults affected the wrong values.");
        }
        finally
        {
            if (view) view.BindPlayer(null, true);
            UnityEngine.Object.DestroyImmediate(root);
            for (int i = 0; i < keys.Length; i++)
                if (existed[i]) PlayerPrefs.SetFloat(keys[i], saved[i]); else PlayerPrefs.DeleteKey(keys[i]);
            PlayerPrefs.Save(); DollSingerViewSettings.Reload();
        }
    }
    private static void CheckAimIntent()
    {
        var root = new GameObject("Inactive ADS intent fixture") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
        try
        {
            var halo = root.AddComponent<DollSingerHaloAim>();
            bool intent = true; int samples = 0;
            halo.SetNetworkPresentation(false, Vector3.forward, Vector3.forward * 20);
            halo.OwnedAimIntent = () => { samples++; return intent; };
            Call(halo, "Update");
            Require((bool)Get(halo, "wantsAim") && samples == 1, "ADS press waited for ECS/was sampled more than once.");
            halo.SetNetworkPresentation(true, Vector3.forward, Vector3.forward * 20);
            intent = false; Call(halo, "Update");
            Require(!(bool)Get(halo, "wantsAim") && samples == 2, "ADS release used the previous network intent.");
            halo.OwnedAimIntent = null;
            halo.SetNetworkPresentation(true, Vector3.forward, Vector3.forward * 20); Call(halo, "Update");
            Require((bool)Get(halo, "wantsAim"), "Remote replicas stopped using synchronized intent.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void CheckCover()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var holder = new GameObject("Muzzle cover fixture"); holder.SetActive(false); SceneManager.MoveGameObjectToScene(holder, scene);
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab");
            var player = UnityEngine.Object.Instantiate(source, holder.transform).GetComponent<PlayerGhost>();
            player.Awake(); player.SetGhostGameObject(player.GetComponent<GhostGameObject>());
            Set(player.GetComponent<FirstPersonController>(), "m_Controller", player.GetComponent<UnityEngine.CharacterController>());
            Set(player.GetComponent<FirstPersonController>(), "m_StandingHeight", player.GetComponent<UnityEngine.CharacterController>().height);
            Set(player.GetComponent<FirstPersonController>(), "m_StandingCenter", player.GetComponent<UnityEngine.CharacterController>().center);
            foreach (var behaviour in holder.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var camera in holder.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var collider in holder.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var view = player.GetComponent<DollSingerNetworkPresentation>();
            Vector3 origin = player.ShotOrigin.position, target = origin + Vector3.forward * 20;
            var wall = new GameObject("Cover") { layer = (int)LayerIndex.Ground }; wall.transform.SetParent(holder.transform);
            var box = wall.AddComponent<BoxCollider>(); box.size = new Vector3(2, 2, .2f);
            wall.transform.position = origin + Vector3.forward * 2;
            holder.SetActive(true); Physics.SyncTransforms();
            Require(view.HasMuzzleObstruction(float2.zero, target, 0, true), "Third person muzzle cover was missed.");
            Require(view.HasMuzzleObstruction(float2.zero, target, .5f, false), "Lean muzzle cover was missed.");
            wall.transform.position = target + Vector3.forward * .1f; Physics.SyncTransforms();
            Require(!view.HasMuzzleObstruction(float2.zero, target, 0, true), "The target surface was labelled as muzzle cover.");
            wall.transform.position = origin + Vector3.forward * 2;
            wall.layer = (int)LayerIndex.ClientPlayer; Physics.SyncTransforms();
            Require(!view.HasMuzzleObstruction(float2.zero, target, 0, true), "Client avatar copy was labelled cover.");
            wall.layer = (int)LayerIndex.ServerPlayer; Physics.SyncTransforms();
            Require(!view.HasMuzzleObstruction(float2.zero, target, 0, true), "Host server avatar copy was labelled cover.");
            wall.layer = (int)LayerIndex.Default; wall.transform.SetParent(player.transform); Physics.SyncTransforms();
            Require(!view.HasMuzzleObstruction(float2.zero, target, 0, true), "Own collider was labelled cover.");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); EditorSceneManager.ClosePreviewScene(scene); Physics.SyncTransforms(); }
    }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    private static object Get(object value, string field) => value.GetType().GetField(field, Private).GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static void Call(object value, string method) => value.GetType().GetMethod(method, Private).Invoke(value, null);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException("Aim/input: " + message); }
}
