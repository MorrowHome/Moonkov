using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Moon.Editor
{
    [CustomEditor(typeof(MoonSilverPrint)), CanEditMultipleObjects]
    public sealed class MoonSilverPrintEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("仅调整环境银盐效果；角色与 UI 受保护。总强度为 0 或取消组件勾选可关闭。灯光、材质反光和 Volume Bloom 独立控制。", HelpBoxMessageType.Info));
            var presets = new VisualElement();
            presets.style.flexDirection = FlexDirection.Row;
            presets.Add(new Button(() => ApplyPreset(targets, false)) { text = "立体银盐" });
            presets.Add(new Button(() => ApplyPreset(targets, true)) { text = "强对比海报" });
            root.Add(presets);
            Field(root, "intensity", "总强度 / 0 关闭");
            Field(root, "previewInSceneView", "在 Scene 视图预览");
            var tone = Group(root, "明暗与体积");
            Field(tone, "shadingRetention", "明暗层次保留");
            Field(tone, "exposure", "滤镜曝光");
            Field(tone, "blackPoint", "黑位");
            Field(tone, "whitePoint", "白位");
            Field(tone, "contrast", "对比度");
            Field(tone, "tonalSeparation", "黑白块面分离");
            var ink = Group(root, "清晰轮廓与表面纹理");
            Field(ink, "contourStrength", "轮廓强度");
            Field(ink, "contourWidth", "轮廓宽度 / 像素");
            Field(ink, "silverRelief", "细节增强");
            Field(ink, "surfacePatina", "表面斑驳");
            Field(ink, "grain", "印刷颗粒");
            var accents = Group(root, "钴蓝点色");
            Field(accents, "accentColor", "点色颜色");
            Field(accents, "accentStrength", "点色强度");
            Field(accents, "accentThreshold", "点色门槛");
            var optics = Group(root, "局部影调与晕光");
            Field(optics, "localAdaptation", "局部提亮");
            Field(optics, "halation", "银盐晕光强度");
            Field(optics, "halationThreshold", "晕光亮度门槛");
            Field(optics, "halationRadius", "晕光扩散尺度");
            var technical = new Foldout { text = "渲染资源", value = false };
            Field(technical, "material", "银盐材质");
            Field(technical, "subjectMaskMaterial", "角色保护材质");
            root.Add(technical);
            return root;
        }

        private void Field(VisualElement parent, string name, string label)
            => parent.Add(new PropertyField(serializedObject.FindProperty(name), label));

        private static Foldout Group(VisualElement parent, string label)
        {
            var foldout = new Foldout { text = label, value = true };
            parent.Add(foldout);
            return foldout;
        }

        public static void ApplyPreset(UnityEngine.Object[] objects, bool graphic)
        {
            Undo.RecordObjects(objects, graphic ? "Strong poster preset" : "Continuous silver preset");
            var so = new SerializedObject(objects);
            void Set(string name, float value) => so.FindProperty(name).floatValue = value;
            Set("intensity", 1);
            Set("exposure", graphic ? .3f : .1f);
            Set("blackPoint", graphic ? .035f : .018f);
            Set("whitePoint", graphic ? .86f : .97f);
            Set("contrast", graphic ? 1.25f : 1.08f);
            Set("shadingRetention", graphic ? 0 : .65f);
            Set("tonalSeparation", graphic ? .5f : .22f);
            Set("contourStrength", graphic ? .65f : .6f);
            Set("contourWidth", 1);
            Set("silverRelief", .32f);
            Set("grain", graphic ? .012f : .006f);
            Set("surfacePatina", graphic ? .07f : .035f);
            Set("localAdaptation", graphic ? .3f : .15f);
            Set("halation", graphic ? .1f : .12f);
            Set("halationThreshold", graphic ? .82f : .86f);
            Set("halationRadius", 1.5f);
            // Keep the art-directed swatch and subject protection unchanged.
            so.ApplyModifiedProperties();
            foreach (var obj in objects)
            {
                EditorUtility.SetDirty(obj);
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Moon Environment/Theophilus/Select style controls")]
        public static void SelectControls()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(TheophilusVisualPassTools.ScenePath);
            if (!scene.isLoaded)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("The formal map must already be loaded during Play.");
                scene = EditorSceneManager.OpenScene(TheophilusVisualPassTools.ScenePath, OpenSceneMode.Additive);
            }
            var effect = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MoonSilverPrint>(true)).Single();
            Selection.activeGameObject = effect.gameObject;
            EditorGUIUtility.PingObject(effect.gameObject);
        }
    }
}
