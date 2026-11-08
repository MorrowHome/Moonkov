using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class BuildUiWarningReview
{
    public static async Task<object> Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use the existing idle Editor.");
        foreach (string name in new[] { "MissionHUD", "ResearchQuests" })
            AssetDatabase.ImportAsset("Assets/Resources/Moonkov/UI/" + name + ".uss", ImportAssetOptions.ForceSynchronousImport);
        var window = ScriptableObject.CreateInstance<BuildUiWarningWindow>();
        window.position = new Rect(60, 60, 980, 720);
        window.titleContent = new GUIContent("Build warning checks");
        window.ShowUtility();
        var root = window.rootVisualElement;
        var controls = new VisualElement();
        controls.AddToClassList("mission-controls");
        controls.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/MissionHUD"));
        for (int i = 0; i < 2; i++)
        {
            var button = new Button { text = "Circuit " + (i + 1) };
            button.AddToClassList("mission-circuit-row");
            if (i == 0) button.AddToClassList("mission-circuit-row-first");
            controls.Add(button);
        }
        root.Add(controls);
        var progress = new ResearchQuestProgress { Completed = (1 << 10) - 1 };
        using var quest = new ResearchQuestView(root, () => progress, (q, claim) => Task.FromResult(false),
            () => { }, () => { }, () => { }, ending => Task.FromResult(false));
        // Render the real ending choices without starting their audio or persisting a choice.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var endingView = new VisualElement();
        endingView.AddToClassList("research-ending");
        quest.Element.Add(endingView);
        typeof(ResearchQuestView).GetField("m_StoryEvent", flags).SetValue(quest, endingView);
        typeof(ResearchQuestView).GetMethod("RenderEnding", flags).Invoke(quest, null);
        var ready = new TaskCompletionSource<bool>();
        root.schedule.Execute(() => ready.TrySetResult(true)).StartingIn(300);
        try
        {
            if (await Task.WhenAny(ready.Task, Task.Delay(5000)) != ready.Task)
                throw new InvalidOperationException("Native UI did not update.");
            var choices = endingView.Query<Button>(className: "research-ending-option").ToList();
            if (controls[0].resolvedStyle.borderTopWidth != 1 || controls[1].resolvedStyle.borderTopWidth != 0)
                throw new InvalidOperationException("Mission first-row border is incorrect.");
            if (choices.Count != 3 || choices.Count(b => b.ClassListContains("research-ending-option-last")) != 1 ||
                choices[0].resolvedStyle.marginRight != 8 || choices[1].resolvedStyle.marginRight != 8 || choices[2].resolvedStyle.marginRight != 0)
                throw new InvalidOperationException("Ending choice margins are incorrect.");
            return new { missionFirstBorder = controls[0].resolvedStyle.borderTopWidth,
                missionNextBorder = controls[1].resolvedStyle.borderTopWidth,
                endingMargins = choices.Select(b => b.resolvedStyle.marginRight).ToArray() };
        }
        finally { window.Close(); }
    }
}

public sealed class BuildUiWarningWindow : EditorWindow { }
