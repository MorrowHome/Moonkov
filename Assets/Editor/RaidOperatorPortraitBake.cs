using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;

public static class RaidOperatorPortraitBake
{
    [Serializable] private sealed class Anchor { public string slot; public float x, y; }
    [Serializable] private sealed class Layout { public Anchor[] anchors; public float aspect = .5f; }
    [MenuItem("Tools/Moonkov/Bake Static Field Operator")]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake outside Play mode.");
        using var view = new MenuCharacterView(true);
        view.Advance(.05f); view.Render(new Vector2(768, 1536));
        if (!view.Target) throw new InvalidOperationException("Character renderer did not produce a portrait.");
        var roots = view.DisplayScene.GetRootGameObjects();
        var animator = roots.SelectMany(r => r.GetComponentsInChildren<Animator>()).First();
        var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).First();
        foreach (var transform in roots.SelectMany(r => r.GetComponentsInChildren<Transform>()))
            if (transform.name == "HaloCrosshair") transform.gameObject.SetActive(false);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
        float top = animator.GetBoneTransform(HumanBodyBones.Head).position.y + .4f;
        float bottom = Mathf.Min(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y, animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y) - .08f;
        var centre = new Vector3(hips.x, (top + bottom) * .5f, hips.z);
        camera.orthographic = true; camera.orthographicSize = (top - bottom) * .54f;
        camera.transform.position = centre + Vector3.forward * 5; camera.transform.LookAt(centre);
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = view.Target });
        var previous = RenderTexture.active; var pixels = new Texture2D(view.Target.width, view.Target.height, TextureFormat.RGBA32, false);
        const string imagePath = "Assets/Resources/Moonkov/UI/FieldOperatorPortrait.png";
        try { RenderTexture.active = view.Target; pixels.ReadPixels(new Rect(0, 0, view.Target.width, view.Target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(imagePath, pixels.EncodeToPNG()); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); }
        var slots = new[] { "Helmet", "Primary", "Shield", "Secondary", "Pistol", "Melee" };
        var bones = new[] { HumanBodyBones.Neck, HumanBodyBones.RightUpperArm, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftUpperLeg };
        var offsets = new[] { new Vector2(-.4f, -.04f), new Vector2(.4f, .02f), new Vector2(-.4f, .04f), new Vector2(.4f, .08f), new Vector2(-.4f, .22f), new Vector2(.4f, .22f) };
        var layout = new Layout { anchors = new Anchor[slots.Length] };
        for (int i = 0; i < slots.Length; i++)
        {
            var point = camera.WorldToViewportPoint(animator.GetBoneTransform(bones[i]).position);
            layout.anchors[i] = new Anchor { slot = slots[i], x = Mathf.Clamp01(point.x + offsets[i].x), y = Mathf.Clamp01(1 - point.y + offsets[i].y) };
        }
        File.WriteAllText("Assets/Resources/Moonkov/UI/FieldOperatorPortraitLayout.json", JsonUtility.ToJson(layout, true));
        AssetDatabase.ImportAsset(imagePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        AssetDatabase.ImportAsset("Assets/Resources/Moonkov/UI/FieldOperatorPortraitLayout.json");
        Debug.Log("Baked static production-character portrait and six bone-derived equipment anchors. Runtime uses only the texture.");
    }
}
