using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // One transparent instrument shared by combat and the health page.
    public sealed class RaidVitalsMonitor : VisualElement
    {
        public const string AnimatePreference = "Moonkov.AnimateVitalSignal";
        public static readonly Color SignalColor = new Color32(196, 225, 255, 255);
        private static readonly Color WarningColor = new Color32(235, 169, 93, 255);
        private static readonly Color DangerColor = new Color32(245, 121, 94, 255);
        private static readonly Color ReserveWarningColor = new Color32(255, 197, 61, 255); // Radix amber 9.
        private static readonly Color ReserveDangerColor = new Color32(229, 72, 77, 255); // Radix red 9.
        private readonly RaidBodyDiagram m_Body;
        private readonly VitalWaveform m_Wave;
        private readonly Label m_Warning;
        private const int StaminaReserve = 5;
        private readonly VisualElement[][] m_Reserves = new VisualElement[6][];
        private readonly int[] m_Counts = { -1, -1, -1, -1, -1, -1 };
        private readonly Color[] m_Colors = new Color[6];
        private readonly VisualElement m_HaloRow;
        private readonly Label m_HaloLabel;
        private bool m_HaloVisible;
        private uint m_HaloWeapon = uint.MaxValue;
        private Color m_HaloLabelColor;
        private string m_LastWarning;
        private readonly Label m_OxygenLabel;
        private int m_LastOxygen = -1;
        private int m_LastHaloEnergy = -1;
        private int m_LastGrace = -1;
        private int m_LastOxygenSeconds = -1;

        public RaidVitalsMonitor(Action<BodyPart> select = null)
        {
            name = "vitals-monitor"; AddToClassList("vitals-monitor"); AddToClassList("moonkov-style-font");
            styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/RaidVitalsStyles"));
            pickingMode = select == null ? PickingMode.Ignore : PickingMode.Position;
            var upper = new VisualElement { pickingMode = PickingMode.Ignore }; upper.AddToClassList("vitals-upper"); Add(upper);
            var bodyHost = new VisualElement { name = "vitals-body", pickingMode = PickingMode.Ignore };
            bodyHost.AddToClassList("vitals-body"); upper.Add(bodyHost);
            m_Body = new RaidBodyDiagram(select); bodyHost.Add(m_Body);
            var signals = new VisualElement { pickingMode = PickingMode.Ignore }; signals.AddToClassList("vitals-signals");
            var trace = new VisualElement { pickingMode = PickingMode.Ignore }; trace.AddToClassList("vitals-trace"); upper.Add(trace);
            m_Wave = new VitalWaveform(); trace.Add(m_Wave);
            m_Warning = new Label { name = "vitals-warning", pickingMode = PickingMode.Ignore };
            m_Warning.AddToClassList("vitals-warning"); signals.Add(m_Warning); Add(signals);
            string[] captions = { "O₂", "BODY ENERGY", "HYDRATION", "STOMACH", "HALO", "STAMINA" };
            for (int i = 0; i < captions.Length; i++)
            {
                var row = new VisualElement { name = "vitals-reserve-" + i, pickingMode = PickingMode.Ignore }; row.AddToClassList("vitals-reserve"); signals.Add(row);
                // Keep existing resource IDs stable; put short-term stamina directly below oxygen.
                if (i == StaminaReserve) signals.Insert(2, row);
                if (i == 4) { m_HaloRow = row; row.AddToClassList("vitals-reserve-halo"); row.style.display = DisplayStyle.None; }
                var label = new Label(captions[i]) { pickingMode = PickingMode.Ignore }; label.AddToClassList("vitals-reserve-label"); row.Add(label);
                if (i == 0) m_OxygenLabel = label;
                if (i == 4) m_HaloLabel = label;
                var cells = new VisualElement { pickingMode = PickingMode.Ignore }; cells.AddToClassList("vitals-cells"); row.Add(cells);
                m_Reserves[i] = new VisualElement[10];
                for (int c = 0; c < m_Reserves[i].Length; c++)
                { var icon = new RaidReserveIcon(i); cells.Add(icon); m_Reserves[i][c] = icon; }
            }
            RegisterCallback<AttachToPanelEvent>(_ => MoonkovLocalization.Bind(this));
        }

        public void Present(in PredictedPlayerGhost state, BodyPart selected = BodyPart.Auto, HaloChargeNotice haloNotice = HaloChargeNotice.None)
        {
            m_Body.Present(state, selected);
            var signal = RaidVitalSignal.Read(state); m_Wave.Present(signal);
            int oxygen = signal.Ready ? RaidOxygen.DisplayPercent(state.Oxygen) : -1;
            if (oxygen != m_LastOxygen)
            {
                m_LastOxygen = oxygen;
                if (panel != null && oxygen >= 0) MoonkovLocalization.Set(m_OxygenLabel, "O₂  {0}%", oxygen);
                else m_OxygenLabel.text = oxygen < 0 ? "O₂" : $"O₂  {oxygen}%";
            }
            Reserve(0, state.Oxygen, signal.Ready);
            Reserve(1, state.Energy, signal.Ready);
            Reserve(2, state.Hydration, signal.Ready);
            Reserve(3, state.Satiety, signal.Ready);
            Reserve(StaminaReserve, state.TacticalSprintStamina / TacticalSprintRules.Capacity * 100, signal.Ready);
            bool haloVisible = signal.Ready && state.HaloEnergyInitialized && state.HaloEquipped;
            if (m_HaloVisible != haloVisible)
            {
                m_HaloVisible = haloVisible;
                m_Counts[4] = -1;
                m_HaloRow.style.display = haloVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            float haloPercent = HaloEnergyRules.Energy(state) / HaloEnergyRules.Maximum(state) * 100;
            int haloDisplay = haloVisible ? Mathf.FloorToInt(Mathf.Clamp(haloPercent, 0, 100)) : -1;
            if (m_LastHaloEnergy != haloDisplay)
            {
                m_LastHaloEnergy = haloDisplay;
                if (panel != null && haloDisplay >= 0) MoonkovLocalization.Set(m_HaloLabel, "{0} {1}%", "HALO", haloDisplay);
                else m_HaloLabel.text = haloDisplay < 0 ? "HALO" : $"HALO {haloDisplay}%";
            }
            Color labelColor = haloVisible && haloPercent <= 0 ? DangerColor : haloVisible && haloPercent <= 25 ? WarningColor : SignalColor;
            if (m_HaloLabelColor != labelColor) { m_HaloLabelColor = labelColor; m_HaloLabel.style.color = labelColor; }
            var appearance = HaloAppearanceCatalog.Current;
            uint haloWeapon = HaloAppearanceCatalog.CurrentWeapon(state);
            if (haloWeapon != m_HaloWeapon)
            {
                m_HaloWeapon = haloWeapon;
                var artwork = RaidReserveIcon.HaloArtwork(haloWeapon);
                foreach (RaidReserveIcon icon in m_Reserves[4]) icon.SetArtwork(artwork);
            }
            Color haloColor = appearance ? appearance.ForWeapon(haloWeapon).Color : SignalColor;
            Reserve(4, haloPercent, haloVisible, haloColor);
            string warning = signal.Warning;
            if (string.IsNullOrEmpty(warning) && signal.Alive && state.Oxygen < RaidOxygen.Capacity && state.OxygenSupplyPerSecond > state.OxygenDrainPerSecond)
                warning = "OXYGEN REFILLING";
            // Reuse the instrument's brief notice line; medical warnings always take priority.
            if (string.IsNullOrEmpty(warning) && haloVisible)
                warning = haloNotice == HaloChargeNotice.LowEnergy ? "NO BATTERY READY" :
                    haloNotice == HaloChargeNotice.MoveCells ? "BATTERY OUT OF REACH" :
                    haloNotice == HaloChargeNotice.Full ? "HALO FULL" : "";
            if (string.IsNullOrEmpty(warning) && signal.Alive && state.TacticalSprintExhausted) warning = "FATIGUED";
            int grace = warning == "OXYGEN DEPLETED" ? Mathf.CeilToInt(state.OxygenGraceRemaining) : -1;
            float estimate = warning == "OXYGEN REFILLING" ? RaidOxygen.RefillSeconds(state) :
                warning == "LOW OXYGEN" || warning == "CRITICAL OXYGEN" ? RaidOxygen.RemainingSeconds(state) : float.PositiveInfinity;
            int seconds = float.IsFinite(estimate) && estimate >= 0 && estimate < int.MaxValue - 1024 ? Mathf.CeilToInt(estimate) : -1;
            if (m_LastWarning == warning && m_LastGrace == grace && m_LastOxygenSeconds == seconds) return;
            m_LastWarning = warning;
            m_LastGrace = grace;
            m_LastOxygenSeconds = seconds;
            m_Warning.style.display = string.IsNullOrEmpty(warning) ? DisplayStyle.None : DisplayStyle.Flex;
            m_Warning.style.color = signal.Critical ? DangerColor : warning == "OXYGEN REFILLING" ? SignalColor : WarningColor;
            if (grace >= 0)
            {
                if (panel != null) MoonkovLocalization.Set(m_Warning, "OXYGEN DEPLETED / {0}s", grace);
                else m_Warning.text = $"OXYGEN DEPLETED / {grace}s";
            }
            else if (seconds >= 0)
            {
                string clock = $"{seconds / 60:00}:{seconds % 60:00}";
                if (panel != null) MoonkovLocalization.Set(m_Warning, "{0} / {1}", warning, clock);
                else m_Warning.text = warning + " / " + clock;
            }
            else if (panel != null) MoonkovLocalization.Set(m_Warning, warning); else m_Warning.text = warning;
        }

        private static Color ReserveColor(float value, int index)
        {
            // Whole-percent steps preserve the style cache while the server drains fractional reserves.
            int percent = Mathf.RoundToInt(Mathf.Clamp(float.IsFinite(value) ? value : 0, 0, 100));
            Color full = index == 2 ? (Color)new Color32(0, 162, 199, 255) :
                index == 1 || index == 3 ? (Color)new Color32(255, 197, 61, 255) :
                index == 5 ? (Color)new Color32(134, 234, 212, 255) : SignalColor;
            return percent <= 50 ? Color.Lerp(ReserveDangerColor, ReserveWarningColor, percent / 50f) :
                Color.Lerp(ReserveWarningColor, full, (percent - 50) / 50f);
        }

        private void Reserve(int index, float value, bool ready) => Reserve(index, value, ready, ready ? ReserveColor(value, index) : SignalColor);

        private void Reserve(int index, float value, bool ready, Color color)
        {
            int filled = !ready ? 0 : RaidVitalSignal.ReserveCells(value, m_Reserves[index].Length);
            if (m_Counts[index] == filled && m_Colors[index] == color) return;
            m_Counts[index] = filled; m_Colors[index] = color;
            for (int i = 0; i < m_Reserves[index].Length; i++)
            {
                var icon = (RaidReserveIcon)m_Reserves[index][i];
                icon.Present(i < filled, color, ready);
            }
        }

        private sealed class VitalWaveform : VisualElement
        {
            private readonly IVisualElementScheduledItem m_Ticker;
            private RaidVitalSignal m_Signal;
            private float m_Phase, m_LastClock;
            private bool m_WasAnimating;
            public VitalWaveform()
            {
                name = "vitals-waveform"; AddToClassList("vitals-waveform"); pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
                m_Ticker = schedule.Execute(Advance).Every(33); m_Ticker.Pause();
                RegisterCallback<AttachToPanelEvent>(_ => { m_LastClock = Time.realtimeSinceStartup; m_Ticker.Resume(); });
                RegisterCallback<DetachFromPanelEvent>(_ => m_Ticker.Pause());
            }
            public void Present(RaidVitalSignal signal)
            {
                bool changed = m_Signal.Ready != signal.Ready || m_Signal.Alive != signal.Alive || m_Signal.Critical != signal.Critical;
                m_Signal = signal; if (changed) MarkDirtyRepaint();
            }
            private void Advance()
            {
                float now = Time.realtimeSinceStartup, delta = Mathf.Clamp(now - m_LastClock, 0, .1f); m_LastClock = now;
                for (VisualElement node = this; node != null; node = node.parent)
                    if (!node.visible || node.resolvedStyle.display == DisplayStyle.None) { m_WasAnimating = false; return; }
                bool animate = m_Signal.Ready && m_Signal.Alive && PlayerPrefs.GetInt(AnimatePreference, 1) != 0 &&
                    (!Application.isPlaying || !GameSettings.Instance.IsPauseMenuOpen);
                if (!animate) { if (m_WasAnimating) MarkDirtyRepaint(); m_WasAnimating = false; return; }
                m_WasAnimating = true; m_Phase = Mathf.Repeat(m_Phase + delta * m_Signal.Frequency, 1); MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext context)
            {
                float width = contentRect.width, height = contentRect.height;
                if (width < 1 || height < 1) return;
                var painter = context.painter2D;
                Color color = !m_Signal.Ready ? new Color(.5f, .55f, .49f, .5f) : m_Signal.Critical ? DangerColor : SignalColor;
                // Constant scrolling phase keeps peaks continuous. The preference freezes a representative trace.
                float phase = PlayerPrefs.GetInt(AnimatePreference, 1) == 0 ? 0 : m_Phase;
                int steps = Mathf.Clamp(Mathf.CeilToInt(height * 1.5f), 128, 320);
                for (int pass = 0; pass < 2; pass++)
                {
                    painter.strokeColor = pass == 0 ? new Color(.02f, .04f, .025f, .8f) : color;
                    painter.lineWidth = pass == 0 ? 3.2f : 1.5f;
                    painter.BeginPath();
                    for (int i = 0; i <= steps; i++)
                    {
                        float y = height * i / steps;
                        float amplitude = m_Signal.Ready && m_Signal.Alive ? RaidVitalSignal.Sample((float)i / steps * 2.3f - phase) : 0;
                        var point = new Vector2(width * .48f + amplitude * width * .44f, y);
                        if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
                    }
                    painter.Stroke();
                }
            }
        }
    }
}
