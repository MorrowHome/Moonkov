using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Explicit, additive authoring of a flat layout study. Never places content on terrain,
    /// edits a gameplay scene, registers loot, or runs automatically on import.
    /// </summary>
    public static class MoonBaseGrayboxTools
    {
        private const string ParentPath = "Assets/MoonEnvironment";
        private const string MenuPath = "Tools/Moon Environment/Graybox/Create New Two-Complex Study";
        private const float BuildingWidth = 24f;
        private const float BuildingDepth = 16f;
        private const float WallHeight = 3.6f;
        private const float WallThickness = 0.3f;
        private const float DoorWidth = 2.4f;
        private const float DoorHeight = 2.8f;
        private const float SlabThickness = 0.3f;

        [MenuItem(MenuPath)]
        public static void CreateStudy()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Wait for imports/builds to finish and leave Play Mode first.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Leave Prefab Mode before creating a graybox study.");
            if (!AssetDatabase.IsValidFolder(ParentPath))
                throw new InvalidOperationException("Open the Moonkov project with Assets/MoonEnvironment present.");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Resolve URP/Lit shader availability before creating the study.");
            Scene previous = SceneManager.GetActiveScene();
            if (!previous.IsValid() || !previous.isLoaded)
                throw new InvalidOperationException("Open a normal scene before creating the study.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene existing = SceneManager.GetSceneAt(i);
                if (existing.isLoaded && string.IsNullOrEmpty(existing.path))
                    throw new InvalidOperationException("Save or close Untitled scenes yourself before creating a study. " +
                        "This tool never saves or closes an existing scene.");
            }
            if (!EditorUtility.DisplayDialog("Create graybox study?",
                "Creates a NEW asset folder and flat, metre-scale prefab study. Existing scenes, terrain, " +
                "gameplay configuration and earlier studies are not saved or overwritten. " +
                "This is not a terrain-fitted or playable raid map. Asset creation is not an Undo operation.",
                "Create New Study", "Cancel")) return;

            string output = CreateFreshFolder();
            Scene temporary = default;
            GameObject savedPrefab = null;
            string prefabPath = output + "/MoonBase_GrayboxStudy.prefab";
            bool cleanupSucceeded = true;
            try
            {
                // A new additive scene owns every temporary GameObject. Never use the user's scene.
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (!SceneManager.SetActiveScene(temporary))
                    throw new InvalidOperationException("Could not activate the temporary authoring scene.");
                Material shell = CreateMaterial(output, "Shell", shader, new Color(0.57f, 0.60f, 0.63f));
                Material logistics = CreateMaterial(output, "Logistics", shader, new Color(0.42f, 0.48f, 0.53f));
                Material research = CreateMaterial(output, "Research", shader, new Color(0.53f, 0.48f, 0.42f));
                Material cover = CreateMaterial(output, "Cover", shader, new Color(0.32f, 0.35f, 0.37f));
                Material floor = CreateMaterial(output, "Floor", shader, new Color(0.39f, 0.40f, 0.41f));
                var root = new GameObject("MoonBase_GrayboxStudy_v1");
                BuildStudy(root.transform, shell, logistics, research, cover, floor);
                ValidateStudy(root);
                RequireUnusedPath(prefabPath);
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success || savedPrefab == null)
                    throw new IOException("Prefab save failed. Inspect the new study folder; it was not cleaned up automatically.");
            }
            catch (Exception)
            {
                Debug.LogError("Study generation did not complete. Only the new folder may contain partial assets: " +
                    output + ". Earlier studies and gameplay assets were not overwritten.");
                throw;
            }
            finally
            {
                // Report cleanup failure without hiding an earlier generation exception.
                if (!previous.IsValid() || !previous.isLoaded || !SceneManager.SetActiveScene(previous))
                {
                    cleanupSucceeded = false;
                    Debug.LogError("Could not restore the original active scene. Check the open scenes before continuing.");
                }
                if (temporary.IsValid() && temporary.isLoaded && !EditorSceneManager.CloseScene(temporary, true))
                {
                    cleanupSucceeded = false;
                    Debug.LogError("Could not close the temporary graybox scene. Close that new scene without saving; " +
                        "do not close or discard your original scene.");
                }
            }
            if (!cleanupSucceeded) return;
            Selection.activeObject = savedPrefab;
            EditorGUIUtility.PingObject(savedPrefab);
            Debug.Log("Graybox study created at " + prefabPath +
                ". Flat prototype only: 2 complexes, 4 buildings, 12 proposed loot sockets. " +
                "Read Docs/MoonBaseGrayboxStudy.md before terrain integration.", savedPrefab);
        }

        private static void BuildStudy(Transform root, Material shell, Material logistics,
            Material research, Material cover, Material floor)
        {
            Transform staging = Group(root, "STUDY_ONLY_RemoveBeforeTerrainIntegration", Vector3.zero);
            staging.gameObject.tag = "EditorOnly";
            BuildReviewGround(staging, floor);

            Transform a = Group(root, "A_Logistics_Residential", new Vector3(-48, 0, 0));
            Building(a, "A1_Logistics", new Vector3(0, 0, -14), shell, logistics, cover, floor,
                "MaintenanceSupplies", "CargoManifest", "UtilityParts");
            Building(a, "A2_Residential", new Vector3(0, 0, 14), shell, logistics, cover, floor,
                "PersonalStorage", "CrewRecords", "EmergencySupplies");
            Transform b = Group(root, "B_Research_Mining", new Vector3(48, 0, 0));
            Building(b, "B1_MiningSupport", new Vector3(0, 0, -14), shell, research, cover, floor,
                "SurveySamples", "DrillSpares", "SurveyRecords");
            Building(b, "B2_Research", new Vector3(0, 0, 14), shell, research, cover, floor,
                "LabConsumables", "ResearchRecords", "SampleContainer");

            Transform routes = Group(root, "Exterior_Cover_Proposals", Vector3.zero);
            // Staggered blocks break selected long angles; they do not bake occlusion or navigation.
            Box(routes, "North_FullCover_01", new Vector3(-24, 1.1f, 32), new Vector3(8, 2.2f, 2), cover);
            Box(routes, "North_FullCover_02", new Vector3(0, 1.1f, 43), new Vector3(8, 2.2f, 2), cover);
            Box(routes, "North_FullCover_03", new Vector3(24, 1.1f, 32), new Vector3(8, 2.2f, 2), cover);
            Box(routes, "Centre_LowCover_01", new Vector3(-18, 0.55f, -4), new Vector3(4, 1.1f, 2), cover);
            Box(routes, "Centre_LowCover_02", new Vector3(18, 0.55f, 4), new Vector3(4, 1.1f, 2), cover);
            Box(routes, "South_FullCover_01", new Vector3(-24, 1.1f, -34), new Vector3(3, 2.2f, 7), cover);
            Box(routes, "South_LowCover_02", new Vector3(0, 0.55f, -44), new Vector3(6, 1.1f, 2), cover);
            Box(routes, "South_FullCover_03", new Vector3(24, 1.1f, -34), new Vector3(3, 2.2f, 7), cover);

            Transform notes = Group(root, "PROPOSALS_ONLY_NoGameplayComponents", Vector3.zero);
            notes.gameObject.tag = "EditorOnly";
            Marker(notes, "PROPOSED_Entry_ReviewOnly", new Vector3(-76, 0, 0));
            Marker(notes, "PROPOSED_Exit_ReviewOnly", new Vector3(76, 0, 0));
            Route(notes, "R1_Direct_Exposed", new[] { new Vector3(-76, 0, 0), new Vector3(0, 0, 0), new Vector3(76, 0, 0) });
            Route(notes, "R2_North_StaggeredCover", new[] { new Vector3(-76, 0, 0), new Vector3(-68, 0, 36),
                new Vector3(-24, 0, 39), new Vector3(0, 0, 36), new Vector3(24, 0, 39),
                new Vector3(68, 0, 36), new Vector3(76, 0, 0) });
            Route(notes, "R3_South_LongerFlank", new[] { new Vector3(-76, 0, 0), new Vector3(-68, 0, -46),
                new Vector3(0, 0, -50), new Vector3(68, 0, -46), new Vector3(76, 0, 0) });
        }

        private static void BuildReviewGround(Transform parent, Material floor)
        {
            // Tile around the four building floors: no overlapping coplanar surfaces or doorway steps.
            // Ground top is Y=0. This is editor-only staging geometry, never a substitute for lunar terrain.
            float[] xs = { -88, -60, -36, 36, 60, 88 };
            float[] zs = { -56, -22, -6, 6, 22, 56 };
            for (int x = 0; x < xs.Length - 1; x++)
            for (int z = 0; z < zs.Length - 1; z++)
            {
                if ((x == 1 || x == 3) && (z == 1 || z == 3)) continue;
                Box(parent, "ReviewGround_" + x + "_" + z,
                    new Vector3((xs[x] + xs[x + 1]) * 0.5f, -SlabThickness * 0.5f,
                        (zs[z] + zs[z + 1]) * 0.5f),
                    new Vector3(xs[x + 1] - xs[x], SlabThickness, zs[z + 1] - zs[z]), floor);
            }
        }

        private static void Building(Transform parent, string name, Vector3 position,
            Material shell, Material accent, Material cover, Material floor,
            string lootOne, string lootTwo, string lootThree)
        {
            Transform building = Group(parent, name, position);
            Transform structure = Group(building, "Structure", Vector3.zero);
            Box(structure, "Floor_TopY0", new Vector3(0, -SlabThickness * 0.5f, 0),
                new Vector3(BuildingWidth, SlabThickness, BuildingDepth), floor);
            Box(structure, "NorthWall", new Vector3(0, WallHeight * 0.5f, BuildingDepth * 0.5f),
                new Vector3(BuildingWidth + WallThickness, WallHeight, WallThickness), shell);
            Box(structure, "SouthWall", new Vector3(0, WallHeight * 0.5f, -BuildingDepth * 0.5f),
                new Vector3(BuildingWidth + WallThickness, WallHeight, WallThickness), shell);
            PortalWall(structure, "WestEntry", -BuildingWidth * 0.5f, -4f, shell);
            PortalWall(structure, "EastEntry", BuildingWidth * 0.5f, 4f, shell);
            PortalWall(structure, "InteriorPartition", 0, 0, shell);
            Transform roof = Group(building, "Roof_DisableOnlyForTopDownReview", Vector3.zero);
            Box(roof, "RoofSlab", new Vector3(0, WallHeight + 0.125f, 0),
                new Vector3(BuildingWidth + WallThickness, 0.25f, BuildingDepth + WallThickness), shell);
            Transform furniture = Group(building, "Replaceable_BlockoutFurniture", Vector3.zero);
            Box(furniture, "WestRack_FullCover", new Vector3(-6, 1f, 2.5f), new Vector3(1.2f, 2f, 4), cover);
            Box(furniture, "EastBench_LowCover", new Vector3(6, 0.55f, -2.5f), new Vector3(1.2f, 1.1f, 4), accent);
            // Empty editor-only points are deliberately not MoonRaidMap loot or physical caches.
            Transform sockets = Group(building, "PROPOSED_LootSockets_NotConnected", Vector3.zero);
            sockets.gameObject.tag = "EditorOnly";
            Marker(sockets, "PROPOSED_Loot_01_" + lootOne, new Vector3(-8, 0.55f, -5));
            Marker(sockets, "PROPOSED_Loot_02_" + lootTwo, new Vector3(8, 0.55f, 5));
            Marker(sockets, "PROPOSED_Loot_03_" + lootThree, new Vector3(8, 0.55f, -5));
        }

        private static void PortalWall(Transform parent, string name, float x, float doorZ, Material material)
        {
            Transform wall = Group(parent, name, Vector3.zero);
            float minimum = -BuildingDepth * 0.5f;
            float maximum = BuildingDepth * 0.5f;
            float lowEnd = doorZ - DoorWidth * 0.5f;
            float highStart = doorZ + DoorWidth * 0.5f;
            Box(wall, "SouthSegment", new Vector3(x, WallHeight * 0.5f, (minimum + lowEnd) * 0.5f),
                new Vector3(WallThickness, WallHeight, lowEnd - minimum), material);
            Box(wall, "NorthSegment", new Vector3(x, WallHeight * 0.5f, (highStart + maximum) * 0.5f),
                new Vector3(WallThickness, WallHeight, maximum - highStart), material);
            Box(wall, "Lintel", new Vector3(x, (DoorHeight + WallHeight) * 0.5f, doorZ),
                new Vector3(WallThickness, WallHeight - DoorHeight, DoorWidth), material);
            Marker(wall, "OPEN_Portal_2.4m_x_2.8m_NoDoorLogic", new Vector3(x, 0, doorZ));
        }

        private static void Route(Transform parent, string name, Vector3[] points)
        {
            Transform route = Group(parent, name, Vector3.zero);
            for (int i = 0; i < points.Length; i++) Marker(route, "Waypoint_" + i.ToString("00"), points[i]);
        }

        private static Transform Group(Transform parent, string name, Vector3 position)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            result.localPosition = position;
            return result;
        }

        private static void Marker(Transform parent, string name, Vector3 position)
        {
            Transform marker = Group(parent, name, position);
            marker.gameObject.tag = "EditorOnly";
        }

        private static void Box(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new InvalidOperationException("Non-positive block dimension: " + name);
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = centre;
            block.transform.localScale = size;
            block.layer = 0; // Existing Default terrain/geometry layer, no project layer changes.
            block.GetComponent<BoxCollider>().isTrigger = false;
            Renderer renderer = block.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            // No Rigidbody, network state, static-bake flags or automatic navigation setup.
        }

        private static Material CreateMaterial(string folder, string name, Shader shader, Color color)
        {
            string path = folder + "/" + name + ".mat";
            RequireUnusedPath(path);
            var material = new Material(shader) { name = "Graybox_" + name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static string CreateFreshFolder()
        {
            for (int i = 1; i < 10000; i++)
            {
                string name = "GrayboxStudy_" + i.ToString("000");
                string path = ParentPath + "/" + name;
                if (Directory.Exists(path) || File.Exists(path) || File.Exists(path + ".meta") ||
                    AssetDatabase.IsValidFolder(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) continue;
                string guid = AssetDatabase.CreateFolder(ParentPath, name);
                if (string.IsNullOrEmpty(guid) || AssetDatabase.GUIDToAssetPath(guid) != path)
                    throw new IOException("Could not create the expected new study folder: " + path);
                return path;
            }
            throw new IOException("No unused study folder name remains.");
        }

        private static void RequireUnusedPath(string path)
        {
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta") ||
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                throw new IOException("Refusing to overwrite an existing asset: " + path);
        }

        private static void ValidateStudy(GameObject root)
        {
            int lootCount = 0;
            int portalCount = 0;
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                if (item.name.StartsWith("PROPOSED_Loot_", StringComparison.Ordinal)) lootCount++;
                if (item.name.StartsWith("OPEN_Portal_", StringComparison.Ordinal)) portalCount++;
            }
            if (lootCount != 12 || portalCount != 12)
                throw new InvalidOperationException("Study recipe must contain 12 proposed loot points and 12 open portals.");
            if (root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 ||
                root.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException("Study must not contain runtime behaviour or rigidbodies.");
            foreach (BoxCollider collider in root.GetComponentsInChildren<BoxCollider>(true))
                if (collider.isTrigger || !collider.enabled)
                    throw new InvalidOperationException("All geometry must use enabled solid box colliders.");
        }
    }
}
