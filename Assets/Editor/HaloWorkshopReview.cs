using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

// Native workshop review. Uses local inventory fixtures, with no account, Play or network World.
public sealed class HaloWorkshopReview : EditorWindow
{
    private readonly Queue<Action> m_Steps = new Queue<Action>();
    private readonly List<string> m_Evidence = new List<string>();
    private ContainerInventoryView m_View;
    private InventoryGraph m_Graph;
    private VisualElement m_Host;
    private Locale m_PreviousLocale;
    private InventoryCommand m_Pending;
    private WeaponRegistry m_Registry;
    private bool m_Hold, m_Finished, m_Batch;
    private int m_Requests, m_AcquireRequests;
    private bool m_AcquireResearch;
    private double m_Ready, m_Deadline;
    private string m_ShotgunId, m_RifleId, m_CapacitorId, m_BridgeId;
    private string m_PreviewSnapshot;
    private Font m_Font;
    private Vector2 m_RequestedSize;
    private static string Output => Path.GetFullPath("LocalData/HaloWorkshopReview");

    [MenuItem("Tools/Moonkov/Review Halo Workshop")]
    public static void Open() => Start(false);

    // Omit -quit and -nographics. This method exits the batch Editor after its scheduled review.
    public static void RunBatch() => Start(true);

    private static void Start(bool batch)
    {
        HaloWorkshopReview window = null;
        try
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Require(root == "/Users/morrow/UnityProjects/Moonkov", "Wrong project: " + root);
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Review requires idle Edit Mode.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "checks.txt"), "RUNNING: native halo workshop review.\n");
            window = CreateInstance<HaloWorkshopReview>();
            window.m_Batch = batch;
            window.titleContent = new GUIContent("光环改装 / 本地验收");
            window.SetSize(1600, 900);
            window.ShowUtility();
            window.Build();
        }
        catch (Exception error)
        {
            if (window != null) window.Finish(error);
            else
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output, "checks.txt"), "FAILED\n" + error);
                Debug.LogException(error);
                if (batch) EditorApplication.Exit(1);
            }
        }
    }

    private void Build()
    {
        m_PreviousLocale = LocalizationSettings.SelectedLocale;
        SetLocale("zh-Hans");
        m_Registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        Require(m_Registry != null, "Real weapon registry is missing.");
        m_Font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        Require(m_Font != null, "Project Chinese source font is missing.");
        var root = rootVisualElement;
        root.style.flexGrow = 1;
        root.style.width = Length.Percent(100); root.style.height = Length.Percent(100);
        root.style.minWidth = 0; root.style.minHeight = 0;
        root.style.backgroundColor = new Color(.067f, .067f, .075f);
        root.AddToClassList("stash-root");
        root.AddToClassList("moonkov-terminal");
        root.AddToClassList("archive-ui");
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI Toolkit/GameUI/MoonkovTerminal.uss"));
        root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ApprovedPages"));
        var inventoryHost = new VisualElement(); inventoryHost.style.display = DisplayStyle.None; root.Add(inventoryHost);
        m_Host = new VisualElement { name = "haloReviewHost" }; m_Host.style.flexGrow = 1; root.Add(m_Host);
        m_View = new ContainerInventoryView(inventoryHost, true, Send) { OperationAudioEnabled = false, WorkshopWeaponRegistry = m_Registry };
        m_View.WorkshopSupplyRequested += research => { m_AcquireRequests++; m_AcquireResearch = research; };
        m_Graph = Fixture(true);
        m_ShotgunId = m_Graph.Items.Single(i => i.Code == "shotgun").Id;
        m_RifleId = m_Graph.Items.Single(i => i.Code == "rifle").Id;
        m_CapacitorId = m_Graph.Items.Single(i => i.Code == "halo_capacitor").Id;
        m_BridgeId = m_Graph.Items.Single(i => i.Code == "halo_charge_bridge").Id;
        Present();
        m_View.SelectWorkshopWeapon(m_ShotgunId); m_View.AttachWorkshop(m_Host);
        var notice = new Label("LOCAL REVIEW / SAMPLE INVENTORY") { pickingMode = PickingMode.Ignore };
        notice.style.position = Position.Absolute; notice.style.bottom = 2; notice.style.left = 12;
        notice.style.fontSize = 12; notice.style.backgroundColor = new Color(.067f, .067f, .075f); notice.style.color = new Color(.7f, .7f, .73f); root.Add(notice);

        Step(() =>
        {
            CheckLayout();
            Require(m_Host.Q<DropdownField>("workshopWeapon") != null, "Owned weapon selector is missing.");
            foreach (string slot in new[] { "Storage", "Shaping", "Utility" }) Require(Button("workshopSlot" + slot) != null, "Workshop slot missing: " + slot);
            foreach (string code in new[] { "halo_capacitor", "halo_charge_bridge", "halo_mooncore_recycler" }) Require(Module(code) != null, "Storage candidate missing: " + code);
            Require(Module("halo_pulse_controller") == null, "Incompatible rifle pulse appeared on shotgun.");
            Stat("Capacity", 4, 6);
            Capture("workshop-zh-native-large.png");
            Pass("中文改装台、三个槽、储能候选和默认扩容预览");
        });
        Step(() =>
        {
            m_PreviewSnapshot = Snapshot();
            Submit(Module("halo_charge_bridge"));
            Stat("Energy", 12, 18); Stat("Charge", 2, 1.2f);
            Require(Snapshot() == m_PreviewSnapshot && m_Requests == 0, "Browsing a module changed inventory.");
            Pass("选择快充只预览参数，不修改物品或版本");
            Submit(Module("halo_capacitor")); Submit(Button("workshopApply"));
            Require(m_Graph.Find(m_CapacitorId).Parent == m_ShotgunId && m_Graph.Find(m_CapacitorId).Region == "Storage", "Install did not attach the exact capacitor item.");
            Require(m_Requests == 1 && m_Graph.Find(m_ShotgunId).LoadedAmmo == 4, "Install duplicated requests or granted ammunition.");
            Pass("原生安装按钮提交一次真实库存事务，扩容不补弹");
        });
        Step(() =>
        {
            var incoming = m_Graph.Find(m_BridgeId);
            string parent = incoming.Parent, region = incoming.Region; int x = incoming.X, y = incoming.Y;
            Submit(Module("halo_charge_bridge")); Submit(Button("workshopApply"));
            var displaced = m_Graph.Find(m_CapacitorId);
            Require(m_Graph.Find(m_BridgeId).Parent == m_ShotgunId, "Replacement did not install incoming item.");
            Require(displaced.Parent == parent && displaced.Region == region && displaced.X == x && displaced.Y == y, "Replacement did not return old module to incoming location.");
            Require(m_Requests == 2 && m_Graph.Find(m_ShotgunId).LoadedAmmo == 4, "Replacement changed ammunition or request count.");
            Submit(Button("workshopRemove"));
            Require(m_Graph.Find(m_BridgeId).Parent == "stash" && !m_Graph.Children(m_ShotgunId, "Storage").Any(), "Remove did not return physical module to storage.");
            Pass("同槽替换保留物品身份和原位置，拆卸回到仓库");
        });
        Step(() =>
        {
            Submit(Module("halo_mooncore_recycler"));
            Stat("Energy", 12, 8); Stat("Charge", 2, 2.4f);
            Require(!Button("workshopApply").enabledInHierarchy, "Unowned module can be installed.");
            Require(Button("workshopAcquire").resolvedStyle.display != DisplayStyle.None, "Unowned module has no acquisition entry.");
            Require(!string.IsNullOrWhiteSpace(m_Host.Q<Label>("workshopOwnership")?.text), "Unowned status is missing.");
            string unownedSnapshot = Snapshot(); Submit(Button("workshopAcquire"));
            Require(m_AcquireRequests == 1 && m_AcquireResearch && Snapshot() == unownedSnapshot, "Recycler acquisition did not route to research without changing inventory.");
            Capture("workshop-unowned-zh-native.png");
            Pass("未拥有的回流器仍可查看真实效果和获取入口");
            Submit(Module("halo_capacitor")); Submit(Button("workshopApply"));
            string combinedSnapshot = Snapshot();
            Submit(Button("workshopSlotShaping")); Submit(Module("halo_focus_lens"));
            Stat("Capacity", 6, 5); Stat("Spread", 5, 2.25f);
            Require(Snapshot() == combinedSnapshot && m_Graph.Find(m_CapacitorId).Parent == m_ShotgunId, "Combined preview changed installed items.");
            Pass("扩容与聚束组合预览保留已装模块，容量6变5、散角5变2.25");
            Submit(Module("halo_fan_array")); Stat("Damage", 144, 144); Stat("Pellets", 8, 12);
            Pass("扇形阵列保持每发总伤害144，弹丸8变12");
            Capture("workshop-shotgun-preview-zh-native.png");
        });
        Step(() =>
        {
            string before = Snapshot(); int requests = m_Requests;
            m_View.SetReadOnly("REVIEW READ ONLY");
            Require(!Button("workshopApply").enabledInHierarchy && !Button("workshopRemove").enabledInHierarchy, "Read-only workshop allows mutations.");
            Submit(Button("workshopApply"), false);
            Require(m_Requests == requests && Snapshot() == before, "Read-only submit reached inventory callback.");
            m_View.SetReadOnly(null);
            Pass("只读状态阻止事务");
            Submit(Button("workshopSlotStorage")); Submit(Module("halo_charge_bridge"));
            m_Hold = true; m_PreviewSnapshot = Snapshot();
            Submit(Button("workshopApply"));
            Require(m_View.Busy && m_Pending != null && Snapshot() == m_PreviewSnapshot, "Pending request changed inventory or failed to set busy.");
            Require(!Button("workshopApply").enabledInHierarchy && !Button("workshopRemove").enabledInHierarchy, "Pending request did not disable mutation buttons.");
            int pendingRequests = m_Requests; Submit(Button("workshopApply"), false);
            Require(m_Requests == pendingRequests, "Busy state allowed a duplicate request.");
            m_Pending = null; m_Hold = false;
            m_View.Present(m_Graph, "REVIEW REJECTED");
            Require(!m_View.Busy && Snapshot() == m_PreviewSnapshot && Button("workshopApply").enabledInHierarchy, "Rejected request changed inventory or prevented retry.");
            Require(m_Host.Q<Label>("workshopStatus")?.text == "REVIEW REJECTED", "Rejected request did not display its failure.");
            Pass("失败回报清除忙碌状态，保留原库存并允许重试");
            m_Hold = true; Submit(Button("workshopApply"));
            ApplyPending(); Require(!m_View.Busy && m_Graph.Find(m_BridgeId).Parent == m_ShotgunId, "Confirmed snapshot did not restore ready state.");
            Pass("提交中不提前修改库存，禁用重复操作，确认快照后恢复");
            m_View.SelectWorkshopWeapon(m_RifleId);
            Submit(Button("workshopSlotShaping"));
            Require(Module("halo_pulse_controller") != null && Module("halo_focus_lens") == null && Module("halo_fan_array") == null, "Weapon selection did not filter compatible modules.");
            Pass("切换步枪只显示兼容整形模块");
        });
        Step(() =>
        {
            m_View.SelectWorkshopWeapon(m_ShotgunId);
            Submit(Button("workshopSlotShaping")); Submit(Module("halo_focus_lens"));
            SetSize(1280, 720);
        });
        Step(() =>
        {
            CheckLayout(); Capture("workshop-zh-native-small.png"); Pass("较小原生窗口内关键操作未裁切，实际尺寸见捕获记录");
            Submit(Button("workshopSlotStorage")); m_Host.style.maxHeight = 420;
        });
        Step(() =>
        {
            CheckCompactLayout(); Capture("workshop-zh-compact-420-native.png"); Pass("420px 正文高度内可见三个供能模块、全部槽位和安装操作");
            m_Host.style.maxHeight = StyleKeyword.Null;
            SetLocale("en"); m_View.SelectWorkshopWeapon(m_ShotgunId); Present();
        });
        Step(() => { CheckLayout(); Capture("workshop-en-native-small.png"); Pass("英文原生布局"); m_Graph = Fixture(false); Present(); });
        Step(() =>
        {
            Require(!Button("workshopApply").enabledInHierarchy && !Button("workshopRemove").enabledInHierarchy, "Empty weapon inventory exposes mutation actions.");
            Require(!string.IsNullOrWhiteSpace(m_Host.Q<Label>("workshopStatus")?.text), "Empty weapon inventory has no status.");
            string emptySnapshot = Snapshot(); Submit(Button("workshopEmptySupply"));
            Require(m_AcquireRequests == 2 && !m_AcquireResearch && Snapshot() == emptySnapshot, "Empty workshop did not route to supply without changing inventory.");
            Capture("workshop-empty-en-native.png"); Pass("没有兼容光环时显示空状态并禁用拆装");
            BuildTerminalTemplate();
        });
        Step(() =>
        {
            CheckLayout(); CheckWorkshopControlsVisible(); Capture("workshop-terminal-template-zh-native.png");
            Pass("真实 StashScreen UXML 终端正文的样式继承与改装操作可见；仅静态模板，不代表完整导航操作"); Finish(null);
        });
        m_Ready = EditorApplication.timeSinceStartup + 1;
        m_Deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += Tick;
    }

    private static InventoryGraph Fixture(bool weapons)
    {
        var graph = InventoryGraph.Create(starter: false);
        if (weapons)
        {
            foreach (string code in new[] { "shotgun", "rifle", "halo_capacitor", "halo_charge_bridge", "halo_focus_lens", "halo_fan_array", "halo_hit_marker", "halo_pulse_controller" })
                Require(graph.AddSupply(code, 1, "stash") == InventoryError.None, "Cannot seed local fixture: " + code);
            graph.Items.Single(i => i.Code == "shotgun").LoadedAmmo = 4;
            graph.Items.Single(i => i.Code == "rifle").LoadedAmmo = 30;
        }
        Require(graph.Validate() == InventoryError.None, "Fixture graph is invalid."); return graph;
    }

    private void Send(InventoryCommand command)
    {
        m_Requests++;
        if (m_Hold) { m_Pending = command; return; }
        Require(m_Graph.TryApply(command) == InventoryError.None, "Real UI transaction was rejected."); Present();
    }
    private void ApplyPending()
    {
        Require(m_Pending != null, "No pending transaction.");
        var command = m_Pending; m_Pending = null; m_Hold = false;
        Require(m_Graph.TryApply(command) == InventoryError.None, "Pending transaction was rejected."); Present();
    }
    private void Present() { m_View.Present(m_Graph); ApplyFont(); }
    private string Snapshot() => JsonUtility.ToJson(m_Graph);
    private Button Button(string name) => m_Host.Q<Button>(name) ?? throw new InvalidOperationException("Missing workshop button: " + name);
    private Button Module(string code) => m_Host.Q<Button>("workshopModule" + code);
    private void Step(Action action) => m_Steps.Enqueue(action);
    private void Pass(string message) => m_Evidence.Add("PASS: " + message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Submit(Button button, bool requireEnabled = true)
    {
        Require(button != null, "Native submit target is missing.");
        if (requireEnabled) Require(button.enabledInHierarchy, "Native submit target is disabled: " + button.name);
        button.Focus(); using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
    }
    private void Stat(string name, float before, float after)
    {
        var row = m_Host.Q("workshopStat" + name); Require(row != null, "Missing comparison row: " + name);
        float Read(string id)
        {
            string text = row.Q<Label>(id)?.text ?? "";
            var match = Regex.Match(text, @"-?\d+(?:[.,]\d+)?");
            Require(match.Success, "Missing numeric comparison: " + name + "/" + id + " = " + text);
            return float.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        }
        Require(Mathf.Abs(Read("before") - before) < .011f && Mathf.Abs(Read("after") - after) < .011f,
            "Unexpected " + name + " comparison: " + row.Q<Label>("before")?.text + " -> " + row.Q<Label>("after")?.text);
    }
    private void SetLocale(string code)
    {
        var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        Require(locale != null, "Missing review locale: " + code); LocalizationSettings.SelectedLocale = locale;
    }
    private void SetSize(int width, int height)
    {
        m_RequestedSize = new Vector2(width, height);
        minSize = maxSize = m_RequestedSize;
        position = new Rect(position.x, position.y, width, height);
        rootVisualElement.style.width = Length.Percent(100); rootVisualElement.style.height = Length.Percent(100);
        Repaint();
    }
    private void BuildTerminalTemplate()
    {
        SetLocale("zh-Hans");
        var root = rootVisualElement; root.Clear(); root.ClearClassList(); root.styleSheets.Clear();
        var shell = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/StashScreen.uxml").CloneTree();
        shell.style.flexGrow = 1; shell.style.minHeight = 0; shell.style.minWidth = 0;
        shell.style.width = Length.Percent(100); shell.style.height = Length.Percent(100); root.Add(shell);
        var terminal = shell.Q("stashScreen");
        foreach (string css in new[] { "archive-ui", "archive-pages", "archive-page-2", "archive-halo-page", "archive-compact" }) terminal.AddToClassList(css);
        terminal.style.minHeight = 0; terminal.style.minWidth = 0;
        terminal.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ProposalSystem"));
        terminal.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/OperationProposal"));
        terminal.Q(className: "stash-body").style.display = DisplayStyle.None;
        terminal.Q("ReconnectPanel").style.display = DisplayStyle.None;
        terminal.Q<Button>("terminalTab2").AddToClassList("terminal-tab-selected");
        var content = terminal.Q("terminalContent"); content.AddToClassList("terminal-content-page"); content.style.display = DisplayStyle.Flex;
        m_Host = new VisualElement { name = "haloReviewHost" }; m_Host.AddToClassList("halo-workshop-host");
        m_Host.style.flexGrow = 1; m_Host.style.minWidth = 0; m_Host.style.minHeight = 0; content.Add(m_Host);
        m_Graph = Fixture(true); m_ShotgunId = m_Graph.Items.Single(i => i.Code == "shotgun").Id;
        m_View.Present(m_Graph); m_View.SelectWorkshopWeapon(m_ShotgunId); m_View.AttachWorkshop(m_Host);
        MoonkovLocalization.Set(terminal.Q<Label>(className: "stash-page-title"), "HALO WORKSHOP");
        MoonkovLocalization.Set(terminal.Q(className: "stash-page-heading")?.Q<Label>(className: "stash-eyebrow"), "LOCAL REVIEW / SAMPLE INVENTORY");
        terminal.Q<Label>("stashAccountName").text = "LOCAL REVIEW";
        MoonkovLocalization.Bind(shell); ApplyFont();
    }
    private void ApplyFont()
    {
        if (!m_Font) return;
        rootVisualElement.Query<TextElement>().ForEach(t => { t.style.unityFontDefinition = FontDefinition.FromFont(m_Font); t.style.letterSpacing = 0; });
    }
    private void Tick()
    {
        if (m_Finished) return;
        try
        {
            Repaint(); ApplyFont();
            if (EditorApplication.timeSinceStartup > m_Deadline) throw new TimeoutException("Native workshop review exceeded 60 seconds.");
            if (EditorApplication.timeSinceStartup < m_Ready) return;
            if (m_Steps.Count > 0) m_Steps.Dequeue()();
            m_Ready = EditorApplication.timeSinceStartup + .45;
        }
        catch (Exception error) { Finish(error); }
    }
    private void CheckLayout()
    {
        RecordLayout("layout");
        var panel = rootVisualElement.panel;
        Require(panel != null, "Native panel is not attached.");
        var panelBounds = panel.visualTree.worldBound;
        var rootBounds = rootVisualElement.worldBound;
        Require(Contains(panelBounds, rootBounds), "Review root exceeds the actual native panel; forced canvas sizes are invalid evidence.");
        var workshop = m_Host.Q("haloWorkshop"); Require(workshop != null && workshop.worldBound.width >= 1000,
            "Workshop did not fill its host: root=" + rootVisualElement.worldBound + ", host=" + m_Host.worldBound + ", workshop=" + workshop?.worldBound);
        var bounds = VisibleBounds();
        foreach (string name in new[] { "workshopWeapon", "workshopSlots", "workshopModules", "workshopApply" })
        {
            var element = m_Host.Q(name); Require(element != null && element.worldBound.width > 10 && element.worldBound.height > 10, "Collapsed control: " + name);
            Require(Contains(bounds, element.worldBound), "Control clipped by the actual native panel: " + name + " / " + element.worldBound);
        }
    }
    private void CheckCompactLayout()
    {
        CheckLayout();
        var bounds = m_Host.worldBound;
        Require(bounds.height <= 421, "Compact review host exceeds 420px: " + bounds);
        Require(m_Host.Q("haloWorkshop").ClassListContains("workshop-compact"), "Compact styling was not activated by actual panel geometry.");
        CheckWorkshopControlsVisible();
    }
    private void CheckWorkshopControlsVisible()
    {
        var bounds = Intersection(VisibleBounds(), m_Host.worldBound);
        foreach (string name in new[] { "workshopSlotStorage", "workshopSlotShaping", "workshopSlotUtility", "workshopApply" })
        {
            var element = m_Host.Q(name);
            Require(element != null && Contains(bounds, element.worldBound),
                "Essential control leaves compact host: " + name + " / " + element?.worldBound);
        }
        var scroll = m_Host.Q<ScrollView>("workshopModules");
        var viewport = scroll.contentViewport.worldBound;
        var candidates = scroll.Query<Button>().ToList().Where(button => button.name?.StartsWith("workshopModule", StringComparison.Ordinal) == true).ToArray();
        Require(candidates.Length == 3, "Compact power-slot review must contain all three candidates.");
        foreach (var button in candidates)
            Require(Contains(viewport, button.worldBound),
                "Power candidate requires scrolling in compact review: " + button.name + " / " + button.worldBound + " vs " + viewport);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMin >= outer.yMin - 1 && inner.yMax <= outer.yMax + 1;
    private static Rect Intersection(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
    private Rect VisibleBounds() => Intersection(rootVisualElement.worldBound, rootVisualElement.panel.visualTree.worldBound);
    private void RecordLayout(string stage)
    {
        var root = rootVisualElement;
        m_Evidence.Add(stage + ": requested=" + m_RequestedSize + "; window=" + position + "; panel=" + root.panel?.visualTree.worldBound +
            "; root=" + root.worldBound + "; host=" + m_Host?.worldBound + "; workshop=" + m_Host?.Q("haloWorkshop")?.worldBound);
    }
    private void Capture(string name)
    {
        ApplyFont();
        RecordLayout("capture " + name);
        var root = rootVisualElement;
        var panelBounds = root.panel.visualTree.worldBound;
        int width = Mathf.RoundToInt(panelBounds.width), height = Mathf.RoundToInt(panelBounds.height);
        Require(width >= 1000 && height >= 420 && Contains(panelBounds, root.worldBound), "Native review panel is too small or clips its review root.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        float dpi = Convert.ToSingle(root.panel.GetType().GetProperty("scaledPixelsPerPoint", flags).GetValue(root.panel));
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
            RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            float brightest = 0; for (int y = 20; y < image.height; y += 47) for (int x = 20; x < image.width; x += 47) brightest = Mathf.Max(brightest, image.GetPixel(x, y).maxColorComponent);
            Require(brightest > .15f, "Blank native render is not valid visual evidence: " + name);
            File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
            m_Evidence.Add("CAPTURE: " + name + "; actual PNG=" + image.width + "x" + image.height + "; native logical panel=" + width + "x" + height + "; dpi=" + dpi);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (image) DestroyImmediate(image); target.Release(); DestroyImmediate(target);
        }
    }
    private void Finish(Exception error)
    {
        if (m_Finished) return; m_Finished = true; EditorApplication.update -= Tick;
        m_Evidence.Add(error == null ? "COMPLETE: native UI and isolated graph transactions. No account writes, Play or network acceptance." : "FAILED\n" + error);
        Directory.CreateDirectory(Output); File.WriteAllLines(Path.Combine(Output, "checks.txt"), m_Evidence);
        if (error != null) Debug.LogException(error); else Debug.Log("Halo workshop review passed: " + Output);
        bool batch = m_Batch; Close(); if (batch) EditorApplication.Exit(error == null ? 0 : 1);
    }
    private void OnDisable()
    {
        EditorApplication.update -= Tick; m_View?.Dispose(); m_View = null;
        if (m_PreviousLocale != null) LocalizationSettings.SelectedLocale = m_PreviousLocale;
    }
}
