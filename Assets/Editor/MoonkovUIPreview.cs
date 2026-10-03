using System.IO;
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
            settings.Motions = new[] {
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Unarmed_Idle.anim"),
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/DollSinger_FingerGunAim.anim"),
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Turn_Left.anim"),
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Turn_Right.anim")
            };
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/DollSinger/Animation/DollSingerThirdPersonHalo.controller");
            AvatarMask turnMask = null;
            foreach (var layer in controller.layers) if (layer.name == "Halo Aim") settings.HaloAimMask = layer.avatarMask;
            foreach (var layer in controller.layers) if (layer.name == "Turn In Place") turnMask = layer.avatarMask;
            settings.MotionMasks = new[] { null, settings.HaloAimMask, turnMask, turnMask };
            foreach (var clip in settings.Motions) if (clip == null) throw new System.Exception("A menu motion clip is missing.");
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            var window = GetWindow<MoonkovUIPreview>(); window.CreateGUI(); window.Repaint();
            Debug.Log("Menu character rebuilt: live model, visible halo, masked Halo Aim gesture, random holds and smooth idle transitions.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Moonkov/Validate Menu Character")]
    public static void ValidateMenuCharacter()
    {
        var view = new MenuCharacterView();
        var host = new VisualElement(); host.style.width = 600; host.style.height = 720;
        host.style.position = Position.Absolute; host.style.left = -2000; host.style.top = 0;
        var window = GetWindow<MoonkovUIPreview>(); window.rootVisualElement.Add(host); host.Add(view.Element);
        view.Element.style.position = Position.Absolute; view.Element.style.left = 0; view.Element.style.right = 0;
        view.Element.style.top = 0; view.Element.style.bottom = 0;
        EditorApplication.delayCall += () =>
        {
            try
            {
                float maximumAim = 0;
                for (int frame = 0; frame < 500; frame++) { view.Advance(.1f); maximumAim = Mathf.Max(maximumAim, view.HaloAimWeight); }
                view.Render();
                if (view.MotionChanges < 4 || view.Target == null || view.Target.width < 1000) throw new System.Exception("Menu motion or high resolution rendering failed.");
                if (!view.HaloVisible || maximumAim < .99f) throw new System.Exception("Halo or Halo Aim pose is missing.");
                var previous = RenderTexture.active;
                var image = new Texture2D(view.Target.width, view.Target.height, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = view.Target; image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                    int visible = 0; foreach (var color in image.GetPixels32()) if (color.a > 128) visible++;
                    if (visible < image.width * image.height / 50) throw new System.Exception("Character render is empty.");
                    Debug.Log($"Menu character check passed: {view.Target.width}x{view.Target.height}, {view.MotionChanges} transitions, visible halo, full Halo Aim weight, nonempty render.");
                }
                finally { RenderTexture.active = previous; DestroyImmediate(image); }
            }
            finally
            {
                var displayScene = view.DisplayScene;
                view.Dispose(); host.RemoveFromHierarchy();
                if (displayScene.IsValid() && displayScene.isLoaded) Debug.LogError("Menu character leaked its preview scene.");
            }
        };
    }
}
