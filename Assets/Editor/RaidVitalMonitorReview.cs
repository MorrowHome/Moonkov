using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Localization.Settings;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using BodyPart = Unity.MP_FPS.BodyPart;

// Actual runtime presenters in an Editor-only window; fixture values never reach a player or profile.
public sealed class RaidVitalMonitorReview : EditorWindow
{
    private RaidHealthView m_View;
    private RaidVitalsMonitor m_Combat;
    private VisualElement m_Host;
    private PredictedPlayerGhost m_State;
    private UnityEngine.Localization.Locale m_PreviousLocale;
    private BodyPart m_Treated = BodyPart.Auto;
    private string m_Consumed;
    [MenuItem("Tools/Moonkov/Review Vital Monitor")]
    public static void Open()
    {
        var window = GetWindow<RaidVitalMonitorReview>(); window.titleContent = new GUIContent("Vital monitor review");
        window.position = new Rect(40,70,1160,760); window.ShowUtility(); window.Focus(); window.Repaint();
    }
    public void CreateGUI()
    {
        m_View?.Dispose(); rootVisualElement.Clear();
        m_PreviousLocale = LocalizationSettings.SelectedLocale;
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
        LocalizationSettings.InitializationOperation.WaitForCompletion();
        rootVisualElement.style.backgroundColor = (Color)new Color32(12,16,18,255);
        rootVisualElement.style.flexDirection = FlexDirection.Row;
        var combat = new VisualElement(); combat.style.width = 300; combat.style.flexShrink = 0;
        combat.style.backgroundColor = (Color)new Color32(70,74,69,255); rootVisualElement.Add(combat);
        var hud = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/PlayerHUD.uxml").CloneTree();
        hud.style.flexGrow = 1; combat.Add(hud);
        hud.Q("weapon-info-container").style.display = DisplayStyle.None; hud.Q("reticle-container").style.display = DisplayStyle.None;
        m_Combat = new RaidVitalsMonitor(); hud.Q("health-body-host").Add(m_Combat);
        m_Host = new VisualElement(); m_Host.AddToClassList("raid-ui"); m_Host.style.flexGrow = 1; rootVisualElement.Add(m_Host);
        m_View = new RaidHealthView(m_Host, part => m_Treated = part, code => m_Consumed = code);
        Scenario(0); SourceFont();
    }
    private void SourceFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf");
        if (font) rootVisualElement.Query<TextElement>().ForEach(t => t.style.unityFontDefinition = FontDefinition.FromFont(font));
    }
    public void Scenario(int scenario)
    {
        m_State = default; RaidHealth.Initialize(ref m_State);
        m_State.Oxygen = 67; m_State.Energy = 54; m_State.Hydration = 42; m_State.Satiety = 36;
        m_State.HaloEnergyInitialized = true; m_State.HaloEquipped = true;
        m_State.HaloCapacity = 100; m_State.HaloEnergy = 62;
        m_State.EquippedWeaponID = DollSingerWeapons.Halo;
        RaidHealth.Set(ref m_State,BodyPart.Chest,65); RaidHealth.Set(ref m_State,BodyPart.Abdomen,50);
        RaidHealth.Set(ref m_State,BodyPart.LeftArm,25); RaidHealth.Set(ref m_State,BodyPart.LeftLeg,45);
        if (scenario == 1) { m_State.Oxygen = 0; m_State.Energy = 0; m_State.Hydration = 0; }
        if (scenario == 2) { m_State.CurrentHealth = 0; }
        if (scenario == 3) m_State.BodyHealthInitialized = false;
        if (scenario == 4) m_State.HaloEnergy = 0;
        if (scenario == 5) { m_State.HaloCapacity = 150; m_State.HaloEnergy = 150; }
        if (scenario == 6) m_State.HaloEquipped = false;
        if (scenario == 7 || scenario == 10) m_State.EquippedWeaponID = DollSingerWeapons.Shotgun;
        if (scenario == 8) m_State.EquippedWeaponID = DollSingerWeapons.Sniper;
        if (scenario == 9) m_State.EquippedWeaponID = DollSingerWeapons.Revolver;
        if (scenario == 10) m_State.HaloEnergy = 18;
        var combatPanel = rootVisualElement.Q("health-info-container");
        combatPanel.style.height = m_State.HaloEquipped && m_State.BodyHealthInitialized ? 302 : 284;
        m_Combat.Present(m_State); m_View.Present(m_State,4,false,2,2,1);
    }
    public static string CheckInteraction()
    {
        var w = Resources.FindObjectsOfTypeAll<RaidVitalMonitorReview>().First();
        w.m_Host.Q<Button>("healthPartLeftArm").Focus();
        using (var submit = NavigationSubmitEvent.GetPooled())
        { submit.target = w.m_Host.Q<Button>("healthTreat"); w.m_Host.Q<Button>("healthTreat").SendEvent(submit); }
        if (w.m_Treated != BodyPart.LeftArm) throw new Exception("Treatment did not target anatomical left arm");
        w.m_View.Present(w.m_State,4,true);
        if (w.m_Host.Q<Button>("healthTreat").enabledSelf || w.m_Host.Q<Button>("healthEat").enabledSelf) throw new Exception("Busy actions remain enabled");
        w.m_View.Present(w.m_State,0,false);
        if (w.m_Host.Q<Button>("healthTreat").enabledSelf) throw new Exception("Missing medicine remains usable");
        w.Scenario(0);
        foreach (var pair in new[] { ("healthEat", "ration"), ("healthDrink", "water"), ("healthRefillOxygen", "oxygen_canister") })
        {
            w.m_Consumed = null; var button = w.m_Host.Q<Button>(pair.Item1);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
            if (w.m_Consumed != pair.Item2) throw new Exception("Supply action did not target " + pair.Item2);
        }
        w.Scenario(0); w.SourceFont();
        return "PASS: scan selection/treatment target, busy/no supplies gating, and food/water/oxygen submit bindings.";
    }
    public static void EnglishCompact()
    {
        var w = Resources.FindObjectsOfTypeAll<RaidVitalMonitorReview>().First();
        w.position = new Rect(40,70,980,660);
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en"); w.Scenario(0); w.SourceFont();
    }

    public static string CheckHaloEnergy()
    {
        var w = Resources.FindObjectsOfTypeAll<RaidVitalMonitorReview>().First();
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        int Filled(VisualElement monitor, int row) => monitor.Q("vitals-reserve-" + row).Query<RaidReserveIcon>(className: "vitals-cell")
            .ToList().Count(cell => cell.Filled);
        foreach (int scenario in new[] { 0, 4, 5, 6 })
        {
            w.Scenario(scenario);
            var tab = w.m_Host.Q("vitals-monitor");
            bool equipped = scenario != 6;
            int expected = scenario == 0 ? 6 : scenario == 5 ? 10 : 0;
            Check(Filled(w.m_Combat, 4) == expected && Filled(tab, 4) == expected, "HUD and health page must show the same normalized halo energy");
            Check((w.m_Combat.Q("vitals-reserve-4").style.display.value != DisplayStyle.None) == equipped,
                "Unequipped halo row must disappear");
            Check(Filled(w.m_Combat, 1) == 5 && Filled(tab, 1) == 5, "Halo changes must not change body nutrition");
        }
        w.Scenario(0); w.m_State.Oxygen = 0;
        w.m_Combat.Present(w.m_State, haloNotice: HaloChargeNotice.LowEnergy);
        Check(w.m_Combat.Q<Label>("vitals-warning").text != MoonkovLocalization.Text("NO BATTERY READY"), "Battery notice must not mask hypoxia");
        w.Scenario(0); w.SourceFont();
        return $"PASS: {checks} shared HUD/health energy, capacity, unequipped and medical warning checks.";
    }

    public static string CheckHaloAppearance()
    {
        var w = Resources.FindObjectsOfTypeAll<RaidVitalMonitorReview>().First();
        var catalog = HaloAppearanceCatalog.Current;
        if (!catalog || catalog.Entries.Length != 13) throw new Exception("Missing formal halo appearances");
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        w.Scenario(0);
        var bodyColor = w.m_Combat.Q("vitals-reserve-1").Q<RaidReserveIcon>(className: "vitals-cell").Tint;
        foreach (var entry in catalog.Entries)
        {
            // Keep energy and cell count identical: color must still refresh when switching weapons.
            w.m_State.EquippedWeaponID = entry.Weapon;
            w.m_Combat.Present(w.m_State); w.m_View.Present(w.m_State, 4, false, 2, 2, 1);
            foreach (var monitor in new VisualElement[] { w.m_Combat, w.m_Host.Q("vitals-monitor") })
            {
                var cells = monitor.Q("vitals-reserve-4").Query<RaidReserveIcon>(className: "vitals-cell").ToList();
                Check(cells.Count == 10 && cells.Take(6).All(cell => cell.Filled) && cells.Skip(6).All(cell => !cell.Filled) &&
                    cells.All(cell => cell.Tint == entry.Color && cell.Artwork == RaidReserveIcon.HaloArtwork(entry.Weapon)),
                    "Every energy cell must use the held halo artwork, with six of ten filled at 62%: " + entry.Weapon);
                Check(monitor.Q("vitals-reserve-4").Q<Label>().text.Contains("62%"), "Halo percentage must show exact energy");
                Check(monitor.Q("vitals-reserve-1").Q<RaidReserveIcon>(className: "vitals-cell").Tint == bodyColor,
                    "Weapon color leaked into body nutrition");
            }
        }
        w.Scenario(10);
        var low = w.m_Combat.Q("vitals-reserve-4");
        Check(low.Q<RaidReserveIcon>(className: "vitals-cell").Tint == catalog.ForWeapon(DollSingerWeapons.Shotgun).Color,
            "Low energy must preserve the equipped halo color");
        Check(low.Q<Label>().style.color.value != RaidVitalsMonitor.SignalColor, "Low energy label lost its warning");
        w.m_State.HaloEnergy = 0; w.m_Combat.Present(w.m_State);
        Check(low.Query<RaidReserveIcon>(className: "vitals-cell").ToList().All(cell => !cell.Filled), "Empty halo has filled cells");
        w.Scenario(0); w.SourceFont();
        return $"PASS: {checks} HUD/Tab weapon color, same-energy switching, empty-cell and low-energy warning checks.";
    }
    private void OnDisable()
    {
        m_View?.Dispose(); m_View = null; rootVisualElement.Clear();
        if (m_PreviousLocale != null) LocalizationSettings.SelectedLocale = m_PreviousLocale;
    }
}
