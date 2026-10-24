using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Uses one offscreen UI Toolkit panel in the existing Editor, without a gameplay session.
public static class ShianMenuLayoutChecks
{
    public static async Task<string> Run()
    {
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
        var target = new RenderTexture(1920, 1080, 24);
        var owner = new GameObject("Shian layout validation");
        var results = new List<string>();
        try
        {
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            var document = owner.AddComponent<UIDocument>(); document.panelSettings = settings;
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/Moonkov/UI/OperationOperator.uxml").CloneTree();
            document.rootVisualElement.Add(tree);
            var page = tree.Q("layoutPreview");
            foreach (int width in new[] { 1280, 1600, 1920 })
            foreach (bool compact in new[] { false, true })
            {
                page.style.width = width; page.style.height = width * 9f / 16;
                page.EnableInClassList("archive-compact", compact);
                await Task.Delay(300);
                foreach (string name in new[] { "chooseDollSinger", "chooseHatsuneMiku", "chooseShian" })
                {
                    var button = page.Q<Button>(name); var label = button.Q<Label>(className: "op-button-label");
                    var mark = button.Q<Label>(className: "op-choice-mark");
                    var text = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    if (button.worldBound.width <= 0 || label.worldBound.xMin < button.worldBound.xMin ||
                        label.worldBound.xMin + text.x > mark.worldBound.xMin + 1 || mark.worldBound.xMax > button.worldBound.xMax + 1)
                        throw new InvalidOperationException($"Operator button overflow at {width} / compact {compact}: {name}, text {text.x}, button {button.worldBound}, label {label.worldBound}, mark {mark.worldBound}");
                    results.Add($"PASS {width}px compact={compact} {name}: button={button.resolvedStyle.width:F1}px, text={text.x:F1}px");
                }
            }
            Directory.CreateDirectory("Temp/ShianValidation");
            File.WriteAllLines("Temp/ShianValidation/menu-layout.txt", results);
            return string.Join("\n", results);
        }
        finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(settings); target.Release(); Object.DestroyImmediate(target); }
    }
}
