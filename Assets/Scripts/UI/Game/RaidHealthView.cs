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
        private readonly Button m_Eat, m_Drink, m_RefillOxygen;
        private readonly Action<string> m_Consume;
        private PredictedPlayerGhost m_State;
        private BodyPart m_Selected = BodyPart.Chest;
        private bool m_Ready, m_Busy;
        private int m_Medicines;
        private int m_Food, m_Water, m_OxygenCanisters;
        private string m_FoodCode, m_WaterCode, m_OxygenCode;
        private float m_MedicalRestore;
        private string m_Feedback;
        public RaidHealthView(VisualElement parent, Action<BodyPart> heal, Action<string> consume = null)
        {
            m_Heal = heal;
            m_Consume = consume;
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
            m_Eat = m_Root.Q<Button>("healthEat"); m_Eat.clicked += Eat;
            m_Drink = m_Root.Q<Button>("healthDrink"); m_Drink.clicked += Drink;
            m_RefillOxygen = m_Root.Q<Button>("healthRefillOxygen"); m_RefillOxygen.clicked += RefillOxygen;
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
        private void Eat() { if (m_Eat.enabledSelf) m_Consume?.Invoke(m_FoodCode); }
        private void Drink() { if (m_Drink.enabledSelf) m_Consume?.Invoke(m_WaterCode); }
        private void RefillOxygen() { if (m_RefillOxygen.enabledSelf) m_Consume?.Invoke(m_OxygenCode); }
        public void Message(string source) { m_Feedback = source; RenderTreatment(); }
        public void Present(in PredictedPlayerGhost state, int medicines, bool busy, int food = 0, int water = 0, int oxygenCanisters = 0,
            string foodCode = "ration", string waterCode = "water", string oxygenCode = "oxygen_canister", float medicalRestore = 40)
        {
            m_State = state; m_Ready = state.BodyHealthInitialized; m_Medicines = medicines; m_Busy = busy;
            m_Food = food; m_Water = water; m_OxygenCanisters = oxygenCanisters;
            m_FoodCode = foodCode; m_WaterCode = waterCode; m_OxygenCode = oxygenCode; m_MedicalRestore = medicalRestore;
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
            m_Root.Q("healthOxygenFill").style.width = Length.Percent(m_Ready ? Mathf.Clamp(state.Oxygen, 0, 100) : 0);
            bool needsSuitAir = RaidOxygen.NeedsSuitAir(state);
            string stage = state.Oxygen <= 0 ? !needsSuitAir ? "RESERVE EMPTY / SUPPLY AVAILABLE" : "HYPOXIA / SEEK AIR" :
                state.Oxygen <= 10 ? "CRITICAL OXYGEN RESERVE" : state.Oxygen <= 25 ? "LOW OXYGEN RESERVE" : "OXYGEN NORMAL";
            var stageLabel = m_Root.Q<Label>("healthOxygenStage"); MoonkovLocalization.Set(stageLabel, m_Ready ? stage : "Waiting for health snapshot");
            stageLabel.EnableInClassList("health-danger", m_Ready && needsSuitAir && state.Oxygen <= 10);
            stageLabel.EnableInClassList("health-caution", m_Ready && state.Oxygen <= 25 && (!needsSuitAir || state.Oxygen > 10));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthAir"), m_Ready ? OxygenAirText(state) : "—");
            MoonkovLocalization.Set(m_Root.Q<Label>("healthOxygenTime"), m_Ready ? "ESTIMATED RESERVE / {0}" : "—", OxygenTimeText(state));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthAtmosphere"), m_Ready ? "PRESSURE {0:0.0} kPa / O₂ PARTIAL {1:0.0} kPa" : "—",
                state.AmbientPressureKPa, state.AmbientOxygenPartialPressureKPa);
            MoonkovLocalization.Set(m_Root.Q<Label>("healthOxygenRate"), m_Ready ? "DRAIN {0:0.00}%/s / SUPPLY {1:0.00}%/s" : "—",
                state.OxygenDrainPerSecond, state.OxygenSupplyPerSecond);
            RenderOxygen();
            NutritionMeter("Energy", "ENERGY / {0:0}%", state.Energy);
            NutritionMeter("Hydration", "HYDRATION / {0:0}%", state.Hydration);
            NutritionMeter("Satiety", "STOMACH / {0:0}% FULL", state.Satiety);
            string consequence = EffectText(RaidNutrition.Effects(state));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthConsequences"), m_Ready ?
                string.IsNullOrEmpty(consequence) ? "NO NEGATIVE CONDITIONS" : consequence : "");
            RenderNutrition();
            m_Diagram.Present(state, m_Selected); RenderTreatment();
        }
        public static string OxygenAirText(in PredictedPlayerGhost state)
        {
            bool refilling = state.Oxygen < RaidOxygen.Capacity && state.OxygenSupplyPerSecond > state.OxygenDrainPerSecond;
            if (state.BreathableAir) return refilling ? "BREATHABLE / REFILLING" : "BREATHABLE / RESERVE HELD";
            if (refilling) return "UNBREATHABLE / REFILLING";
            if (!RaidOxygen.NeedsSuitAir(state)) return "UNBREATHABLE / SUIT SUPPLY";
            return state.AmbientPressureKPa <= .1f ? "VACUUM / RESERVE IN USE" : "UNBREATHABLE / RESERVE IN USE";
        }
        public static string OxygenTimeText(in PredictedPlayerGhost state)
        {
            float remaining = RaidOxygen.RemainingSeconds(state);
            if (float.IsPositiveInfinity(remaining)) return MoonkovLocalization.Text("NO NET DRAIN");
            int seconds = Mathf.CeilToInt(Mathf.Max(0, remaining));
            return MoonkovLocalization.Format($"{seconds / 60:00}:{seconds % 60:00}");
        }
        private void RenderOxygen()
        {
            m_RefillOxygen.SetEnabled(m_Ready && !m_Busy && m_Consume != null && m_OxygenCanisters > 0 &&
                RaidOxygenConsumables.CanConsume(m_State, Inventory.ConsumableCatalog.Get(m_OxygenCode)));
            MoonkovLocalization.Set(m_RefillOxygen, "REFILL O₂ / {0}", m_OxygenCanisters);
            MoonkovLocalization.Set(m_Root.Q<Label>("healthOxygenSupplyStatus"),
                "Next cylinder: +{0:0}% oxygen. Keep it in pockets or the chest rig; excess oxygen is lost.",
                Inventory.ConsumableCatalog.Get(m_OxygenCode)?.Oxygen ?? 0);
            bool hypoxic = m_Ready && m_State.CurrentHealth > 0 && RaidOxygen.NeedsSuitAir(m_State) && m_State.Oxygen <= 0;
            var grace = m_Root.Q<Label>("healthOxygenGrace");
            grace.style.display = hypoxic ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(grace, m_State.OxygenGraceRemaining > 0 ? "HYPOXIA / HEALTH LOSS IN {0:0.0}s" : "HYPOXIA / HEALTH LOSS ACTIVE",
                m_State.OxygenGraceRemaining);
        }
        private void NutritionMeter(string name, string text, float value)
        {
            MoonkovLocalization.Set(m_Root.Q<Label>("health" + name + "Value"), m_Ready ? text : "—", value);
            var fill = m_Root.Q("health" + name + "Fill"); fill.style.width = Length.Percent(m_Ready ? value : 0);
            fill.style.backgroundColor = name == "Satiety" ? value >= RaidNutrition.FullThreshold ? new Color(.64f, .46f, .2f) : new Color(.4f, .52f, .43f) : RaidBodyDiagram.ConditionColor(value / 100);
        }
        private void RenderNutrition()
        {
            m_Eat.SetEnabled(m_Ready && !m_Busy && m_Consume != null && m_Food > 0 && RaidNutrition.CanConsume(m_State, Inventory.ConsumableCatalog.Get(m_FoodCode)));
            m_Drink.SetEnabled(m_Ready && !m_Busy && m_Consume != null && m_Water > 0 && RaidNutrition.CanConsume(m_State, Inventory.ConsumableCatalog.Get(m_WaterCode)));
            MoonkovLocalization.Set(m_Eat, "EAT / {0}", m_Food); MoonkovLocalization.Set(m_Drink, "DRINK / {0}", m_Water);
            MoonkovLocalization.Set(m_Root.Q<Label>("healthFoodStatus"),
                "Next food: {0}. Next drink: {1}. Inspect supplies in inventory for recovery and fullness.",
                Inventory.InventoryCatalog.Get(m_FoodCode)?.Name ?? "NONE", Inventory.InventoryCatalog.Get(m_WaterCode)?.Name ?? "NONE");
        }
        public static string EffectText(SurvivalEffects effects)
        {
            var lines = new System.Collections.Generic.List<string>();
            void Add(SurvivalEffects flag, string text) { if ((effects & flag) != 0) lines.Add(MoonkovLocalization.Text(text)); }
            Add(SurvivalEffects.Hungry, "HUNGRY / MOVEMENT AND JUMP REDUCED");
            Add(SurvivalEffects.Starving, "STARVING / NO SPRINT, HEALTH LOSS AFTER GRACE");
            Add(SurvivalEffects.Thirsty, "THIRSTY / MOVEMENT AND HANDLING SLOWED");
            Add(SurvivalEffects.Dehydrated, "DEHYDRATED / NO SPRINT, HEALTH LOSS AFTER GRACE");
            Add(SurvivalEffects.FullStomach, "FULL STOMACH / SPRINT SPEED REDUCED");
            Add(SurvivalEffects.LowOxygen, "LOW OXYGEN / MOVEMENT AND HANDLING SLOWED");
            Add(SurvivalEffects.Hypoxia, "HYPOXIA / NO SPRINT, SEEK AIR");
            Add(SurvivalEffects.LegDisabled, "LEG DISABLED / SPRINT LIMITED");
            Add(SurvivalEffects.ArmDisabled, "ARM DISABLED / HANDLING SLOWED");
            Add(SurvivalEffects.AbdomenDisabled, "ABDOMEN DISABLED / DOUBLE NUTRITION DRAIN");
            return string.Join("\n", lines);
        }
        private void RenderTreatment()
        {
            MoonkovLocalization.Set(m_Root.Q<Label>("healthSelectedPart"), "TREATMENT / {0}", MoonkovLocalization.Text(Name(m_Selected)));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthMedicalCount"), "ACCESSIBLE MEDICAL / {0}", m_Medicines);
            MoonkovLocalization.Set(m_Treat, "TREAT SELECTED PART / +{0:0} HP", m_MedicalRestore);
            m_Treat.SetEnabled(m_Ready && !m_Busy && m_Medicines > 0 && RaidHealth.CanHeal(m_State, m_Selected));
            MoonkovLocalization.Set(m_Root.Q<Label>("healthMedicalStatus"), m_Busy ? "Waiting for server confirmation…" :
                !string.IsNullOrEmpty(m_Feedback) ? m_Feedback : !m_Ready ? "Waiting for health snapshot" :
                m_Medicines == 0 ? "Medical supplies must be in pockets or the chest rig." :
                !RaidHealth.CanHeal(m_State, m_Selected) ? "This body part does not need treatment." :
                "Uses the first accessible medicine. Choose a specific item from inventory to control consumption.");
        }
        public void Dispose() { m_Treat.clicked -= Treat; m_Eat.clicked -= Eat; m_Drink.clicked -= Drink; m_RefillOxygen.clicked -= RefillOxygen; m_Root.RemoveFromHierarchy(); }
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
