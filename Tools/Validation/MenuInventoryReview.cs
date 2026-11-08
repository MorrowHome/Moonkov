using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

// Uses real menu presenters and the real account/profile clients with an in-memory HTTP handler.
// Never authenticates, writes PlayerPrefs, opens a socket, or modifies a player save.
public static class MenuInventoryReview
{
    private const string Output = "LocalData/MenuInventoryReview20261108";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string SyncTranslations()
    {
        var source = Decode<Dictionary<string, string>>(File.ReadAllText("Assets/Localization/Translations.json"));
        var keys = new[] { "Syncing inventory…", "RETRY SYNC", "Inventory confirmation is interrupted. Retry synchronization first.",
            "Inventory synchronization is catching up. Please wait briefly.", "Purchases arrive in personal storage." };
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach (var table in collection.StringTables)
        {
            foreach (string key in keys) table.AddEntry(key, table.LocaleIdentifier.Code == "zh-Hans" ? (string)source[key] : key);
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
        return "Updated five menu synchronization strings and shared keys.";
    }

    public static string Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || PlayerProfileClient.IsLan ||
            PlayerProfileClient.MenuInventoryPending != 0) throw new InvalidOperationException("Use an idle Editor without pending profile actions.");
        Directory.CreateDirectory(Output);
        _ = Run().ContinueWith(task =>
        {
            if (task.IsFaulted) File.WriteAllText(Path.Combine(Output, "native-result.json"),
                Encode(new { passed = false, error = task.Exception.ToString() }));
        }, TaskScheduler.Default);
        return "Started native menu review with held in-memory responses; result will be written under " + Output;
    }
    private static async Task Run()
    {
        var account = Save(typeof(AccountClient));
        var profile = Save(typeof(PlayerProfileClient));
        var locale = LocalizationSettings.SelectedLocale;
        var previousSettings = GameSettings.Instance;
        var wire = new Wire(); var http = new HttpClient(wire);
        MenuInventoryReviewWindow window = null;
        MoonkovShopView shop = null;
        StashScreen stash = null;
        var checks = new List<string>(); string error = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        async Task Until(Func<bool> condition) { while (!condition()) await Task.Delay(20, timeout.Token); }
        try
        {
            var settings = (GameSettings)Activator.CreateInstance(typeof(GameSettings), true);
            typeof(GameSettings).GetField("m_PlayerName", Instance).SetValue(settings, "Menu review fixture");
            typeof(GameSettings).GetField("m_PlayerCharacter", Instance).SetValue(settings, 2);
            typeof(GameSettings).GetProperty("Instance", Static).SetValue(null, settings);
            Set(typeof(PlayerProfileClient), "s_Offline", null); Set(typeof(PlayerProfileClient), "s_OfflineChanged", null);
            Set(typeof(PlayerProfileClient), "s_MenuInventory", null); Set(typeof(PlayerProfileClient), "s_MenuInventoryScope", null);
            Set(typeof(AccountClient), "InventoryChanged", null); Set(typeof(AccountClient), "s_Http", http);
            Set(typeof(AccountClient), "s_PreferenceKey", "IsolatedMenuInventoryReview");
            Set(typeof(AccountClient), "s_Token", new string('a', 64)); Set(typeof(AccountClient), "s_Expires", DateTime.UtcNow.AddHours(1));
            Set(typeof(AccountClient), "s_BaseUrl", "https://menu-review.invalid");
            Set(typeof(AccountClient), "s_VerifiedServiceUrl", "https://menu-review.invalid");
            Set(typeof(AccountClient), "s_VerifiedPlayerId", Wire.Player);
            Set(typeof(AccountClient), "s_ProfileRequest", 0L); Set(typeof(AccountClient), "s_AppliedProfileRequest", 0L);
            Set(typeof(AccountClient), "<Inventory>k__BackingField", wire.Graph.Clone());
            Set(typeof(AccountClient), "<RaidActive>k__BackingField", false);
            Set(typeof(AccountClient), "<Recovery>k__BackingField", new RecoveryInbox());
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            window = ScriptableObject.CreateInstance<MenuInventoryReviewWindow>();
            window.titleContent = new GUIContent("Moonkov / Menu sync review");
            window.position = new Rect(35, 45, 1280, 720); window.ShowUtility();
            var root = window.rootVisualElement;
            root.style.flexGrow = 1;
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI Toolkit/GameUI/MoonkovTerminal.uss"));
            root.AddToClassList("moonkov-terminal"); root.AddToClassList("archive-ui");
            VisualElement ShopShell()
            {
                var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/Window").CloneTree();
                root.Add(tree);
                var shell = tree.Q("terminalWindow") ?? tree;
                shell.style.position = Position.Relative; shell.style.left = 0; shell.style.top = 0;
                shell.style.width = Length.Percent(100); shell.style.height = Length.Percent(100);
                return shell;
            }
            var shopShell = ShopShell();
            shop = new MoonkovShopView(shopShell, () => { }, initialProduct: "nocturne");
            await Until(() => shopShell.Q<Button>("purchaseBuy")?.enabledInHierarchy == true);
            var buyButton = shopShell.Q<Button>("purchaseBuy");
            Click(buyButton);
            Check(PlayerProfileClient.MenuInventoryPending == 1 && PlayerProfileClient.MenuInventory.Count("nocturne") == 1 &&
                AccountClient.Inventory.Count("nocturne") == 0, "real purchase click updates presentation before its HTTP receipt");
            Check(buyButton.enabledInHierarchy, "purchase button stays available during confirmation");
            Click(buyButton);
            Check(PlayerProfileClient.MenuInventoryPending == 2 && PlayerProfileClient.MenuInventory.Count("dust") == 364,
                "second real click queues and reserves its own balance immediately");
            await Until(() => wire.Pending.Count == 1);
            Check(wire.Posts == 1 && wire.Graph.Count("nocturne") == 0, "held receipt does not start a second concurrent write");
            await Task.Delay(100, timeout.Token);
            Capture(window, "shop-pending.png");
            shop.Dispose(); shop = null; root.Clear();

            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/StashScreen.uxml").CloneTree(root);
            stash = new StashScreen(root, () => { }, () => { }, () => { });
            stash.Present("Menu review fixture", 2, 500, 0, 6, true, false);
            stash.NavigatePage(1);
            var containers = (ContainerInventoryView)typeof(StashScreen).GetField("m_Containers", Instance).GetValue(stash);
            containers.OperationAudioEnabled = false;
            string displayId = PlayerProfileClient.MenuInventory.Items.First(item => item.Code == "nocturne").Id;
            var move = new InventoryCommand { Operation = InventoryOperation.Move, ItemId = displayId, Parent = "equipment", Region = "Primary" };
            typeof(ContainerInventoryView).GetMethod("Command", Instance).Invoke(containers, new object[] { move });
            Check(!containers.Busy && PlayerProfileClient.MenuInventory.Equipped("Primary")?.Id == displayId,
                "real storage presenter acknowledges the equip gesture without waiting");
            Check(PlayerProfileClient.MenuInventoryPending == 3 && wire.Posts == 1,
                "closing supplier and opening storage preserves the same ordered queue");
            wire.Pending.Dequeue().TrySetResult(false);
            await Until(() => wire.Posts == 2 && wire.Pending.Count == 1);
            Check(PlayerProfileClient.MenuInventory.Equipped("Primary")?.Id == displayId && !containers.Busy,
                "first purchase receipt preserves the later equip and stable item identity");
            wire.Pending.Dequeue().TrySetResult(false);
            await Until(() => wire.Posts == 3 && wire.Pending.Count == 1);
            Check(wire.LastMove?.ItemId != displayId && wire.Graph.Find(wire.LastMove?.ItemId)?.Code == "nocturne",
                "real outgoing equip uses the authoritative purchased item ID");
            wire.Pending.Dequeue().TrySetResult(false);
            await Until(() => PlayerProfileClient.MenuInventoryPending == 0);
            Check(AccountClient.Inventory.Equipped("Primary")?.Code == "nocturne" && AccountClient.StashDust == 364,
                "all three receipts produce the expected saved equipment and balance");
            await PlayerProfileClient.FlushMenuInventoryAsync(timeout.Token);
            Check(PlayerProfileClient.MenuInventory.Equipped("Primary")?.Id == displayId, "flush keeps the menu's display identity stable");
            await Task.Delay(100, timeout.Token);
            Capture(window, "storage-confirmed.png");

            stash.Dispose(); stash = null; root.Clear();
            shopShell = ShopShell(); shop = new MoonkovShopView(shopShell, () => { }, initialProduct: "nocturne");
            await Until(() => shopShell.Q<Button>("purchaseBuy")?.enabledInHierarchy == true);
            Click(shopShell.Q<Button>("purchaseBuy"));
            Check(PlayerProfileClient.MenuInventory.Count("nocturne") == 3, "new order appears immediately after reopening supplier");
            await Until(() => wire.Pending.Count == 1);
            wire.Pending.Dequeue().TrySetResult(true);
            await Until(() => PlayerProfileClient.MenuInventoryPending == 0);
            Check(PlayerProfileClient.MenuInventory.Count("nocturne") == 2 && PlayerProfileClient.MenuInventory.Count("dust") == 364 &&
                AccountClient.Inventory.Equipped("Primary")?.Code == "nocturne", "real rejection restores the order without undoing confirmed equipment");
            Check(shopShell.Q<Button>("purchaseBuy").enabledInHierarchy, "rejected order does not leave purchase input locked");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            shop?.Dispose(); stash?.Dispose(); if (window) window.Close();
            typeof(PlayerProfileClient).GetMethod("ResetMenuInventory", Static).Invoke(null, null);
            http.Dispose(); Restore(account); Restore(profile);
            typeof(GameSettings).GetProperty("Instance", Static).SetValue(null, previousSettings);
            LocalizationSettings.SelectedLocale = locale;
            File.WriteAllText(Path.Combine(Output, "native-result.json"), Encode(new
                { passed = error == null, checks = checks.Count, details = checks, error, wire.Posts,
                    mode = "Single Editor native menus with in-memory HTTP; no sockets, accounts, player saves or Play mode" }));
        }
    }
    // Pipeline's dynamic compiler references two Newtonsoft assemblies; select the runtime one explicitly.
    private static readonly Type JsonType = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
    private static string Encode(object value) => (string)JsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    private static T Decode<T>(string json) => (T)JsonType.GetMethods().Single(method => method.Name == "DeserializeObject" &&
        method.IsGenericMethodDefinition && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(string))
        .MakeGenericMethod(typeof(T)).Invoke(null, new object[] { json });
    private static Dictionary<FieldInfo, object> Save(Type type) => type.GetFields(Static).Where(f => !f.IsInitOnly && !f.IsLiteral).ToDictionary(f => f, f => f.GetValue(null));
    private static void Restore(Dictionary<FieldInfo, object> fields) { foreach (var pair in fields) pair.Key.SetValue(null, pair.Value); }
    private static void Set(Type type, string name, object value) => type.GetField(name, Static).SetValue(null, value);
    private static void Click(Button button) { using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt); }
    private static void Capture(EditorWindow window, string name)
    {
        var rect = window.position; float scale = EditorGUIUtility.pixelsPerPoint;
        int width = (int)(rect.width * scale), height = (int)(rect.height * scale);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position * scale, width, height)); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    private sealed class Wire : HttpMessageHandler
    {
        public const string Player = "5712a9b0-aa4c-4b99-a716-06c205793ce0";
        public InventoryGraph Graph = InventoryGraph.Create(starter: false);
        public readonly Queue<TaskCompletionSource<bool>> Pending = new Queue<TaskCompletionSource<bool>>();
        public int Posts;
        public InventoryCommand LastMove;
        public Wire() { Graph.AddSupply("dust", 500, "stash"); Graph.AddSupply("cells", 6, "stash"); }
        private object Profile() => new { PlayerId = Player, DisplayName = "Menu review fixture", InventoryJson = Encode(Graph),
            RecoveryJson = Encode(new RecoveryInbox()), Dust = Graph.Count("dust"), Alloy = 0, Cells = Graph.Count("cells") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri.AbsolutePath;
            if (path == "/auth/me") return Reply(Profile());
            if (path == "/auth/shop") return Reply(new { Offers = ShopCatalog.Offers, Profile = Profile() });
            string body = await request.Content.ReadAsStringAsync();
            Posts++; var gate = new TaskCompletionSource<bool>(); Pending.Enqueue(gate);
            if (path == "/auth/inventory/move") LastMove = Decode<InventoryCommand>(body);
            using (cancellationToken.Register(() => gate.TrySetCanceled()))
                if (await gate.Task) return Reply(new { Error = "inventory_Full" }, HttpStatusCode.Conflict);
            if (path == "/auth/shop/trade")
            {
                string error = ShopRules.Apply(Graph, Decode<ShopCommand>(body), out var next);
                if (error != null) return Reply(new { Error = error }, HttpStatusCode.Conflict);
                Graph = next;
            }
            else if (path == "/auth/inventory/move")
            {
                var error = Graph.TryApply(LastMove);
                if (error != InventoryError.None) return Reply(new { Error = "inventory_" + error }, HttpStatusCode.Conflict);
            }
            else throw new InvalidOperationException("Unexpected fixture request path.");
            return Reply(Profile());
        }
        private static HttpResponseMessage Reply(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new HttpResponseMessage(status) { Content = new StringContent(Encode(value)) };
    }
}
public sealed class MenuInventoryReviewWindow : EditorWindow { }
