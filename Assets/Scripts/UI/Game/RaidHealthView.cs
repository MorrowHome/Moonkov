using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    // Treatment still sends an intent through the existing inventory request/ack stream.
    public sealed class RaidHealthView : IDisposable
    {
        private readonly VisualElement m_Root;
        private readonly RaidVitalsMonitor m_Monitor;
        private readonly Action<BodyPart> m_Heal;
        private readonly Action<string> m_Consume;
        private readonly Button m_Treat, m_Eat, m_Drink, m_RefillOxygen;
        private PredictedPlayerGhost m_State;
        private BodyPart m_Selected = BodyPart.Chest;
        private bool m_Ready, m_Busy;
        private int m_Medicines, m_Food, m_Water, m_OxygenCanisters;
        private string m_FoodCode, m_WaterCode, m_OxygenCode, m_Feedback, m_BusyMessage;

        public RaidHealthView(VisualElement parent, Action<BodyPart> heal, Action<string> consume = null)
        {
            m_Heal = heal; m_Consume = consume;
            m_Root = TerminalLayout.Clone("RaidHealth", "raidHealthPage");
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/RaidHealthStyles")); parent.Add(m_Root);
            m_Monitor = new RaidVitalsMonitor(Select); m_Root.Q("healthDiagram").Add(m_Monitor);
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
        { m_Selected = part; m_Feedback = null; m_Monitor.Present(m_State, part); RenderActions(); }
        private void Treat() { if (m_Treat.enabledSelf) m_Heal?.Invoke(m_Selected); }
        private void Eat() { if (m_Eat.enabledSelf) m_Consume?.Invoke(m_FoodCode); }
        private void Drink() { if (m_Drink.enabledSelf) m_Consume?.Invoke(m_WaterCode); }
        private void RefillOxygen() { if (m_RefillOxygen.enabledSelf) m_Consume?.Invoke(m_OxygenCode); }
        public void Message(string source) { m_Feedback = source; RenderActions(); }

        public void Present(in PredictedPlayerGhost state, int medicines, bool busy, int food = 0, int water = 0, int oxygenCanisters = 0,
            string foodCode = "ration", string waterCode = "water", string oxygenCode = "oxygen_canister", float medicalRestore = 40, string busyMessage = null)
        {
            m_BusyMessage = busyMessage;
            m_State = state; m_Ready = state.BodyHealthInitialized; m_Busy = busy; m_Medicines = medicines;
            m_Food = food; m_Water = water; m_OxygenCanisters = oxygenCanisters;
            m_FoodCode = foodCode; m_WaterCode = waterCode; m_OxygenCode = oxygenCode;
            m_Monitor.Present(state, m_Selected);
            MoonkovLocalization.Set(m_Root.Q<Label>("healthAir"), m_Ready ? OxygenAirText(state) : "");
            MoonkovLocalization.Set(m_Root.Q<Label>("healthOxygenTime"), m_Ready ? "EST. {0}" : "", OxygenTimeText(state));
            string consequence = EffectText(RaidNutrition.Effects(state));
            if (state.AlienFoodEffectSeconds > 0)
            {
                if (!string.IsNullOrEmpty(consequence)) consequence += "\n";
                consequence += MoonkovLocalization.Format($"{AlienBiology.Activity(state.AlienFoodEffect)} / {state.AlienFoodEffectSeconds:0}s");
            }
            var conditions = m_Root.Q<Label>("healthConsequences");
            conditions.style.display = m_Ready && !string.IsNullOrEmpty(consequence) ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(conditions, m_Ready ? consequence : "");
            RenderActions();
        }

        private void RenderActions()
        {
            m_Treat.SetEnabled(m_Ready && !m_Busy && m_Heal != null && m_Medicines > 0 && RaidHealth.CanHeal(m_State, m_Selected));
            m_Eat.SetEnabled(m_Ready && !m_Busy && m_Consume != null && m_Food > 0 && RaidNutrition.CanConsume(m_State, ConsumableCatalog.Get(m_FoodCode)));
            m_Drink.SetEnabled(m_Ready && !m_Busy && m_Consume != null && m_Water > 0 && RaidNutrition.CanConsume(m_State, ConsumableCatalog.Get(m_WaterCode)));
            m_RefillOxygen.SetEnabled(m_Ready && !m_Busy && m_Consume != null && m_OxygenCanisters > 0 &&
                RaidOxygenConsumables.CanConsume(m_State, ConsumableCatalog.Get(m_OxygenCode)));
            string message = m_Busy ? m_BusyMessage ?? "USING ITEM" : !string.IsNullOrEmpty(m_Feedback) ? m_Feedback :
                !m_Ready ? "Waiting for health snapshot" : m_Medicines == 0 ? "Medical supplies must be in pockets or the chest rig." : "";
            var status = m_Root.Q<Label>("healthMedicalStatus"); status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            MoonkovLocalization.Set(status, message);
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
        public static string EffectText(SurvivalEffects effects)
        {
            var lines = new System.Collections.Generic.List<string>();
            void Add(SurvivalEffects flag, string text) { if ((effects & flag) != 0) lines.Add(MoonkovLocalization.Text(text)); }
            Add(SurvivalEffects.Hungry, "HUNGRY / MOVEMENT AND JUMP REDUCED");
            Add(SurvivalEffects.Starving, "STARVING / NO SPRINT, HEALTH LOSS AFTER GRACE");
            Add(SurvivalEffects.Thirsty, "THIRSTY / MOVEMENT AND HANDLING SLOWED");
            Add(SurvivalEffects.Dehydrated, "DEHYDRATED / NO SPRINT, HEALTH LOSS AFTER GRACE");
            Add(SurvivalEffects.FullStomach, "FULL STOMACH / SPRINT SPEED REDUCED");
            if((effects & SurvivalEffects.AlienSurge)!=0)
                lines.Add(MoonkovLocalization.Text("ALIEN SURGE / HALO DAMAGE +30%"));
            Add(SurvivalEffects.LowOxygen, "LOW OXYGEN / MOVEMENT AND HANDLING SLOWED");
            Add(SurvivalEffects.Hypoxia, "HYPOXIA / NO SPRINT, SEEK AIR");
            Add(SurvivalEffects.LegDisabled, "LEG DISABLED / SPRINT LIMITED");
            Add(SurvivalEffects.ArmDisabled, "ARM DISABLED / HANDLING SLOWED");
            Add(SurvivalEffects.AbdomenDisabled, "ABDOMEN DISABLED / DOUBLE NUTRITION DRAIN");
            return string.Join("\n", lines);
        }
        public void Dispose()
        { m_Treat.clicked -= Treat; m_Eat.clicked -= Eat; m_Drink.clicked -= Drink; m_RefillOxygen.clicked -= RefillOxygen; m_Root.RemoveFromHierarchy(); }
    }
}
