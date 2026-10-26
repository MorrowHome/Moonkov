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
        RaidHealth.Set(ref m_State,BodyPart.Chest,65); RaidHealth.Set(ref m_State,BodyPart.Abdomen,50);
        RaidHealth.Set(ref m_State,BodyPart.LeftArm,25); RaidHealth.Set(ref m_State,BodyPart.LeftLeg,45);
        if (scenario == 1) { m_State.Oxygen = 0; m_State.Energy = 0; m_State.Hydration = 0; }
        if (scenario == 2) { m_State.CurrentHealth = 0; }
        if (scenario == 3) m_State.BodyHealthInitialized = false;
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
    private void OnDisable()
    {
        m_View?.Dispose(); m_View = null; rootVisualElement.Clear();
        if (m_PreviousLocale != null) LocalizationSettings.SelectedLocale = m_PreviousLocale;
    }
}
