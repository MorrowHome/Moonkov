using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;

// A bounded native UI panel. All actions use an isolated graph; no profile or network writes.
public sealed class MooncoreResearchReview : EditorWindow
{
    private const string Folder = "LocalData/ResearchRedesign";
    private Locale m_PreviousLocale;
    private string m_PreviousSelection;
    private bool m_PreviousStage, m_Finished;
    private Action m_NextStep;
    private double m_NextAt;
    private MoonkovResearchView m_View;
    private InventoryGraph m_Graph, m_FabricatedGraph;
    private ShopCommand m_Order;
    private int m_Requests, m_Checks, m_WorkshopRequests, m_StorageRequests;
    private readonly List<string> m_Evidence = new List<string>();
    private readonly Queue<Action> m_Flow = new Queue<Action>();
    private static readonly BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    public static string Status { get; private set; } = "Not started";

    public static string SyncTranslations()
    {
        var translations = JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var keys = new HashSet<string>();
        foreach (string path in new[] {
            "Assets/Scripts/UI/Game/MoonkovResearchView.cs", "Assets/Scripts/UI/Game/MoonkovShopView.cs",
            "Assets/Resources/Moonkov/UI/Components/ResearchPanel.uxml", "Assets/Resources/Moonkov/UI/Tasks.uxml" })
            foreach (Match match in Regex.Matches(File.ReadAllText(path), "\"([^\"]+)\""))
            {
                string text = match.Groups[1].Value.Replace("&amp;", "&");
                if (translations[text] != null) keys.Add(text);
            }
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        if (collection == null) throw new InvalidOperationException("MoonkovUI collection is missing.");
        int changed = 0;
        foreach (var table in collection.StringTables)
        {
            foreach (string key in keys)
            {
                string value = table.LocaleIdentifier.Code == "zh-Hans" ? translations[key].Value<string>() : key;
                if (table.GetEntry(key)?.Value == value) continue;
                table.AddEntry(key, value); EditorUtility.SetDirty(table); changed++;
            }
            AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
        return "Research translations updated: " + changed;
    }

    [MenuItem("Tools/Moonkov/Mooncore/Review Research Layout")]
    public static void Start() => Begin(false);
    public static void ReviewLayout() => Begin(true);
    private static void Begin(bool layoutOnly)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Requires idle Edit mode.");
        if (Resources.FindObjectsOfTypeAll<MooncoreResearchReview>().Length != 0)
            throw new InvalidOperationException("Research review is already open.");
        Directory.CreateDirectory(Folder); Status = "Running";
        if (layoutOnly && File.Exists(Folder + "/checks.txt") && !File.Exists(Folder + "/flow-checks.txt"))
            File.Copy(Folder + "/checks.txt", Folder + "/flow-checks.txt");
        File.WriteAllText(Folder + "/checks.txt", "Running native research review.\n");
        var window = CreateInstance<MooncoreResearchReview>();
        window.titleContent = new GUIContent("研究与制造");
        window.position = new Rect(20, 20, 1440, 820); window.minSize = new Vector2(640, 420);
        window.ShowUtility();
        window.m_NextStep = layoutOnly ? window.LayoutFlow : window.CheckFlow; window.m_NextAt = EditorApplication.timeSinceStartup + .7;
        EditorApplication.update += window.Tick;
    }
    public void CreateGUI()
    {
        m_PreviousLocale = LocalizationSettings.SelectedLocale;
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
        m_PreviousSelection = (string)typeof(MoonkovResearchView).GetField("s_Selected", Static).GetValue(null);
        m_PreviousStage = (bool)typeof(MoonkovResearchView).GetField("s_Fabricating", Static).GetValue(null);
        var root = rootVisualElement;
        root.AddToClassList("stash-root"); root.AddToClassList("moonkov-terminal"); root.AddToClassList("archive-ui");
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI Toolkit/GameUI/MoonkovTerminal.uss"));
        root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ApprovedPages"));
        root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/Research"));
        var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/Window").CloneTree();
        var shell = tree.Q("terminalWindow"); shell.RemoveFromHierarchy(); root.Add(shell);
        shell.AddToClassList("terminal-shop-window"); shell.AddToClassList("research-window"); shell.AddToClassList("research-dedicated");
        shell.style.left = Length.Percent(3); shell.style.top = Length.Percent(3);
        MoonkovLocalization.Set(shell.Q<Label>("windowTitle"), "RESEARCH & FABRICATION");
        shell.Q<Button>("windowClose").clicked += Close;
        AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        m_View = new MoonkovResearchView((operation, code) =>
        {
            m_Requests++;
            m_Order = new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = m_Graph.Version, Operation = operation, Code = code, Quantity = 1 };
        }, () => m_WorkshopRequests++, () => m_StorageRequests++);
        m_Graph = Fixture(); m_View.SelectMooncore(m_Graph);
        shell.Add(m_View.Root); Present();
    }

    private static InventoryGraph Fixture()
    {
        var graph = InventoryGraph.Create(stash: true, starter: false);
        for (int i = 0; i < 3; i++) graph.Items.Add(new InventoryItem {
            Id = Guid.NewGuid().ToString("D"), Code = "mooncore_fragment", Parent = "stash", Region = "main", X = i, Y = 0,
            FoundInRaid = true, MissionRecovered = true, MissionWorldId = Guid.NewGuid().ToString("D"),
            MissionOperationId = Guid.NewGuid().ToString("D"), MissionContentVersion = 1 });
        graph.AddSupply("circuit_board", 4, "stash"); graph.AddSupply("copper_coil", 3, "stash");
        foreach (string code in AlienBiology.Research.SelectMany(r => r.Materials).Distinct().Where(c => c.StartsWith("alien_")))
            graph.AddSupply(code, 8, "stash");
        return graph;
    }
    private void Present(bool ready = true, bool busy = false, bool pending = false)
    { m_View.Present(m_Graph, ready, busy, pending); Font(); }
    private void Font()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        rootVisualElement.Query<TextElement>().ForEach(t => { t.style.unityFontDefinition = new FontDefinition { font = source }; t.style.letterSpacing = 0; });
    }
    private void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); m_Checks++; }
    private Button Button(string name) => m_View.Root.Q<Button>(name);
    private void Submit(Button button, bool requireEnabled = true)
    {
        Check(button != null && (!requireEnabled || button.enabledInHierarchy), "Submit target unavailable: " + button?.name);
        button.Focus(); using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
    }
    private void CheckFlow()
    {
        m_Flow.Enqueue(() => {
            Check(m_View.Root.Query<VisualElement>(className: "research-choice").ToList().Count == 5, "Five research projects must remain available.");
            var choice = m_View.Root.Q("mooncore_stabilization").Q<Button>(); choice.Focus(); Present();
            Check(ReferenceEquals(choice, m_View.Root.Q("mooncore_stabilization").Q<Button>()) && rootVisualElement.panel.focusController.focusedElement == choice, "Inventory refresh recreated the focused choice.");
            Check(m_Graph.Validate() == InventoryError.None && AlienBiology.MooncoreResearch.CanAfford(m_Graph), "Isolated fixture must contain valid recovered research materials.");
            Submit(Button("goStorage"));
        });
        m_Flow.Enqueue(() => { Check(m_StorageRequests == 1, "Storage navigation count: " + m_StorageRequests); Submit(Button("fabricationStage")); });
        m_Flow.Enqueue(() => { Check(!Button("researchConfirm").enabledInHierarchy, "Locked recipe can fabricate."); Submit(Button("researchStage")); });
        m_Flow.Enqueue(() => Present(true, true));
        m_Flow.Enqueue(() => { Submit(Button("researchConfirm"), false); Check(m_Requests == 0, "Busy state submitted an order."); Present(true, false, true); });
        m_Flow.Enqueue(() => {
            Submit(Button("researchConfirm"), false); Check(m_Requests == 0, "Uncertain order allowed another charge.");
            m_View.Present(InventoryGraph.Create(stash: true, starter: false), true, false, false);
        });
        m_Flow.Enqueue(() => {
            Check(!Button("researchConfirm").enabledInHierarchy, "Missing materials can research.");
            Check(m_View.Root.Query<VisualElement>(className: "is-missing").ToList().Count == 2, "Material shortages are not shown individually.");
            Present();
        });
        m_Flow.Enqueue(() => { Submit(Button("researchConfirm")); Submit(Button("researchConfirm"), false); });
        m_Flow.Enqueue(() => {
            Check(m_Requests == 1 && m_Order.Operation == ShopOperation.Research && m_Order.Code == "mooncore_stabilization", "Research click duplicated or changed the transaction.");
            Check(ShopRules.Apply(m_Graph, m_Order, out var researched) == null, "Isolated research transaction failed.");
            m_Graph = researched; Present();
        });
        m_Flow.Enqueue(() => {
            Check(AlienBiology.MooncoreResearch.Unlocked(m_Graph) && m_Graph.Count("halo_mooncore_recycler") == 0, "Research granted a free module or failed to unlock.");
            Submit(Button("researchConfirm"));
        });
        m_Flow.Enqueue(() => {
            Check(m_Requests == 1 && Button("fabricationStage").ClassListContains("is-selected"), "Completed research did not advance to fabrication without charging.");
            Submit(Button("researchConfirm"));
        });
        m_Flow.Enqueue(() => {
            Check(m_Requests == 2 && m_Order.Operation == ShopOperation.Fabricate && m_Order.Code == "halo_mooncore_recycler", "Fabrication used the wrong transaction.");
            Check(ShopRules.Apply(m_Graph, m_Order, out var fabricated) == null, "Isolated fabrication failed.");
            m_Graph = fabricated; Present();
            Check(m_Graph.Count("halo_mooncore_recycler") == 1 && m_View.Root.Q<Label>("researchInstalled").text.Contains("0"), "Fabrication should create one uninstalled module.");
        });
        m_Flow.Enqueue(() => Submit(Button("goWorkshop")));
        m_Flow.Enqueue(() => {
            Check(m_WorkshopRequests == 1, "Workshop navigation count: " + m_WorkshopRequests);
            m_View.Present(null, false, false, false);
            Check(!Button("researchConfirm").enabledInHierarchy && m_View.Root.Q("researchNumbers").ClassListContains("research-hidden"), "A missing profile retained an actionable stale view.");
            m_FabricatedGraph = m_Graph; m_Graph = Fixture();
            m_View.SelectMooncore(m_Graph); Present();
        });
        NextFlow();
    }
    private void NextFlow()
    {
        try { if (m_Flow.Count == 0) Later(CaptureResearch); else { m_Flow.Dequeue()(); Later(NextFlow); } }
        catch (Exception ex) { Finish(ex); }
    }
    private void LayoutFlow()
    {
        try
        {
            m_FabricatedGraph = Fixture(); AlienBiology.MooncoreResearch.Unlock(m_FabricatedGraph);
            m_FabricatedGraph.AddSupply("halo_mooncore_recycler", 1, "stash");
            Later(CaptureResearch);
        }
        catch (Exception ex) { Finish(ex); }
    }
    private void CaptureResearch()
    {
        try
        {
            Layout(); Capture("research-zh.png");
            m_Graph = m_FabricatedGraph; m_View.SelectMooncore(m_Graph); Present();
            position = new Rect(position.x, position.y, 1080, 620); Later(CheckCompact);
        }
        catch (Exception ex) { Finish(ex); }
    }
    private void CheckCompact()
    {
        try
        {
            Layout(); Capture("fabrication-zh-compact.png");
            Submit(m_View.Root.Q("halo_capacity").Q<Button>()); Present();
            Check(m_View.Root.Q("researchStages").ClassListContains("research-hidden") && Button("researchConfirm").enabledInHierarchy, "Biological research inherited fabrication state.");
            position = new Rect(position.x, position.y, 720, 650); Later(CheckNarrow);
        }
        catch (Exception ex) { Finish(ex); }
    }
    private void CheckNarrow()
    {
        try
        {
            Layout(); Check(m_View.Root.ClassListContains("research-narrow"), "Narrow layout did not adapt.");
            Capture("biology-zh-narrow.png");
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            position = new Rect(position.x, position.y, 1080, 620); Present(); Later(CheckEnglish);
        }
        catch (Exception ex) { Finish(ex); }
    }
    private void CheckEnglish()
    {
        try { Layout(); Capture("biology-en.png"); Finish(null); }
        catch (Exception ex) { Finish(ex); }
    }
    private void Later(Action action) { m_NextStep = action; m_NextAt = EditorApplication.timeSinceStartup + .35; }
    private void Tick()
    {
        if (m_Finished || m_NextStep == null || EditorApplication.timeSinceStartup < m_NextAt) return;
        var action = m_NextStep; m_NextStep = null; action();
    }
    private void Layout()
    {
        var root = m_View.Root.worldBound; var footer = m_View.Root.Q(className: "research-footer").worldBound;
        var action = Button("researchConfirm").worldBound;
        Check(action.width >= 120 && action.height >= 40 && root.Contains(action.min) && root.Contains(action.max - Vector2.one), "Primary action is clipped or too small.");
        Check(footer.yMin >= m_View.Root.Q<ScrollView>("researchContent").worldBound.yMax - 1, "Scroll content overlaps the fixed action footer.");
        Check(m_View.Root.Q<Label>("researchTitle").worldBound.width > 100, "Research title has no readable width.");
        Check(m_View.Root.Q<ScrollView>("researchContent").contentViewport.resolvedStyle.height >= 100, "Details have no usable scroll viewport.");
        var overview = m_View.Root.Q(className: "research-overview").worldBound;
        var payment = m_View.Root.Q(className: "research-payment").worldBound;
        Check(!overview.Overlaps(payment) && overview.height >= m_View.Root.Q(className: "research-showcase").worldBound.height - 1,
            "Effect preview and material region overlap or collapse.");
        Check(m_View.Root.Query<Label>().ToList().All(label => label.resolvedStyle.fontSize >= 12), "Label is below the minimum font size.");
    }
    private void Capture(string name)
    {
        Font(); Repaint(); var root = rootVisualElement;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        int width = Mathf.RoundToInt(root.resolvedStyle.width), height = Mathf.RoundToInt(root.resolvedStyle.height);
        float dpi = Convert.ToSingle(root.panel.GetType().GetProperty("scaledPixelsPerPoint", flags).GetValue(root.panel));
        var target = new RenderTexture(Mathf.RoundToInt(width * dpi), Mathf.RoundToInt(height * dpi), 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create(); var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; Texture2D texture = null;
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
            RenderTexture.active = target;
            texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            Check(texture.GetPixels32().Count(p => p.r > 100 || p.g > 100 || p.b > 100) > 100, "Native research capture is blank.");
            File.WriteAllBytes(Path.Combine(Folder, name), texture.EncodeToPNG());
            m_Evidence.Add(name + ": native panel " + width + "x" + height + ", dpi " + dpi);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; if (texture) DestroyImmediate(texture); target.Release(); DestroyImmediate(target); }
    }
    private void Finish(Exception error)
    {
        if (m_Finished) return; m_Finished = true;
        Status = error == null ? "Passed " + m_Checks + " checks" : "Failed: " + error;
        m_Evidence.Add(Status); File.WriteAllLines(Path.Combine(Folder, "checks.txt"), m_Evidence);
        if (error != null) Debug.LogException(error); else Debug.Log(Status + "; isolated native UI, no Play, accounts or networking.");
        Close();
    }
    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (m_PreviousLocale != null) LocalizationSettings.SelectedLocale = m_PreviousLocale;
        if (m_PreviousSelection != null)
        {
            typeof(MoonkovResearchView).GetField("s_Selected", Static).SetValue(null, m_PreviousSelection);
            typeof(MoonkovResearchView).GetField("s_Fabricating", Static).SetValue(null, m_PreviousStage);
        }
    }
}
