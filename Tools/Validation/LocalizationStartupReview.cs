using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

// Compile outside Assets with the Editor command runner. Uses public APIs only.
public sealed class LocalizationStartupReview : EditorWindow
{
    private HalosTerminalBackdrop m_Backdrop;
    private Label m_Status, m_Formatted;
    private readonly List<string> m_Checks = new List<string>();
    private const string Output = "LocalData/StartupLoadingReview/checks.txt";
    private const string Source = "Loading Gameplay scene...";

    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Review requires an idle Editor.");
        var window = CreateInstance<LocalizationStartupReview>();
        window.titleContent = new GUIContent("Moonkov / Startup loading review");
        window.position = new Rect(40, 50, 1280, 720);
        window.ShowUtility();
        window.Run();
    }

    private void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        m_Checks.Add("PASS " + message);
    }

    private async void Run()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var errors = new List<string>();
            MoonkovPlayerBuildChecks.ValidateStartupAssets(errors);
            Require(errors.Count == 0, "startup build settings: " + string.Join(", ", errors));
            foreach (var group in AddressableAssetSettingsDefaultObject.Settings.groups.Where(g => g != null && g.Name.Any(c => c > 127)))
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                string filename = BuildUtility.GetNameWithHashNaming(schema.BundleNaming,
                    "0123456789abcdef0123456789abcdef", group.Name.ToLowerInvariant() + "_assets_all.bundle");
                Require(filename.All(c => c < 128), "ASCII bundle filename: " + group.Name);
            }
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/Loading.uxml").CloneTree();
            tree.style.flexGrow = 1; rootVisualElement.Add(tree);
            var canvas = tree.Q("Canvas"); m_Status = tree.Q<Label>("LoadingStatus");
            m_Formatted = new Label(); m_Formatted.style.display = DisplayStyle.None; canvas.Add(m_Formatted);
            tree.Q<ProgressBar>().value = .4f;
            m_Backdrop = new HalosTerminalBackdrop(canvas, true); m_Backdrop.Present(true, false);
            var initialization = LocalizationSettings.InitializationOperation;
            bool pending = !initialization.IsDone;
            MoonkovLocalization.Set(m_Status, Source);
            MoonkovLocalization.Set(m_Formatted, "{0}", Source);
            Require(!string.IsNullOrEmpty(m_Status.text), "initial loading label is readable; initialization pending=" + pending);
            await initialization.Task;
            var table = await LocalizationSettings.StringDatabase.GetTableAsync(MoonkovLocalization.Strings).Task;
            string expected = table.GetEntry(Source).LocalizedValue;
            rootVisualElement.schedule.Execute(() => Finish(expected)).StartingIn(100);
        }
        catch (Exception exception) { File.WriteAllText(Output, "FAIL " + exception); Debug.LogException(exception); }
    }

    private void Finish(string expected)
    {
        try
        {
            Require(m_Status.text == expected, "bound label matches the loaded table");
            Require(m_Formatted.text == expected, "formatted arguments match the loaded table");
            Require(MoonkovLocalization.Text(Source) == expected, "ready text lookup returns the translation");
            Require(MoonkovLocalization.Text("__missing_startup_key__") == "__missing_startup_key__", "missing entry keeps source text");
            Require(rootVisualElement.Q<ProgressBar>().value == .4f, "decoration leaves loading progress unchanged");
            File.WriteAllText(Output, string.Join("\n", m_Checks));
            Debug.Log("Startup loading review: " + m_Checks.Count + " checks passed.");
        }
        catch (Exception exception) { File.WriteAllText(Output, "FAIL " + exception); Debug.LogException(exception); }
    }

    private void OnDisable() { m_Backdrop?.Dispose(); rootVisualElement.Clear(); }
}
