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
            root.Add(new HelpBox("正式地图采用原始 PBR 材质与写实照明，此历史滤镜默认停用。开发局内按 F1 临时开启风格化，再按恢复：可预览停用或强度为 0 的组件，不改场景保存的强度与预设。编辑模式下仍可手动启用组件并选择实验预设。", HelpBoxMessageType.Info));
            var presets = new VisualElement();
            presets.style.flexDirection = FlexDirection.Row;
            presets.Add(new Button(() => ApplyFaithful(targets)) { text = "写实照明（默认）" });
            presets.Add(new Button(() => ApplyPreset(targets, false)) { text = "旧黑白钴蓝实验" });
            presets.Add(new Button(() => ApplyPreset(targets, true)) { text = "旧强块面实验" });
            root.Add(presets);
            Field(root, "faithfulLighting", "原始照明 / 跳过风格化");
            Field(root, "intensity", "总强度 / 0 关闭");
            Field(root, "previewInSceneView", "在 Scene 视图预览");
            var tone = Group(root, "历史实验参数：黑白块面与光照层次");
            Field(tone, "shadingRetention", "明暗层次保留");
            Field(tone, "exposure", "风格曝光 / EV");
            Field(tone, "blackPoint", "线性黑位参考");
            Field(tone, "whitePoint", "线性白面参考");
            Field(tone, "contrast", "对比度");
            Field(tone, "tonalSeparation", "黑白块面分离");
            var ink = Group(root, "白面纹理、光泽与薄轮廓");
            Field(ink, "contourStrength", "轮廓强度");
            Field(ink, "contourWidth", "轮廓宽度 / 像素");
            Field(ink, "silverRelief", "原材质细节保留");
            Field(ink, "highlightRetention", "真实 HDR 高光保留");
            var accents = Group(root, "钴蓝点色");
            Field(accents, "accentColor", "点色颜色");
            Field(accents, "accentStrength", "点色强度");
            Field(accents, "accentThreshold", "原蓝色饱和度门槛");
            var technical = new Foldout { text = "渲染资源", value = false };
            Field(technical, "material", "银盐材质");
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
            Undo.RecordObjects(objects, graphic ? "Stronger black white cobalt blocks" : "Authored black white cobalt finish");
            var so = new SerializedObject(objects);
            void Set(string name, float value) => so.FindProperty(name).floatValue = value;
            so.FindProperty("faithfulLighting").boolValue = false;
            Set("intensity", 1);
            Set("exposure", 1f);
            Set("blackPoint", graphic ? .035f : .025f);
            Set("whitePoint", .26f);
            Set("contrast", graphic ? 1.65f : 1.15f);
            Set("shadingRetention", graphic ? .08f : .35f);
            Set("tonalSeparation", graphic ? .95f : .88f);
            Set("contourStrength", graphic ? .58f : .48f);
            Set("contourWidth", .85f);
            Set("silverRelief", .4f);
            Set("highlightRetention", 1);
            Set("accentStrength", .92f);
            Set("accentThreshold", .48f);
            Set("grain", 0); Set("surfacePatina", 0);
            Set("localAdaptation", 0); Set("halation", 0);
            // Keep the art-directed swatch and subject protection unchanged.
            so.ApplyModifiedProperties();
            foreach (var obj in objects)
            {
                EditorUtility.SetDirty(obj);
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
            SceneView.RepaintAll();
        }

        public static void ApplyFaithful(UnityEngine.Object[] objects)
        {
            Undo.RecordObjects(objects, "Faithful environment lighting");
            var so = new SerializedObject(objects);
            so.FindProperty("faithfulLighting").boolValue = true;
            so.FindProperty("intensity").floatValue = 0;
            so.FindProperty("previewInSceneView").boolValue = false;
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
