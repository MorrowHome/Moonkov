using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine.Localization.Tables;

public static class RaidReturnGuidanceChecks
{
    [MenuItem("Tools/Moonkov/Check Return Guidance")]
    public static string Run()
    {
        int count = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); count++; }
        var guide = new RaidReturnGuidance();
        bool Tick(float seconds, float now = 0, bool active = true, bool extracting = false, bool visible = true, int raid = 1)
            => guide.Update(raid, seconds, active, extracting, visible, now);
        Check(!Tick(301) && !guide.ShowBeacon, "Normal expedition stays quiet");
        Check(Tick(300) && guide.Stage == 1 && guide.ShowBeacon, "Five-minute boundary reveals beacon");
        Check(!Tick(299, 1), "Repeated snapshot does not repeat sound");
        Check(!Tick(290, 8) && !guide.ShowBeacon, "Early reminder closes after eight seconds");
        Check(Tick(120, 10) && guide.Stage == 2 && guide.ShowBeacon, "Two-minute boundary requests return");
        Check(!Tick(100, 30) && guide.ShowBeacon, "Urgent beacon stays visible");
        Check(Tick(60, 40) && guide.Stage == 3, "Final-minute boundary escalates");
        Check(!Tick(59, 41) && guide.ShowBeacon, "Final minute does not spam");
        Check(!Tick(0, 42) && !guide.ShowBeacon, "Expiry stops guidance");
        Check(!Tick(30, 43, active: false) && !guide.ShowBeacon, "Death or settlement hides guidance");
        Check(Tick(30, raid: 2) && guide.Stage == 3, "New raid and late join emit only current urgency");
        Check(!Tick(240, 1, raid: 2), "Corrected timer does not replay obsolete warnings");
        guide.Reset();
        Check(!Tick(290, visible: false), "Hidden HUD defers warning");
        Check(!Tick(100, 10, visible: false), "Hidden HUD does not queue multiple sounds");
        Check(Tick(50, 20) && guide.Stage == 3, "Reopening presents only latest warning");
        Check(!Tick(49, 21), "Deferred warning emits once");
        guide.Reset();
        Check(!Tick(50, extracting: true), "Extraction countdown has priority over warning sound");
        Check(Tick(45, 2), "Cancelled extraction receives pending warning");
        guide.Reset();
        Check(!Tick(float.NaN) && !guide.ShowBeacon, "NaN does not trigger warning");
        Check(!Tick(float.PositiveInfinity) && !guide.ShowBeacon, "Infinite time does not trigger warning");
        Check(!Tick(-1) && !guide.ShowBeacon, "Negative time does not trigger warning");
        Check(!Tick(50, raid: 0) && !guide.ShowBeacon, "Connecting state does not trigger warning");
        Check(Tick(50), "Reset allows a new expedition reminder");
        foreach (string locale in new[] { "en", "zh-Hans" })
        {
            var table = AssetDatabase.LoadAssetAtPath<StringTable>($"Assets/Localization/Tables/MoonkovUI_{locale}.asset");
            foreach (float seconds in new[] { 300f, 120f, 60f })
            {
                guide.Reset(); Tick(seconds);
                string text = table?.GetEntry(guide.BeaconText)?.LocalizedValue;
                Check(!string.IsNullOrEmpty(text) && (locale == "en" || text != guide.BeaconText) &&
                    string.Format(text, 1294f, "N").Contains("1294"), $"Localized return warning: {locale}/{seconds}");
            }
        }
        return $"PASS: {count} return guidance checks; no profiles, scenes or network changed.";
    }
}
