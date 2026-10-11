using UnityEditor;
using UnityEditor.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>The delivered full v02 preset; generic authoring remains in the shared adapter.</summary>
    public static class MC00FullLightControlTools
    {
        public const string Source = "SourceData/Models/MC00LightControlsFullV02";
        public const string Folder = "Assets/MoonEnvironment/Variants/MC00LightControlsFullV02";
        public const string ScenePath = Folder + "/Scenes/MC00GameExportPreview.unity";
        public static MC00SwitchSampleTools.Delivery Delivery => new MC00SwitchSampleTools.Delivery {
            sourceFolder = Source, assetFolder = Folder, revision = "full_rollout_v02",
            sourceSha256 = "e61f2fab253a0801f3ebb7084fbb7163e48bd156c9ce79074825e4e81d5ca5f0",
            lightCount = 118, controlledLightCount = 118, switchCount = 135
        };
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/1 - Import assets")]
        public static void Import() => MC00SwitchSampleTools.ImportDelivery(Delivery);
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/2 - Create separate candidate")]
        public static void Create() => MC00SwitchSampleTools.CreateDelivery(Delivery);
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/3 - Optimize CPU submission")]
        public static void Optimize() => MC00SwitchSampleTools.OptimizeDelivery(Delivery);
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/4 - Bind all switches")]
        public static void Bind() => MC00SwitchSampleTools.BindDelivery(Delivery);
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/Open candidate")]
        public static void Open()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/Stand at G03 ceiling controls")]
        public static void G03() => MC00SwitchSampleTools.StandAt("PANEL_MC00_G03_CEILING", ScenePath, Source);
        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/Stand at L22 operations controls")]
        public static void L22() => MC00SwitchSampleTools.StandAt("PANEL_MC00_L22_OPERATIONS", ScenePath, Source);
    }
}
