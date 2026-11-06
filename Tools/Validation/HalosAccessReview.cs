using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

// Run through Pipeline run_script. This file stays outside Assets to avoid an Editor reload.
public static class HalosAccessReview
{
    private static bool s_ReloadLocked;
    private static double s_UnlockAt;

    public static void ReleaseReload()
    {
        if (!s_ReloadLocked) return;
        s_ReloadLocked = false; EditorApplication.update -= ReleaseExpiredReload;
        EditorApplication.UnlockReloadAssemblies();
    }

    private static void ReleaseExpiredReload()
    {
        if (EditorApplication.timeSinceStartup >= s_UnlockAt) ReleaseReload();
    }

    public static string SetupAndOpen()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Review requires an idle Editor.");
        foreach (string name in new[] { "Emblem", "Terminal", "Noise", "FieldRule", "SignalResidual" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Moonkov/UI/Halos/" + name + ".png");
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false; importer.isReadable = false;
            importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = name == "Terminal" || name == "SignalResidual" ? 2048 : 1024;
            importer.textureCompression = name == "Emblem" || name == "FieldRule" ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        var translations = new Dictionary<string, string> {
            { "Reduce motion", "减少动画" },
            { "Signing in...", "正在登录…" }, { "Creating account...", "正在注册…" }
        };
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach (var table in collection.StringTables)
        {
            foreach (string key in new[] { "Reduce motion", "Signing in...", "Creating account..." })
                table.AddEntry(key, table.LocaleIdentifier.Code == "zh-Hans" ? translations[key] : key);
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
        return Open();
    }

    public static string Open()
    {
        foreach (var previous in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.titleContent.text == "Moonkov / Halos UI").ToArray()) previous.Close();
        EditorApplication.LockReloadAssemblies(); s_ReloadLocked = true;
        s_UnlockAt = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update += ReleaseExpiredReload;
        var window = ScriptableObject.CreateInstance<HalosAccessReviewWindow>();
        window.titleContent = new GUIContent("Moonkov / Halos UI");
        window.position = new Rect(30, 40, 1280, 720); window.minSize = new Vector2(640, 480);
        window.ShowUtility(); window.Initialize();
        return "Native UI review opened. No Play mode, account requests, or player saves.";
    }
}

public sealed class HalosAccessReviewWindow : EditorWindow
{
    private const string Output = "LocalData/HalosReview";
    private VisualElement m_Canvas, m_Form;
    private HalosTerminalBackdrop m_Backdrop;
    private Locale m_PreviousLocale;
    private LoadingData m_Loading;
    private readonly List<string> m_Checks = new List<string>();
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public void Initialize()
    {
        m_PreviousLocale = LocalizationSettings.SelectedLocale;
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
        Directory.CreateDirectory(Output); Login();
    }

    private void ClearView()
    {
        m_Backdrop?.Dispose(); m_Backdrop = null; rootVisualElement.Clear();
        m_Canvas = m_Form = null;
    }

    public void Login()
    {
        ClearView();
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/MainMenu.uxml").CloneTree();
        tree.style.flexGrow = 1; rootVisualElement.Add(tree);
        m_Canvas = tree.Q("Canvas"); m_Canvas.AddToClassList("archive-access-account");
        m_Canvas.AddToClassList("archive-login-plates");
        m_Canvas.RegisterCallback<GeometryChangedEvent>(e => m_Canvas.EnableInClassList("halos-compact", e.newRect.height < 800));
        tree.Q("HidingBackground").style.display = DisplayStyle.None;
        tree.Q("stashScreenHost").style.display = DisplayStyle.None;
        var connection = tree.Q("ConnectionPanel"); connection.style.display = DisplayStyle.None;
        m_Form = Resources.Load<VisualTreeAsset>("Moonkov/UI/AccountAccess").CloneTree().Q("AccountPanel");
        m_Form.RemoveFromHierarchy(); connection.parent.Insert(0, m_Form);
        var mode = tree.Q<Button>("ProfileMode"); mode.RemoveFromHierarchy();
        m_Form.Q("accountSecondaryActions").Insert(0, mode);
        m_Backdrop = new HalosTerminalBackdrop(m_Canvas, false);
        MoonkovLocalization.Bind(rootVisualElement);
        UseSourceFont(); m_Backdrop.Present(true, false);
    }

    public void Loading()
    {
        ClearView();
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/Loading.uxml").CloneTree();
        tree.style.flexGrow = 1; rootVisualElement.Add(tree); m_Canvas = tree.Q("Canvas");
        m_Backdrop = new HalosTerminalBackdrop(m_Canvas, true);
        MoonkovLocalization.Bind(rootVisualElement); UseSourceFont();
        m_Loading = (LoadingData)Activator.CreateInstance(typeof(LoadingData), true);
        tree.Q<ProgressBar>().SetBinding("value", new DataBinding { dataSource = m_Loading,
            dataSourcePath = new PropertyPath(LoadingData.LoadingProgressPropertyName), bindingMode = BindingMode.ToTarget });
        tree.Q<Label>("LoadingStatus").SetBinding("text", new DataBinding { dataSource = m_Loading,
            dataSourcePath = new PropertyPath(LoadingData.LoadingStatusTextPropertyName), bindingMode = BindingMode.ToTarget });
        m_Loading.UpdateLoading(LoadingData.LoadingSteps.LoadGameScene, .5f);
        m_Backdrop.Present(true, false);
    }

    private void UseSourceFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        rootVisualElement.Query<TextElement>().ForEach(e =>
        {
            if (e.ClassListContains("halos-terminal-text")) return;
            e.style.unityFontDefinition = FontDefinition.FromFont(font);
        });
    }

    public void SetLocale(string code)
    {
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
        UseSourceFont();
    }

    // Freeze the public presentation for visual inspection; no private panel rendering.
    public void FreezeAnimation(double seconds)
    {
        typeof(HalosTerminalBackdrop).GetField("m_Started", PrivateInstance).SetValue(m_Backdrop, TerminalMotion.Now - seconds);
        typeof(HalosTerminalBackdrop).GetMethod("Tick", PrivateInstance).Invoke(m_Backdrop, null);
        ((IVisualElementScheduledItem)typeof(HalosTerminalBackdrop).GetField("m_Tick", PrivateInstance).GetValue(m_Backdrop)).Pause();
    }

    private void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        m_Checks.Add("PASS " + message);
    }

    public void CheckLogin()
    {
        Require(m_Canvas.Q("halosIntro").resolvedStyle.display == DisplayStyle.None, "click skip reveals login immediately");
        Require(m_Form.resolvedStyle.visibility == Visibility.Visible, "login form visible after skip");
        Require(m_Canvas.worldBound.width > 600 && Math.Abs(m_Canvas.worldBound.height - rootVisualElement.worldBound.height) < 2, "native canvas fills actual panel");
        var account = m_Form.Q<TextField>("accountUsername"); var password = m_Form.Q<TextField>("accountPassword");
        Require(!account.isPasswordField && password.isPasswordField, "username editable and password masked");
        account.value = "review_operator"; password.value = "review_password";
        Require(account.value == "review_operator" && password.value == "review_password", "input buffers preserve typed content");
        var primary = m_Form.Q<Button>("accountLogin").resolvedStyle.unityBackgroundImageTintColor;
        Require(primary.b > primary.r + .25f, "primary action retains blue emphasis");
        var frame = m_Form.Q<Button>("accountLogin").resolvedStyle.backgroundImage.texture;
        Require(frame != null && m_Form.Q<Button>("accountRegister").resolvedStyle.backgroundImage.texture == frame &&
            m_Form.Q<Button>("accountQuit").resolvedStyle.backgroundImage.texture == frame &&
            m_Form.Q<Button>("ProfileMode").resolvedStyle.backgroundImage.texture == frame &&
            account.Q(className: "unity-text-field__input").resolvedStyle.backgroundImage.texture == frame,
            "inputs and four actions reuse the artist frame");
        Require(!m_Canvas.Query<TextElement>().ToList().Any(e => e.text.Contains("skip") || e.text.Contains("跳过")), "skip gesture has no visible instruction label");
        var bounds = m_Canvas.worldBound;
        foreach (var name in new[] { "accountUsername", "accountPassword", "accountLogin", "accountRegister", "accountQuit", "ProfileMode", "accessReduceMotion" })
        {
            var e = m_Form.Q(name); var r = e.worldBound;
            Require(r.width > 12 && r.height >= 16 && r.xMin >= bounds.xMin && r.xMax <= bounds.xMax + 1 && r.yMin >= bounds.yMin && r.yMax <= bounds.yMax + 1,
                name + " fits at " + bounds.size);
        }
        Require(account.worldBound.yMax <= password.worldBound.yMin && password.worldBound.yMax <= m_Form.Q<Button>("accountLogin").worldBound.yMin, "fields and actions do not overlap");
        m_Backdrop.Present(false, false); m_Backdrop.Present(true, false);
        Require(!m_Canvas.ClassListContains("halos-intro-active"), "returning to login does not replay skipped intro");
        m_Backdrop.Present(true, true);
        Require(m_Canvas.Q(className: "halos-stage").style.display == DisplayStyle.None, "reduced motion stops decorative animation");
        SaveChecks();
    }

    public void CheckLoading()
    {
        var progress = m_Canvas.Q<ProgressBar>();
        Require(m_Canvas.resolvedStyle.backgroundImage.texture != null, "battle loading retains existing background artwork");
        Require(Math.Abs(progress.value - .4f) < .001f, "progress follows real LoadingData binding");
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
        { e.target = m_Canvas; m_Canvas.SendEvent(e); }
        Require(m_Canvas.Q(className: "halos-stage").style.display == DisplayStyle.None, "left click skips loading decoration");
        Require(m_Canvas.style.display != DisplayStyle.None && Math.Abs(progress.value - .4f) < .001f && !string.IsNullOrEmpty(m_Canvas.Q<Label>("LoadingStatus").text),
            "skip preserves loading screen, progress, and status");
        m_Loading.UpdateLoading(LoadingData.LoadingSteps.LoadClient, .5f);
        rootVisualElement.schedule.Execute(() => Guard(() =>
        {
            Require(Math.Abs(progress.value - .75f) < .001f, "real progress continues after animation skip");
            m_Backdrop.Present(false, false);
            Require(m_Canvas.Q("halosBackdrop").style.display == DisplayStyle.None, "hidden loading screen suspends decoration");
            m_Backdrop.Dispose();
            Require(m_Canvas.Q("halosBackdrop") == null, "dispose removes decoration and handlers");
            Loading(); FreezeAnimation(.95); SaveChecks();
            File.AppendAllText(Path.Combine(Output, "checks.txt"), "COMPLETE\n");
        })).StartingIn(250);
    }

    public void RunChecks()
    {
        m_Checks.Clear(); File.WriteAllText(Path.Combine(Output, "checks.txt"), "RUNNING\n"); Login();
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
        { var intro = m_Canvas.Q("halosIntro"); e.target = intro; intro.SendEvent(e); }
        rootVisualElement.schedule.Execute(() => Guard(() =>
        {
            CheckLogin(); position = new Rect(position.x, position.y, 800, 600);
            rootVisualElement.schedule.Execute(() => Guard(() =>
            {
                CheckLogin(); position = new Rect(position.x, position.y, 1280, 720); Loading();
                rootVisualElement.schedule.Execute(() => Guard(CheckLoading)).StartingIn(350);
            })).StartingIn(350);
        })).StartingIn(600);
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { SaveChecks(); File.AppendAllText(Path.Combine(Output, "checks.txt"), "FAILED " + e + "\n"); Debug.LogException(e); }
    }
    private void SaveChecks() => File.WriteAllLines(Path.Combine(Output, "checks.txt"), m_Checks);
    private void OnDisable()
    {
        m_Backdrop?.Dispose();
        if (m_PreviousLocale != null) LocalizationSettings.SelectedLocale = m_PreviousLocale;
        HalosAccessReview.ReleaseReload();
    }
}
