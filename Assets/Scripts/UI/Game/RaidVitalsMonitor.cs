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
        private readonly RaidBodyDiagram m_Body;
        private readonly VitalWaveform m_Wave;
        private readonly Label m_Warning;
        private readonly VisualElement[][] m_Reserves = new VisualElement[4][];
        private readonly int[] m_Counts = { -1, -1, -1, -1 };
        private readonly Color[] m_Colors = new Color[4];
        private string m_LastWarning;
        private readonly Label m_OxygenLabel;
        private int m_LastOxygen = -1;
        private int m_LastGrace = -1;

        public RaidVitalsMonitor(Action<BodyPart> select = null)
        {
            name = "vitals-monitor"; AddToClassList("vitals-monitor");
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
            string[] captions = { "O₂", "BODY ENERGY", "HYDRATION", "STOMACH" };
            for (int i = 0; i < 4; i++)
            {
                var row = new VisualElement { name = "vitals-reserve-" + i, pickingMode = PickingMode.Ignore }; row.AddToClassList("vitals-reserve"); signals.Add(row);
                var label = new Label(captions[i]) { pickingMode = PickingMode.Ignore }; label.AddToClassList("vitals-reserve-label"); row.Add(label);
                if (i == 0) m_OxygenLabel = label;
                var cells = new VisualElement { pickingMode = PickingMode.Ignore }; cells.AddToClassList("vitals-cells"); row.Add(cells);
                m_Reserves[i] = new VisualElement[12];
                for (int c = 0; c < 12; c++)
                { var tile = new VisualElement { pickingMode = PickingMode.Ignore }; tile.AddToClassList("vitals-cell"); cells.Add(tile); m_Reserves[i][c] = tile; }
            }
            RegisterCallback<AttachToPanelEvent>(_ => MoonkovLocalization.Bind(this));
        }

        public void Present(in PredictedPlayerGhost state, BodyPart selected = BodyPart.Auto)
        {
            m_Body.Present(state, selected);
            var signal = RaidVitalSignal.Read(state); m_Wave.Present(signal);
            int oxygen = signal.Ready && float.IsFinite(state.Oxygen) ? Mathf.CeilToInt(Mathf.Clamp(state.Oxygen, 0, 100)) : -1;
            if (oxygen != m_LastOxygen)
            {
                m_LastOxygen = oxygen;
                if (panel != null && oxygen >= 0) MoonkovLocalization.Set(m_OxygenLabel, "O₂  {0}%", oxygen);
                else m_OxygenLabel.text = oxygen < 0 ? "O₂" : $"O₂  {oxygen}%";
            }
            bool needsAir = RaidOxygen.NeedsSuitAir(state);
            Reserve(0, state.Oxygen, signal.Ready, needsAir && state.Oxygen <= 10 ? DangerColor : needsAir && state.Oxygen <= 25 ? WarningColor : SignalColor);
            Reserve(1, state.Energy, signal.Ready, state.Energy <= 0 ? DangerColor : state.Energy <= 25 ? WarningColor : SignalColor);
            Reserve(2, state.Hydration, signal.Ready, state.Hydration <= 0 ? DangerColor : state.Hydration <= 25 ? WarningColor : SignalColor);
            Reserve(3, state.Satiety, signal.Ready, state.Satiety >= RaidNutrition.FullThreshold ? WarningColor : SignalColor);
            string warning = signal.Warning;
            if (string.IsNullOrEmpty(warning) && signal.Alive && state.Oxygen < RaidOxygen.Capacity && state.OxygenSupplyPerSecond > state.OxygenDrainPerSecond)
                warning = "OXYGEN REFILLING";
            int grace = warning == "OXYGEN DEPLETED" ? Mathf.CeilToInt(state.OxygenGraceRemaining) : -1;
            if (m_LastWarning == warning && m_LastGrace == grace) return;
            m_LastWarning = warning;
            m_LastGrace = grace;
            m_Warning.style.display = string.IsNullOrEmpty(warning) ? DisplayStyle.None : DisplayStyle.Flex;
            m_Warning.style.color = signal.Critical ? DangerColor : warning == "OXYGEN REFILLING" ? SignalColor : WarningColor;
            if (grace >= 0)
            {
                if (panel != null) MoonkovLocalization.Set(m_Warning, "OXYGEN DEPLETED / {0}s", grace);
                else m_Warning.text = $"OXYGEN DEPLETED / {grace}s";
            }
            else if (panel != null) MoonkovLocalization.Set(m_Warning, warning); else m_Warning.text = warning;
        }

        private void Reserve(int index, float value, bool ready, Color color)
        {
            int filled = ready ? RaidVitalSignal.ReserveCells(value, 12) : 0;
            if (m_Counts[index] == filled && m_Colors[index] == color) return;
            m_Counts[index] = filled; m_Colors[index] = color;
            for (int i = 0; i < m_Reserves[index].Length; i++)
            {
                var tile = m_Reserves[index][i]; bool live = i < filled;
                tile.style.backgroundColor = live ? color : Color.clear;
                var line = live ? color : new Color(.48f, .55f, .44f, .55f);
                tile.style.borderLeftColor = tile.style.borderRightColor = tile.style.borderTopColor = tile.style.borderBottomColor = line;
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
