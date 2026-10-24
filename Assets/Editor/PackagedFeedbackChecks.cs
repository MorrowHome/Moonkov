using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Focused Edit-mode checks. No Play session, profile, network or scene save.
public sealed class PackagedFeedbackChecks : EditorWindow
{
    private bool m_ReloadLocked;
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static void Hud(int width, int height, string folder)
    {
        var window = CreateInstance<PackagedFeedbackChecks>(); window.titleContent = new GUIContent("HUD 布局验证");
        EditorApplication.LockReloadAssemblies();
        window.m_ReloadLocked = true;
        window.minSize = window.maxSize = new Vector2(width, height); window.position = new Rect(30, 30, width, height); window.ShowUtility();
        var root = window.rootVisualElement;
        Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(root);
        Resources.Load<VisualTreeAsset>("Moonkov/UI/MissionHUD").CloneTree(root);
        AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/Leaderboard/ActionFeedUi.uxml").CloneTree(root);
        var rail = root.Q<ScrollView>("raidRightRail");
        foreach (string name in new[] { "raidMinimap", "raidExtractionPanel", "missionHud" })
        { var element = root.Q(name); rail.Add(element); element.style.display = DisplayStyle.Flex; element.style.opacity = 1; }
        rail.Insert(2, root.Q(className: "raid-cargo-instrument"));
        root.Q<Label>("raidBag").text = "随身物资 / 12 件";
        root.Q<Label>("missionTitle").text = "月面设施调查 / 长标题适配";
        root.Q<Label>("missionStep").text = "检查反应堆供电并带回设施记录";
        root.Q<Label>("missionLocation").text = "MC00 · L22 · 距离 180 米";
        root.Q<Label>("missionRisk").text = "附近检测到敌对活动";
        root.Q<Label>("raidExtraction").text = "撤离点可用 · 距离 240 米";
        root.Q<Label>("raidTimer").text = "24:38";
        root.Q<Label>("raidClock").text = "任务剩余时间";
        root.Q("ActionFeedContainer").Add(new Label("玩家 A → 目标 B"));
        root.schedule.Execute(() =>
        {
            try
            {
                var map = root.Q("raidMinimap").worldBound; var exit = root.Q("raidExtractionPanel").worldBound; var mission = root.Q("missionHud").worldBound;
                Require(map.yMax <= exit.yMin && exit.yMax <= mission.yMin, "Right HUD rows overlap.");
                Require(root.Q("ActionFeedContainer").worldBound.xMax <= map.xMin, "Kill feed overlaps right rail.");
                Capture(window, Path.Combine(folder, "hud-" + width + ".png"));
                File.WriteAllText(Path.Combine(folder, "hud-" + width + ".txt"), "PASS: map, extraction and long localized mission rows do not overlap; kill feed is left of rail.");
            }
            catch (Exception error) { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "hud-" + width + ".txt"), error.ToString()); throw; }
            finally { window.Close(); }
        }).StartingIn(900);
    }
    private void OnDisable()
    {
        if (!m_ReloadLocked) return;
        m_ReloadLocked = false; EditorApplication.UnlockReloadAssemblies();
    }
    private static void Capture(EditorWindow window, string path)
    {
        var root = window.rootVisualElement; const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        float dpi = Convert.ToSingle(root.panel.GetType().GetProperty("scaledPixelsPerPoint", flags).GetValue(root.panel));
        int width = Mathf.RoundToInt(root.resolvedStyle.width), height = Mathf.RoundToInt(root.resolvedStyle.height);
        var target = new RenderTexture(Mathf.RoundToInt(width * dpi), Mathf.RoundToInt(height * dpi), 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create();
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; Texture2D image = null;
        try
        {
            RenderTexture.active = target; GL.Viewport(new Rect(0, 0, target.width, target.height)); GL.sRGBWrite = false; GL.Clear(true, true, Color.black); GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, width, height, 0);
                root.panel.GetType().GetMethod("Repaint", flags, null, new[] { typeof(Event) }, null).Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                root.panel.GetType().GetMethod("Render", flags).Invoke(root.panel, null);
            }
            finally { GL.PopMatrix(); }
            image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; if (image) DestroyImmediate(image); target.Release(); DestroyImmediate(target); }
    }
}
