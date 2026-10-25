using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

// Called explicitly by the targeted review. Never changes the source PNG pixels.
public static class HaloModuleArtworkSetup
{
    public const string TexturePath = "Assets/Resources/Moonkov/HaloModules/ModuleArtwork.png";
    public const string MaterialPath = "Assets/Resources/Moonkov/HaloModules/ModuleArtwork.mat";
    public const string ResourcePath = "Moonkov/HaloModules/ModuleArtwork";
    private static readonly string[] Names = { "halo_capacitor", "halo_charge_bridge", "halo_mooncore_recycler",
        "halo_focus_lens", "halo_fan_array", "halo_pulse_controller", "halo_hit_marker" };

    public static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Prepare halo artwork in Edit Mode only.");
        string sourcePath = Path.Combine(Application.dataPath, "Resources/Moonkov/HaloModules/ModuleArtwork.png");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Halo module artwork is missing.", sourcePath);
        var shader = Shader.Find("DollSinger/RifleHaloInk");
        if (shader == null) throw new InvalidOperationException("The existing RifleHaloInk shader is unavailable.");
        var existingMaterial = AssetDatabase.LoadMainAssetAtPath(MaterialPath);
        if (existingMaterial != null && !(existingMaterial is Material))
            throw new InvalidOperationException("The halo artwork material path contains another asset type.");

        // Decode with Unity at the original resolution. Import limits must never
        // scale the coordinate system used by SpriteRect metadata.
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        TextureImporter importer = null;
        Dictionary<string, GUID> expectedIds = null;
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                throw new InvalidOperationException("Unity could not decode the halo artwork PNG.");
            int width = source.width, height = source.height;
            if (width < 8 || height < 4 || width % 4 != 0 || height % 2 != 0)
                throw new InvalidOperationException("Halo artwork must be an exact four-column, two-row sheet.");
            var pixels = source.GetPixels32();
            if (!pixels.Any(pixel => pixel.a == 0) || !pixels.Any(pixel => pixel.a >= 128))
                throw new InvalidOperationException("Halo artwork needs real transparent pixels and visible shapes.");
            if (pixels.Any(pixel => pixel.a > 0 && (pixel.r != 255 || pixel.g != 255 || pixel.b != 255)))
                throw new InvalidOperationException("Halo module artwork must use white alpha masks. Runtime applies each slot's flat tint.");
            var rects = Slice(pixels, width, height);

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("The artwork is not handled by TextureImporter.");
            Configure(importer, true);
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            var provider = Provider(importer);
            var textureProvider = provider.GetDataProvider<ITextureDataProvider>();
            if (textureProvider == null) throw new InvalidOperationException("The importer cannot provide original texture dimensions.");
            textureProvider.GetTextureActualWidthAndHeight(out int actualWidth, out int actualHeight);
            if (actualWidth != width || actualHeight != height)
                throw new InvalidOperationException("Unity source texture dimensions do not match the decoded PNG.");

            var previous = provider.GetSpriteRects() ?? Array.Empty<SpriteRect>();
            if (previous.Any(rect => !Names.Contains(rect.name)) || previous.Select(rect => rect.name).Distinct().Count() != previous.Length)
                throw new InvalidOperationException("Unexpected existing sprite names were preserved. Review this artwork's metadata before slicing.");
            var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameProvider == null) throw new InvalidOperationException("Stable sprite name/file IDs are not supported by this importer.");
            var pairs = nameProvider.GetNameFileIdPairs().ToList();
            expectedIds = new Dictionary<string, GUID>();
            for (int i = 0; i < rects.Length; i++)
            {
                var old = previous.FirstOrDefault(rect => rect.name == Names[i]);
                var pair = pairs.FirstOrDefault(value => value.name == Names[i]);
                GUID id = old != null && !old.spriteID.Empty() ? old.spriteID : pair != null ? pair.GetFileGUID() : default;
                if (id.Empty()) id = GUID.Generate();
                rects[i].spriteID = id;
                expectedIds.Add(Names[i], id);
                if (pair == null) pairs.Add(new SpriteNameFileIdPair(Names[i], id));
                else pair.SetFileGUID(id);
            }
            provider.SetSpriteRects(rects);
            nameProvider.SetNameFileIdPairs(pairs);
            provider.Apply();
            importer.SaveAndReimport();
            Debug.Log($"Halo module artwork: Unity decoded {width}x{height}; seven alpha-trimmed sprite rects prepared.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            // No readable source texture is retained in the shipped asset, even
            // if a metadata capability or validation check interrupted Prepare.
            if (importer != null)
            {
                importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                Configure(importer, false);
                importer.SaveAndReimport();
            }
        }

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var material = existingMaterial as Material;
        if (material == null)
        {
            material = new Material(shader) { name = "ModuleArtwork" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Gain", 1);
        material.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);

        var loaded = Resources.LoadAll<Sprite>(ResourcePath);
        if (loaded.Length != Names.Length || Names.Any(name => loaded.Count(sprite => sprite.name == name) != 1))
            throw new InvalidOperationException("Resources.LoadAll did not return exactly the seven named halo module sprites.");
        foreach (var sprite in loaded)
            if (sprite.texture == null || sprite.rect.width <= 0 || sprite.rect.height <= 0 || sprite.vertices.Length < 3)
                throw new InvalidOperationException("The imported sprite is empty: " + sprite.name);
        var finalRects = Provider((TextureImporter)AssetImporter.GetAtPath(TexturePath)).GetSpriteRects();
        foreach (var rect in finalRects)
            if (!expectedIds.TryGetValue(rect.name, out var id) || rect.spriteID != id)
                throw new InvalidOperationException("A halo module sprite ID changed during reimport: " + rect.name);

        // The runtime class is updated in parallel with this importer. Reflection
        // permits this editor file to compile during that update; completion still
        // requires the explicit cache refresh API, never a field-name workaround.
        var refresh = typeof(HaloModuleVisual).GetMethod("RefreshArtworkCache", BindingFlags.Public | BindingFlags.Static,
            null, Type.EmptyTypes, null);
        if (refresh == null) throw new InvalidOperationException("HaloModuleVisual.RefreshArtworkCache must exist before the artwork review runs.");
        refresh.Invoke(null, null);
        Debug.Log("Halo module artwork ready: seven stable sprite IDs, transparent tight rects, original PNG pixels, readable=false.");
    }

    private static void Configure(TextureImporter importer, bool readable)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 1000;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        // A small mask atlas needs crisp alpha edges. Keep it lossless instead of
        // introducing block compression around the simple geometric silhouettes.
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.isReadable = readable;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }

    private static ISpriteEditorDataProvider Provider(TextureImporter importer)
    {
        var factory = new SpriteDataProviderFactories(); factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null) throw new InvalidOperationException("The importer has no sprite data provider.");
        provider.InitSpriteEditorDataProvider();
        var edit = provider.GetDataProvider<ISpriteFrameEditCapability>();
        if (edit == null) throw new InvalidOperationException("Sprite editing capabilities are not available.");
        var capability = edit.GetEditCapability();
        foreach (var required in new[] { EEditCapability.EditSpriteName, EEditCapability.EditSpriteRect,
            EEditCapability.EditPivot, EEditCapability.EditBorder, EEditCapability.CreateAndDeleteSprite })
            if (!capability.HasCapability(required))
                throw new InvalidOperationException("Sprite metadata operation is unsupported: " + required);
        return provider;
    }

    private static SpriteRect[] Slice(Color32[] pixels, int width, int height)
    {
        int cellWidth = width / 4, cellHeight = height / 2;
        int margin = Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(cellWidth, cellHeight) * .01f));
        var result = new SpriteRect[Names.Length];
        for (int cell = 0; cell < 8; cell++)
        {
            int x0 = cell % 4 * cellWidth, y0 = height - (cell / 4 + 1) * cellHeight;
            int minX = x0 + cellWidth, minY = y0 + cellHeight, maxX = x0 - 1, maxY = y0 - 1;
            int visible = 0, nonTransparent = 0;
            for (int y = y0; y < y0 + cellHeight; y++)
                for (int x = x0; x < x0 + cellWidth; x++)
                {
                    byte alpha = pixels[y * width + x].a;
                    if (alpha > 0) nonTransparent++;
                    if (alpha >= 8)
                    {
                        minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y); visible++;
                    }
                }
            if (cell == 7)
            {
                if (nonTransparent != 0) throw new InvalidOperationException("The eighth artwork cell must stay fully transparent.");
                continue;
            }
            if (visible < 4 || maxX - minX < 1 || maxY - minY < 1)
                throw new InvalidOperationException("Artwork cell has no usable alpha shape: " + Names[cell]);
            minX = Mathf.Max(x0, minX - margin); minY = Mathf.Max(y0, minY - margin);
            maxX = Mathf.Min(x0 + cellWidth - 1, maxX + margin); maxY = Mathf.Min(y0 + cellHeight - 1, maxY + margin);
            bool transparent = false;
            for (int y = minY; y <= maxY && !transparent; y++)
                for (int x = minX; x <= maxX; x++)
                    if (pixels[y * width + x].a == 0) { transparent = true; break; }
            if (!transparent) throw new InvalidOperationException("The artwork shape has no transparent border: " + Names[cell]);
            result[cell] = new SpriteRect { name = Names[cell], rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1),
                alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f), border = Vector4.zero };
        }
        for (int i = 0; i < result.Length; i++)
            for (int j = i + 1; j < result.Length; j++)
                if (result[i].rect.Overlaps(result[j].rect)) throw new InvalidOperationException("Artwork sprite rects overlap.");
        return result;
    }
}
