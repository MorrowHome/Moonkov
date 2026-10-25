using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
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
    private double m_LayoutDeadline, m_NextLayoutProbe;
    private RenderTexture m_LayoutTarget;
    private string m_ShotgunId, m_RifleId, m_CapacitorId, m_BridgeId;
    private string m_PreviewSnapshot;
    private Font m_Font;
    private Vector2 m_RequestedSize;
    private readonly List<Texture2D> m_ComparisonTextures = new List<Texture2D>();
    private static readonly string[] ReviewModuleCodes = { "halo_capacitor", "halo_charge_bridge", "halo_mooncore_recycler", "halo_focus_lens", "halo_fan_array", "halo_pulse_controller", "halo_hit_marker" };
    private static string Output => Path.GetFullPath("LocalData/HaloWorkshopReview");

    [MenuItem("Tools/Moonkov/Review Halo Workshop")]
    public static void Open() => Start(false);

    // Omit -quit and -nographics. This method exits the batch Editor after its scheduled review.
    public static void RunBatch() { HaloModuleArtworkSetup.Prepare(); SyncAppearanceTranslations(); Start(true); }

    // Authoring setup for this appearance change only. The normal review menu never writes tables.
    public static void SyncAppearanceTranslations()
    {
        var translations = JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var keys = new[] { "BEFORE", "AFTER", "CHARGE", "FIRE", "Appearance preview unavailable.", "SCROLL TO ZOOM" }
            .Concat(ReviewModuleCodes.Select(HaloModules.Description)).ToArray();
        Require(keys.Length == 13 && keys.Distinct().Count() == 13, "Appearance translation key list is incomplete.");
        foreach (string key in keys) Require(translations[key]?.Type == JTokenType.String, "Missing appearance translation: " + key);
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        Require(collection != null, "MoonkovUI string table collection is missing.");
        foreach (var table in collection.StringTables)
        {
            foreach (string key in keys)
            {
                string value = table.LocaleIdentifier.Code == "zh-Hans" ? translations[key].Value<string>() : key;
                if (table.GetEntry(key)?.Value == value) continue;
                table.AddEntry(key, value); EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }

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
        Step(CheckInteractivePreview);
        Step(CheckPreviewPlayback);
        Step(CheckPreviewReopening);
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
            Require((WorkshopPreview().Modules & (ushort)HaloModule.MooncoreRecycler) != 0, "Unowned module does not have a real appearance preview.");
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
            Require((WorkshopPreview().Modules & (ushort)(HaloModule.Capacitor | HaloModule.FocusLens)) == (ushort)(HaloModule.Capacitor | HaloModule.FocusLens), "After-preview omitted a module already installed in another slot.");
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
            Pass("真实 StashScreen UXML 终端正文的样式继承与改装操作可见；仅静态模板，不代表完整导航操作");
            CheckPreviewRelease(); CheckRendererAndGeometry(); BuildComparisonSheet("rifle");
        });
        Step(() =>
        {
            CheckComparisonSheet(); Capture("halo-rifle-components-native.png");
            Pass("真实步枪预制体原样、全部兼容单模块和三槽组合对照"); BuildComparisonSheet("shotgun");
        });
        Step(() =>
        {
            CheckComparisonSheet(); Capture("halo-shotgun-components-native.png");
            Pass("真实霰弹预制体原样、全部兼容单模块和两种三槽组合对照");
            Require(PreviewStageCount() == 0, "Comparison sheet retained a live preview stage."); BuildHeadwearSheet("rifle");
        });
        Step(() =>
        {
            CheckComparisonSheet(); Capture("halo-rifle-headwear-native.png");
            Pass("真实角色头顶佩戴：同一正面俯视角下原样、单模块和三槽满配"); BuildHeadwearSheet("shotgun");
        });
        Step(() =>
        {
            CheckComparisonSheet(); Capture("halo-shotgun-headwear-native.png");
            Pass("真实霰弹光环头顶佩戴三联对照"); Finish(null);
        });
        m_Ready = EditorApplication.timeSinceStartup + 1;
        m_Deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += Tick;
    }

    private HaloWorkshopPreview WorkshopPreview()
    {
        var field = typeof(ContainerInventoryView).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.FieldType == typeof(HaloWorkshopPreview));
        var preview = field?.GetValue(m_View) as HaloWorkshopPreview;
        Require(preview != null, "The real inventory presenter did not create an appearance renderer.");
        preview.Step(0);
        var image = m_Host.Q<Image>("workshopHaloPreview");
        Require(preview.Available && image != null && preview.Element == image && image.image == preview.Target && preview.Target && preview.Target.IsCreated(),
            "Workshop appearance is not backed by the real renderer's live RenderTexture.");
        return preview;
    }
    private static int PreviewStageCount() => Resources.FindObjectsOfTypeAll<Camera>().Count(camera => camera && camera.gameObject.scene.IsValid() &&
        camera.gameObject.scene.name.StartsWith("Moonkov halo workshop preview ", StringComparison.Ordinal));

    private void CheckInteractivePreview()
    {
        string snapshot = Snapshot(); int requests = m_Requests;
        var preview = WorkshopPreview();
        Require(preview.WeaponCode == "shotgun" && (preview.Modules & (ushort)HaloModule.Capacitor) != 0, "Default appearance is not the selected module's after-preview.");
        ulong after = TargetHash(preview.Target);
        Submit(Button("workshopPreviewCurrent")); preview = WorkshopPreview();
        Require(preview.Modules == 0, "Before-preview includes an uninstalled module.");
        ulong current = TargetHash(preview.Target);
        Require(current != after, "Before and after renders are identical despite the capacitor geometry.");
        Submit(Button("workshopPreviewAfter")); preview = WorkshopPreview();
        Require((preview.Modules & (ushort)HaloModule.Capacitor) != 0 && TargetHash(preview.Target) != current, "After-preview did not restore the real modified appearance.");
        Submit(Module("halo_charge_bridge")); preview = WorkshopPreview();
        Require((preview.Modules & (ushort)HaloModule.ChargeBridge) != 0 && TargetHash(preview.Target) != after, "Selecting another module did not change the real rendered geometry.");
        ScrollPreview(preview.Element, -4); preview.Step(0); float zoom = preview.Zoom;
        Require(zoom > 1 && zoom <= 2, "Native scroll did not zoom within the inspection range.");
        Submit(Button("workshopPreviewCurrent")); preview = WorkshopPreview();
        Require(Mathf.Approximately(preview.Zoom, zoom) && preview.Modules == 0, "Before-preview changed the inspection framing or used the wrong actual mask.");
        Submit(Button("workshopPreviewAfter")); preview = WorkshopPreview();
        Require(Mathf.Approximately(preview.Zoom, zoom), "Before/after comparison changed inspection framing.");
        Submit(Module("halo_capacitor")); preview = WorkshopPreview();
        Require(Mathf.Approximately(preview.Zoom, zoom), "Changing module reset the inspection zoom.");
        ScrollPreview(preview.Element, 40); preview.Step(0); Require(Mathf.Approximately(preview.Zoom, 1), "Inspection zoom did not return to its default lower bound.");
        Require(Snapshot() == snapshot && m_Requests == requests, "Appearance comparison submitted an inventory transaction.");
        Submit(Module("halo_capacitor"));
        Pass("真实 RenderTexture 外观随模块和改装前后切换，物品树不变");
        Pass("原生滚轮缩放限制在1至2倍，切换模块和改装前后保持同一取景");
    }
    private static void ScrollPreview(VisualElement target, float delta)
    {
        using (var evt = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = target.worldBound.center, delta = new Vector2(0, delta) })) target.SendEvent(evt);
    }
    private void CheckPreviewPlayback()
    {
        Submit(Module("halo_charge_bridge"));
        var preview = WorkshopPreview(); string snapshot = Snapshot(); int requests = m_Requests;
        Submit(Button("workshopPreviewCharge")); preview.Step(.15f); ulong chargeA = TargetHash(preview.Target);
        preview.Step(.25f); Require(TargetHash(preview.Target) != chargeA, "Charging example does not advance its real rendered geometry.");
        preview.Step(4);
        Submit(Button("workshopPreviewShot")); preview.Step(.02f); ulong shotA = TargetHash(preview.Target);
        preview.Step(.13f); Require(TargetHash(preview.Target) != shotA, "Firing example does not advance its real rendered geometry.");
        preview.Step(1);
        Require(Snapshot() == snapshot && m_Requests == requests && m_Graph.Find(m_ShotgunId).LoadedAmmo == 4, "Appearance playback spent real ammunition or changed inventory.");
        Submit(Module("halo_capacitor"));
        Pass("充能和开火示例推进真实外观，不消耗实际弹量或发库存请求");
        CheckBurstPreview();
    }
    private void CheckBurstPreview()
    {
        string snapshot = Snapshot(); int requests = m_Requests;
        var item = m_Graph.Find(m_RifleId); int savedAmmo = item.LoadedAmmo, savedVersion = m_Graph.Version;
        float interval = m_Registry.GetWeaponData(DollSingerWeapons.Halo, (ushort)HaloModule.PulseController).CooldownInMs;
        try
        {
            m_View.SelectWorkshopWeapon(m_RifleId); Submit(Button("workshopSlotShaping")); Submit(Module("halo_pulse_controller"));
            var preview = WorkshopPreview();
            int VisibleAmmo() => preview.DisplayScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RifleHaloVisual>(true)).Single().VisibleAmmo;
            Require(preview.CanPlayShot && Button("workshopPreviewShot").enabledInHierarchy, "Loaded pulse preview cannot fire.");
            Submit(Button("workshopPreviewShot")); Require(VisibleAmmo() == savedAmmo - 1, "Pulse example did not release its first round.");
            preview.Step(interval * .5f); Require(VisibleAmmo() == savedAmmo - 1, "Pulse example released a second round before its weapon interval.");
            preview.Step(interval * .55f); Require(VisibleAmmo() == savedAmmo - 2, "Pulse example did not release its second round.");
            preview.Step(interval); Require(VisibleAmmo() == savedAmmo - 3, "Pulse example did not release its third round.");
            preview.Step(1); Require(VisibleAmmo() == savedAmmo && Snapshot() == snapshot && m_Requests == requests, "Pulse example did not reset or spent inventory ammunition.");
            item.LoadedAmmo = 2; m_Graph.Version++; Present(); preview = WorkshopPreview(); string twoRounds = Snapshot();
            Submit(Button("workshopPreviewShot")); Require(VisibleAmmo() == 1, "Two-round pulse example did not release the first round.");
            preview.Step(interval * 1.05f); Require(VisibleAmmo() == 0, "Two-round pulse example did not release the second round.");
            preview.Step(interval); Require(VisibleAmmo() == 0, "Two-round pulse example released an unavailable third round.");
            preview.Step(1); Require(VisibleAmmo() == 2 && Snapshot() == twoRounds && m_Requests == requests, "Two-round example did not reset or changed its fixture.");
            item.LoadedAmmo = 0; m_Graph.Version++; Present(); preview = WorkshopPreview(); string empty = Snapshot();
            Require(!preview.CanPlayShot && !Button("workshopPreviewShot").enabledInHierarchy, "Empty halo exposes an enabled firing example.");
            preview.PlayShot(interval); preview.Step(1);
            Require(VisibleAmmo() == 0 && Snapshot() == empty && m_Requests == requests, "Empty firing example created ammunition or changed inventory.");
        }
        finally
        {
            item.LoadedAmmo = savedAmmo; m_Graph.Version = savedVersion; Present(); m_View.SelectWorkshopWeapon(m_ShotgunId);
            Submit(Button("workshopSlotStorage")); Submit(Module("halo_capacitor"));
        }
        Require(Snapshot() == snapshot && m_Requests == requests, "Pulse review did not restore its isolated inventory fixture.");
        Pass("连发示例按武器间隔依次显示三发，余弹两发时只显示两发，结束复位；空弹禁用开火且不改库存");
    }
    private void CheckPreviewReopening()
    {
        var preview = WorkshopPreview(); int stages = PreviewStageCount(); string snapshot = Snapshot();
        Require(stages == 1, "The workshop must own exactly one isolated appearance stage, found " + stages);
        for (int i = 0; i < 6; i++)
        {
            Submit(Module(i % 2 == 0 ? "halo_charge_bridge" : "halo_capacitor")); WorkshopPreview();
            Require(PreviewStageCount() == stages, "Changing appearance leaked an active preview stage.");
        }
        for (int i = 0; i < 3; i++)
        {
            preview = WorkshopPreview(); var target = preview.Target; var scene = preview.DisplayScene;
            m_View.AttachWorkshop(m_Host); WorkshopPreview();
            RequireReleased(scene, target, "Reopening workshop");
            Require(PreviewStageCount() == stages, "Reopening appearance preview accumulated stages.");
        }
        Require(Snapshot() == snapshot, "Reopening appearance preview changed the inventory.");
        Pass("反复切换和重开保持一个预览场景，旧场景和纹理已释放");
    }
    private void CheckPreviewRelease()
    {
        var preview = WorkshopPreview(); var scene = preview.DisplayScene; var target = preview.Target;
        string snapshot = Snapshot();
        m_View.Suspend(); RequireReleased(scene, target, "Leaving workshop");
        Require(PreviewStageCount() == 0, "Leaving the workshop retained an active appearance stage.");
        m_View.AttachWorkshop(m_Host); preview = WorkshopPreview(); scene = preview.DisplayScene; target = preview.Target;
        m_View.Dispose(); m_View = null;
        RequireReleased(scene, target, "Disposing inventory presenter");
        Require(PreviewStageCount() == 0 && Snapshot() == snapshot, "Presenter disposal retained an appearance stage or changed inventory.");
        Pass("离开页面和释放 presenter 均销毁预览场景和 RenderTexture");
    }
    private static void RequireReleased(Scene scene, RenderTexture target, string operation)
    {
        Require(!scene.IsValid() || !scene.isLoaded, operation + " retained its private scene.");
        Require(!target || !target.IsCreated(), operation + " retained its private RenderTexture.");
    }
    private void CheckRendererAndGeometry()
    {
        string snapshot = Snapshot(); var preview = new HaloWorkshopPreview(); Scene scene = default; RenderTexture target = null;
        try
        {
            var rifle = m_Registry.GetWeaponData(DollSingerWeapons.Halo);
            preview.Present("rifle", 0, rifle.MagazineSize, rifle.MagazineSize); preview.Step(0);
            Require(preview.Available && preview.Target && preview.Target.IsCreated(), "Direct real-prefab renderer is unavailable.");
            ulong original = TargetHash(preview.Target);
            var capacitor = m_Registry.GetWeaponData(DollSingerWeapons.Halo, (ushort)HaloModule.Capacitor);
            preview.Present("rifle", (ushort)HaloModule.Capacitor, capacitor.MagazineSize, rifle.MagazineSize); preview.Step(0);
            Require(TargetHash(preview.Target) != original, "Rifle module renderer produced the unchanged original image.");
            var shotgun = m_Registry.GetWeaponData(DollSingerWeapons.Shotgun);
            preview.Present("shotgun", (ushort)HaloModule.MooncoreRecycler, shotgun.MagazineSize, shotgun.MagazineSize); preview.Step(0);
            Require(preview.WeaponCode == "shotgun" && preview.Modules == (ushort)HaloModule.MooncoreRecycler && PreviewStageCount() == 1,
                "Changing the direct renderer's weapon retained an old stage or wrong profile.");
            scene = preview.DisplayScene; target = preview.Target;
        }
        finally { preview.Dispose(); }
        RequireReleased(scene, target, "Disposing direct renderer");
        Require(PreviewStageCount() == 0 && Snapshot() == snapshot, "Direct renderer changed inventory or retained its stage.");
        Pass("离屏真预制体渲染和武器切换有效，释放后没有预览场景");
        CheckModuleArtwork();

        var geometryScene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Halo module geometry review") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, geometryScene);
        try
        {
            var settings = Resources.Load<MenuCharacterSettings>(GameSettings.MenuCharacterSettingsPath(GameSettings.DollSingerCharacterIndex));
            Require(settings && settings.RifleHaloPreviewPrefab && settings.ShotgunHaloPreviewPrefab, "Actual halo source references are missing.");
            foreach (string weaponCode in new[] { "rifle", "shotgun" })
            {
                var source = weaponCode == "rifle" ? settings.RifleHaloPreviewPrefab : settings.ShotgunHaloPreviewPrefab;
                var model = Instantiate(source, root.transform, false); model.SetActive(false);
                foreach (var behaviour in model.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                var originalRenderers = model.GetComponentsInChildren<Renderer>(true);
                Require(originalRenderers.Length > 0, "Original halo has no body renderers: " + weaponCode);
                var rifleBody = model.GetComponent<RifleHaloVisual>(); var shotgunBody = model.GetComponent<ShotgunHaloVisual>();
                if (rifleBody) { var tint = rifleBody.lightColor; tint.a = .63f; rifleBody.lightColor = tint; }
                if (shotgunBody) { var tint = shotgunBody.lightColor; tint.a = .63f; shotgunBody.lightColor = tint; }
                rifleBody?.SetAimBlend(1); shotgunBody?.SetAimBlend(1);
                var moduleObject = new GameObject("Actual module geometry review"); moduleObject.transform.SetParent(model.transform, false);
                var visual = moduleObject.AddComponent<HaloModuleVisual>(); visual.SetAimBlend(1);
                visual.Configure(weaponCode == "rifle");
                var bounds = new List<(string Code, string Slot, Bounds Bounds)>();
                foreach (string code in ReviewModuleCodes.Where(code => HaloModules.Compatible(weaponCode, code)))
                {
                    ushort mask = (ushort)HaloModules.FromCode(code);
                    var weapon = m_Registry.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode), mask);
                    rifleBody?.SetMagazineCapacity(weapon.MagazineSize); rifleBody?.SetLocalState(weapon.MagazineSize, false, 0, weapon.MagazineSize); rifleBody?.Step(0);
                    shotgunBody?.SetModuleProfile(mask, weapon.MagazineSize); shotgunBody?.SetLocalState(weapon.MagazineSize, false, 0); shotgunBody?.Step(1);
                    visual.SetState(mask, weapon.MagazineSize, weapon.MagazineSize, false, 0, weapon.MagazineSize, 0); visual.Step(0);
                    var sprites = ActiveModuleSprites(moduleObject.transform);
                    Require(sprites.Length == 2 && sprites.All(sprite => sprite.sprite == HaloModuleVisual.Artwork((HaloModule)mask)), "Single module must display one pair of its actual Sprite: " + code);
                    CheckMirrorPair(sprites, model.transform, code);
                    CheckBodyDominance(originalRenderers, model.transform, sprites, code);
                    foreach (var sprite in sprites)
                    {
                        var box = SpriteBounds(sprite, model.transform);
                        CheckCenterClear(box, code);
                        bounds.Add((code, HaloModules.Slot(HaloModules.FromCode(code)), box));
                        m_Evidence.Add("SPRITE: " + weaponCode + "/" + code + "; actual local XY corners bounds=" + box.ToString("F3"));
                    }
                }
                for (int i = 0; i < bounds.Count; i++) for (int j = i + 1; j < bounds.Count; j++)
                {
                    var a = bounds[i]; var b = bounds[j]; if (a.Slot == b.Slot) continue;
                    Require(!OverlapXY(a.Bounds, b.Bounds), "Different physical slots overlap: " + weaponCode + "/" + a.Code + " + " + b.Code);
                }
                HaloModule fullMask = weaponCode == "rifle" ? HaloModule.Capacitor | HaloModule.PulseController | HaloModule.HitMarker :
                    HaloModule.MooncoreRecycler | HaloModule.FanArray | HaloModule.HitMarker;
                var full = m_Registry.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode), (ushort)fullMask);
                rifleBody?.SetMagazineCapacity(full.MagazineSize); rifleBody?.SetLocalState(full.MagazineSize, false, 0, full.MagazineSize); rifleBody?.Step(0);
                shotgunBody?.SetModuleProfile((ushort)fullMask, full.MagazineSize); shotgunBody?.SetLocalState(full.MagazineSize, false, 0); shotgunBody?.Step(1);
                visual.SetState((ushort)fullMask, full.MagazineSize, full.MagazineSize, false, 0, full.MagazineSize, 0); visual.Step(0);
                var fullSprites = ActiveModuleSprites(moduleObject.transform);
                Require(fullSprites.Length == 6, "Three-slot configuration must display exactly three mirrored pairs: " + weaponCode);
                foreach (var pair in fullSprites.GroupBy(sprite => sprite.sprite)) CheckMirrorPair(pair.ToArray(), model.transform, weaponCode + "/" + pair.Key.name);
                CheckBodyDominance(originalRenderers, model.transform, fullSprites, weaponCode + " full configuration");
                var fullBounds = fullSprites.Select(sprite => SpriteBounds(sprite, model.transform)).ToArray();
                for (int i = 0; i < fullBounds.Length; i++)
                {
                    CheckCenterClear(fullBounds[i], fullSprites[i].sprite.name);
                    for (int j = i + 1; j < fullBounds.Length; j++) Require(!OverlapXY(fullBounds[i], fullBounds[j]), "Full configuration Sprite rectangles overlap: " + weaponCode);
                }
                // Charge and release must keep both complete drawings mirrored, including asymmetric notches.
                visual.SetState((ushort)fullMask, 0, full.MagazineSize, true, .5f, full.MagazineSize, 1); visual.Step(.05f);
                foreach (var pair in ActiveModuleSprites(moduleObject.transform).GroupBy(sprite => sprite.sprite)) CheckMirrorPair(pair.ToArray(), model.transform, weaponCode + " charging");
                visual.SetState((ushort)fullMask, full.MagazineSize - 1, full.MagazineSize, false, 0, full.MagazineSize, 2, 2); visual.Step(.03f);
                foreach (var pair in ActiveModuleSprites(moduleObject.transform).GroupBy(sprite => sprite.sprite)) CheckMirrorPair(pair.ToArray(), model.transform, weaponCode + " release");
                ushort conflicting = (ushort)ReviewModuleCodes.Aggregate(HaloModule.None, (mask, code) => mask | HaloModules.FromCode(code));
                visual.SetState(conflicting, full.MagazineSize, full.MagazineSize, false, 0, full.MagazineSize, 2); visual.Step(0);
                var exclusive = ActiveModuleSprites(moduleObject.transform);
                Require(exclusive.Length == 6 && exclusive.GroupBy(sprite => sprite.sprite).Count() == 3, "Conflicting slot bits displayed extra pairs: " + weaponCode);
                visual.SetState(0, full.MagazineSize, full.MagazineSize, false, 0, full.MagazineSize, 2); visual.Step(0);
                Require(ActiveModuleSprites(moduleObject.transform).Length == 0, "Original halo still displays physical modules with an empty mask.");
                DestroyImmediate(model);
            }
        }
        finally { DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(geometryScene); }
        Pass("单模块显示左右镜像一对，满配恰好三对；充能和开火保持对称，跨槽不重叠，中心留白且原光环占主体");
    }

    private void CheckModuleArtwork()
    {
        var sprites = Resources.LoadAll<Sprite>("Moonkov/HaloModules/ModuleArtwork");
        Require(sprites.Length == ReviewModuleCodes.Length && sprites.Select(sprite => sprite.name).Distinct().Count() == ReviewModuleCodes.Length,
            "The artwork atlas must expose exactly seven distinct module Sprites.");
        var material = Resources.Load<Material>("Moonkov/HaloModules/ModuleArtwork");
        Require(material && material.shader && material.shader.name == "DollSinger/RifleHaloInk" && Mathf.Approximately(material.GetFloat("_Gain"), 1f),
            "Module artwork must use the shared flat ink material without HDR gain.");
        var sources = new Dictionary<string, Texture2D>();
        var slotColors = new Dictionary<string, Color>();
        try
        {
            foreach (string code in ReviewModuleCodes)
            {
                var module = HaloModules.FromCode(code); string slot = HaloModules.Slot(module);
                var tint = HaloModuleVisual.ArtworkColor(module);
                if (slotColors.TryGetValue(slot, out var previousTint)) Require(tint == previousTint, "Shapes in one slot use different palette colors: " + code);
                else slotColors.Add(slot, tint);
                var sprite = HaloModuleVisual.Artwork(module);
                Require(sprite && sprite.name == code && sprites.Contains(sprite), "Missing or mismatched drawn module Sprite: " + code);
                string path = AssetDatabase.GetAssetPath(sprite.texture);
                Require(Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path), "Module Sprite source is not the real PNG: " + code);
                if (!sources.TryGetValue(path, out var image))
                {
                    image = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    sources.Add(path, image);
                    Require(ImageConversion.LoadImage(image, File.ReadAllBytes(path)), "Cannot decode module artwork PNG.");
                }
                var pixels = image.GetPixels32(); var rect = sprite.rect;
                float scaleX = (float)image.width / sprite.texture.width, scaleY = (float)image.height / sprite.texture.height;
                int minX = Mathf.Clamp(Mathf.FloorToInt(rect.xMin * scaleX), 0, image.width - 1), maxX = Mathf.Clamp(Mathf.CeilToInt(rect.xMax * scaleX), minX + 1, image.width);
                int minY = Mathf.Clamp(Mathf.FloorToInt(rect.yMin * scaleY), 0, image.height - 1), maxY = Mathf.Clamp(Mathf.CeilToInt(rect.yMax * scaleY), minY + 1, image.height);
                int total = (maxX - minX) * (maxY - minY), solid = 0, clear = 0;
                for (int y = minY; y < maxY; y++) for (int x = minX; x < maxX; x++)
                {
                    var pixel = pixels[y * image.width + x]; byte alpha = pixel.a;
                    Require(alpha <= 16 || pixel.r >= 250 && pixel.g >= 250 && pixel.b >= 250,
                        "Module source must be pure white for its slot palette tint: " + code);
                    if (alpha >= 192) solid++;
                    if (alpha <= 16) clear++;
                }
                Require(total > 0 && solid >= total * .12f && clear >= total * .02f,
                    "Artwork needs solid graphic mass and real transparent cutouts, rather than a thin-line placeholder: " + code);
                m_Evidence.Add("ARTWORK: " + code + "; source pixels=" + total + "; solid alpha=" + ((float)solid / total).ToString("P0", CultureInfo.InvariantCulture) +
                    "; transparent=" + ((float)clear / total).ToString("P0", CultureInfo.InvariantCulture));
            }
        }
        finally { foreach (var image in sources.Values) if (image) DestroyImmediate(image); }
        Pass("七件纯白几何 Sprite 名称齐全，有实色体块和透明留白；共用低增益纯色材质");
    }
    private static SpriteRenderer[] ActiveModuleSprites(Transform root) => root.GetComponentsInChildren<SpriteRenderer>(true)
        .Where(renderer => renderer.enabled && renderer.sprite && ActiveBelow(renderer.transform, root)).ToArray();
    private static bool ActiveBelow(Transform element, Transform root)
    {
        for (var current = element; current != null; current = current.parent)
        {
            if (current == root) return true;
            if (!current.gameObject.activeSelf) return false;
        }
        return false;
    }
    private static Bounds SpriteBounds(SpriteRenderer renderer, Transform reference)
    {
        Require(renderer && renderer.sprite, "Cannot measure missing Sprite artwork.");
        var sprite = renderer.sprite.bounds;
        Vector3 Corner(float x, float y) => reference.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(x, y, sprite.center.z)));
        var bounds = new Bounds(Corner(sprite.min.x, sprite.min.y), Vector3.zero);
        bounds.Encapsulate(Corner(sprite.min.x, sprite.max.y)); bounds.Encapsulate(Corner(sprite.max.x, sprite.min.y)); bounds.Encapsulate(Corner(sprite.max.x, sprite.max.y));
        return bounds;
    }
    private static void CheckMirrorPair(SpriteRenderer[] pair, Transform reference, string context)
    {
        Require(pair.Length == 2 && pair[0].sprite == pair[1].sprite, "A module is not one complete mirrored pair: " + context);
        Require(pair[0].sharedMaterial == pair[1].sharedMaterial && pair[0].sharedMaterial &&
            pair[0].sharedMaterial.shader.name == "DollSinger/RifleHaloInk" && pair[0].color == pair[1].color,
            "Pair tint or flat material differs: " + context);
        var expected = HaloModuleVisual.ArtworkColor(HaloModules.FromCode(pair[0].sprite.name)); var actual = pair[0].color;
        float brightness = Mathf.Max(actual.r, actual.g, actual.b) / Mathf.Max(expected.r, expected.g, expected.b);
        Require(brightness > 0 && Mathf.Abs(actual.r - expected.r * brightness) < .002f &&
            Mathf.Abs(actual.g - expected.g * brightness) < .002f && Mathf.Abs(actual.b - expected.b * brightness) < .002f,
            "Rendered module differs from its slot palette color: " + context);
        var rifle = reference.GetComponent<RifleHaloVisual>(); var shotgun = reference.GetComponent<ShotgunHaloVisual>();
        float parentAlpha = rifle ? rifle.lightColor.a : shotgun ? shotgun.lightColor.a : 1f;
        Require(Mathf.Approximately(actual.a, parentAlpha), "Module did not preserve its parent halo alpha: " + context);
        var vertices = pair[0].sprite.vertices;
        foreach (var vertex in vertices)
        {
            Vector3 Point(SpriteRenderer renderer) => reference.InverseTransformPoint(renderer.transform.TransformPoint(
                new Vector3(renderer.flipX ? -vertex.x : vertex.x, renderer.flipY ? -vertex.y : vertex.y, 0)));
            var a = Point(pair[0]); var b = Point(pair[1]);
            Require(Vector3.Distance(new Vector3(-a.x, a.y, a.z), b) < .0001f,
                "Whole Sprite geometry, including its notch, is not mirrored across the headwear centerline: " + context);
        }
        Require(!OverlapXY(SpriteBounds(pair[0], reference), SpriteBounds(pair[1], reference)), "A module's mirrored pair overlaps: " + context);
    }
    private static bool OverlapXY(Bounds a, Bounds b) => Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > .0001f &&
        Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > .0001f;
    private static void CheckCenterClear(Bounds bounds, string code)
    {
        float x = Mathf.Clamp(0, bounds.min.x, bounds.max.x), y = Mathf.Clamp(0, bounds.min.y, bounds.max.y);
        Require(x * x + y * y > .01f, "Module Sprite rectangle covers the central sight: " + code);
    }
    private static void CheckBodyDominance(Renderer[] original, Transform model, SpriteRenderer[] modules, string context)
    {
        Require(original.Length > 0 && original.All(renderer => renderer && renderer.transform.IsChildOf(model)), "Module update removed an original halo renderer: " + context);
        var bodySprites = original.OfType<SpriteRenderer>().Where(renderer => renderer.sprite).ToArray();
        Require(bodySprites.Length > 0, "Original halo Sprite artwork is missing: " + context);
        var body = SpriteBounds(bodySprites[0], model);
        foreach (var renderer in bodySprites.Skip(1)) body.Encapsulate(SpriteBounds(renderer, model));
        float bodyArea = body.size.x * body.size.y, moduleArea = 0;
        Require(bodyArea > .01f, "Original halo body has no meaningful visible range: " + context);
        foreach (var renderer in modules)
        {
            var bounds = SpriteBounds(renderer, model); float width = bounds.size.x / body.size.x, height = bounds.size.y / body.size.y;
            Require(Mathf.Max(width, height) >= .075f && width < .22f && height < .22f && width * height >= .002f,
                "Drawn module must have readable graphic mass while remaining a small body accent: " + context + "/" + renderer.sprite.name);
            moduleArea += bounds.size.x * bounds.size.y;
        }
        Require(moduleArea < bodyArea * .2f, "Module rectangles dominate the original halo body: " + context);
        var combined = body;
        foreach (var renderer in modules) combined.Encapsulate(SpriteBounds(renderer, model));
        Require(combined.size.x <= body.size.x * 1.30f && combined.size.y <= body.size.y * 1.30f,
            "The paired accents enlarged the original halo silhouette too much: " + context);
    }

    private void BuildComparisonSheet(string weaponCode)
    {
        ClearComparisonTextures();
        var masks = new List<ushort> { 0 };
        var labels = new List<string> { "原样" };
        foreach (string code in ReviewModuleCodes.Where(code => HaloModules.Compatible(weaponCode, code)))
        { masks.Add((ushort)HaloModules.FromCode(code)); labels.Add(MoonkovLocalization.Text(InventoryCatalog.Get(code).Name)); }
        HaloModule[] combinations = weaponCode == "shotgun"
            ? new[] { HaloModule.Capacitor | HaloModule.FocusLens | HaloModule.HitMarker, HaloModule.MooncoreRecycler | HaloModule.FanArray | HaloModule.HitMarker }
            : new[] { HaloModule.Capacitor | HaloModule.PulseController | HaloModule.HitMarker };
        foreach (var mask in combinations)
        {
            masks.Add((ushort)mask);
            labels.Add(string.Join(" + ", ReviewModuleCodes.Where(code => (mask & HaloModules.FromCode(code)) != 0).Select(code => MoonkovLocalization.Text(InventoryCatalog.Get(code).Name))));
        }
        string snapshot = Snapshot(); var preview = new HaloWorkshopPreview(); Scene scene = default; RenderTexture target = null;
        ulong original = 0;
        try
        {
            var standard = m_Registry.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode));
            for (int i = 0; i < masks.Count; i++)
            {
                var data = m_Registry.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode), masks[i]);
                preview.Present(weaponCode, masks[i], data.MagazineSize, standard.MagazineSize); preview.Step(0);
                Require(preview.Available && preview.Target && preview.Target.IsCreated(), "Actual prefab gallery renderer is unavailable: " + weaponCode);
                var texture = ReadTarget(preview.Target); m_ComparisonTextures.Add(texture);
                ulong hash = TextureHash(texture); if (i == 0) original = hash;
                else Require(hash != original, "Module gallery frame is identical to the original: " + weaponCode + "/" + labels[i]);
                scene = preview.DisplayScene; target = preview.Target;
            }
        }
        finally { preview.Dispose(); }
        RequireReleased(scene, target, "Building comparison sheet");
        Require(PreviewStageCount() == 0 && Snapshot() == snapshot, "Appearance comparison wrote inventory or retained its render stage.");
        BuildComparisonLayout((weaponCode == "rifle" ? "步枪光环" : "霰弹光环") + " / 原样与改装", labels);
    }
    private void BuildHeadwearSheet(string weaponCode)
    {
        string snapshot = Snapshot(); ClearComparisonTextures();
        m_ComparisonTextures.AddRange(RenderHeadwearFrames(m_Registry, weaponCode));
        Require(Snapshot() == snapshot, "Headwear comparison changed the inventory.");
        BuildComparisonLayout((weaponCode == "rifle" ? "步枪光环" : "霰弹光环") + " / 真实角色头顶佩戴", new[] { "原样", "单模块", "三槽装满" });
    }
    private void BuildComparisonLayout(string title, IList<string> labels)
    {
        var root = rootVisualElement; root.Clear(); root.ClearClassList(); root.styleSheets.Clear();
        var heading = new Label(title);
        heading.style.fontSize = 18; heading.style.color = new Color(.93f, .94f, .95f); heading.style.marginLeft = 14; heading.style.marginTop = 8; heading.style.marginBottom = 8; root.Add(heading);
        int rows = Mathf.CeilToInt(labels.Count / 3f);
        var grid = new VisualElement { name = "appearanceComparisonGrid" }; grid.style.flexGrow = 1; grid.style.minHeight = 0; root.Add(grid);
        for (int rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexGrow = 1; row.style.flexBasis = 0; row.style.minHeight = 0; grid.Add(row);
            for (int column = 0; column < 3; column++)
            {
                int index = rowIndex * 3 + column;
                var card = new VisualElement(); card.style.flexGrow = 1; card.style.flexBasis = 0; card.style.minWidth = 0; card.style.minHeight = 0;
                card.style.marginLeft = 6; card.style.marginRight = 6; card.style.marginBottom = 6; card.style.paddingLeft = 8; card.style.paddingRight = 8;
                card.style.backgroundColor = new Color(.094f, .098f, .106f); row.Add(card);
                if (index >= labels.Count) { card.style.backgroundColor = Color.clear; continue; }
                var image = new Image { name = "comparisonImage" + index, image = m_ComparisonTextures[index], scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.flexGrow = 1; image.style.flexBasis = 0; image.style.minHeight = 0; card.Add(image);
                var label = new Label(labels[index]) { name = "comparisonLabel" + index }; label.style.fontSize = 12; label.style.color = new Color(.93f, .94f, .95f);
                label.style.whiteSpace = WhiteSpace.Normal; label.style.unityTextAlign = TextAnchor.MiddleCenter; label.style.minHeight = 34; label.style.flexShrink = 0; card.Add(label);
            }
        }
        var notice = new Label("原生预制体 / 本地显示示例 / 不写入玩家库存"); notice.style.fontSize = 12; notice.style.color = new Color(.69f, .71f, .73f); notice.style.marginLeft = 14; notice.style.marginBottom = 4; root.Add(notice);
        ApplyFont();
    }
    [MenuItem("Tools/Moonkov/Capture Halo Headwear")]
    public static void CaptureHeadwear()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Headwear capture requires idle Edit Mode.");
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        Require(registry, "Real weapon registry is missing."); Directory.CreateDirectory(Output);
        foreach (string code in new[] { "rifle", "shotgun" })
        {
            var frames = RenderHeadwearFrames(registry, code); Texture2D sheet = null;
            try
            {
                sheet = new Texture2D(frames[0].width * 3, frames[0].height, TextureFormat.RGBA32, false);
                for (int i = 0; i < frames.Length; i++)
                {
                    File.WriteAllBytes(Path.Combine(Output, "halo-" + code + "-headwear-" + i + ".png"), frames[i].EncodeToPNG());
                    sheet.SetPixels(i * frames[i].width, 0, frames[i].width, frames[i].height, frames[i].GetPixels());
                }
                sheet.Apply(); File.WriteAllBytes(Path.Combine(Output, "halo-" + code + "-headwear-triptych.png"), sheet.EncodeToPNG());
            }
            finally { foreach (var frame in frames) if (frame) DestroyImmediate(frame); if (sheet) DestroyImmediate(sheet); }
        }
        Debug.Log("Actual halo headwear captures (original / single / full): " + Output);
    }
    private static Texture2D[] RenderHeadwearFrames(WeaponRegistry registry, string weaponCode)
    {
        var settings = Resources.Load<MenuCharacterSettings>(GameSettings.MenuCharacterSettingsPath(GameSettings.DollSingerCharacterIndex));
        Require(settings && settings.VisualPrefab, "Actual character reference is missing.");
        var source = weaponCode == "rifle" ? settings.RifleHaloPreviewPrefab : settings.ShotgunHaloPreviewPrefab;
        Require(source, "Actual halo prefab is missing: " + weaponCode);
        var frames = new List<Texture2D>();
        var portrait = new MenuCharacterView(idleOnly: true, characterIndex: GameSettings.DollSingerCharacterIndex);
        Scene scene = default; RenderTexture target = null;
        try
        {
            // Reuse the existing character presentation's animation, materials and lights.
            portrait.Advance(0); scene = portrait.DisplayScene;
            Require(scene.IsValid() && scene.isLoaded, "Actual character stage was not created.");
            var stage = scene.GetRootGameObjects().Single(root => root.name == "Menu character render stage");
            var model = stage.transform.Cast<Transform>().FirstOrDefault(child => child.GetComponentInChildren<Animator>(true));
            Require(model, "Actual character model is absent from its stage."); model.rotation = Quaternion.identity;
            var animator = model.GetComponentInChildren<Animator>(true); var head = animator.GetBoneTransform(HumanBodyBones.Head);
            Require(head, "Actual character has no humanoid head.");
            foreach (var child in model.GetComponentsInChildren<Transform>(true)) if (child.name == "HaloCrosshair") child.gameObject.SetActive(false);
            var halo = Instantiate(source, stage.transform, false); halo.name = "Headwear comparison actual halo";
            foreach (var child in halo.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            foreach (var behaviour in halo.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            // These are the actual idle head anchor and plane used by DollSingerHaloAim.
            halo.transform.SetPositionAndRotation(head.position + Vector3.up * DollSingerHaloAim.IdleHeadHeight, Quaternion.LookRotation(Vector3.up, Vector3.forward));
            halo.transform.localScale = Vector3.one * .72f;
            var rifle = halo.GetComponent<RifleHaloVisual>(); var shotgun = halo.GetComponent<ShotgunHaloVisual>();
            if (rifle) { rifle.lightColor = settings.IdleHaloColor; rifle.SetAimBlend(0); }
            if (shotgun) { shotgun.lightColor = settings.IdleHaloColor; shotgun.SetAimBlend(0); }
            var layer = new GameObject("Headwear comparison module pairs"); layer.layer = 5; layer.transform.SetParent(halo.transform, false);
            var modules = layer.AddComponent<HaloModuleVisual>(); modules.enabled = false; modules.Configure(weaponCode == "rifle"); modules.SetAimBlend(0);
            var cameraObject = new GameObject("Headwear comparison camera"); cameraObject.transform.SetParent(stage.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .43f;
            camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.067f, .071f, .078f);
            camera.nearClipPlane = .01f; camera.farClipPlane = 10; camera.scene = scene;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            var focus = head.position + Vector3.up * .08f;
            camera.transform.position = focus + new Vector3(0, .65f, 1.65f); camera.transform.LookAt(focus);
            target = new RenderTexture(512, 576, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            target.Create(); camera.aspect = (float)target.width / target.height;
            var masks = new[] { HaloModule.None, HaloModule.Capacitor, weaponCode == "rifle" ?
                HaloModule.Capacitor | HaloModule.PulseController | HaloModule.HitMarker : HaloModule.Capacitor | HaloModule.FanArray | HaloModule.HitMarker };
            ulong original = 0, previous = 0;
            for (int i = 0; i < masks.Length; i++)
            {
                var weapon = registry.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode), (ushort)masks[i]);
                if (rifle) { rifle.SetMagazineCapacity(weapon.MagazineSize); rifle.SetLocalState(weapon.MagazineSize, false, 0, weapon.MagazineSize); rifle.Step(0); }
                if (shotgun) { shotgun.SetModuleProfile((ushort)masks[i], weapon.MagazineSize); shotgun.SetLocalState(weapon.MagazineSize, false, 0); shotgun.Step(0); }
                modules.SetState((ushort)masks[i], weapon.MagazineSize, weapon.MagazineSize, false, 0, weapon.MagazineSize, 0); modules.Step(0);
                int expectedSprites = i == 0 ? 0 : i == 1 ? 2 : 6;
                Require(ActiveModuleSprites(layer.transform).Length == expectedSprites, "Headwear profile has the wrong visible pair count.");
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var frame = ReadTarget(target); frames.Add(frame); ulong hash = TextureHash(frame);
                if (i == 0) original = hash;
                else Require(hash != original && hash != previous, "Actual headwear capture did not change after installation: " + weaponCode);
                previous = hash;
            }
            return frames.ToArray();
        }
        catch { foreach (var frame in frames) if (frame) DestroyImmediate(frame); throw; }
        finally
        {
            portrait.Dispose();
            if (target) { target.Release(); DestroyImmediate(target); }
            Require(!scene.IsValid() || !scene.isLoaded, "Headwear capture retained its private character stage.");
        }
    }
    private void CheckComparisonSheet()
    {
        var bounds = VisibleBounds();
        foreach (var image in rootVisualElement.Query<Image>().ToList())
            Require(image.image is Texture2D && image.worldBound.width >= 100 && image.worldBound.height >= 100 && Contains(bounds, image.worldBound), "Comparison image is missing or clipped: " + image.name);
        foreach (var label in rootVisualElement.Query<Label>().ToList()) Require(Contains(bounds, label.worldBound), "Comparison label is clipped: " + label.name);
    }
    private static Texture2D ReadTarget(RenderTexture target)
    {
        Require(target && target.IsCreated(), "Cannot read an uncreated appearance RenderTexture.");
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image;
        }
        finally { RenderTexture.active = previous; }
    }
    private static ulong TextureHash(Texture2D texture)
    {
        ulong hash = 14695981039346656037UL;
        unchecked { foreach (var color in texture.GetPixels32()) { hash = (hash ^ color.r) * 1099511628211UL; hash = (hash ^ color.g) * 1099511628211UL; hash = (hash ^ color.b) * 1099511628211UL; hash = (hash ^ color.a) * 1099511628211UL; } }
        return hash;
    }
    private static ulong TargetHash(RenderTexture target)
    { var image = ReadTarget(target); try { return TextureHash(image); } finally { DestroyImmediate(image); } }
    private void ClearComparisonTextures()
    { foreach (var texture in m_ComparisonTextures) if (texture) DestroyImmediate(texture); m_ComparisonTextures.Clear(); }

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
            if (!PrepareNativeLayout()) return;
            if (m_Steps.Count > 0) m_Steps.Dequeue()();
            m_Ready = EditorApplication.timeSinceStartup + .45;
        }
        catch (Exception error) { Finish(error); }
    }
    private bool PrepareNativeLayout()
    {
        double now = EditorApplication.timeSinceStartup;
        if (m_LayoutDeadline == 0)
        {
            m_LayoutDeadline = now + 5;
            RecordLayout("layout preparation before");
        }
        if (now < m_NextLayoutProbe) return false;
        m_NextLayoutProbe = now + .2;
        var panel = rootVisualElement.panel;
        if (panel != null && FiniteRect(panel.visualTree.worldBound))
        {
            var bounds = panel.visualTree.worldBound;
            if (bounds.width > 1 && bounds.height > 1)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                float dpi = Convert.ToSingle(panel.GetType().GetProperty("scaledPixelsPerPoint", flags).GetValue(panel));
                int width = Mathf.RoundToInt(bounds.width * dpi), height = Mathf.RoundToInt(bounds.height * dpi);
                if (m_LayoutTarget == null || m_LayoutTarget.width != width || m_LayoutTarget.height != height)
                {
                    if (m_LayoutTarget) { m_LayoutTarget.Release(); DestroyImmediate(m_LayoutTarget); }
                    m_LayoutTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Require(m_LayoutTarget.Create(), "Could not create the native layout preparation target.");
                }
                var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
                try
                {
                    RenderTexture.active = m_LayoutTarget; GL.Viewport(new Rect(0, 0, width, height)); GL.sRGBWrite = false;
                    GL.Clear(true, true, Color.clear); GL.PushMatrix();
                    try
                    {
                        GL.LoadPixelMatrix(0, bounds.width, bounds.height, 0);
                        panel.GetType().GetMethod("Repaint", flags, null, new[] { typeof(Event) }, null)
                            .Invoke(panel, new object[] { new Event { type = EventType.Repaint } });
                        panel.GetType().GetMethod("Render", flags).Invoke(panel, null);
                    }
                    finally { GL.PopMatrix(); }
                }
                finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            }
        }
        bool ready = panel != null && FiniteRect(panel.visualTree.worldBound) && FiniteRect(rootVisualElement.worldBound) &&
            rootVisualElement.worldBound.width > 1 && rootVisualElement.worldBound.height > 1;
        if (ready && m_View != null)
        {
            var workshop = m_Host?.Q("haloWorkshop");
            ready = m_Host?.panel == panel && FiniteRect(m_Host.worldBound) && workshop != null && FiniteRect(workshop.worldBound) &&
                m_Host.worldBound.width > 1 && m_Host.worldBound.height > 1 && workshop.worldBound.width > 1 && workshop.worldBound.height > 1;
        }
        if (ready)
        {
            RecordLayout("layout preparation ready"); m_LayoutDeadline = m_NextLayoutProbe = 0; return true;
        }
        if (EditorApplication.timeSinceStartup >= m_LayoutDeadline)
        {
            RecordLayout("layout preparation timeout");
            throw new TimeoutException("Native panel did not produce finite review geometry within five seconds; layout assertions were not skipped.");
        }
        EditorApplication.QueuePlayerLoopUpdate(); Repaint(); return false;
    }
    private static bool FiniteRect(Rect bounds) => float.IsFinite(bounds.x) && float.IsFinite(bounds.y) && float.IsFinite(bounds.width) && float.IsFinite(bounds.height);
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
        EditorApplication.update -= Tick; m_View?.Dispose(); m_View = null; ClearComparisonTextures();
        if (m_LayoutTarget) { m_LayoutTarget.Release(); DestroyImmediate(m_LayoutTarget); m_LayoutTarget = null; }
        if (m_PreviousLocale != null) LocalizationSettings.SelectedLocale = m_PreviousLocale;
    }
}
