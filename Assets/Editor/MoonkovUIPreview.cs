using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;

public sealed partial class MoonkovUIPreview : EditorWindow
{
    private StashScreen m_Screen;
    private VisualElement m_PreviewHost;
    private Toggle m_LiveToggle;
    private double m_LiveUntil;
    private void SetLivePreview(bool live)
    {
        m_PreviewHost?.EnableInClassList(TerminalMotion.LiveEditorPreviewClass,live);
        m_LiveUntil=live ? EditorApplication.timeSinceStartup+30 : 0;
    }
    private void OnInspectorUpdate()
    {
        if(m_LiveUntil<=0 || EditorApplication.timeSinceStartup<m_LiveUntil)return;
        SetLivePreview(false);m_LiveToggle?.SetValueWithoutNotify(false);
    }
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) CreateGUI();
        else SuspendPreview();
    }
    private void SuspendPreview()
    {
        SetLivePreview(false);
        m_Screen?.Dispose(); m_Screen = null; rootVisualElement.Clear();
        var message = new Label("UI preview is paused during Play mode. View the menu in the Game window.");
        message.style.whiteSpace = WhiteSpace.Normal; message.style.paddingLeft = 24; message.style.paddingTop = 24;
        rootVisualElement.Add(message);
    }
    [MenuItem("Tools/Moonkov/UI Preview")]
    public static void Open()
    {
        var window = GetWindow<MoonkovUIPreview>(); window.titleContent = new GUIContent("Moonkov UI / Preview");
        window.minSize = new Vector2(1000, 680); window.Show();
    }
    [MenuItem("Tools/Moonkov/Loadout Preview")]
    public static void OpenLoadout()
    {
        Open(); var window = GetWindow<MoonkovUIPreview>();
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (window.m_Screen == null) window.CreateGUI();
        window.m_Screen.NavigatePage(1);
    }
    public void CreateGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { SuspendPreview(); return; }
        m_Screen?.Dispose(); m_Screen = null; rootVisualElement.Clear();
        m_LiveUntil=0;
        m_LiveToggle=new Toggle("ANIMATE PREVIEW / 30 SECONDS") {tooltip="Static by default. Live 3D/UI previews stop after 30 seconds; Play mode stays animated."};
        m_LiveToggle.style.height=26;m_LiveToggle.style.flexShrink=0;rootVisualElement.Add(m_LiveToggle);
        m_LiveToggle.RegisterValueChangedCallback(e=>SetLivePreview(e.newValue));
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/StashScreen.uxml");
        var host = new VisualElement(); host.style.flexGrow = 1; tree.CloneTree(host); rootVisualElement.Add(host);
        m_PreviewHost=host;
        m_Screen = new StashScreen(host, () => Debug.Log("UI preview: in-game connection setup opens here."), () => Debug.Log("UI preview: no account is logged in."), () => Present(), preview: true);
        Present();
    }
    private void Present() => m_Screen?.Present("PREVIEW / SAMPLE DATA", 2, 128, 36, 12, true, false);
    private void OnDisable()
    {
        SetLivePreview(false);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        m_Screen?.Dispose(); m_Screen = null;
    }

    [MenuItem("Tools/Moonkov/Capture UI Preview", true)]
    [MenuItem("Tools/Moonkov/Capture All Pages", true)]
    [MenuItem("Tools/Moonkov/Validate UI Interactions", true)]
    [MenuItem("Tools/Moonkov/Rebuild Menu Character", true)]
    [MenuItem("Tools/Moonkov/Validate Menu Character", true)]
    [MenuItem("Tools/Moonkov/Validate Loadout", true)]
    private static bool CanRunPreviewTools() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Moonkov/Capture UI Preview")]
    public static void CapturePreview()
    {
        var window = GetWindow<MoonkovUIPreview>(); window.Focus(); window.Repaint();
        EditorApplication.delayCall += () =>
        {
            var rect = window.position;
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
            var pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position, width, height);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.SetPixels(pixels); image.Apply();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/UI/MoonkovHubPreview.png"));
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { DestroyImmediate(image); }
            Debug.Log("Saved the actual Unity UI preview to Docs/UI/MoonkovHubPreview.png.");
        };
    }

    [MenuItem("Tools/Moonkov/Capture All Pages")]
    public static void CaptureAllPages()
    {
        var window = GetWindow<MoonkovUIPreview>(); window.Focus();
        int page = 0, frames = 0; window.m_Screen.NavigatePage(page);
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            window.Repaint(); if (++frames < 75) return; frames = 0;
            var rect = window.position;
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position, width, height)); image.Apply();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Docs/UI/MoonkovPage{page}.png"));
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { DestroyImmediate(image); }
            if (++page < 9) { window.m_Screen.NavigatePage(page); return; }
            EditorApplication.update -= tick; window.m_Screen.NavigatePage(0);
            Debug.Log("Saved nine Moonkov page captures to Docs/UI/.");
        };
        EditorApplication.update += tick;
    }

    [MenuItem("Tools/Moonkov/Validate UI Interactions")]
    public static void ValidateInteractions()
    {
        var layout = new StashLayout(10, 24);
        layout.Add("a", 2, 2, 0, 0); layout.Add("b", 1, 2, 2, 0);
        if (layout.TryMove("b", 1, 0, false) || layout.TryMove("b", 10, 0, false)) throw new System.Exception("Invalid placement accepted.");
        if (!layout.TryMove("b", 3, 2, true)) throw new System.Exception("Valid rotated placement rejected.");
        layout.Items["b"].Locked = true;
        if (layout.TryMove("b", 4, 3, true)) throw new System.Exception("Locked position changed.");
        var window = GetWindow<MoonkovUIPreview>();
        for (int page = 0; page < 9; page++) window.m_Screen.NavigatePage(page);
        window.m_Screen.NavigatePage(0);
        var scope = new VisualElement(); window.rootVisualElement.Add(scope);
        using (var windows = new TerminalWindows(scope, () => { }))
        {
            var first = windows.Open("First"); var second = windows.Open("Second");
            using (var key = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) scope.SendEvent(key);
            if (first.parent == null || second.parent != null) throw new System.Exception("ESC closed the wrong window.");
        }
        scope.RemoveFromHierarchy();
        Debug.Log("Moonkov UI checks passed: grid collision, bounds, rotation, locking; nine pages; ESC top-window behavior.");
    }

    [MenuItem("Tools/Moonkov/Rebuild Menu Character")]
    public static void RebuildMenuCharacter()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "MenuCharacterVisual";
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is SecondaryBoneSpring) && !(behaviour is SkirtHandAvoidance)) DestroyImmediate(behaviour);
            foreach (var camera in model.GetComponentsInChildren<Camera>(true)) DestroyImmediate(camera);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
            foreach (var audio in model.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            foreach (var listener in model.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);
            foreach (var light in model.GetComponentsInChildren<Light>(true)) DestroyImmediate(light);
            foreach (var transform in model.GetComponentsInChildren<Transform>(true))
                if (transform.name == "HaloBolt") transform.gameObject.SetActive(false);
            foreach (var renderer in model.GetComponentsInChildren<ParticleSystemRenderer>(true)) renderer.enabled = false;
            foreach (var renderer in model.GetComponentsInChildren<TrailRenderer>(true)) renderer.enabled = false;
            foreach (var lod in model.GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0);
            var animator = model.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Directory.CreateDirectory("Assets/Resources/Moonkov");
            var visual = PrefabUtility.SaveAsPrefabAsset(model, "Assets/Resources/Moonkov/MenuCharacterVisual.prefab");
            const string settingsPath = "Assets/Resources/Moonkov/MenuCharacterSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<MenuCharacterSettings>(settingsPath);
            if (settings == null) { settings = CreateInstance<MenuCharacterSettings>(); AssetDatabase.CreateAsset(settings, settingsPath); }
            settings.VisualPrefab = visual;
            MenuCharacterAppearanceSetup.Bind(settings, Unity.MP_FPS.GameSettings.DollSingerCharacterIndex);
            ConfigureMenuMotions(settings);
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            var window = GetWindow<MoonkovUIPreview>(); window.CreateGUI(); window.Repaint();
            Debug.Log("Menu character rebuilt: live model, visible halo, masked Halo Aim gesture, random holds and smooth idle transitions.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Moonkov/Update Menu Motions")]
    public static void UpdateMenuMotions()
    {
        var settings = AssetDatabase.LoadAssetAtPath<MenuCharacterSettings>("Assets/Resources/Moonkov/MenuCharacterSettings.asset");
        if (settings == null) throw new System.Exception("Rebuild the menu character first.");
        ConfigureMenuMotions(settings);
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        Debug.Log($"Menu motion catalog updated: {settings.Motions.Length} entries, all character clips and four weapons' aim/fire/reload bakes.");
    }

    private static void ConfigureMenuMotions(MenuCharacterSettings settings)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/DollSinger/Animation/DollSingerThirdPersonHalo.controller");
        var turns = controller.layers.First(layer => layer.name == "Turn In Place").avatarMask;
        settings.HaloAimMask = controller.layers.First(layer => layer.name == "Halo Aim").avatarMask;
        settings.GestureBake = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DollSinger/Animation/Blender/WeaponGestures.json");
        var clips = new List<AnimationClip> {
            AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Unarmed_Idle.anim"),
            AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/DollSinger_FingerGunAim.anim"),
            AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Turn_Left.anim"),
            AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Turn_Right.anim")
        };
        var available = controller.animationClips.Concat(AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/DollSinger/Animation" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
            .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)).Distinct().OrderBy(clip => clip.name, System.StringComparer.Ordinal);
        foreach (var clip in available) if (!clips.Contains(clip)) clips.Add(clip);
        var masks = new List<AvatarMask>();
        var options = new List<MenuCharacterMotion>();
        foreach (var clip in clips)
        {
            if (clip == null || !clip.isHumanMotion || clip.length <= 0) throw new System.Exception("Invalid menu motion: " + clip);
            int weapon = clip.name == "DollSinger_RifleGesture" ? 1 : clip.name == "DollSinger_RevolverGesture" ? 2 :
                clip.name == "DollSinger_ShotgunGesture" ? 3 : clip.name == "DollSinger_SniperGesture" ? 4 : 0;
            bool turn = clip.name == "Turn_Left" || clip.name == "Turn_Right";
            bool upper = weapon > 0 || clip.name == "DollSinger_FingerGunAim" || clip.name == "DollSinger_FreeFallCover";
            masks.Add(turn ? turns : upper ? settings.HaloAimMask : null);
            options.Add(new MenuCharacterMotion {
                Name = clip.name, Loop = clip.isLooping && !turn, Weapon = weapon,
                HoldSeconds = upper ? 3.5f : 0, Turn = turn ? clip.name == "Turn_Left" ? -1 : 1 : 0,
                PreserveHands = upper || clip.name.StartsWith("Prone_") || clip.name.StartsWith("Crouch_"),
                Tiptoe = clip.name.StartsWith("Tiptoe_")
            });
        }
        for (int weapon = 1; weapon <= 4; weapon++)
        {
            var aim = options.First(option => option.Weapon == weapon);
            foreach (var action in new[] { MenuWeaponAction.Fire, MenuWeaponAction.Reload })
            {
                clips.Add(clips[0]); masks.Add(null);
                options.Add(new MenuCharacterMotion { Name = aim.Name + "_" + action, Weapon = weapon, WeaponAction = action,
                    Loop = true, PreserveHands = true, HoldSeconds = action == MenuWeaponAction.Fire ? 1.5f : weapon == 1 ? 2f : weapon == 4 ? 3f : 1f });
            }
        }
        settings.Motions = clips.ToArray(); settings.MotionMasks = masks.ToArray(); settings.MotionOptions = options.ToArray();
    }

    [MenuItem("Tools/Moonkov/Validate Menu Character")]
    public static void ValidateMenuCharacter()
    {
        var view = new MenuCharacterView(characterIndex: Unity.MP_FPS.GameSettings.DollSingerCharacterIndex);
        try
        {
            var inventory = Unity.MP_FPS.Inventory.InventoryGraph.Create(false, false);
            inventory.Items.Add(new Unity.MP_FPS.Inventory.InventoryItem { Id = System.Guid.NewGuid().ToString("D"),
                Code = "rifle", Parent = "equipment", Region = "Primary" });
            view.SetEquipmentInventory(inventory);
            var settings = Resources.Load<MenuCharacterSettings>("Moonkov/MenuCharacterSettings");
            if (settings.MotionOptions.Length != settings.Motions.Length || settings.MotionMasks.Length != settings.Motions.Length || settings.GestureBake == null)
                throw new System.Exception("The menu motion catalog is incomplete.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/DollSinger/Animation/DollSingerThirdPersonHalo.controller");
            if (controller.animationClips.Any(clip => !settings.Motions.Contains(clip))) throw new System.Exception("A character motion was omitted.");
            var seen = new HashSet<int>();
            var expected = Enumerable.Range(1, settings.Motions.Length - 1)
                .Where(i => settings.Motion(i).Weapon == 0 || settings.Motion(i).Weapon == 1).ToHashSet();
            float maximumAim = 0;
            int previousMotion = 0, lastAction = 0;
            bool complete = false;
            for (int frame = 0; frame < 24000; frame++)
            {
                view.Advance(.1f); maximumAim = Mathf.Max(maximumAim, view.HaloAimWeight);
                int current = view.CurrentMotionIndex;
                if (current != 0 && current != previousMotion)
                {
                    if (seen.SetEquals(expected))
                    {
                        if (lastAction == current) throw new System.Exception("Motion repeated across shuffle cycles.");
                        complete = true; break;
                    }
                    if (!seen.Add(current)) throw new System.Exception("A motion repeated before the catalog was exhausted: " + view.CurrentMotion);
                    lastAction = current;
                }
                previousMotion = current;
            }
            if (!complete) throw new System.Exception("Automatic playback did not cover every motion.");
            var model = view.DisplayScene.GetRootGameObjects().Single().GetComponentInChildren<Animator>();
            if (model.fireEvents || model.applyRootMotion || model.runtimeAnimatorController != null)
                throw new System.Exception("The display animator may trigger gameplay or root motion.");
            foreach (var bone in model.GetComponentsInChildren<Transform>())
                if (!float.IsFinite(bone.position.sqrMagnitude)) throw new System.Exception("Invalid pose: " + bone.name);
            view.Render(new Vector2(600, 720));
            if (view.MotionChanges < 4 || view.Target == null || view.Target.width < 1000) throw new System.Exception("Menu motion or high resolution rendering failed.");
            if (!view.HaloVisible || maximumAim < .99f) throw new System.Exception("Halo or Halo Aim pose is missing.");
            var previous = RenderTexture.active;
            var image = new Texture2D(view.Target.width, view.Target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = view.Target; image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                int visible = 0; foreach (var color in image.GetPixels32()) if (color.a > 128) visible++;
                if (visible < image.width * image.height / 50) throw new System.Exception("Character render is empty.");
                Directory.CreateDirectory("LocalData/MenuMotions");
                File.WriteAllBytes("LocalData/MenuMotions/ValidatedCharacter.png", image.EncodeToPNG());
                Debug.Log($"Menu character check passed: {seen.Count}/{expected.Count} equipped-weapon actions without repetition, {view.Target.width}x{view.Target.height}, {view.MotionChanges} transitions, visible halo, full aim weight, finite bones, nonempty render.");
            }
            finally { RenderTexture.active = previous; DestroyImmediate(image); }
        }
        finally
        {
            var displayScene = view.DisplayScene;
            view.Dispose();
            if (displayScene.IsValid() && displayScene.isLoaded) Debug.LogError("Menu character leaked its preview scene.");
        }
    }
}
