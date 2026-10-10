using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Read-only presentation of the owning player's replicated health. Treatment
    // sends an intent through RaidHUD's existing inventory request/ack stream.
    public sealed class RaidHealthView : IDisposable
    {
        private readonly VisualElement m_Root;
        private readonly Button[] m_Parts = new Button[RaidHealth.PartCount];
        private readonly Label[] m_Values = new Label[RaidHealth.PartCount];
        private readonly VisualElement[] m_Fills = new VisualElement[RaidHealth.PartCount];
        private readonly RaidBodyDiagram m_Diagram;
        private readonly Action<BodyPart> m_Heal;
        private readonly Button m_Treat;
        private PredictedPlayerGhost m_State;
        private BodyPart m_Selected = BodyPart.Chest;
        private bool m_Ready, m_Busy;
        private int m_Medicines;
        private string m_Feedback;
        public RaidHealthView(VisualElement parent, Action<BodyPart> heal)
        {
            m_Heal = heal;
            m_Root = TerminalLayout.Clone("RaidHealth", "raidHealthPage");
            // Clone detaches this page from its TemplateContainer; keep its own
            // stylesheet on the page, just as ContainerInventoryView does.
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/RaidHealthStyles")); parent.Add(m_Root);
            m_Diagram = new RaidBodyDiagram(); m_Root.Q("healthDiagram").Add(m_Diagram);
            var list = m_Root.Q<ScrollView>("healthParts");
            for (int i = 0; i < RaidHealth.PartCount; i++)
            {
                var part = (BodyPart)i;
                var row = new Button(() => Select(part)) { name = "healthPart" + part };
                row.AddToClassList("health-part"); list.Add(row); m_Parts[i] = row;
                var heading = new VisualElement(); heading.AddToClassList("health-part-heading"); row.Add(heading);
                var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList("health-part-name");
                heading.Add(label); MoonkovLocalization.Set(label, Name(part));
                m_Values[i] = new Label { pickingMode = PickingMode.Ignore }; m_Values[i].AddToClassList("health-part-value"); heading.Add(m_Values[i]);
                var meter = new VisualElement { pickingMode = PickingMode.Ignore }; meter.AddToClassList("health-meter"); row.Add(meter);
                var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("health-meter-fill"); meter.Add(fill); m_Fills[i] = fill;
            }
            m_Treat = m_Root.Q<Button>("healthTreat"); m_Treat.clicked += Treat;
            MoonkovLocalization.Bind(m_Root); Select(m_Selected);
        }
        public static string Name(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return "HEAD"; case BodyPart.Chest: return "CHEST";
                case BodyPart.Abdomen: return "ABDOMEN"; case BodyPart.LeftArm: return "LEFT ARM";
                case BodyPart.RightArm: return "RIGHT ARM"; case BodyPart.LeftLeg: return "LEFT LEG";
                case BodyPart.RightLeg: return "RIGHT LEG"; default: return "BODY";
            }
        }
        private void Select(BodyPart part)
        {
            m_Selected = part;
            m_Feedback = null;
            for (int i = 0; i < m_Parts.Length; i++) m_Parts[i].EnableInClassList("health-part-selected", i == (int)part);
            m_Diagram.Present(m_State, m_Selected); RenderTreatment();
        }
        private void Treat() { if (m_Treat.enabledSelf) m_Heal(m_Selected); }
        public void Message(string source) { m_Feedback = source; RenderTreatment(); }
        public void Present(in PredictedPlayerGhost state, int medicines, bool busy)
        {
            m_State = state; m_Ready = state.BodyHealthInitialized; m_Medicines = medicines; m_Busy = busy;
            MoonkovLocalization.Set(m_Root.Q<Label>("healthTotal"), m_Ready ? "{0:0} / {1:0}" : "Waiting for health snapshot", state.CurrentHealth, state.MaxHealth);
            for (int i = 0; i < RaidHealth.PartCount; i++)
            {
                var part = (BodyPart)i; float hp = RaidHealth.Get(state, part), max = RaidHealth.Maximum(part);
                MoonkovLocalization.Set(m_Values[i], m_Ready ? "{0:0} / {1:0}" : "—", hp, max);
                m_Fills[i].style.width = Length.Percent(m_Ready ? hp / max * 100 : 0);
                m_Fills[i].style.backgroundColor = RaidBodyDiagram.ConditionColor(hp / max);
                m_Values[i].EnableInClassList("health-danger", m_Ready && hp <= 0);
            }
            MoonkovLocalization.Set(m_Root.Q<Label>("healthOxygenValue"), m_Ready ? "{0:0}%" : "—", state.Oxygen);
            m_Root.Q("healthOxygenFill").style.width = Length.Percent(m_Ready ? state.Oxygen : 0);
            string stage = state.Oxygen <= 0 ? "HYPOXIA / SEEK AIR" : state.Oxygen <= 10 ? "CRITICAL OXYGEN" : state.Oxygen <= 25 ? "LOW OXYGEN" : "OXYGEN NORMAL";
            var stageLabel = m_Root.Q<Label>("healthOxygenStage"); MoonkovLocalization.Set(stageLabel, m_Ready ? stage : "Waiting for health snapshot");
            stageLabel.EnableInClassList("health-danger", m_Ready && state.Oxygen <= 10);
            stageLabel.EnableInClassList("health-caution", m_Ready && state.Oxygen > 10 && state.Oxygen <= 25);
            MoonkovLocalization.Set(m_Root.Q<Label>("healthAir"), state.BreathableAir ? "PRESSURIZED / REFILLING" : "VACUUM / RESERVE IN USE");
            string consequence = MoonkovLocalization.Text(state.LeftLegHealth <= 0 || state.RightLegHealth <= 0 ? "LEG DISABLED / SPRINT LIMITED" : "LEGS FUNCTIONAL");
            if (state.LeftArmHealth <= 0 || state.RightArmHealth <= 0) consequence += "\n" + MoonkovLocalization.Text("ARM DISABLED / HANDLING SLOWED");
            MoonkovLocalization.Set(m_Root.Q<Label>("healthConsequences"), m_Ready ? consequence : "");
            m_Diagram.Present(state, m_Selected); RenderTreatment();
        }
        private void RenderTreatment()
        {
            MoonkovLocalization.Set(m_Root.Q<Label>("healthSelectedPart"), "TREATMENT / {0}", MoonkovLocalization.Text(Name(m_Selected)));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthMedicalCount"), "ACCESSIBLE MEDICAL / {0}", m_Medicines);
            m_Treat.SetEnabled(m_Ready && !m_Busy && m_Medicines > 0 && RaidHealth.CanHeal(m_State, m_Selected));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthMedicalStatus"), m_Busy ? "Waiting for server confirmation…" :
                !string.IsNullOrEmpty(m_Feedback) ? m_Feedback : !m_Ready ? "Waiting for health snapshot" :
                m_Medicines == 0 ? "Medical supplies must be in pockets or the chest rig." :
                !RaidHealth.CanHeal(m_State, m_Selected) ? "This body part does not need treatment." : "One injector restores up to 40 HP to the selected part.");
        }
        public void Dispose() { m_Treat.clicked -= Treat; m_Root.RemoveFromHierarchy(); }
    }

    internal sealed class RaidBodyDiagram : VisualElement
    {
        private PredictedPlayerGhost m_State;
        private BodyPart m_Selected;
        public RaidBodyDiagram()
        {
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1; style.width = Length.Percent(100);
            generateVisualContent += Draw;
        }
        public void Present(in PredictedPlayerGhost state, BodyPart selected)
        {
            bool changed = m_Selected != selected || m_State.HeadHealth != state.HeadHealth || m_State.ChestHealth != state.ChestHealth ||
                m_State.AbdomenHealth != state.AbdomenHealth || m_State.LeftArmHealth != state.LeftArmHealth || m_State.RightArmHealth != state.RightArmHealth ||
                m_State.LeftLegHealth != state.LeftLegHealth || m_State.RightLegHealth != state.RightLegHealth || m_State.BodyHealthInitialized != state.BodyHealthInitialized;
            m_State = state; m_Selected = selected; if (changed) MarkDirtyRepaint();
        }
        public static Color ConditionColor(float fraction) => fraction <= 0 ? new Color(.25f, .24f, .23f) :
            fraction < .3f ? new Color(.72f, .28f, .22f) : fraction < .7f ? new Color(.72f, .54f, .24f) : new Color(.42f, .55f, .43f);
        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1 || contentRect.height < 1) return;
            var painter = context.painter2D;
            float scale = Mathf.Min(contentRect.width / 200, contentRect.height / 400);
            var origin = new Vector2((contentRect.width - 200 * scale) * .5f, (contentRect.height - 400 * scale) * .5f);
            void Shape(BodyPart part, params Vector2[] points)
            {
                painter.fillColor = m_State.BodyHealthInitialized ? ConditionColor(RaidHealth.Get(m_State, part) / RaidHealth.Maximum(part)) : Color.gray;
                painter.strokeColor = part == m_Selected ? new Color(.18f, .26f, .20f) : new Color(.70f, .73f, .65f);
                painter.lineWidth = part == m_Selected ? 3f : 1f;
                painter.BeginPath(); painter.MoveTo(origin + points[0] * scale);
                for (int i = 1; i < points.Length; i++) painter.LineTo(origin + points[i] * scale);
                painter.ClosePath(); painter.Fill(); painter.Stroke();
            }
            Shape(BodyPart.Head, new Vector2(85, 10), new Vector2(115, 10), new Vector2(123, 27), new Vector2(116, 53), new Vector2(107, 62), new Vector2(93, 62), new Vector2(84, 53), new Vector2(77, 27));
            Shape(BodyPart.Chest, new Vector2(86, 66), new Vector2(114, 66), new Vector2(141, 80), new Vector2(132, 144), new Vector2(68, 144), new Vector2(59, 80));
            Shape(BodyPart.Abdomen, new Vector2(70, 149), new Vector2(130, 149), new Vector2(135, 202), new Vector2(118, 220), new Vector2(82, 220), new Vector2(65, 202));
            // Front view: the subject's left is on the viewer's right.
            Shape(BodyPart.LeftArm, new Vector2(145, 82), new Vector2(160, 90), new Vector2(169, 149), new Vector2(179, 211), new Vector2(172, 231), new Vector2(160, 218), new Vector2(153, 156), new Vector2(136, 135));
            Shape(BodyPart.RightArm, new Vector2(55, 82), new Vector2(40, 90), new Vector2(31, 149), new Vector2(21, 211), new Vector2(28, 231), new Vector2(40, 218), new Vector2(47, 156), new Vector2(64, 135));
            Shape(BodyPart.LeftLeg, new Vector2(103, 225), new Vector2(134, 211), new Vector2(137, 272), new Vector2(129, 360), new Vector2(138, 381), new Vector2(107, 383), new Vector2(104, 355), new Vector2(99, 285));
            Shape(BodyPart.RightLeg, new Vector2(97, 225), new Vector2(66, 211), new Vector2(63, 272), new Vector2(71, 360), new Vector2(62, 381), new Vector2(93, 383), new Vector2(96, 355), new Vector2(101, 285));
        }
    }
}
