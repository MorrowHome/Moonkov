using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HaloAppearanceSetup
{
    private const string AssetPath = "Assets/Resources/Moonkov/HaloAppearance.asset";
    private const string TexturePath = "Assets/Resources/Moonkov/HaloDefenseArtwork.png";

    [MenuItem("Tools/Moonkov/Rebuild Halo Defense Artwork")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        const int tileSize = 512, columns = 4;
        var atlas = new Texture2D(tileSize * columns, tileSize * columns, TextureFormat.RGBA32, false);
        atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
        var entries = new HaloAppearanceCatalog.Entry[DollSingerWeapons.Nocturne - DollSingerWeapons.Halo + 1];
        try
        {
            for (uint id = DollSingerWeapons.Halo; id <= DollSingerWeapons.Nocturne; id++)
            {
                var weapon = (DollSingerHaloAim.HaloWeapon)(id - 1);
                var scene = EditorSceneManager.NewPreviewScene();
                Texture2D pixels = null;
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/" + weapon + "Halo.prefab");
                    if (!prefab) throw new InvalidOperationException("Missing formal halo: " + weapon);
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    root.transform.localScale = Vector3.one;
                    Color color = Pose(root);
                    var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    if (renderers.Length == 0) throw new InvalidOperationException("Empty formal halo: " + weapon);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    pixels = HaloWeaponSetup.Rasterize(renderers, bounds, tileSize);
                    var mask = pixels.GetPixels32();
                    if (!mask.Any(pixel => pixel.a > 10)) throw new InvalidOperationException("Empty defense artwork: " + weapon);
                    // Preserve authored facets and inner marks as well as the silhouette.
                    // The equipped halo supplies the hue; this mask retains the artwork's contrast.
                    int peak = mask.Max(pixel => Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
                    for (int i = 0; i < mask.Length; i++)
                    {
                        byte ink = (byte)(Mathf.Max(mask[i].r, Mathf.Max(mask[i].g, mask[i].b)) * 255 / Mathf.Max(1, peak));
                        mask[i] = new Color32(ink, ink, ink, mask[i].a);
                    }
                    int index = (int)(id - DollSingerWeapons.Halo), x = index % columns, y = index / columns;
                    atlas.SetPixels32(x * tileSize, y * tileSize, tileSize, tileSize, mask);
                    color.a = 1;
                    entries[index] = new HaloAppearanceCatalog.Entry
                    {
                        Weapon = id, Color = color,
                        ArtworkRect = new Vector4(x / (float)columns, y / (float)columns, 1f / columns, 1f / columns)
                    };
                }
                finally
                {
                    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            atlas.Apply(); File.WriteAllBytes(TexturePath, atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var catalog = AssetDatabase.LoadAssetAtPath<HaloAppearanceCatalog>(AssetPath);
            if (!catalog) { catalog = ScriptableObject.CreateInstance<HaloAppearanceCatalog>(); AssetDatabase.CreateAsset(catalog, AssetPath); }
            catalog.Artwork = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath); catalog.Entries = entries;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            Debug.Log("Halo appearance: baked all 13 formal prefab assemblies and colors into one shared defense atlas.");
        }
        finally { UnityEngine.Object.DestroyImmediate(atlas); }
    }

    private static Color Pose(GameObject root)
    {
        if (root.TryGetComponent<RifleHaloVisual>(out var rifle))
        {
            rifle.SetEquipped(false, 30); rifle.SetAimBlend(0); rifle.SetEquipped(true, 30); rifle.ApplyLayout();
            return rifle.lightColor;
        }
        if (root.TryGetComponent<RevolverHaloVisual>(out var revolver))
        {
            revolver.SetEquipped(false, 6); revolver.SetAimBlend(0); revolver.SetEquipped(true, 6); revolver.Step(1);
            return revolver.lightColor;
        }
        if (root.TryGetComponent<ShotgunHaloVisual>(out var shotgun))
        {
            shotgun.SetEquipped(false, 4); shotgun.SetAimBlend(0); shotgun.SetEquipped(true, 4); shotgun.ApplyLayout();
            return shotgun.lightColor;
        }
        if (root.TryGetComponent<SniperHaloVisual>(out var sniper))
        {
            sniper.SetEquipped(false, 5); sniper.SetAimBlend(0); sniper.SetEquipped(true, 5); sniper.ApplyLayout();
            return sniper.lightColor;
        }
        var arsenal = root.GetComponent<ArsenalHaloVisual>();
        if (!arsenal) throw new InvalidOperationException("Unsupported halo prefab: " + root.name);
        arsenal.SetEquipped(true); arsenal.SetAimBlend(0); arsenal.SetNetworkState(30, 30, false, 0, 0, 0, 30, 0); arsenal.Step(0);
        return arsenal.lightColor;
    }
}
