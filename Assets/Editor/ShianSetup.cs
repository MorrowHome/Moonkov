using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds the additional operator without editing the original character assets.</summary>
public static class ShianSetup
{
    public const string Root = "Assets/Shian";
    public const string ModelPath = Root + "/Art/Model/Shian.fbx";
    public const string PlayerPath = Root + "/Prefabs/ShianPlayer.prefab";
    public const string NetworkPath = Root + "/Prefabs/ShianNetworkPlayer.prefab";
    public const string MenuPath = Root + "/Prefabs/ShianMenuVisual.prefab";
    public const string CorpsePath = Root + "/Prefabs/ShianCorpseVisual.prefab";
    public const string DemoPath = Root + "/Scenes/ShianDemo.unity";
    private const string SourcePlayer = "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab";
    private const string SourceNetwork = "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab";
    private const string SourceModel = "Assets/DollSinger/Art/Model/LuoTianYi_DollSinger.fbx";
    private const string ResourceScene = "Assets/Scenes/GameResourcesSubScene.unity";
    private const string MenuSettingsPath = "Assets/Resources/Moonkov/ShianMenuCharacterSettings.asset";

    [Serializable] private sealed class Manifest
    {
        public string[] hairRoots, skirtRoots, tieRoots, chestRoots, passiveChainBones, firstPersonHiddenBonePrefixes;
        public TextureEntry[] textures;
        public MaterialEntry[] materials;
    }
    [Serializable] private sealed class MaterialEntry { public string name, sourceName, semanticName; public int textureIndex, flags, slot; public float[] diffuse; }
    [Serializable] private sealed class TextureEntry { public string asset; }
    [Serializable] private sealed class Bind { public BindBone[] bones; }
    [Serializable] private sealed class BindBone
    {
        public string name, parent;
        public float[] position, rotation, localPosition, localRotation, scale;
    }

    [MenuItem("Tools/Shian/Import Model And Export Bind")]
    public static void ImportModelAndExportBind()
    {
        EnsureEditMode();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (!importer) throw new InvalidOperationException("Missing Shian FBX: " + ModelPath);
        var model = Require<GameObject>(ModelPath);
        var transforms = model.GetComponentsInChildren<Transform>(true);
        var mapping = HumanMapping();
        var names = new HashSet<string>(transforms.Select(t => t.name));
        foreach (var pair in mapping)
            if (!names.Contains(pair.Value)) throw new InvalidOperationException("Missing Humanoid bone: " + pair.Value);
        importer.isReadable = true;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.humanDescription = new HumanDescription
        {
            human = mapping.Select(pair => new HumanBone { humanName = pair.Key, boneName = pair.Value,
                limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
            skeleton = CreateAvatarReferencePose(model, mapping),
            upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
            armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false
        };
        importer.SaveAndReimport();
        model = Require<GameObject>(ModelPath);
        var importedAnimator = model.GetComponent<Animator>();
        var avatar = importedAnimator ? importedAnimator.avatar : null;
        if (!avatar || !avatar.isHuman || !avatar.isValid) throw new InvalidOperationException("Shian Humanoid Avatar is invalid.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var holder = Staging(scene);
            var instance = Instantiate(model, holder.transform);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var bones = instance.GetComponentsInChildren<Transform>(true).Where(t => !t.GetComponent<Renderer>()).ToArray();
            if (bones.Select(t => t.name).Distinct().Count() != bones.Length)
                throw new InvalidOperationException("Shian bind contains duplicate transform names.");
            var bind = new Bind { bones = bones.Select(t => new BindBone
            {
                name = t.name, parent = t == instance.transform ? "" : t.parent.name,
                position = V(t.position), rotation = Q(t.rotation), localPosition = V(t.localPosition),
                localRotation = Q(t.localRotation), scale = V(t.lossyScale)
            }).ToArray() };
            Directory.CreateDirectory("Tools/Blender");
            File.WriteAllText("Tools/Blender/ShianBind.json", JsonUtility.ToJson(bind, true));
            Debug.Log("Shian Humanoid imported; exported " + bones.Length + " bind transforms without evaluating the Animator.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static SkeletonBone[] CreateAvatarReferencePose(GameObject model, Dictionary<string, string> mapping)
    {
        // The mesh is bound in an A-pose. Humanoid needs a separate T-pose
        // reference; using the mesh bind here pulls idle arms behind the body.
        // Pose only a temporary instance so the FBX bind and exact-rig gestures stay intact.
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var transforms = instance.GetComponentsInChildren<Transform>(true);
            var bones = transforms.ToDictionary(t => t.name);
            foreach (var side in new[] { ("Left", Vector3.left), ("Right", Vector3.right) })
            {
                var upper = bones[mapping[side.Item1 + "UpperArm"]];
                var lower = bones[mapping[side.Item1 + "LowerArm"]];
                var hand = bones[mapping[side.Item1 + "Hand"]];
                Vector3 direction = instance.transform.TransformDirection(side.Item2);
                upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, direction) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, direction) * lower.rotation;
            }
            return transforms.Select(t => new SkeletonBone { name = t.name, position = t.localPosition,
                rotation = t.localRotation, scale = t.localScale }).ToArray();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Shian/Build Additional Operator")]
    public static void Build()
    {
        EnsureEditMode();
        AssertSceneWritable(ResourceScene); AssertSceneWritable(DemoPath);
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath.StartsWith(Root + "/", StringComparison.Ordinal) && stage.scene.isDirty)
            throw new InvalidOperationException("Save or close the dirty Shian prefab stage before rebuilding.");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/Art/Model/ShianImport.json"));
        if (manifest?.materials == null || manifest.materials.Length == 0 || manifest.textures == null)
            throw new InvalidOperationException("Shian import manifest is incomplete.");
        var model = Require<GameObject>(ModelPath);
        var importedAnimator = model.GetComponent<Animator>();
        var avatar = importedAnimator ? importedAnimator.avatar : null;
        if (!avatar || !avatar.isHuman || !avatar.isValid) throw new InvalidOperationException("Run Import Model And Export Bind first.");
        Require<TextAsset>(Root + "/Animation/WeaponGestures.json");
        Require<TextAsset>(Root + "/Animation/MeleeGestures.json");
        foreach (var folder in new[] { Root + "/Art/Materials", Root + "/Prefabs", Root + "/Scenes" }) Directory.CreateDirectory(folder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var materials = BuildMaterials(manifest);
        BuildPlayer(manifest, materials);
        BuildNetwork();
        BuildVisuals();
        RegisterNetwork();
        BuildDemo();
        Debug.Log("Shian local/network/menu/corpse assets and isolated demo built. Dollsinger assets were preserved.");
    }

    [MenuItem("Tools/Shian/Update Character Translations")]
    public static void UpdateTranslations()
    {
        EnsureEditMode();
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        if (!collection) throw new InvalidOperationException("MoonkovUI string table collection is missing.");
        var translations = JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var keys = new[] { "SHIAN" };
        foreach (string key in keys)
            if (string.IsNullOrEmpty(translations[key]?.Value<string>())) throw new InvalidOperationException("Missing character translation: " + key);
        var tables = collection.StringTables.Where(table => table.LocaleIdentifier.Code == "en" || table.LocaleIdentifier.Code == "zh-Hans").ToArray();
        if (!tables.Any(table => table.LocaleIdentifier.Code == "en") || !tables.Any(table => table.LocaleIdentifier.Code == "zh-Hans"))
            throw new InvalidOperationException("Both English and Chinese MoonkovUI string tables are required.");
        int changed = 0;
        foreach (var table in tables)
        foreach (string key in keys)
        {
            string value = table.LocaleIdentifier.Code == "zh-Hans" ? translations[key].Value<string>() : key;
            if (table.GetEntry(key)?.Value == value) continue;
            table.AddEntry(key, value); EditorUtility.SetDirty(table); changed++;
        }
        if (changed > 0)
        {
            EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            foreach (var table in tables) AssetDatabase.SaveAssetIfDirty(table);
        }
        Debug.Log("Character translations updated: " + changed + " entries. Font assets were not modified.");
    }

    private static Dictionary<string, string> HumanMapping()
    {
        var result = new Dictionary<string, string>
        { { "Hips", "腰" }, { "Spine", "上半身" }, { "Chest", "上半身2" }, { "Neck", "首" }, { "Head", "頭" } };
        foreach (var side in new[] { ("Left", ".L"), ("Right", ".R") })
        {
            foreach (var bone in new[] { ("Shoulder", "肩"), ("UpperArm", "腕"), ("LowerArm", "ひじ"), ("Hand", "手首"),
                ("UpperLeg", "足"), ("LowerLeg", "ひざ"), ("Foot", "足首"), ("Toes", "足先EX") })
                result.Add(side.Item1 + bone.Item1, bone.Item2 + side.Item2);
            var fingers = new[] { ("Thumb", "親指"), ("Index", "人指"), ("Middle", "中指"), ("Ring", "薬指"), ("Little", "小指") };
            var segments = new[] { "Proximal", "Intermediate", "Distal" };
            foreach (var finger in fingers)
            for (int i = 0; i < segments.Length; i++)
                result.Add(side.Item1 + " " + finger.Item1 + " " + segments[i], finger.Item2 + (char)('０' + i + (finger.Item1 == "Thumb" ? 0 : 1)) + side.Item2);
        }
        return result;
    }

    private static Material[] BuildMaterials(Manifest manifest)
    {
        var template = Require<Material>("Assets/DollSinger/Art/Materials/Body.mat");
        return manifest.materials.Select(entry =>
        {
            if (entry.textureIndex < 0 || entry.textureIndex >= manifest.textures.Length)
                throw new InvalidOperationException("Invalid Shian texture index: " + entry.name);
            string texturePath = manifest.textures[entry.textureIndex].asset;
            var texture = Require<Texture2D>(texturePath);
            var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            bool faceTexture = entry.semanticName == "Face";
            if (textureImporter && (textureImporter.anisoLevel != 0 || faceTexture &&
                (textureImporter.maxTextureSize != 2048 || textureImporter.mipmapEnabled ||
                 textureImporter.textureCompression != TextureImporterCompression.Uncompressed)))
            {
                textureImporter.anisoLevel = 0;
                if (faceTexture)
                {
                    // The mirrored face UV follows an island with very narrow
                    // padding. Block compression/mips produce a visible centre seam.
                    textureImporter.maxTextureSize = 2048;
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    textureImporter.mipmapEnabled = false;
                }
                textureImporter.SaveAndReimport();
            }
            string path = Root + "/Art/Materials/" + entry.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(template); AssetDatabase.CreateAsset(material, path); }
            else { material.shader = template.shader; material.CopyPropertiesFromMaterial(template); }
            material.name = entry.slot == 9 || (entry.slot >= 30 && entry.slot <= 38) ? "Hair" :
                string.IsNullOrEmpty(entry.semanticName) ? entry.name : entry.semanticName;
            for (int i = 0; i < material.shader.GetPropertyCount(); i++)
                if (material.shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                    material.SetTexture(material.shader.GetPropertyName(i), null);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", entry.diffuse != null && entry.diffuse.Length == 4
                ? new Color(entry.diffuse[0], entry.diffuse[1], entry.diffuse[2], entry.diffuse[3]) : Color.white);
            // PMX bit 4 enables edges. Applying outlines to the split face/ruffle
            // surfaces creates seams which are absent in the authored model.
            bool outline = (entry.flags & 16) != 0;
            material.shader = Shader.Find(outline ? "Moonkov/lilToonMoonCutoutOutline" : "Moonkov/lilToonMoonCutout")
                ?? throw new InvalidOperationException("Missing Moonkov character shader.");
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", outline ? .0015f : 0);
            foreach (string property in new[] { "_UseEmission", "_UseEmission2nd", "_Cull" })
                if (material.HasProperty(property)) material.SetFloat(property, 0);
            foreach (string property in new[] { "_ShadowReceive", "_Shadow2ndReceive", "_Shadow3rdReceive" })
                if (material.HasProperty(property)) material.SetFloat(property, 1);
            if (entry.sourceName == "cheek")
            {
                // The source blush texture has a soft 0..0.5 alpha ramp; clipping it
                // turns the blush into a hard patch. Use lilToon's premultiplied pass.
                material.shader = Shader.Find("Hidden/lilToonTransparent") ?? throw new InvalidOperationException("Missing transparent lilToon shader.");
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_AlphaToMask", 0); material.SetFloat("_ZWrite", 0);
                material.SetFloat("_Cutoff", .001f); material.renderQueue = 3000;
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }).ToArray();
    }

    [MenuItem("Tools/Shian/Refresh Materials")]
    public static void RefreshMaterials()
    {
        EnsureEditMode();
        BuildMaterials(JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/Art/Model/ShianImport.json")));
    }

    private static void BuildPlayer(Manifest manifest, Material[] materials)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var holder = Staging(scene);
            var source = Instantiate(Require<GameObject>(SourcePlayer), holder.transform);
            var player = Instantiate(Require<GameObject>(ModelPath), holder.transform);
            player.name = "ShianPlayer";
            player.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); player.transform.localScale = Vector3.one;
            var sourceObjects = HierarchyObjects(source);
            var remap = new Dictionary<Object, Object> { [source] = player, [source.transform] = player.transform };
            var avatar = player.GetComponent<Animator>().avatar;
            foreach (var component in source.GetComponents<Component>())
            {
                if (!component) throw new InvalidOperationException("The source player has a missing component.");
                if (component is Transform) continue;
                var copy = player.GetComponent(component.GetType());
                if (!copy) copy = player.AddComponent(component.GetType());
                if (!copy) throw new InvalidOperationException("Could not add Shian component: " + component.GetType().FullName);
                EditorUtility.CopySerialized(component, copy); remap[component] = copy;
            }
            var animator = player.GetComponent<Animator>(); animator.avatar = avatar;
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var modelChildren = new HashSet<string>(Require<GameObject>(SourceModel).transform.Cast<Transform>().Select(t => t.name));
            foreach (var child in source.transform.Cast<Transform>().ToArray())
                if (!modelChildren.Contains(child.name)) child.SetParent(player.transform, false);
            AddHierarchyRemap(source, player, sourceObjects, remap);
            RemapReferences(player, sourceObjects, remap);
            Object.DestroyImmediate(source);
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.sharedMaterials = materials;
                renderer.receiveShadows = true; renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.updateWhenOffscreen = true;
            }
            ConfigureBody(player, manifest);
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void ConfigureBody(GameObject player, Manifest manifest)
    {
        var bones = player.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        Transform Bone(string name) => bones.TryGetValue(name, out var value) ? value : throw new InvalidOperationException("Shian bone missing: " + name);
        var capsule = player.GetComponent<CharacterController>();
        capsule.height = 1.65f; capsule.radius = .25f; capsule.center = new Vector3(0, .825f, 0);
        capsule.skinWidth = .025f; capsule.stepOffset = .3f;
        var spring = player.GetComponent<SecondaryBoneSpring>();
        spring.hairRoots = (manifest.hairRoots ?? Array.Empty<string>()).Concat(manifest.tieRoots ?? Array.Empty<string>()).Select(Bone).ToArray();
        var outerSkirtRoots = manifest.skirtRoots.Select(Bone).ToArray();
        spring.skirtRoots = outerSkirtRoots;
        spring.passiveChainBones = (manifest.passiveChainBones ?? Array.Empty<string>()).Select(Bone).ToArray();
        spring.chestRoots = (manifest.chestRoots ?? Array.Empty<string>()).Select(Bone).ToArray();
        spring.separateInnerSkirt = false; spring.frontSkirtBodyColliders = Array.Empty<SecondaryBoneSpring.BoneCapsule>();
        spring.bodyCollisionExemptBones = Array.Empty<Transform>();
        spring.skirtColliders = new[] { Sphere("手首.L", .038f), Sphere("人指２.L", .021f), Sphere("手首.R", .038f), Sphere("人指２.R", .021f) };
        var legs = new[] { Capsule("足.L", "ひざ.L", .082f), Capsule("足.R", "ひざ.R", .082f),
            Capsule("ひざ.L", "足首.L", .065f), Capsule("ひざ.R", "足首.R", .065f) };
        spring.skirtBodyColliders = legs;
        spring.hairBodyColliders = new[] { Capsule("腰", "上半身2", .10f), Capsule("上半身2", "首", .12f) }.Concat(legs).ToArray();
        var avoidance = player.GetComponent<SkirtHandAvoidance>();
        avoidance.animator = player.GetComponent<Animator>(); avoidance.skirtCenter = Bone("腰");
        avoidance.leftHand = Bone("手首.L"); avoidance.rightHand = Bone("手首.R");
        avoidance.outerSkirtRoots = outerSkirtRoots; avoidance.spring = spring; avoidance.minChainAlignment = .65f;
        var input = player.GetComponent<DollSingerInput>();
        var movement = player.GetComponent<DollSingerMovement>();
        var view = player.GetComponentInChildren<DollSingerView>(true);
        var halo = player.GetComponent<DollSingerHaloAim>();
        movement.input = input; movement.view = view; movement.m_GroundedRadius = capsule.radius;
        view.player = player; view.input = input; view.fallbackEyeWorldOffset = new Vector3(0, .065f, .045f);
        view.thirdPersonOrbitHeight = 1.22f;
        view.firstPersonHiddenBonePrefixes = manifest.firstPersonHiddenBonePrefixes ?? new[] { "頭", "前髪" };
        halo.input = input; halo.view = view; avoidance.haloAim = halo;
        SetReference(halo, "gestureBake", Require<TextAsset>(Root + "/Animation/WeaponGestures.json"));
        var melee = player.GetComponent<DollSingerMeleePresentation>();
        if (!melee) melee = player.AddComponent<DollSingerMeleePresentation>();
        melee.gestureBake = Require<TextAsset>(Root + "/Animation/MeleeGestures.json");
        if (!player.GetComponent<HumanoidTwistFollowers>()) player.AddComponent<HumanoidTwistFollowers>();

        SecondaryBoneSpring.BoneSphere Sphere(string name, float radius) => new SecondaryBoneSpring.BoneSphere { bone = Bone(name), radius = radius };
        SecondaryBoneSpring.BoneCapsule Capsule(string start, string end, float radius) => new SecondaryBoneSpring.BoneCapsule { start = Bone(start), end = Bone(end), radius = radius };
    }

    private static void BuildNetwork()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var holder = Staging(scene);
            var root = Instantiate(Require<GameObject>(SourceNetwork), holder.transform); root.name = "ShianNetworkPlayer";
            var presentation = root.GetComponent<DollSingerNetworkPresentation>();
            var oldVisual = (new SerializedObject(presentation).FindProperty("m_Model").objectReferenceValue as Component).gameObject;
            var visual = Instantiate(Require<GameObject>(PlayerPath), root.transform); visual.name = "ShianVisual";
            visual.transform.SetLocalPositionAndRotation(oldVisual.transform.localPosition, oldVisual.transform.localRotation);
            var oldObjects = HierarchyObjects(oldVisual);
            var remap = new Dictionary<Object, Object>(); AddHierarchyRemap(oldVisual, visual, oldObjects, remap);
            RemapReferences(root, oldObjects, remap); Object.DestroyImmediate(oldVisual);
            var input = visual.GetComponent<DollSingerInput>(); input.enabled = false;
            visual.GetComponent<DollSingerMovement>().enabled = false;
            var localCapsule = visual.GetComponent<CharacterController>(); var capsule = root.GetComponent<CharacterController>();
            capsule.height = localCapsule.height; capsule.radius = localCapsule.radius; capsule.center = localCapsule.center; localCapsule.enabled = false;
            var view = visual.GetComponentInChildren<DollSingerView>(true);
            view.camera.enabled = false; view.camera.tag = "Untagged";
            foreach (var listener in visual.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            view.gameObject.SetActive(false);
            var halo = visual.GetComponent<DollSingerHaloAim>(); SetBoolean(halo, "networkControlled", true);
            SetReference(presentation, "m_Model", visual.GetComponent<DollSingerMovement>());
            SetReference(presentation, "m_Input", input); SetReference(presentation, "m_View", view); SetReference(presentation, "m_Halo", halo);
            var player = root.GetComponent<PlayerGhost>();
            SetReference(player, "<VisualShotOrigin1P>k__BackingField", halo.haloVisual);
            SetReference(player, "<VisualShotOrigin3P>k__BackingField", halo.haloVisual);
            var shot = root.transform.Find("NetworkShotOrigin"); if (shot) shot.localPosition = new Vector3(0, 1.5f, 0);
            visual.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, NetworkPath);
            UpdateNetworkIdentity();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static void UpdateNetworkIdentity()
    {
        EnsureEditMode();
        var prefab = Require<GameObject>(NetworkPath);
        var serialized = new SerializedObject(prefab.GetComponent<GhostAuthoringComponent>());
        // A cloned Ghost initially carries its source prefab's serialized ID.
        // Persist the new asset GUID instead of relying on a later OnValidate.
        serialized.FindProperty("prefabId").stringValue = AssetDatabase.AssetPathToGUID(NetworkPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(prefab);
    }

    private static void BuildVisuals()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var holder = Staging(scene);
            var menu = Instantiate(Require<GameObject>(PlayerPath), holder.transform); menu.name = "ShianMenuVisual";
            var rig = menu.transform.Find("ViewRig"); if (rig) Object.DestroyImmediate(rig.gameObject);
            foreach (var behaviour in menu.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is SecondaryBoneSpring spring) spring.enabled = false;
                else if (!(behaviour is SkirtHandAvoidance) && !(behaviour is HumanoidTwistFollowers)) Object.DestroyImmediate(behaviour);
            foreach (var collider in menu.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var listener in menu.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(listener);
            foreach (var audio in menu.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
            var animator = menu.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            foreach (var t in menu.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            var menuPrefab = PrefabUtility.SaveAsPrefabAsset(menu, MenuPath);
            var settings = AssetDatabase.LoadAssetAtPath<MenuCharacterSettings>(MenuSettingsPath);
            var source = Require<MenuCharacterSettings>("Assets/Resources/Moonkov/MenuCharacterSettings.asset");
            if (!settings) { settings = Object.Instantiate(source); AssetDatabase.CreateAsset(settings, MenuSettingsPath); }
            else EditorUtility.CopySerialized(source, settings);
            settings.name = "ShianMenuCharacterSettings"; settings.VisualPrefab = menuPrefab;
            settings.GestureBake = Require<TextAsset>(Root + "/Animation/WeaponGestures.json");
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
            var corpse = Instantiate(menuPrefab, holder.transform); corpse.name = "ShianCorpseVisual";
            foreach (var behaviour in corpse.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is SecondaryBoneSpring) && !(behaviour is HumanoidTwistFollowers)) Object.DestroyImmediate(behaviour);
            foreach (var t in corpse.GetComponentsInChildren<Transform>(true))
                if (t.name == "HaloCrosshair" || t.name == "HaloBolt" || t.name == "HaloCrosshairOffset") t.gameObject.SetActive(false);
            foreach (var light in corpse.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);
            var corpsePrefab = PrefabUtility.SaveAsPrefabAsset(corpse, CorpsePath);
            var corpseSettings = Require<CorpsePresentationSettings>("Assets/Resources/Moonkov/CorpsePresentation.asset");
            var visuals = corpseSettings.CharacterVisuals ?? Array.Empty<GameObject>();
            if (visuals.Length < 5) Array.Resize(ref visuals, 5);
            visuals[4] = corpsePrefab; corpseSettings.CharacterVisuals = visuals;
            EditorUtility.SetDirty(corpseSettings); AssetDatabase.SaveAssetIfDirty(corpseSettings);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void RegisterNetwork()
    {
        var prefab = Require<GameObject>(NetworkPath); string guid = AssetDatabase.AssetPathToGUID(NetworkPath);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (!settings || !settings.DefaultGroup) throw new InvalidOperationException("Addressables default group is missing.");
        settings.CreateOrMoveEntry(guid, settings.DefaultGroup).address = NetworkPath;
        EditorUtility.SetDirty(settings); EditorUtility.SetDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(settings); AssetDatabase.SaveAssetIfDirty(settings.DefaultGroup);
        var scene = SceneManager.GetSceneByPath(ResourceScene); bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ResourceScene, OpenSceneMode.Additive);
        try
        {
            bool playerRegistered = false, ghostRegistered = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var authoring in root.GetComponentsInChildren<PlayerEntityPrefabsAuthoring>(true))
                { SetReference(authoring, "<ShianEntityPrefab>k__BackingField", prefab.GetComponent<GhostAuthoringComponent>()); playerRegistered = true; }
                foreach (var authoring in root.GetComponentsInChildren<GhostPrefabsAuthoring>(true))
                {
                    var serialized = new SerializedObject(authoring); var list = serialized.FindProperty("<GhostPrefabs>k__BackingField");
                    bool found = false;
                    for (int i = 0; i < list.arraySize; i++) found |= list.GetArrayElementAtIndex(i).FindPropertyRelative("m_AssetGUID").stringValue == guid;
                    if (!found)
                    {
                        int index = list.arraySize; list.InsertArrayElementAtIndex(index);
                        var reference = list.GetArrayElementAtIndex(index);
                        reference.FindPropertyRelative("m_AssetGUID").stringValue = guid;
                        reference.FindPropertyRelative("m_SubObjectName").stringValue = "";
                        reference.FindPropertyRelative("m_SubObjectType").stringValue = "";
                        reference.FindPropertyRelative("m_SubObjectGUID").stringValue = "";
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    ghostRegistered = true;
                }
            }
            if (!playerRegistered || !ghostRegistered) throw new InvalidOperationException("Resource scene is missing player/ghost authoring.");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void BuildDemo()
    {
        if (!File.Exists(DemoPath) && !AssetDatabase.CopyAsset("Assets/DollSinger/Scenes/DollSingerDemo.unity", DemoPath))
            throw new InvalidOperationException("Could not copy the standalone character demo.");
        var scene = SceneManager.GetSceneByPath(DemoPath); bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Additive);
        try
        {
            var oldPlayer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DollSingerMovement>(true)).FirstOrDefault();
            if (!oldPlayer) throw new InvalidOperationException("Demo player is missing.");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(PlayerPath), scene);
            player.transform.SetParent(oldPlayer.transform.parent, false);
            player.transform.SetPositionAndRotation(oldPlayer.transform.position, oldPlayer.transform.rotation);
            var oldObjects = HierarchyObjects(oldPlayer.gameObject); var remap = new Dictionary<Object, Object>();
            AddHierarchyRemap(oldPlayer.gameObject, player, oldObjects, remap);
            foreach (var root in scene.GetRootGameObjects()) RemapReferences(root, oldObjects, remap);
            Object.DestroyImmediate(oldPlayer.gameObject);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }

    private static HashSet<Object> HierarchyObjects(GameObject root) => new HashSet<Object>(root.GetComponentsInChildren<Transform>(true)
        .SelectMany(t => new Object[] { t.gameObject }.Concat(t.GetComponents<Component>())));

    private static void AddHierarchyRemap(GameObject source, GameObject destination, HashSet<Object> sourceObjects, Dictionary<Object, Object> remap)
    {
        var byName = destination.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var body = destination.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
        remap[source] = destination; remap[source.transform] = destination.transform;
        foreach (var value in sourceObjects)
        {
            if (!value || remap.ContainsKey(value)) continue;
            var original = value is GameObject go ? go.transform : value is Component component ? component.transform : null;
            if (!original) continue;
            if (original.IsChildOf(destination.transform)) { remap[value] = value; continue; }
            Transform target = original == source.transform ? destination.transform : destination.transform.Find(AnimationUtility.CalculateTransformPath(original, source.transform));
            if (!target) byName.TryGetValue(original.name, out target);
            if (value is SkinnedMeshRenderer) remap[value] = body;
            else if (value is GameObject) remap[value] = target ? target.gameObject : null;
            else if (value is Transform) remap[value] = target;
            else if (value is Component c) remap[value] = target ? target.GetComponent(c.GetType()) : null;
        }
    }

    private static void RemapReferences(GameObject root, HashSet<Object> sourceObjects, Dictionary<Object, Object> remap)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (!component || component is Transform) continue;
            var serialized = new SerializedObject(component); var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.propertyPath == "m_GameObject" || property.propertyPath == "m_Script") continue;
                var value = property.objectReferenceValue;
                if (value && sourceObjects.Contains(value)) property.objectReferenceValue = remap.TryGetValue(value, out var mapped) ? mapped : null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static GameObject Instantiate(GameObject asset, Transform parent)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        return instance;
    }
    private static GameObject Staging(Scene scene)
    {
        var holder = new GameObject("Shian asset staging"); holder.SetActive(false); SceneManager.MoveGameObjectToScene(holder, scene); return holder;
    }
    private static T Require<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new InvalidOperationException("Missing asset: " + path);
        return asset;
    }
    private static void SetReference(Object target, string property, Object value)
    { var serialized = new SerializedObject(target); var field = serialized.FindProperty(property) ?? throw new InvalidOperationException("Missing field: " + property); field.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetBoolean(Object target, string property, bool value)
    { var serialized = new SerializedObject(target); serialized.FindProperty(property).boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void AssertSceneWritable(string path)
    { var scene = SceneManager.GetSceneByPath(path); if (scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save or close the dirty scene before rebuilding: " + path); }
    private static void EnsureEditMode()
    { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before building Shian assets."); }
    private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    private static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
}
