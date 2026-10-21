using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Explicit, nonblocking check in the existing one-World UI fixture. No World,
// character, account, transport, profile or simulated consumption is created.
public static class QuickSlotCancelInputChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> Trace = new List<object>();
    private static Action s_Next;
    private static double s_Due, s_Started;
    private static int s_Frame, s_Checks, s_Slot, s_ProbeSlot, s_Quantity, s_Version;
    private static string s_Item, s_File;
    private static uint s_UseRequest;
    private static TextField s_Field;
    public static string Status { get; private set; } = "not run";
    private static RaidHUD Hud => BountyClashUIFixture.Hud;
    private static DollSingerInput Input => BountyClashUIFixture.Input;
    private static World World => BountyClashUIFixture.ClientWorld;
    private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
    private static QuickSlotBindings Bindings => Read<QuickSlotBindings>(Hud, "m_QuickBindings");
    private static InventoryGraph Graph => (InventoryGraph)typeof(RaidHUD).GetProperty("QuickGraph", Hidden).GetValue(Hud);
    private static VisualElement Root => Hud.GetComponent<UIDocument>().rootVisualElement;

    // First BeginIsolated, bind a real tactical item and select it in the actual
    // fixture, acknowledge prior checks, close inventory, and focus Game view.
    public static string Run(int slot = 9)
    {
        if (s_Next != null) throw new InvalidOperationException("Cancel input check already running.");
        if (!EditorApplication.isPlaying || !Hud || !Input || World == null || !World.IsCreated)
            throw new InvalidOperationException("Start the one-character UI fixture and BeginIsolated first.");
        if (slot < 1 || slot > 9 || Bindings.Selected != slot - 1 || RaidHUD.InventoryOpen || !Input.CanReadPlayerInput || Input.LightAdjusting || RaidHUD.TimedActionInputActive)
            throw new InvalidOperationException("Select the bound tactical slot, finish pending actions/light adjustment, and capture Game view first.");
        s_Item = Bindings[slot - 1]; var item = Graph.Find(s_Item);
        if (!QuickSlotBindings.CanBind(Graph, item) || TacticalItemRules.Find(item.Code) == null)
            throw new InvalidOperationException("Use a real accessible tactical item, preferably stamina_shot.");
        s_ProbeSlot = 0;
        for (int i = 0; i < QuickSlotBindings.Count; i++)
            if (i != slot - 1 && Bindings.Available(i, Graph)) { s_ProbeSlot = i + 1; break; }
        if (s_ProbeSlot == 0) throw new InvalidOperationException("Keep one other real available binding so the text selection guard is meaningfully tested.");
        BountyClashUIFixture.ClearIntents(); QuickSlotPlayChecks.ExpectUseIntent(s_Item, 0);
        s_Slot = slot; s_Quantity = item.Quantity; s_Version = Graph.Version; s_Checks = 0; Trace.Clear();
        s_File = Path.Combine(Path.GetTempPath(), "Moonkov-CancelInput-" + Guid.NewGuid().ToString("N") + ".json");
        s_Started = EditorApplication.timeSinceStartup; Status = "running";
        EditorApplication.update += Tick;
        // Real input resets a previous crouch/prone toggle without writing pose.
        QuickSlotPlayChecks.HoldInput(.15f, false, Key.Space);
        Later(StartUse); return s_File;
    }
    private static void Later(Action next, int frames = 3, double delay = .18)
    { s_Next = next; s_Frame = Time.frameCount + frames; s_Due = EditorApplication.timeSinceStartup + delay; }
    private static void Tick()
    {
        if (s_Next == null) return;
        try
        {
            if (!EditorApplication.isPlaying || !Hud || World == null || !World.IsCreated) throw new InvalidOperationException("Fixture ended during input check.");
            if (EditorApplication.timeSinceStartup - s_Started > 60) throw new TimeoutException("Game frames did not advance; keep the Game view running.");
            if (Time.frameCount < s_Frame || EditorApplication.timeSinceStartup < s_Due) return;
            var action = s_Next; s_Next = null; action();
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    private static void Check(bool ok, string label)
    {
        Trace.Add(new { label, pass = ok, frame = Time.frameCount, stance = Input.Stance.ToString(), timed = RaidHUD.TimedActionInputActive,
            quantity = Graph.Find(s_Item)?.Quantity ?? 0, graphVersion = Graph.Version });
        if (!ok) throw new InvalidOperationException(label); s_Checks++;
    }
    private static void Quantity()
    { Check((Graph.Find(s_Item)?.Quantity ?? 0) == s_Quantity && Graph.Version == s_Version, "Client quantity/version remain unchanged"); }
    private static List<RaidTacticalItemActionRpc> Requests(TacticalItemAction action)
    {
        var result = new List<RaidTacticalItemActionRpc>(); var em = World.EntityManager;
        using var q = em.CreateEntityQuery(typeof(RaidTacticalItemActionRpc), typeof(SendRpcCommandRequest));
        using var entities = q.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities) { var request = em.GetComponentData<RaidTacticalItemActionRpc>(entity); if (request.Action == action) result.Add(request); }
        return result;
    }
    private static void StartUse()
    {
        Check(Input.Stance == DollSingerStance.Standing, "Fresh input starts from standing");
        Check(Bindings.Selected == s_Slot - 1, "Real tactical selection remains active"); Quantity();
        BountyClashUIFixture.ClearIntents(); QuickSlotPlayChecks.PressPrimary(); Later(CancelBeforeStarted);
    }
    private static void CancelBeforeStarted()
    {
        QuickSlotPlayChecks.ExpectUseIntent(s_Item, 1);
        var use = Requests(TacticalItemAction.Use); Check(use.Count == 1 && use[0].ItemId.ToString() == s_Item, "One real Use intent for exact bound identity");
        s_UseRequest = use[0].RequestId;
        Check(TacticalItemsFeedback.LocalOwner.Remaining == 0, "No Started feedback was injected"); Quantity();
        QuickSlotPlayChecks.HoldInput(5, false, Key.X); Later(CheckCancel);
    }
    private static void CheckCancel()
    {
        var cancel = Requests(TacticalItemAction.Cancel);
        Check(cancel.Count == 1 && cancel[0].RequestId == s_UseRequest + 1, "Exactly one sequential Cancel before Started");
        Check(Input.Stance != DollSingerStance.Tiptoe, "Cancellation X never enters tiptoe"); Quantity();
        // Deliberately injected UI acknowledgement, not server execution or spend.
        BountyClashUIFixture.AcknowledgeFixture(); Later(CheckHeldAfterAck);
    }
    private static void CheckHeldAfterAck()
    {
        Check(!RaidHUD.TimedActionInputActive, "Injected acknowledgement clears UI pending state");
        Check(Input.Stance != DollSingerStance.Tiptoe, "Same held X stays suppressed after acknowledgement");
        Check(Requests(TacticalItemAction.Cancel).Count == 1, "Held X does not duplicate Cancel"); Quantity();
        QuickSlotPlayChecks.HoldInput(.05f, false); Later(PressFreshX);
    }
    private static void PressFreshX()
    {
        Check(Input.Stance == DollSingerStance.Standing, "X release clears gesture suppression");
        QuickSlotPlayChecks.HoldInput(5, false, Key.X); Later(CheckFreshX);
    }
    private static void CheckFreshX()
    {
        Check(Input.Stance == DollSingerStance.Tiptoe, "Next independent X gesture restores normal tiptoe");
        Check(Requests(TacticalItemAction.Cancel).Count == 1, "Fresh non-action X sends no Cancel"); Quantity();
        QuickSlotPlayChecks.HoldInput(.05f, false); Later(OpenMenu);
    }
    private static Mouse DeviceMouse => (Mouse)typeof(QuickSlotPlayChecks).GetField("s_Mouse", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static void QueuePrimaryForBlockedUI()
    {
        // Existing virtual device; this phase intentionally bypasses the helper's
        // refusal to click inventory so the production block itself is exercised.
        InputSystem.QueueStateEvent(DeviceMouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left).WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right));
    }
    private static void OpenMenu()
    {
        BountyClashUIFixture.ClearIntents(); BountyClashUIFixture.Inventory(true); Later(MenuInput);
    }
    private static void MenuInput()
    {
        Check(RaidHUD.InventoryOpen, "Production inventory menu is actually open");
        QuickSlotPlayChecks.HoldInput(5, false, Key.Digit1 + s_ProbeSlot - 1); QueuePrimaryForBlockedUI(); Later(CheckMenu);
    }
    private static void CheckMenu()
    {
        QuickSlotPlayChecks.ExpectUseIntent(s_Item, 0); QuickSlotPlayChecks.ExpectWeaponSuppressed(); Check(Requests(TacticalItemAction.Cancel).Count == 0, "Menu sends no tactical actions without pending use"); Quantity();
        // Hover real item and send a real focused TextField key event through the
        // HUD's trickle-down handler, where binding would otherwise be permitted.
        s_Field = new TextField("Input guard") { name = "quickCancelTextGuard", value = "typed" };
        s_Field.style.position = Position.Absolute; s_Field.style.left = 8; s_Field.style.top = 8; s_Field.style.width = 220; Root.Add(s_Field); s_Field.Focus();
        Later(TextBindInput);
    }
    private static void TextBindInput()
    {
        Check((bool)typeof(RaidHUD).GetProperty("QuickCanBind", Hidden).GetValue(Hud), "Inventory binding is otherwise permitted, so typing guard is actually exercised");
        var inventory = Read<ContainerInventoryView>(Hud, "m_InventoryView");
        var tiles = Read<Dictionary<string, VisualElement>>(inventory, "m_Tiles");
        if (!tiles.TryGetValue(s_Item, out var tile)) throw new InvalidOperationException("Open the container containing the selected tactical item.");
        var hover = new Event { type = EventType.MouseMove, mousePosition = tile.worldBound.center };
        using (var pointer = PointerMoveEvent.GetPooled(hover))
        using (var enter = PointerEnterEvent.GetPooled((IPointerEvent)pointer))
        { enter.target = tile; tile.SendEvent(enter); }
        string beforeProbe = Bindings[s_ProbeSlot - 1], beforeSelected = Bindings[s_Slot - 1];
        using (var key = KeyDownEvent.GetPooled((char)('0' + s_ProbeSlot), KeyCode.Alpha1 + s_ProbeSlot - 1, EventModifiers.None)) { key.target = s_Field; s_Field.SendEvent(key); }
        Check(Bindings[s_ProbeSlot - 1] == beforeProbe && Bindings[s_Slot - 1] == beforeSelected, "Focused text input does not rebind hovered item"); Quantity();
        QuickSlotPlayChecks.HoldInput(.05f, false); BountyClashUIFixture.Inventory(false); Later(FocusCombatText, 4);
    }
    private static void FocusCombatText() { s_Field.Focus(); Later(CombatTextInput, 2); }
    private static void CombatTextInput()
    {
        Check(QuickSlotBindings.Typing(Root.panel.focusController.focusedElement as VisualElement), "Actual live panel has text focus");
        Check(Input.CanReadPlayerInput, "Combat input would be readable without the text guard");
        BountyClashUIFixture.ClearIntents(); QuickSlotPlayChecks.HoldInput(5, false, Key.Digit1 + s_ProbeSlot - 1); QuickSlotPlayChecks.PressPrimary(true); Later(CheckCombatText);
    }
    private static void CheckCombatText()
    {
        QuickSlotPlayChecks.ExpectUseIntent(s_Item, 0); QuickSlotPlayChecks.ExpectWeaponSuppressed();
        Check(Bindings.Selected == s_Slot - 1, "Text focus blocks combat number selection"); Quantity();
        QuickSlotPlayChecks.HoldInput(.05f, false); s_Field.Blur(); Later(StartPendingTextUse);
    }
    private static void StartPendingTextUse()
    {
        BountyClashUIFixture.ClearIntents(); QuickSlotPlayChecks.PressPrimary(); Later(FocusPendingText);
    }
    private static void FocusPendingText()
    {
        QuickSlotPlayChecks.ExpectUseIntent(s_Item, 1);
        Check(RaidHUD.TimedActionInputActive, "Second real Use remains pending before text focus");
        s_Field.Focus(); Later(PendingTextX);
    }
    private static void PendingTextX()
    {
        Check(QuickSlotBindings.Typing(Root.panel.focusController.focusedElement as VisualElement), "Pending action has actual live TextField focus");
        QuickSlotPlayChecks.HoldInput(.3f, false, Key.X); Later(CheckPendingTextX);
    }
    private static void CheckPendingTextX()
    {
        Check(Requests(TacticalItemAction.Cancel).Count == 0, "Typing X does not cancel the pending action");
        QuickSlotPlayChecks.ExpectUseIntent(s_Item, 1); QuickSlotPlayChecks.ExpectWeaponSuppressed(); Quantity();
        // Explicit UI-only cleanup; no server action or consumption is simulated.
        BountyClashUIFixture.AcknowledgeFixture(); Finish(true, null);
    }
    public static void Stop() { if (Status == "running") Finish(false, "Stopped by fixture owner"); }
    private static void Finish(bool passed, string error)
    {
        s_Next = null; EditorApplication.update -= Tick;
        try { QuickSlotPlayChecks.HoldInput(.05f, false); } catch { }
        s_Field?.Blur(); s_Field?.RemoveFromHierarchy(); s_Field = null;
        Status = passed ? "passed" : "failed";
        var result = new { Status, Checks = s_Checks, ItemId = s_Item, InitialQuantity = s_Quantity, LocalUIOnly = true,
            NoTransport = true, ServerConsumptionTested = false, Acknowledgement = "explicit fixture injection", Error = error, Trace };
        if (!string.IsNullOrEmpty(s_File)) File.WriteAllText(s_File, JsonConvert.SerializeObject(result, Formatting.Indented));
        Debug.Log("QuickSlot cancel input checks " + Status + ": " + s_Checks + " assertions; evidence=" + s_File + (error == null ? "" : "; " + error));
    }
}
