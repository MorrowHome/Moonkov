using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Editor
{
    /// <summary>Creates reusable templates without editing a map or replacing authored assets.</summary>
    public static class MoonkovOxygenTools
    {
        public const string SettingsPath = "Assets/Resources/Moonkov/Oxygen/DefaultOxygenSettings.asset";
        public const string RoomPath = "Assets/Prefabs/Survival/BreathableRoom.prefab";
        public const string StationPath = "Assets/Prefabs/Survival/OxygenRefillStation.prefab";
        private const string PrefabFolder = "Assets/Prefabs/Survival";

        [MenuItem("Tools/Moonkov/Oxygen/Create Reusable Assets")]
        public static void CreateReusableAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating oxygen templates.");

            EnsureFolder("Assets/Resources/Moonkov/Oxygen");
            EnsureFolder(PrefabFolder);
            if (!Existing<OxygenSettings>(SettingsPath))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<OxygenSettings>(), SettingsPath);

            if (!Existing<GameObject>(RoomPath)) CreatePrefab(RoomPath, "Breathable Room", BuildRoom);
            if (!Existing<GameObject>(StationPath)) CreatePrefab(StationPath, "Oxygen Refill Station", BuildStation);
            Debug.Log("Oxygen templates ready. Existing assets were preserved; assign settings and place zones explicitly.");
        }

        [MenuItem("GameObject/Moonkov/Survival/Breathable Room", false, 10)]
        public static void AddRoom(MenuCommand command) => PlacePrefab(RoomPath, command);

        [MenuItem("GameObject/Moonkov/Survival/Oxygen Refill Station", false, 11)]
        public static void AddStation(MenuCommand command) => PlacePrefab(StationPath, command);

        private static void PlacePrefab(string path, MenuCommand command)
        {
            CreateReusableAssets();
            var parent = command.context as GameObject ?? Selection.activeGameObject;
            if (parent && EditorUtility.IsPersistent(parent)) parent = null;
            var scene = parent ? parent.scene : SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open a scene before placing an oxygen template.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            GameObjectUtility.SetParentAndAlign(instance, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Create " + prefab.name);
            Selection.activeGameObject = instance;
        }

        private static void CreatePrefab(string path, string name, Action<GameObject> build)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, preview);
                build(root);
                if (!PrefabUtility.SaveAsPrefabAsset(root, path))
                    throw new InvalidOperationException("Could not create oxygen prefab: " + path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void BuildRoom(GameObject root)
        {
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0, 1.5f, 0);
            box.size = new Vector3(6, 3, 6);
            var air = root.AddComponent<BreathableZone>();
            air.Configure("room", 101.325f, .21f);
            // New rooms distinguish safe breathing from tank filling. Legacy zones retain their default.
            air.AllowsSuitRefill = false;
        }

        private static void BuildStation(GameObject root)
        {
            var body = MaterialAsset("OxygenStationBody", new Color(.075f, .12f, .15f), .55f);
            var oxygen = MaterialAsset("OxygenStationCyan", new Color(.06f, .75f, .76f), .35f);
            var warning = MaterialAsset("OxygenStationSafety", new Color(1, .72f, .12f), .2f);
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0, 1.2f, 1.05f);
            box.size = new Vector3(2.4f, 2.4f, 2);
            var supply = root.AddComponent<OxygenRefillZone>();
            supply.Configure(10, requiresBreathableAir: true);
            supply.Powered = true;

            Shape(root, "Base", PrimitiveType.Cube, new Vector3(0, .08f, 0),
                new Vector3(1.1f, .16f, .65f), body, true);
            Shape(root, "Supply cabinet", PrimitiveType.Cube, new Vector3(0, .85f, -.16f),
                new Vector3(.88f, 1.5f, .3f), body, true);
            Shape(root, "Tank left", PrimitiveType.Cylinder, new Vector3(-.27f, .64f, .14f),
                new Vector3(.28f, .48f, .28f), oxygen);
            Shape(root, "Tank right", PrimitiveType.Cylinder, new Vector3(.27f, .64f, .14f),
                new Vector3(.28f, .48f, .28f), oxygen);
            Shape(root, "Left valve", PrimitiveType.Cube, new Vector3(-.27f, 1.16f, .14f),
                new Vector3(.18f, .08f, .08f), warning);
            Shape(root, "Right valve", PrimitiveType.Cube, new Vector3(.27f, 1.16f, .14f),
                new Vector3(.18f, .08f, .08f), warning);
            Shape(root, "Sign plate", PrimitiveType.Cube, new Vector3(0, 1.46f, .02f),
                new Vector3(.96f, .36f, .12f), oxygen);
            Shape(root, "Umbilical hose", PrimitiveType.Cylinder, new Vector3(.48f, .74f, .29f),
                new Vector3(.065f, .36f, .065f), body);
            Shape(root, "Automatic suit connector", PrimitiveType.Cube, new Vector3(.48f, 1.12f, .29f),
                new Vector3(.17f, .16f, .18f), warning);
            Shape(root, "Supply area marker", PrimitiveType.Cube, new Vector3(0, .015f, 1.05f),
                new Vector3(1.8f, .025f, .045f), oxygen);

            var label = new GameObject("Automatic unlimited powered umbilical");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0, 1.46f, .087f);
            label.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var text = label.AddComponent<TextMesh>();
            text.text = "O2 / AUTO SUPPLY\nPOWERED UMBILICAL";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.characterSize = .035f;
            text.color = new Color(.025f, .065f, .075f);
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        }

        private static void Shape(GameObject root, string name, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material, bool solid = false)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        private static Material MaterialAsset(string name, Color color, float metallic)
        {
            string path = PrefabFolder + "/" + name + ".mat";
            var existing = Existing<Material>(path);
            if (existing) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is required by the oxygen station template.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", .4f);
            try { AssetDatabase.CreateAsset(material, path); }
            catch { UnityEngine.Object.DestroyImmediate(material); throw; }
            return material;
        }

        private static T Existing<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (!asset && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) return null;
            if (asset is T typed) return typed;
            throw new InvalidOperationException("An incompatible asset already occupies " + path + ". It was not overwritten.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, path.Substring(split + 1))))
                throw new InvalidOperationException("Could not create asset folder: " + path);
        }
    }
}
