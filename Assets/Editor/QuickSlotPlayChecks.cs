using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Drives the live player's real UI callbacks and real InputSystem frame path.
// Bindings use an injected in-memory store. Primary use additionally requires a
// temporary offline profile, because it goes through the real server transport.
public static class QuickSlotPlayChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static RaidHUD s_Hud;
    private static QuickSlotBindings s_Previous;
    private static IQuickSlotStore s_PreviousValidationStore;
    private static string s_PreviousValidationScope;
    private static bool s_MemoryInstalled;
    private static Keyboard s_Keyboard;
    private static Mouse s_Mouse;
    private static DollSingerInput s_FixtureInput;
    private static World s_IsolatedWorld;
    private static Func<int, int> s_PreviousSelection;
    private static Func<bool, bool> s_PreviousPrimary;
    private static Func<bool> s_PreviousDarkSight;
    private static double s_ReleaseAt;
    private static int s_PressedFrame;
    private static string s_Evidence;
    private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    private static void Write(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    private static VisualElement Root => Read<VisualElement>(s_Hud, "m_Root");
    private static QuickSlotBindings Bindings => Read<QuickSlotBindings>(s_Hud, "m_QuickBindings");
    private static ContainerInventoryView InventoryView => Read<ContainerInventoryView>(s_Hud, "m_InventoryView");
    private static InventoryGraph Graph => (InventoryGraph)typeof(RaidHUD).GetProperty("QuickGraph", Private).GetValue(s_Hud);
    private static DollSingerInput Input
    {
        get
        {
            if (s_FixtureInput) return s_FixtureInput;
            foreach (var view in Object.FindObjectsByType<DollSingerNetworkPresentation>(FindObjectsSortMode.None))
                if (view.OwnedInput != null) return view.OwnedInput;
            throw new InvalidOperationException("No live owned DollSinger input. Use the actual Raid client.");
        }
    }
    public static void InstallMemoryPreferences()
    {
        if (s_MemoryInstalled) return;
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Install in the Play menu before opening the temporary profile/Raid.");
        var hud = Object.FindAnyObjectByType<RaidHUD>();
        if (hud)
        {
            var bindings = Read<QuickSlotBindings>(hud, "m_QuickBindings");
            if (bindings != null && !string.IsNullOrEmpty(Read<string>(bindings, "m_Scope")))
                throw new InvalidOperationException("Profile slots already initialized. Restart the isolated Play fixture and install memory preferences before opening its profile.");
        }
        s_PreviousValidationStore = QuickSlotPreferenceStore.ValidationStore;
        s_PreviousValidationScope = QuickSlotPreferenceStore.ValidationScope;
        QuickSlotPreferenceStore.ValidationScope = "quickslot-validation/" + Guid.NewGuid().ToString("N");
        QuickSlotPreferenceStore.ValidationStore = new MemoryStore(); s_MemoryInstalled = true;
        EditorApplication.playModeStateChanged -= PlayChanged;
        EditorApplication.playModeStateChanged += PlayChanged;
    }
    public static string Begin() => BeginCore(Object.FindAnyObjectByType<RaidHUD>(), null, null);
    public static string BeginIsolated(RaidHUD hud, DollSingerInput input)
    {
        if (!hud || !input) throw new ArgumentNullException("The isolated HUD and real input component are required.");
        var world = Read<World>(hud, "m_World");
        RequireSingleDisconnectedWorld(world);
        return BeginCore(hud, input, world);
    }
    private static void RequireSingleDisconnectedWorld(World world)
    {
        if (world == null || !world.IsCreated || !world.IsClient()) throw new InvalidOperationException("A single lightweight ClientWorld is required.");
        int count = 0;
        foreach (var candidate in World.All)
            if (candidate.IsCreated && (candidate.IsClient() || candidate.IsServer())) count++;
        using var connections = world.EntityManager.CreateEntityQuery(typeof(NetworkStreamConnection));
        if (count != 1 || !connections.IsEmptyIgnoreFilter) throw new InvalidOperationException("Isolated UI checks require one ClientWorld, no server World and no transport connections.");
    }
    private static string BeginCore(RaidHUD hud, DollSingerInput fixtureInput, World isolatedWorld)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter a live temporary-profile Raid first.");
        if (!s_MemoryInstalled) throw new InvalidOperationException("Call InstallMemoryPreferences in the Play menu before loading the fixture profile/HUD.");
        if (s_Hud != null) throw new InvalidOperationException("An inspection is already running. End it before starting another.");
        s_Hud = hud;
        string scope = string.IsNullOrEmpty(QuickSlotPreferenceStore.ValidationScope) ? PlayerProfileClient.LocalPreferenceScope : QuickSlotPreferenceStore.ValidationScope;
        if (!s_Hud || string.IsNullOrEmpty(scope)) throw new InvalidOperationException("The fixture HUD and in-memory validation scope are required.");
        s_Previous = Bindings;
        s_FixtureInput = fixtureInput; s_IsolatedWorld = isolatedWorld;
        if (s_FixtureInput)
        {
            s_PreviousSelection = s_FixtureInput.QuickSlotSelection; s_PreviousPrimary = s_FixtureInput.QuickSlotPrimaryUse;
            s_PreviousDarkSight = s_FixtureInput.DarkSightMode; RaidQuickSlotInput.Bind(s_FixtureInput);
        }
        Write(s_Hud, "m_QuickBindings", new QuickSlotBindings(new MemoryStore()));
        Bindings.UseProfile(scope); Bindings.InitializeWeapons(Graph);
        s_Keyboard = InputSystem.AddDevice<Keyboard>("QuickSlotCheckKeyboard");
        s_Mouse = InputSystem.AddDevice<Mouse>("QuickSlotCheckMouse");
        s_Evidence = Path.Combine(Path.GetTempPath(), "Moonkov-QuickSlots-" + Guid.NewGuid().ToString("N") + ".jsonl");
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= PlayChanged;
        EditorApplication.playModeStateChanged += PlayChanged;
        Record("begin"); return s_Evidence;
    }
    private static void Ensure() { if (!s_Hud) throw new InvalidOperationException("Call QuickSlotPlayChecks.Begin first."); }
    private static void EnsurePrimaryIsIsolated()
    {
        if (s_IsolatedWorld != null)
        {
            RequireSingleDisconnectedWorld(s_IsolatedWorld);
            if (Read<World>(s_Hud, "m_World") != s_IsolatedWorld) throw new InvalidOperationException("The fixture World changed.");
            return;
        }
        if (!PlayerProfileClient.IsOffline) throw new InvalidOperationException("Primary-use tests require the isolated offline profile; no real accounts.");
        string file = Path.GetFullPath(PlayerProfileClient.OfflineStore.FilePath);
        string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!file.StartsWith(temporary, StringComparison.Ordinal)) throw new InvalidOperationException("Profile is outside the system temporary directory. Primary use refused.");
    }
    public static string Items()
    {
        Ensure(); var values = new List<object>();
        foreach (var item in Graph.Items)
            if (QuickSlotBindings.CanBind(Graph, item)) values.Add(new { item.Id, item.Code, item.Quantity });
        return JsonConvert.SerializeObject(values);
    }
    private static VisualElement ItemTile(string itemId)
    {
        var tiles = Read<Dictionary<string, VisualElement>>(InventoryView, "m_Tiles");
        if (!tiles.TryGetValue(itemId, out var tile) || tile.panel == null) throw new InvalidOperationException("Open the actual inventory/container containing that item first.");
        return tile;
    }
    public static void BindViaKey(string itemId, int number)
    {
        Ensure(); if (number < 1 || number > 9) throw new ArgumentOutOfRangeException(nameof(number));
        var tile = ItemTile(itemId);
        var hover = new Event { type = EventType.MouseMove, mousePosition = tile.worldBound.center };
        using (var pointer = PointerMoveEvent.GetPooled(hover))
        using (var enter = PointerEnterEvent.GetPooled((IPointerEvent)pointer))
        { enter.target = tile; tile.SendEvent(enter); }
        using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Alpha1 + number - 1, EventModifiers.None)) { evt.target = tile; tile.SendEvent(evt); }
        if (Bindings[number - 1] != itemId) throw new InvalidOperationException("Real inventory hover + key callback refused binding. Check life/busy/UI guards.");
        Record("bind-key-" + number);
    }
    private static void Pointer(VisualElement target, EventType type, Vector2 position, int button)
    {
        var evt = new Event { type = type, mousePosition = position, button = button, clickCount = 1 };
        if (type == EventType.MouseDown) { using var down = PointerDownEvent.GetPooled(evt); down.target = target; target.SendEvent(down); }
        else if (type == EventType.MouseUp) { using var up = PointerUpEvent.GetPooled(evt); up.target = target; target.SendEvent(up); }
        else { using var move = PointerMoveEvent.GetPooled(evt); move.target = target; target.SendEvent(move); }
    }
    public static void BindViaDrag(string itemId, int number)
    {
        Ensure(); var tile = ItemTile(itemId); var slot = Root.Q("quickSlot" + number);
        int version = Graph.Version; var start = tile.worldBound.center; var end = slot.worldBound.center;
        Pointer(tile, EventType.MouseDown, start, 0);
        Pointer(tile, EventType.MouseDrag, end, 0);
        Pointer(tile, EventType.MouseUp, end, 0);
        if (Bindings[number - 1] != itemId || Graph.Version != version) throw new InvalidOperationException("Real Drag/drop callback failed or mutated inventory.");
        Record("bind-drag-" + number);
    }
    public static void ClearViaRightClick(int number)
    {
        Ensure(); var slot = Root.Q("quickSlot" + number);
        Pointer(slot, EventType.MouseDown, slot.worldBound.center, 1);
        Pointer(slot, EventType.MouseUp, slot.worldBound.center, 1);
        if (Bindings[number - 1] != null) throw new InvalidOperationException("Real right-click clear callback refused.");
        Record("clear-right-" + number);
    }
    public static void PressNumber(int number)
    {
        Ensure(); if (number < 1 || number > 9) throw new ArgumentOutOfRangeException(nameof(number));
        PressKey(Key.Digit1 + number - 1);
    }
    public static void PressKey(Key key) => HoldInput(.15f, false, key);
    // No left mouse state is generated here: online isolated Bounty fixtures may
    // exercise V + RMB without passing the primary-use offline-profile guard.
    public static void HoldInput(float seconds, bool rightMouse, params Key[] keys)
    {
        Ensure();
        if (seconds <= 0 || seconds > 30 || float.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var key in keys)
            if (!((key >= Key.Digit1 && key <= Key.Digit9) || key == Key.V || key == Key.B || key == Key.X || key == Key.Tab || key == Key.I || key == Key.Escape)) EnsurePrimaryIsIsolated();
        s_PressedFrame = Time.frameCount;
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(keys));
        var state = new MouseState();
        if (rightMouse) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right);
        InputSystem.QueueStateEvent(s_Mouse, state);
        s_ReleaseAt = EditorApplication.timeSinceStartup + seconds;
    }
    public static void PressPrimary(bool aim = false)
    {
        Ensure(); EnsurePrimaryIsIsolated();
        if (!Input.CanReadPlayerInput || RaidHUD.InventoryOpen) throw new InvalidOperationException("Close inventory and capture the actual Game view first.");
        s_PressedFrame = Time.frameCount;
        var state = new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
        if (aim) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right);
        InputSystem.QueueStateEvent(s_Mouse, state);
        s_ReleaseAt = EditorApplication.timeSinceStartup + .15;
    }
    private static void Update()
    {
        if (!s_Hud || !EditorApplication.isPlaying) { End(); return; }
        if (s_ReleaseAt <= 0 || EditorApplication.timeSinceStartup < s_ReleaseAt || Time.frameCount < s_PressedFrame + 2) return;
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState());
        InputSystem.QueueStateEvent(s_Mouse, new MouseState()); s_ReleaseAt = 0;
    }
    public static string Record(string label)
    {
        Ensure(); var graph = Graph; var item = Bindings.SelectedItem(graph); DollSingerInput input = null;
        try { input = Input; } catch (InvalidOperationException) { }
        var slots = new List<object>();
        for (int i = 0; i < 9; i++) slots.Add(new { Number = i + 1, ItemId = Bindings[i], Code = Bindings.Code(i), Available = Bindings.Available(i, graph) });
        var json = JsonConvert.SerializeObject(new {
            Label = label, Frame = Time.frameCount, Time = Time.realtimeSinceStartup, GraphVersion = graph?.Version,
            InventoryOpen = RaidHUD.InventoryOpen, Bindings.Selected, SelectedId = item?.Id, SelectedCode = item?.Code, Quantity = item?.Quantity,
            Fire = input != null && input.FireHeld, Aim = input != null && input.AimHeld, WeaponSlot = input ? input.WeaponSlotPressed : 0,
            ToolSelected = RaidHUD.QuickSlotToolSelected, Owner = TacticalItemsFeedback.LocalOwner, Slots = slots
        });
        File.AppendAllText(s_Evidence, json + Environment.NewLine); Debug.Log("QuickSlotPlayCheck " + json); return json;
    }
    public static void ExpectUseIntent(string itemId, int expectedCount = 1)
    {
        Ensure(); RequireSingleDisconnectedWorld(s_IsolatedWorld);
        var em = s_IsolatedWorld.EntityManager; int total = 0, matching = 0;
        void Count<T>(Func<T, string> id, Func<T, bool> isUse = null) where T : unmanaged, IComponentData
        {
            using var query = em.CreateEntityQuery(typeof(T), typeof(SendRpcCommandRequest));
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var request = em.GetComponentData<T>(entity);
                if (isUse != null && !isUse(request)) continue;
                total++; if (id(request) == itemId) matching++;
            }
        }
        Count<RaidTacticalItemActionRpc>(x => x.ItemId.ToString(), x => x.Action == TacticalItemAction.Use);
        Count<RaidMedicalUseRpc>(x => x.ItemId.ToString());
        Count<RaidConsumableUseRpc>(x => x.ItemId.ToString());
        Count<RaidGrenadeThrowRpc>(x => x.ItemId.ToString());
        if (total != expectedCount || matching != expectedCount) throw new InvalidOperationException("Unexpected item use intent count or identity: " + matching + "/" + total);
        Record("queued-use-" + matching);
    }
    public static void ExpectWeaponSuppressed()
    {
        Ensure(); var input = Input;
        if (input.FireHeld || input.FirePressed || input.AimHeld || input.ReloadPressed) throw new InvalidOperationException("Selected tool leaked weapon input.");
        Record("weapon-input-suppressed");
    }
    public static void ExpectSelected(int number)
    { Ensure(); if (Bindings.Selected != number - 1) throw new InvalidOperationException("Unexpected selected slot: " + Bindings.Selected); Record("selected-" + number); }
    public static void ExpectQuantity(string itemId, int quantity)
    { Ensure(); if ((Graph.Find(itemId)?.Quantity ?? 0) != quantity) throw new InvalidOperationException("Unexpected authoritative item quantity."); Record("quantity-" + quantity); }
    private static void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) End(); }
    public static void End()
    {
        EditorApplication.update -= Update; EditorApplication.playModeStateChanged -= PlayChanged;
        if (s_Hud && s_Previous != null) { Write(s_Hud, "m_QuickBindings", s_Previous); Write(s_Hud, "m_QuickWasTool", false); }
        if (s_FixtureInput)
        {
            RaidQuickSlotInput.Unbind(s_FixtureInput);
            s_FixtureInput.QuickSlotSelection = s_PreviousSelection; s_FixtureInput.QuickSlotPrimaryUse = s_PreviousPrimary;
            s_FixtureInput.DarkSightMode = s_PreviousDarkSight;
        }
        s_FixtureInput = null; s_IsolatedWorld = null; s_PreviousSelection = null; s_PreviousPrimary = null; s_PreviousDarkSight = null;
        if (s_Keyboard != null && s_Keyboard.added) InputSystem.RemoveDevice(s_Keyboard);
        if (s_Mouse != null && s_Mouse.added) InputSystem.RemoveDevice(s_Mouse);
        TacticalItemsFeedback.SetSelectedItem(null); TacticalItemsFeedback.SetPresentationBlocked(true);
        if (s_MemoryInstalled)
        { QuickSlotPreferenceStore.ValidationStore = s_PreviousValidationStore; QuickSlotPreferenceStore.ValidationScope = s_PreviousValidationScope; }
        s_MemoryInstalled = false; s_PreviousValidationStore = null; s_PreviousValidationScope = null;
        s_Hud = null; s_Previous = null; s_Keyboard = null; s_Mouse = null; s_ReleaseAt = 0;
    }
    private sealed class MemoryStore : IQuickSlotStore
    {
        private readonly Dictionary<string, QuickSlotSave> m_Values = new Dictionary<string, QuickSlotSave>();
        public QuickSlotSave Load(string scope) => m_Values.TryGetValue(scope, out var value) ? value : null;
        public void Save(string scope, QuickSlotSave value) => m_Values[scope] = value;
    }
}
