using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.DollSinger.Editor
{
    /// <summary>Local prefab assembly and a server-free scene, isolated from template Ghosts.</summary>
    public static class DollSingerTools
    {
        public const string Root = "Assets/DollSinger";
        public const string PrefabPath = Root + "/Prefabs/DollSingerPlayer.prefab";
        public const string ScenePath = Root + "/Scenes/DollSingerDemo.unity";

        [MenuItem("Tools/Doll Singer/Open Demo")]
        public static void OpenDemo()
        {
            EnsureEditMode();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Doll Singer/Add Local Player to Current Scene")]
        public static void AddPlayer()
        {
            EnsureEditMode();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(PrefabPath));
            Undo.RegisterCreatedObjectUndo(instance, "Add Doll Singer local player");
            if (SceneView.lastActiveSceneView) instance.transform.position = SceneView.lastActiveSceneView.pivot;
            Selection.activeGameObject = instance;
        }

        public static void FinishMigration()
        {
            EnsureEditMode();
            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(Root + "/Art/Model/LuoTianYi_DollSinger.fbx");
            if (!modelImporter.isReadable)
            {
                // Runtime first-person presentation filters triangles while retaining skinning.
                modelImporter.isReadable = true;
                modelImporter.SaveAndReimport();
            }
            var controller = Require<AnimatorController>(Root + "/Animation/DollSingerThirdPersonHalo.controller");
            // The old controller has an orphaned Fly asset. Its inactive flight state can use InAir.
            var states = controller.layers[0].stateMachine.states;
            var inAir = states.First(x => x.state.name == "InAir").state.motion;
            var fly = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller))
                .OfType<AnimatorState>().FirstOrDefault(state => state.name == "Fly");
            if (fly) { fly.motion = inAir; EditorUtility.SetDirty(fly); }
            var glow = Require<Material>(Root + "/Art/Materials/HaloGlow.mat");
            var core = Require<Material>(Root + "/Art/Materials/HaloAdditive.mat");
            var haloShader = Shader.Find("DollSinger/HaloLine");
            if (!haloShader) throw new InvalidOperationException("Halo shader did not import.");
            glow.shader = core.shader = haloShader;
            core.SetFloat("_Gain", 1f);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(glow);
            EditorUtility.SetDirty(core);

            GameObject player = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                player.name = "DollSingerPlayer";
                var animator = player.GetComponent<Animator>();
                if (!animator || !animator.avatar || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new InvalidOperationException("Doll Singer humanoid avatar is invalid.");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var cc = EnsureComponent<CharacterController>(player);
                cc.height = 1.7f;
                cc.radius = 0.28f;
                cc.center = new Vector3(0f, 0.85f, 0f);
                cc.skinWidth = 0.025f;
                cc.stepOffset = 0.3f;
                var input = EnsureComponent<DollSingerInput>(player);
                var movement = EnsureComponent<DollSingerMovement>(player);
                movement.input = input;
                movement.m_GroundLayers = Physics.DefaultRaycastLayers;
                var existingRig = player.transform.Find("ViewRig");
                var rig = existingRig ? existingRig.gameObject : new GameObject("ViewRig");
                rig.transform.SetParent(player.transform, false);
                var view = EnsureComponent<DollSingerView>(rig);
                var existingCamera = rig.GetComponentInChildren<Camera>(true);
                var cameraObject = existingCamera ? existingCamera.gameObject : new GameObject("DollSinger Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(rig.transform, false);
                cameraObject.transform.localPosition = new Vector3(0f, 2.4f, -4f);
                cameraObject.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
                var camera = cameraObject.GetComponent<Camera>();
                camera.fieldOfView = 45f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000f;
                camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                view.camera = camera;
                view.player = player;
                view.input = input;
                view.followPlayerRotation = false;
                movement.view = view;
                movement.m_CinemachineCameraTarget = rig;
                var halo = player.GetComponent<DollSingerHaloAim>();
                halo.input = input;
                halo.view = view;
                var spring = player.GetComponent<SecondaryBoneSpring>();
                spring.chestRoots = player.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "胸.L" || t.name == "胸.R").ToArray();
                foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = true;
                PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            PrepareDemoProfile();
            AssetDatabase.SaveAssets();
            CreateDemoIfMissing();
        }

        private static VolumeProfile PrepareDemoProfile()
        {
            Directory.CreateDirectory(Root + "/Demo");
            AssetDatabase.Refresh();
            string path = Root + "/Demo/DollSingerVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.components.RemoveAll(component => !component);
            if (!profile.TryGet<Bloom>(out var bloom))
            {
                bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(0.3f);
                bloom.threshold.Override(1f);
            }
            if (!profile.TryGet<Tonemapping>(out var tone))
            {
                tone = profile.Add<Tonemapping>(true);
                tone.mode.Override(TonemappingMode.ACES);
            }
            foreach (var component in profile.components)
                if (!EditorUtility.IsPersistent(component)) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void CreateDemoIfMissing()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Demo");
            AssetDatabase.Refresh();
            var profile = PrepareDemoProfile();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PracticeGround" };
            material.SetColor("_BaseColor", new Color(0.19f, 0.21f, 0.27f));
            AssetDatabase.CreateAsset(material, Root + "/Demo/PracticeGround.mat");
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.45f);
                var light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                var volume = new GameObject("Doll Singer Bloom", typeof(Volume)).GetComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;
                var stage = new GameObject("Practice Stage");
                CreateBlock(stage.transform, "Ground", new Vector3(0f, -0.2f, 0f), new Vector3(40f, 0.4f, 40f), material);
                CreateBlock(stage.transform, "Lean Cover", new Vector3(2f, 0.65f, 4f), new Vector3(1f, 1.3f, 3f), material);
                CreateBlock(stage.transform, "Aim Target", new Vector3(0f, 1.5f, 12f), new Vector3(1.5f, 3f, 0.5f), material);
                CreateBlock(stage.transform, "Low Step", new Vector3(-3f, 0.15f, 2f), new Vector3(2f, 0.3f, 2f), material);
                CreateBlock(stage.transform, "Middle Step", new Vector3(-3f, 0.45f, 4f), new Vector3(2f, 0.9f, 2f), material);
                CreateBlock(stage.transform, "Fall Platform", new Vector3(-3f, 0.9f, 6f), new Vector3(3f, 1.8f, 3f), material);
                var localPlayer = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(PrefabPath), scene);
                localPlayer.transform.position = Vector3.up * 0.05f;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (original.IsValid()) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
        }

        private static void CreateBlock(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static T EnsureComponent<T>(GameObject root) where T : Component
        {
            var component = root.GetComponent<T>();
            return component ? component : root.AddComponent<T>();
        }

        private static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing asset: " + path);
            return asset;
        }

        private static void EnsureEditMode()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        }
    }
}
