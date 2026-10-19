using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using BodyPart = Unity.MP_FPS.BodyPart;

// Renders the real UXML/presenters into a private runtime panel, never desktop pixels.
// All review inventory and health data are local fixtures; no account or raid writes.
[InitializeOnLoad]
public static class MoonkovUIRuntimeReview
{
    private const string Pending = "Moonkov.UIReview.Pending", StartScene = "Moonkov.UIReview.StartScene";
    private static readonly string[] AllCases = { "home", "home-dark", "warehouse", "halo", "suppliers", "tasks", "base", "comms", "settings", "deployment", "account", "session", "host", "direct", "connecting", "loading", "raidpack", "loot", "health", "halo-light", "interaction", "extraction", "result", "cargo", "storage", "pause", "death", "score", "diagnostics", "hud-bright", "hud-dark", "hud-alerts", "workshop", "shop", "emergency", "research", "airlock", "warehouse-compact", "settings-compact", "home-compact", "deployment-compact", "halo-compact", "item", "operator", "location", "briefing", "recovery", "ready", "operator-compact", "location-compact", "briefing-compact", "recovery-compact", "ready-compact" };
    private static string[] Cases = AllCases;
    private static GameObject s_Object;
    private static PanelSettings s_Settings;
    private static RenderTexture s_Target;
    private static VisualElement s_Root;
    private static StashScreen s_Stash;
    private static ContainerInventoryView s_Inventory;
    private static RaidHealthView s_Health;
    private static Scene s_Scene;
    private static int s_Index, s_PrepareCalls, s_CharacterSettledFrame;
    private static double s_Ready;
    private static bool s_Running, s_KeepCurrentPlay, s_DeploymentVerified;
    private static UnityEngine.Localization.Locale s_PreviousLocale;
    private static readonly List<string> Evidence = new List<string>();
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/UI/Native-20261018"));
    static MoonkovUIRuntimeReview() { EditorApplication.playModeStateChanged += State; }

    [MenuItem("Tools/Moonkov/Review Approved UI (Runtime RenderTexture)")]
    public static void Start() => StartSelected(AllCases);

    public static void StartSelected(params string[] cases)
    {
        if (cases == null || cases.Length == 0 || cases.Any(c => !AllCases.Contains(c))) throw new ArgumentException("Unknown review case");
        var entry = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/MainMenu.uxml").CloneTree();
        foreach (var id in new[] { "Canvas", "HidingBackground", "PlayerNameField", "ConnetionModeOption", "SessionName", "SessionNameField", "CreateJoinGame", "StartHost", "ConnectToServer", "QuitButton", "ProfileMode", "ConnectionPanel", "stashScreenHost" })
            if (entry.Q(id) == null) throw new InvalidOperationException("Actual MainMenu bootstrap binding missing: " + id);
        SessionState.SetString("Moonkov.UIReview.Cases", string.Join("|", cases));
        if (Application.dataPath.Replace('\\', '/') != "E:/UnityProjects/FPS_Template/Assets") throw new InvalidOperationException("Wrong project.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play mode.");
        SessionState.SetString(StartScene, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    public static void ReviewCurrentPlay(params string[] cases)
    {
        if (!EditorApplication.isPlaying || s_Running) throw new InvalidOperationException("Requires current Play and no active review");
        if (cases == null || cases.Length == 0 || cases.Any(c => !AllCases.Contains(c))) throw new ArgumentException("Unknown review case");
        SessionState.SetString("Moonkov.UIReview.Cases", string.Join("|", cases));
        s_KeepCurrentPlay = true;
        Setup();
    }

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) Setup();
        if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            var previous = SessionState.GetString(StartScene, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            SessionState.SetBool(Pending, false);
        }
    }
    private static void Setup()
    {
        try
        {
            Cases = SessionState.GetString("Moonkov.UIReview.Cases", string.Join("|", AllCases)).Split('|');
            Directory.CreateDirectory(Output); Evidence.Clear();
            s_Scene = SceneManager.CreateScene("Moonkov UI Review / temporary");
            s_Object = new GameObject("Moonkov UI Review / sample data") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(s_Object, s_Scene);
            s_Settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
            s_Settings.scaleMode = PanelScaleMode.ConstantPixelSize; s_Settings.scale = 1;
            s_Target = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32); s_Target.Create();
            s_Settings.targetTexture = s_Target; s_Settings.clearColor = true; s_Settings.colorClearValue = Color.black;
            var doc = s_Object.AddComponent<UIDocument>(); doc.panelSettings = s_Settings;
            s_Root = doc.rootVisualElement; s_Root.style.flexGrow = 1;
            s_Root.style.width = Length.Percent(100); s_Root.style.height = Length.Percent(100);
            s_PreviousLocale = LocalizationSettings.SelectedLocale;
            var locale = LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
            if (locale != null) LocalizationSettings.SelectedLocale = locale; // no PlayerPrefs write
            s_Index = 0; s_Running = true; Build(); EditorApplication.update += Tick;
        }
        catch (Exception ex) { Debug.LogException(ex); Finish(false); }
    }
    private static void DisposeViews()
    {
        s_Stash?.Dispose(); s_Stash = null;
        s_Inventory?.Dispose(); s_Inventory = null;
        s_Health?.Dispose(); s_Health = null;
    }
    private static void Build()
    {
        s_DeploymentVerified = false; s_CharacterSettledFrame = -1;
        DisposeViews(); s_Root.Clear(); s_Root.styleSheets.Clear(); s_Root.ClearClassList();
        s_Root.style.backgroundColor = new Color(.03f, .035f, .045f);
        s_Root.style.display = DisplayStyle.Flex;
        var name = Cases[s_Index];
        bool compact = name.EndsWith("-compact");
        s_Root.style.flexGrow = compact ? 0 : 1;
        s_Root.style.width = compact ? new StyleLength(1280) : new StyleLength(Length.Percent(100));
        s_Root.style.height = compact ? new StyleLength(720) : new StyleLength(Length.Percent(100));
        if (new[] { "home", "home-dark", "warehouse", "halo", "suppliers", "tasks", "base", "comms", "settings", "deployment", "operator", "location", "briefing", "recovery", "ready" }.Contains(name)) BuildTerminal(name);
        else if (compact) BuildTerminal(name.Replace("-compact", ""));
        else if (name == "account" || name == "session")
        {
            Load("Assets/UI Toolkit/GameUI/MainMenu.uxml"); s_Root.Q("stashScreenHost").style.display = DisplayStyle.None;
            if (name == "account")
            {
                var panel = s_Root.Q("ConnectionPanel"); panel.style.display = DisplayStyle.None;
                Resources.Load<VisualTreeAsset>("Moonkov/UI/AccountAccess").CloneTree(panel.parent);
                var canvas = s_Root.Q("Canvas"); canvas.AddToClassList("archive-access-account"); canvas.AddToClassList("archive-login-plates");
                var mode = s_Root.Q<Button>("ProfileMode"); mode.RemoveFromHierarchy(); s_Root.Q("accountSecondaryActions").Insert(0, mode);
                var username = s_Root.Q<TextField>("accountUsername");
                username.SetValueWithoutNotify("local_review"); // local visual fixture; never submitted
                username.Focus();
            }
        }
        else if (name == "host") Load("Assets/UI Toolkit/GameUI/StartHostPopup.uxml");
        else if (name == "direct") Load("Assets/UI Toolkit/GameUI/DirectConnectPopup.uxml");
        else if (name == "connecting") Load("Assets/UI Toolkit/GameUI/ConnectionStatus.uxml");
        else if (name == "loading") { Load("Assets/UI Toolkit/GameUI/Loading.uxml"); s_Root.Q<ProgressBar>().value = .63f; }
        else if (name == "pause") Load("Assets/UI Toolkit/GameUI/PauseMenu.uxml");
        else if (name == "death") Load("Assets/UI Toolkit/GameUI/Respawn.uxml");
        else if (name == "score") Load("Assets/UI Toolkit/GameUI/Leaderboard/LeaderboardUi.uxml");
        else if (name == "diagnostics") Load("Assets/UI Toolkit/GameUI/NetworkStatus.uxml");
        else if (name == "item")
        {
            Load("Assets/UI Toolkit/GameUI/StashScreen.uxml");
            s_Stash = new StashScreen(s_Root, () => { }, () => { }, () => { }, preview: true);
            s_Stash.Present("LOCAL UI REVIEW", 2, 128, 36, 12, true, false);
            s_Stash.NavigatePage(1);
            s_Root.schedule.Execute(() =>
            {
                var tile = s_Root.Q<Button>(className: "inventory-item");
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1, mousePosition = tile.worldBound.center })) tile.SendEvent(down);
                var inspect = s_Root.Query<Button>(className: "terminal-button").ToList().FirstOrDefault(b => b.text == "INSPECT" || b.text == MoonkovLocalization.Text("INSPECT"));
                if (inspect == null) throw new Exception("Native item inspect action missing");
                Submit(inspect);
            }).StartingIn(400);
        }
        else if (name == "workshop")
        {
            s_Root.AddToClassList("archive-ui");
            s_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ApprovedPages"));
            var host = new VisualElement(); host.style.flexGrow = 1; s_Root.Add(host);
            s_Inventory = new ContainerInventoryView(host, true, _ => { });
            s_Inventory.Present(InventoryFixture(), "LOCAL REVIEW / SAMPLE ITEMS"); s_Inventory.OpenWorkshop();
            var choose = host.Query<Button>(className: "terminal-button").ToList().FirstOrDefault();
            if (choose != null) Submit(choose);
        }
        else if (name == "shop" || name == "emergency" || name == "research") BuildShop(name);
        else BuildField(name);
        MoonkovLocalization.Bind(s_Root);
        var notice = new Label("NATIVE UNITY UI / LOCAL REVIEW DATA") { pickingMode = PickingMode.Ignore };
        notice.style.position = Position.Absolute; notice.style.bottom = 2; notice.style.left = 10;
        notice.style.fontSize = 10; notice.style.backgroundColor = Color.black; notice.style.color = Color.white;
        s_Root.Add(notice);
        s_Ready = EditorApplication.timeSinceStartup + 2.6;
    }
    private static void BuildTerminal(string name)
    {
        Load("Assets/UI Toolkit/GameUI/StashScreen.uxml");
        s_Stash = new StashScreen(s_Root, () => { s_PrepareCalls++; }, () => { }, () => { }, preview: true);
        s_Stash.Present("LOCAL UI REVIEW", 2, 128, 36, 12, true, false);
        int page = name == "warehouse" ? 1 : name == "halo" ? 2 : name == "suppliers" ? 3 : name == "tasks" ? 5 : name == "base" ? 6 : name == "comms" ? 7 : name == "settings" ? 8 : name == "home" || name == "home-dark" ? 0 : 4;
        s_Stash.NavigatePage(page);
        for (int i = 0; i < 9; i++) if (s_Root.Q<Button>("terminalTab" + i) == null) throw new Exception("Missing first-level entry " + i);
        if (page == 1) s_Root.Q<Button>(className: "inventory-item")?.Focus();
        if (name == "home-dark") { var root = s_Root.Q("stashScreen"); root.RemoveFromClassList("moon-home-light"); root.AddToClassList("moon-home-dark"); }
        if (page == 4)
        {
            if (s_Root.Q("operationStepper") == null) throw new Exception("Five-page departure navigation missing");
            int step = Array.IndexOf(new[] { "operator", "location", "briefing", "recovery", "ready" }, name);
            if (step < 0) step = 0;
            int selected = step;
            s_Root.schedule.Execute(() =>
            {
                try
                {
                    Submit(s_Root.Q<Button>("operationStep" + selected));
                    var portrait = s_Root.Q<Image>("operatorPortrait");
                    if ((selected == 0) != (portrait != null)) throw new Exception("Character must be exclusive to the operator step");
                    if (selected == 2 && s_Root.Q<IntegerField>("operationCarryCells").value != PlayerProfileClient.CarryCells) throw new Exception("Carry field must show current profile selection");
                    if (selected == 3 && s_Root.Q<Button>("purchaseContract").enabledSelf) throw new Exception("Unavailable insurance must stay disabled");
                }
                catch (Exception ex) { Evidence.Add(ex.ToString()); Finish(false); }
            }).StartingIn(150);
        }
    }
    private static InventoryGraph InventoryFixture()
    {
        var graph = InventoryGraph.Create(); graph.AddSupply("dust", 128, "stash"); graph.AddSupply("alloy", 36, "stash"); graph.AddSupply("cells", 12, "stash");
        graph.AddSupply("cells", 3); graph.AddSupply("medkit", 2); graph.AddSupply("dust", 7);
        graph.AddSupply("oxygen_canister", 1); graph.AddSupply("ration", 1);
        return graph;
    }
    private static void BuildShop(string name)
    {
        s_Root.AddToClassList("archive-ui"); s_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ApprovedPages"));
        var window = new VisualElement(); window.AddToClassList("terminal-shop-window");
        window.AddToClassList("terminal-window"); window.style.alignSelf = Align.Center; window.style.marginTop = 50;
        s_Root.Add(window);
        var shell = Resources.Load<VisualTreeAsset>("Moonkov/UI/Components/ShopCatalog").CloneTree().Q("shopCatalog"); shell.RemoveFromHierarchy(); window.Add(shell);
        var heading = new Label(MoonkovLocalization.Text(name == "research" ? "HALO RESEARCH" : name == "emergency" ? "EMERGENCY SUPPLY" : "BUY")); heading.AddToClassList("terminal-title"); shell.Q("shopHead").Add(heading);
        var balance = new Label("MOON DUST / —"); balance.AddToClassList("terminal-heading"); shell.Q("shopHead").Add(balance);
        foreach (var tab in new[] { "BUY", "SELL", "EMERGENCY SUPPLY", "HALO RESEARCH" }) { var button = new Button { text = tab }; button.AddToClassList("terminal-button"); button.SetEnabled(false); shell.Q("shopTabs").Add(button); }
        var list = shell.Q<ScrollView>("shopList");
        var codes = name == "emergency" ? new[] { "pistol", "cells" } : name == "research" ? new[] { "alien_neural_core", "alien_tissue" } : new[] { "rifle", "cells", "medkit", "backpack" };
        foreach (var code in codes)
        {
            var row = Resources.Load<VisualTreeAsset>("Moonkov/UI/Components/ShopProduct").CloneTree().Q("shopProduct"); row.RemoveFromHierarchy(); list.Add(row);
            var art = new StashItemArt(ContainerInventoryView.Art(code)); art.style.flexGrow = 1; row.Q("productArt").Add(art);
            row.Q<Label>("productTitle").text = InventoryCatalog.Get(code).Name;
            var definition = InventoryCatalog.Get(code);
            row.Q<Label>("productDetail").text = definition.Width + " × " + definition.Height + " / " + definition.Weight.ToString("0.00") + " kg";
            var button = new Button { text = name == "research" ? "RESEARCH" : "BUY / —" }; button.AddToClassList("terminal-button"); button.SetEnabled(false); row.Add(button);
            if (code == codes[0])
            {
                row.AddToClassList("archive-product-selected"); shell.Q<Label>("inspectTitle").text = definition.Name;
                shell.Q<Label>("inspectDetail").text = row.Q<Label>("productDetail").text;
                var preview = new StashItemArt(ContainerInventoryView.Art(code)); preview.style.flexGrow = 1; shell.Q("inspectArt").Add(preview);
            }
        }
        var status = new Label("LOCAL REVIEW / NO ACCOUNT TRANSACTION"); status.AddToClassList("terminal-shop-status"); window.Add(status);
    }
    private static void BuildField(string name)
    {
        Load("Assets/UI Toolkit/GameUI/PlayerHUD.uxml");
        Resources.Load<VisualTreeAsset>("Moonkov/RaidUI").CloneTree(s_Root);
        Set("ammo-label", "18 / 30"); Set("health-state", "HEALTH"); Set("health-value", "440/440");
        s_Root.Q("reloading-label").style.display = DisplayStyle.None;
        var reticle = s_Root.Q("player-reticle"); reticle.AddToClassList("kits-reticle-cross"); reticle.style.width = reticle.style.height = 22;
        Set("raidTimer", "18:42"); Set("raidClock", "EXPEDITION / LOCAL REVIEW"); Set("raidBag", "CARGO / 4"); Set("raidGrenade", "GRENADE / 1");
        s_Root.Q("raidOxygenPanel").style.display = DisplayStyle.Flex;
        Set("raidOxygenReserve", "O₂ / 76%"); Set("raidOxygenTime", "EST. 08:20"); Set("raidOxygenAir", "SUIT AIR"); s_Root.Q("raidOxygenFill").style.width = Length.Percent(76);
        if (name == "hud-bright") s_Root.style.backgroundColor = new Color(.94f, .94f, .92f);
        if (name == "hud-alerts")
        {
            s_Root.Q("reloading-label").style.display = DisplayStyle.Flex;
            s_Root.Q("raidOxygenPanel").AddToClassList("oxygen-critical");
            Set("raidOxygenReserve", "O₂ / 5%"); Set("raidOxygenWarning", "HYPOXIA / NO SPRINT, SEEK AIR");
            s_Root.Q("raidOxygenWarning").style.display = DisplayStyle.Flex;
            s_Root.Q("raidOxygenWarning").AddToClassList("oxygen-critical");
            s_Root.Q("raidOxygenFill").style.width = Length.Percent(5);
            Set("health-state", "HEALTH / CRITICAL"); Set("health-value", "65/440"); s_Root.Q("health-info-container").AddToClassList("health-critical");
        }
        if (name == "raidpack" || name == "loot" || name == "health")
        {
            s_Root.Q("raidInventory").style.display = DisplayStyle.Flex;
            if (name == "health")
            {
                s_Root.Q("raidInventoryHost").style.display = DisplayStyle.None;
                var host = s_Root.Q("raidHealthHost"); host.style.display = DisplayStyle.Flex;
                s_Health = new RaidHealthView(host, _ => { });
                var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); RaidHealth.Set(ref health, BodyPart.LeftArm, 20);
                s_Health.Present(health, 0, false);
                s_Root.Q("raidEquipmentTab").RemoveFromClassList("raid-tab-active"); s_Root.Q("raidHealthTab").AddToClassList("raid-tab-active");
            }
            else
            {
                var graph = InventoryFixture();
                if (name == "loot")
                {
                    graph.Items.Add(new InventoryItem { Id = "loot", Code = "loot" }); graph.AddSupply("dust", 7, "loot"); graph.AddSupply("cells", 2, "loot");
                }
                s_Inventory = new ContainerInventoryView(s_Root.Q("raidInventoryHost"), false, _ => { });
                s_Inventory.Present(graph, "LOCAL REVIEW / SAMPLE ITEMS"); s_Inventory.Show();
            }
        }
        if (name == "halo-light")
        {
            var host = Resources.Load<VisualTreeAsset>("Moonkov/UI/HaloLightPanel").Instantiate(); host.AddToClassList("halo-light-host"); s_Root.Add(host);
            host.Q("haloAimControls").style.display = DisplayStyle.None; host.Q("haloIdleTab").AddToClassList("halo-selected");
            Set("haloLightStatus", "EVERYDAY"); Set("haloRangeValue", "6 m"); Set("haloIdleValue", "100%");
        }
        if (name == "interaction")
        {
            var prompt = s_Root.Q("raidInteraction"); prompt.AddToClassList("is-visible"); Set("raidPrompt", "OPEN CONTAINER"); Set("raidInteractionState", "AVAILABLE"); Set("raidInteractionRange", "1.4 m");
        }
        if (name == "airlock")
        {
            Resources.Load<VisualTreeAsset>("Moonkov/AirlockPrompt").CloneTree(s_Root);
            s_Root.Q("airlockPrompt").AddToClassList("is-visible"); s_Root.Q("airlockMarker").style.display = DisplayStyle.None;
            Set("airlockAction", "OPEN"); Set("airlockPressure", "PRESSURE / LOCAL REVIEW"); Set("interactionRange", "1.4 m");
        }
        if (name == "extraction")
        {
            s_Root.Q("raidExtractionPanel").AddToClassList("is-visible"); Set("raidExtraction", "EXTRACTING / 05.2"); s_Root.Q("raidExtractionFill").style.width = Length.Percent(35);
        }
        if (name == "result" || name == "cargo" || name == "storage")
        {
            var result = s_Root.Q("raidResult"); result.style.display = DisplayStyle.Flex;
            bool cargo = name == "cargo", storage = name == "storage";
            result.EnableInClassList("raid-result-cargo", cargo); result.EnableInClassList("raid-result-storage", storage);
            Set("raidResultHeading", storage ? "PERSONAL STORAGE" : cargo ? "RECOVERED CARGO" : "SURVIVED");
            Set("raidResultText", "EXPEDITION / LOCAL REVIEW DATA"); Set("raidSaveStatus", "Waiting for server confirmation…");
            s_Root.Q("raidManifest").style.display = name == "result" ? DisplayStyle.None : DisplayStyle.Flex;
            Set("raidManifestDust", "128"); Set("raidManifestAlloy", "36"); Set("raidManifestCells", "12");
            for (int i = 0; i < 3; i++) s_Root.Q("raidStep" + i).EnableInClassList("raid-step-active", i == (storage ? 2 : cargo ? 1 : 0));
            s_Root.Q("raidLoadout").style.display = storage ? DisplayStyle.Flex : DisplayStyle.None;
            s_Root.Q("raidDeploy").SetEnabled(false); s_Root.Q("raidReturn").SetEnabled(false);
            s_Root.Q("raidDeploy").style.display = s_Root.Q("raidReturn").style.display = storage ? DisplayStyle.Flex : DisplayStyle.None;
            s_Root.Q("raidResultNext").style.display = storage ? DisplayStyle.None : DisplayStyle.Flex;
            s_Root.Q("raidResultBack").SetEnabled(name != "result");
        }
    }
    private static void Load(string path) { AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path).CloneTree(s_Root); }
    private static void Set(string name, string text) { var e = s_Root.Q<Label>(name); if (e != null) MoonkovLocalization.Set(e, text); }
    private static void Submit(Button button) { button.Focus(); using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt); }
    // Exercise the real native buttons without modifying account inventory or launching a raid.
    private static void VerifyOperationFlow(int step)
    {
        try
        {
            if (!s_Root.Q("stashScreen").ClassListContains("operation-step-" + step)) throw new Exception("Departure flow stopped at step " + step);
            if (step < 4)
            {
                if (step == 2)
                {
                    s_Stash.Present("LOCAL UI REVIEW", 2, 128, 36, 12, true, true);
                    if (s_Root.Q<IntegerField>("operationCarryCells").enabledSelf || s_Root.Q<Button>("carryPlus").enabledSelf || s_Root.Q<Button>("carryMinus").enabledSelf) throw new Exception("Busy carry controls must be disabled");
                    s_Stash.Present("LOCAL UI REVIEW", 2, 128, 36, 12, true, false);
                }
                s_Root.schedule.Execute(() =>
                {
                    Submit(s_Root.Q<Button>("operationContinue"));
                    s_Root.schedule.Execute(() => VerifyOperationFlow(step + 1)).StartingIn(180);
                }).StartingIn(180);
                return;
            }
            Submit(s_Root.Q<Button>("back"));
            s_Root.schedule.Execute(() =>
            {
                try
                {
                    if (!s_Root.Q("stashScreen").ClassListContains("operation-step-3")) throw new Exception("Native Back did not return to recovery");
                    Submit(s_Root.Q<Button>("operationContinue"));
                    s_Root.schedule.Execute(() =>
                    {
                        try
                        {
                            int before = s_PrepareCalls;
                            Submit(s_Root.Q<Button>("launch"));
                            if (s_PrepareCalls != before + 1) throw new Exception("Launch must call existing prepare callback once");
                            s_Stash.Present("LOCAL UI REVIEW", 2, 128, 36, 12, true, true);
                            if (s_Root.Q<Button>("launch").enabledSelf) throw new Exception("Busy launch must be disabled");
                            s_DeploymentVerified = true;
                            Evidence.Add("departure: native stepper, Continue 0-4, Back/Continue, existing launch callback once; busy carry and launch disabled; no profile transaction");
                            s_Ready = EditorApplication.timeSinceStartup + .8;
                        }
                        catch (Exception ex) { Evidence.Add(ex.ToString()); Finish(false); }
                    }).StartingIn(180);
                }
                catch (Exception ex) { Evidence.Add(ex.ToString()); Finish(false); }
            }).StartingIn(180);
        }
        catch (Exception ex) { Evidence.Add(ex.ToString()); Finish(false); }
    }
    private static void Tick()
    {
        if (!s_Running || EditorApplication.timeSinceStartup < s_Ready) return;
        try
        {
            if (s_Root.resolvedStyle.display == DisplayStyle.None) throw new Exception("Review root unexpectedly hidden");
            var stage = s_Root.Q("operationMain");
            if (stage != null && stage.resolvedStyle.opacity < .99f) { s_Ready = EditorApplication.timeSinceStartup + .15; return; }
            var portrait = s_Root.Q<Image>("operatorPortrait");
            if (portrait != null)
            {
                if (portrait.image == null) { s_Ready = EditorApplication.timeSinceStartup + .15; return; }
                if (s_CharacterSettledFrame < 0) { s_CharacterSettledFrame = Time.frameCount; s_Ready = EditorApplication.timeSinceStartup + .3; return; }
                if (Time.frameCount <= s_CharacterSettledFrame) return;
            }
            bool compact = Cases[s_Index].EndsWith("-compact");
            if (Cases[s_Index] == "account")
            {
                var field = s_Root.Q<TextField>("accountUsername");
                foreach (var text in field.Query<TextElement>().ToList())
                    Evidence.Add("account text: chars=" + text.text.Length + "; classes=" + string.Join(",", text.GetClasses()) + "; color=" + text.resolvedStyle.color + "; size=" + text.resolvedStyle.fontSize + "; bounds=" + text.worldBound + "; font=" + text.resolvedStyle.unityFontDefinition);
            }
            var image = new Texture2D(compact ? 1280 : s_Target.width, compact ? 720 : s_Target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = s_Target; image.ReadPixels(new Rect(0, s_Target.height - image.height, image.width, image.height), 0, 0); image.Apply();
                float brightest = 0;
                for (int y = 10; y < image.height; y += 71)
                    for (int x = 10; x < image.width; x += 71)
                        brightest = Mathf.Max(brightest, image.GetPixel(x, y).maxColorComponent);
                if (brightest < .03f) throw new InvalidOperationException("Invalid blank RenderTexture; no visual acceptance: " + Cases[s_Index]);
                File.WriteAllBytes(Path.Combine(Output, Cases[s_Index] == "ready" && s_DeploymentVerified ? "ready-busy.png" : Cases[s_Index] + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
            if (Cases[s_Index] == "ready" && !s_DeploymentVerified)
            {
                s_Ready = double.MaxValue;
                Submit(s_Root.Q<Button>("operationStep0"));
                s_Root.schedule.Execute(() => VerifyOperationFlow(0)).StartingIn(180);
                return;
            }
            if (Cases[s_Index] == "item" && s_Root.Q("itemInspection") == null) throw new Exception("Native item inspection window missing");
            if (Cases[s_Index] == "halo")
            {
                Submit(s_Root.Q<Button>("haloNode0"));
                if (!s_Root.Q("haloNode0").ClassListContains("terminal-node-selected") ||
                    s_Root.Q<Label>("haloComponentTitle").text != MoonkovLocalization.Text(InventoryCatalog.Get("halo_capacitor").Name))
                    throw new Exception("Construction node selection did not update the native inspector");
                Submit(s_Root.Q<Button>("haloNode4"));
                if (s_Root.Q("haloComponentArt").Q<Image>().image != Resources.Load<Texture2D>("HaloIcons/Rifle"))
                    throw new Exception("Construction must retain the real project halo appearance");
                Evidence.Add("halo: native node submit updates inspector; center selection restores original HaloIcons/Rifle; no inventory transaction");
            }
            Evidence.Add(Cases[s_Index] + ": native runtime panel " + (compact ? "1280x720" : "1600x900") + "; local fixtures; not a gameplay/network acceptance.");
            if (++s_Index < Cases.Length) Build(); else Finish(true);
        }
        catch (Exception ex) { Debug.LogException(ex); Evidence.Add(ex.ToString()); Finish(false); }
    }
    private static void Finish(bool success)
    {
        Evidence.Add(success ? "COMPLETE" : "FAILED");
        Directory.CreateDirectory(Output); File.WriteAllLines(Path.Combine(Output, "Runtime-review.txt"), Evidence);
        Cleanup();
        if (!s_KeepCurrentPlay) EditorApplication.ExitPlaymode();
        s_KeepCurrentPlay = false;
    }
    private static void Cleanup()
    {
        s_Running = false; EditorApplication.update -= Tick; DisposeViews();
        if (s_PreviousLocale != null) LocalizationSettings.SelectedLocale = s_PreviousLocale;
        s_PreviousLocale = null;
        if (s_Object) UnityEngine.Object.DestroyImmediate(s_Object);
        if (s_Settings) UnityEngine.Object.DestroyImmediate(s_Settings);
        if (s_Target) { s_Target.Release(); UnityEngine.Object.DestroyImmediate(s_Target); }
        if (s_Scene.IsValid() && s_Scene.isLoaded) SceneManager.UnloadSceneAsync(s_Scene);
    }
}
