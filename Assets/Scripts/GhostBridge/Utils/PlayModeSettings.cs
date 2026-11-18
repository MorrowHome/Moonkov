// 保存当前电脑的编辑器试玩设置。GhostSpawner 会读取是否保留编辑器预览物体，方便调试场景，配置写到 EditorPrefs。
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class PlayModeSettingsHandler {
    public const string k_SaveOpenScenesOnPlayKey = "PlayModeSettings.SaveOpenScenesOnPlay";
    public const string k_AutomaticallyPlayCurrentLevelKey = "PlayModeSettings.AutomaticallyPlayCurrentLevel";
    public const string k_BootDeveloperMenu = "PlayModeSettings.BootDeveloperMenu";
    public const string k_SkipUITransitions = "PlayModeSettings.SkipUITransitions";
    public const string k_SHowEditorVisuals = "PlayModeSettings.ShowEditorVisuals";

    public class PlayModeSettings {
        public bool SaveOpenScenesOnPlay;
        public bool AutomaticallyPlayCurrentLevel;
        public bool BootDeveloperMenu;
        public bool SkipUITransitions;
        public bool ShowEditorVisuals;
    }

    // 每项编辑器开关都有默认值。按当前电脑保存的值组装一份设置后，生成器就能决定试玩时是否保留场景预览。
    public static PlayModeSettings GetEditorSettings() {
        return new PlayModeSettings {
            SaveOpenScenesOnPlay = EditorPrefs.GetBool(k_SaveOpenScenesOnPlayKey, true),
            AutomaticallyPlayCurrentLevel = EditorPrefs.GetBool(k_AutomaticallyPlayCurrentLevelKey, false),
            BootDeveloperMenu = EditorPrefs.GetBool(k_BootDeveloperMenu, true),
            SkipUITransitions = EditorPrefs.GetBool(k_SkipUITransitions, true),
            ShowEditorVisuals = EditorPrefs.GetBool(k_SHowEditorVisuals, false)
        };
    }

    // 设置页写回的是本机 EditorPrefs。场景文件和存档不需要记录开发者的试玩偏好。
    public static void SetEditorSettings(PlayModeSettings settings) {
        EditorPrefs.SetBool(k_SaveOpenScenesOnPlayKey, settings.SaveOpenScenesOnPlay);
        EditorPrefs.SetBool(k_AutomaticallyPlayCurrentLevelKey, settings.AutomaticallyPlayCurrentLevel);
        EditorPrefs.SetBool(k_BootDeveloperMenu, settings.BootDeveloperMenu);
        EditorPrefs.SetBool(k_SkipUITransitions, settings.SkipUITransitions);
        EditorPrefs.SetBool(k_SHowEditorVisuals, settings.ShowEditorVisuals);
    }
}

internal class PlayModeSettingsGUIContent {
    private static readonly GUIContent m_SaveOpenScenesOnPlay = new("Automatically save open scenes on play",
        "Will save any open scenes with any changes before entering play mode");

    private static readonly GUIContent m_AutomaticallyPlayCurrentLevel = new(
        "Automatically host and play the current level", "Will host in IP mode with current playmode settings");

    private static readonly GUIContent m_BootDeveloperMenu =
        new("Automatically boot into the Developer menu & skip the frontend",
            "Will be override by Automatically host and play");

    private static readonly GUIContent m_SkipUITransitions =
        new("Skip UI transitions and fades for faster developer experience");

    private static readonly GUIContent m_ShowEditorVisuals = new("Show [EDITORVISUALS] in the inspector");

    public static void DrawSettingsButtons(PlayModeSettingsHandler.PlayModeSettings settings) {
        EditorGUI.indentLevel += 1;
        settings.SaveOpenScenesOnPlay =
            EditorGUILayout.ToggleLeft(m_SaveOpenScenesOnPlay, settings.SaveOpenScenesOnPlay);
        settings.AutomaticallyPlayCurrentLevel = EditorGUILayout.ToggleLeft(m_AutomaticallyPlayCurrentLevel,
            settings.AutomaticallyPlayCurrentLevel);
        settings.BootDeveloperMenu = EditorGUILayout.ToggleLeft(m_BootDeveloperMenu, settings.BootDeveloperMenu);
        settings.SkipUITransitions = EditorGUILayout.ToggleLeft(m_SkipUITransitions, settings.SkipUITransitions);
        settings.ShowEditorVisuals = EditorGUILayout.ToggleLeft(m_ShowEditorVisuals, settings.ShowEditorVisuals);
        EditorGUI.indentLevel -= 1;
    }
}

internal static class PlayModeSettingsProvider {
    [SettingsProvider]
    public static SettingsProvider CreateSettingsProvider() {
        var provider = new SettingsProvider("Preferences/PlayMode Settings", SettingsScope.User) {
            label = "PlayMode Settings",
            guiHandler = (searchContext) => {
                var settings = PlayModeSettingsHandler.GetEditorSettings();
                EditorGUI.BeginChangeCheck();
                PlayModeSettingsGUIContent.DrawSettingsButtons(settings);
                if (EditorGUI.EndChangeCheck()) PlayModeSettingsHandler.SetEditorSettings(settings);
            },

            // Unity 偏好设置里供搜索栏匹配的关键词。
            keywords = new HashSet<string>(new[] { "PlayMode", "Settings" })
        };
        return provider;
    }
}
#endif