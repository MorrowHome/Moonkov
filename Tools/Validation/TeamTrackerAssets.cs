using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering.Universal;

// Run through Pipeline in the existing idle Editor. Only writes this item's assets and two strings.
public static class TeamTrackerAssets
{
    private const string Root = "Assets/Resources/Moonkov/Items/";
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("team_tracker");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var pieces = new List<GameObject>();
        var meshes = new List<Mesh>();
        RenderTexture target = null;
        try
        {
            var dark = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Item_dark.mat");
            var steel = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Item_steel.mat");
            string accentPath = Root + "Materials/TeamTracker_Teal.mat";
            var teal = AssetDatabase.LoadAssetAtPath<Material>(accentPath);
            if (!teal)
            {
                teal = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(teal, accentPath);
            }
            teal.SetColor("_BaseColor", new Color(.07f, .65f, .58f)); teal.SetFloat("_Smoothness", .35f);
            teal.SetColor("_EmissionColor", new Color(.05f, .4f, .33f)); teal.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(teal); AssetDatabase.SaveAssetIfDirty(teal);
            var materials = new[] { dark, steel, teal };
            void Box(string name, Vector3 position, Vector3 scale, int material, float angle = 0)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
                go.transform.SetParent(root.transform); go.transform.localPosition = position;
                go.transform.localRotation = Quaternion.Euler(0, 0, angle); go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = materials[material];
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); pieces.Add(go);
            }
            Box("Body", Vector3.zero, new Vector3(.09f, .13f, .035f), 0);
            Box("Display frame", new Vector3(0, .014f, -.019f), new Vector3(.074f, .083f, .006f), 1);
            Box("Dark display", new Vector3(0, .014f, -.023f), new Vector3(.064f, .073f, .004f), 0);
            for (int n = 0; n < 24; n++)
            {
                float angle = n * 15 * Mathf.Deg2Rad;
                Box("Radar ring", new Vector3(Mathf.Sin(angle) * .023f, .014f + Mathf.Cos(angle) * .023f, -.026f),
                    new Vector3(.0063f, .0012f, .001f), 2, -n * 15);
            }
            Box("North contact", new Vector3(-.008f, .028f, -.026f), new Vector3(.004f, .004f, .001f), 2);
            Box("East contact", new Vector3(.012f, .014f, -.026f), new Vector3(.004f, .004f, .001f), 2);
            Box("South contact", new Vector3(-.006f, .003f, -.026f), new Vector3(.004f, .004f, .001f), 2);
            Box("Antenna base", new Vector3(.028f, .071f, 0), new Vector3(.014f, .014f, .021f), 1);
            Box("Antenna", new Vector3(.028f, .094f, 0), new Vector3(.006f, .035f, .006f), 0);
            Box("Status", new Vector3(-.02f, -.045f, -.019f), new Vector3(.012f, .004f, .004f), 2);
            Box("Button", new Vector3(.02f, -.045f, -.02f), new Vector3(.014f, .014f, .007f), 1);
            foreach (var material in materials)
            {
                var combine = pieces.Where(go => go.GetComponent<Renderer>().sharedMaterial == material)
                    .Select(go => new CombineInstance { mesh = go.GetComponent<MeshFilter>().sharedMesh, transform = go.transform.localToWorldMatrix }).ToArray();
                var mesh = new Mesh(); mesh.CombineMeshes(combine, true, true); meshes.Add(mesh);
            }
            var combined = new Mesh { name = "T-01 Squad tracker" };
            combined.CombineMeshes(meshes.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false, false);
            string meshPath = Root + "Models/team_tracker.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (saved) { EditorUtility.CopySerialized(combined, saved); UnityEngine.Object.DestroyImmediate(combined); }
            else { saved = combined; AssetDatabase.CreateAsset(saved, meshPath); }
            EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
            foreach (var go in pieces) UnityEngine.Object.DestroyImmediate(go);
            pieces.Clear();
            root.AddComponent<MeshFilter>().sharedMesh = saved;
            root.AddComponent<MeshRenderer>().sharedMaterials = materials;
            PrefabUtility.SaveAsPrefabAsset(root, Root + "Prefabs/team_tracker.prefab");
            root.layer = 5;
            var cameraNode = new GameObject("Item icon camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraNode, scene);
            var camera = cameraNode.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.cullingMask = 1 << 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.orthographic = true; camera.orthographicSize = .112f; camera.nearClipPlane = .01f; camera.farClipPlane = 4;
            camera.transform.position = new Vector3(.16f, .12f, -.3f); camera.transform.LookAt(new Vector3(0, .018f, 0));
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            var lamp = new GameObject("Icon key"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lamp, scene);
            var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.cullingMask = 1 << 5;
            lamp.transform.rotation = Quaternion.Euler(35, 155, 0);
            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            camera.Render(); var previous = RenderTexture.active;
            var icon = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            string iconPath = Root + "Icons/team_tracker.png";
            try
            {
                RenderTexture.active = target; icon.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); icon.Apply();
                File.WriteAllBytes(iconPath, icon.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(icon); }
            AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
            importer.textureType = TextureImporterType.Default; importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false; importer.maxTextureSize = 512; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            var translations = new Dictionary<string, string>
            {
                ["T-01 Squad tracker"] = "T-01 队友识别器",
                ["Equip in a special slot to see teammates outlined in teal, even through walls. Only the wearer sees the effect. No energy cost. Inactive in a backpack; retained on death or timeout while equipped."] =
                    "装入特殊装备槽后，仅装备者能看到队友的青色轮廓，可穿墙显示。不消耗能量。放在背包中不生效；装备时死亡或超时保留。"
            };
            var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
            foreach (var pair in translations)
                foreach (var reference in collection.Tables)
                {
                    var table = (StringTable)reference.asset;
                    table.AddEntry(pair.Key, table.LocaleIdentifier.Code == "zh-Hans" ? pair.Value : pair.Key);
                    EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
                }
            EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            return PhysicalItemAssetSetup.Validate();
        }
        finally
        {
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
