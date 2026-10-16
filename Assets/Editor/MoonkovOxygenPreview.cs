using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;

public sealed class MoonkovOxygenPreview : EditorWindow
{
    private PredictedPlayerGhost m_State;
    private InventoryGraph m_Inventory;
    private RaidHealthView m_View;
    private Slider m_Oxygen;
    private Label m_Status;
    private int m_Mode;
    private AtmosphereSample Atmosphere => m_Mode == 1
        ? new AtmosphereSample("Preview room", 101.325f, .21f, true, false) : default;
    private float Supply => m_Mode == 2 ? 10 : 0;

    [MenuItem("Tools/Moonkov/Oxygen/Preview Health Panel")]
    public static void Open()
    {
        var window = GetWindow<MoonkovOxygenPreview>();
        window.titleContent = new GUIContent("Oxygen / Health Preview");
        window.minSize = new Vector2(1100, 750); window.Show();
    }

    public void CreateGUI()
    {
        m_View?.Dispose(); rootVisualElement.Clear();
        minSize = new Vector2(1100, 750);
        var toolbar = new VisualElement(); toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.height = 32; toolbar.style.flexShrink = 0; rootVisualElement.Add(toolbar);
        m_Oxygen = new Slider("Oxygen", 0, 100) { showInputField = true };
        m_Oxygen.style.flexGrow = 1; m_Oxygen.style.marginRight = 12; toolbar.Add(m_Oxygen);
        m_Oxygen.RegisterValueChangedCallback(e =>
        {
            m_State.Oxygen = e.newValue; m_State.HypoxiaSeconds = 0;
            RefreshEnvironment(); Present();
        });
        toolbar.Add(new Button(() => SetMode(0)) { text = "Vacuum" });
        toolbar.Add(new Button(() => SetMode(1)) { text = "Breathable" });
        toolbar.Add(new Button(() => SetMode(2)) { text = "Refill Station" });
        toolbar.Add(new Button(Step) { text = "Step 10s" });
        toolbar.Add(new Button(ResetState) { text = "Reset" });
        m_Status = new Label(); m_Status.style.height = 26; m_Status.style.flexShrink = 0;
        rootVisualElement.Add(m_Status);
        var host = new VisualElement(); host.AddToClassList("raid-ui");
        host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/RaidUI"));
        host.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/RaidHealthStyles"));
        host.style.position = Position.Relative; host.style.flexGrow = 1; host.style.minHeight = 0;
        rootVisualElement.Add(host);
        m_View = new RaidHealthView(host, _ => { }, Consume);
        ResetState();
    }

    private void ResetState()
    {
        m_State = default; RaidHealth.Initialize(ref m_State); m_Mode = 0;
        m_Inventory = InventoryGraph.Create(false, false);
        m_Inventory.AddSupply(RaidOxygenConsumables.CanisterCode, 3, InventoryCatalog.Pockets);
        RefreshEnvironment(); Present();
    }
    private void SetMode(int mode) { m_Mode = mode; RefreshEnvironment(); Present(); }
    private void RefreshEnvironment()
    {
        // Sample the real rules on a disposable copy; switching modes advances no time.
        var sample = m_State;
        RaidOxygen.Tick(ref sample, Atmosphere, OxygenParameters.Default, 1, 0, Supply);
        m_State.BreathableAir = sample.BreathableAir;
        m_State.AmbientPressureKPa = sample.AmbientPressureKPa;
        m_State.AmbientOxygenPartialPressureKPa = sample.AmbientOxygenPartialPressureKPa;
        m_State.OxygenDrainPerSecond = sample.OxygenDrainPerSecond;
        m_State.OxygenSupplyPerSecond = sample.OxygenSupplyPerSecond;
        if (m_State.Oxygen > 0 || !RaidOxygen.NeedsSuitAir(m_State))
            m_State.HypoxiaSeconds = m_State.OxygenGraceRemaining = 0;
        else m_State.OxygenGraceRemaining = Mathf.Max(0, OxygenParameters.Default.GraceSeconds - m_State.HypoxiaSeconds);
    }
    private void Step()
    {
        RaidOxygen.Tick(ref m_State, Atmosphere, OxygenParameters.Default, 10, 1, Supply);
        Present();
    }
    private void Consume(string code)
    {
        var item = m_Inventory.Items.Find(i => i.Code == code && m_Inventory.ConsumableAccessible(i));
        if (item == null) return;
        var result = RaidOxygenConsumables.Consume(ref m_State, m_Inventory, item.Id, m_Inventory.Version);
        m_View.Message(result == InventoryError.None ? "" : result.ToString()); Present();
    }
    private void Present()
    {
        m_Oxygen.SetValueWithoutNotify(m_State.Oxygen);
        m_Status.text = "LOCAL PREVIEW / " + (m_Mode == 0 ? "VACUUM" : m_Mode == 1 ? "BREATHABLE, NO TANK REFILL" : "VACUUM UMBILICAL") +
            " / Changes affect only this window. Reset restores health and three canisters.";
        m_View.Present(m_State, 0, false, oxygenCanisters: m_Inventory.Count(RaidOxygenConsumables.CanisterCode, true));
    }
    private void OnDisable() { m_View?.Dispose(); m_View = null; m_Inventory = null; }
}
