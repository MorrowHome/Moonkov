using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Presentation only. Confirmation uses the supplier's versioned, receipted transaction.
    public sealed class MoonkovResearchView
    {
        public VisualElement Root { get; }
        private static string s_Selected = "mooncore_stabilization";
        private static bool s_Fabricating;
        private readonly Action<ShopOperation, string> m_Order;
        private readonly Action m_Workshop, m_Storage;
        private readonly List<Choice> m_Choices = new List<Choice>();
        private readonly Material[] m_Materials;
        private readonly Stat[] m_Stats;
        private readonly ScrollView m_Catalog, m_Content;
        private readonly Button m_Confirm;
        private WeaponRegistry m_Registry;
        private InventoryGraph m_Graph;
        private string m_ArtCode, m_NumberCode;
        private int m_NumberBranches = -1, m_NumberLevel = -1;
        private bool m_Ready, m_Busy, m_Pending, m_CanCommit, m_Loading;

        public MoonkovResearchView(Action<ShopOperation, string> order, Action workshop, Action storage = null)
        {
            m_Order = order; m_Workshop = workshop; m_Storage = storage;
            Root = TerminalLayout.Clone("Components/ResearchPanel", "researchPanel");
            Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/Research"));
            m_Catalog = Root.Q<ScrollView>("researchChoices");
            m_Content = Root.Q<ScrollView>("researchContent");
            m_Confirm = Root.Q<Button>("researchConfirm");
            foreach (var research in AlienBiology.Research)
            {
                if (research == AlienBiology.MooncoreResearch || research.Branch == 0)
                {
                    var group = new Label(); group.AddToClassList("research-choice-group");
                    MoonkovLocalization.Set(group, research.Branch == 0 ? "BIOLOGICAL UPGRADES" : "MOONCORE ENGINEERING");
                    m_Catalog.Add(group);
                }
                var choice = new Choice(research); m_Catalog.Add(choice.Root); m_Choices.Add(choice);
                choice.Button.clicked += () => Select(research.Code);
            }
            int materialCount = AlienBiology.Research.Max(r => r.Materials.Length);
            m_Materials = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
            { m_Materials[i] = new Material(); Root.Q("researchMaterials").Add(m_Materials[i].Root); }
            m_Stats = new Stat[3];
            for (int i = 0; i < m_Stats.Length; i++)
            { m_Stats[i] = new Stat(); Root.Q("researchNumbers").Add(m_Stats[i].Root); }
            Root.Q<Button>("researchStage").clicked += () => Stage(false);
            Root.Q<Button>("fabricationStage").clicked += () => Stage(true);
            Root.Q<Button>("goWorkshop").clicked += () => { if (!m_Busy && !m_Pending) m_Workshop?.Invoke(); };
            Root.Q<Button>("goStorage").clicked += () => { if (!m_Busy && !m_Pending) m_Storage?.Invoke(); };
            m_Confirm.clicked += Confirm;
            Root.RegisterCallback<GeometryChangedEvent>(Resize);
            Present(null, false, false, false);
        }

        private void Resize(GeometryChangedEvent evt)
        {
            bool narrow = evt.newRect.width < 840;
            Root.EnableInClassList("research-compact", evt.newRect.width < 1160);
            Root.EnableInClassList("research-narrow", narrow);
            var mode = narrow ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            if (m_Catalog.mode != mode) m_Catalog.mode = mode;
        }
        private void Select(string code)
        {
            s_Selected = code; m_Content.scrollOffset = Vector2.zero;
            Present(m_Graph, m_Ready, m_Busy, m_Pending, m_Loading);
        }
        private void Stage(bool fabricate)
        {
            s_Fabricating = fabricate; m_Content.scrollOffset = Vector2.zero;
            Present(m_Graph, m_Ready, m_Busy, m_Pending, m_Loading);
        }
        public void SelectMooncore(InventoryGraph graph)
        {
            s_Selected = AlienBiology.MooncoreResearch.Code;
            s_Fabricating = graph != null && AlienBiology.MooncoreResearch.Unlocked(graph);
        }
        public void Present(InventoryGraph graph, bool ready, bool busy, bool pending, bool loading = false)
        {
            m_Graph = graph; m_Ready = ready; m_Busy = busy; m_Pending = pending; m_Loading = loading;
            var selected = AlienBiology.FindResearch(s_Selected) ?? AlienBiology.MooncoreResearch;
            s_Selected = selected.Code;
            foreach (var choice in m_Choices)
            {
                bool complete = graph != null && choice.Definition.Unlocked(graph);
                choice.Root.EnableInClassList("is-selected", selected == choice.Definition);
                choice.Root.EnableInClassList("is-complete", complete);
                MoonkovLocalization.Set(choice.State, graph == null ? "Loading…" : complete ? "Completed" : choice.Definition.CanAfford(graph) ? "Materials ready" : "Materials needed");
            }
            bool mooncore = selected == AlienBiology.MooncoreResearch, unlocked = graph != null && selected.Unlocked(graph);
            bool fabricate = mooncore && s_Fabricating;
            var recipe = fabricate ? AlienBiology.MooncoreFabrication : selected;
            Set("researchCategory", mooncore ? "MOONCORE ENGINEERING" : "BIOLOGICAL UPGRADES");
            Set("researchTitle", selected.Name);
            Set("researchDescription", mooncore ? fabricate ? "Create a module for your halo's power slot." : "Unlock the Mooncore recycler fabrication recipe." : "A permanent upgrade for all your halos.");
            string artCode = recipe.Icon;
            if (m_ArtCode != artCode)
            { var art = Root.Q("researchArt"); art.Clear(); art.Add(new StashItemArt(artCode)); m_ArtCode = artCode; }
            Root.Q("researchStages").EnableInClassList("research-hidden", !mooncore);
            Root.Q("researchStage").EnableInClassList("is-selected", !fabricate);
            Root.Q("fabricationStage").EnableInClassList("is-selected", fabricate);
            Set("researchOutcomeType", mooncore ? fabricate ? "FABRICATED MODULE" : "FABRICATION RECIPE" : "PERMANENT UPGRADE");
            Set("researchOutcome", mooncore ? "Mooncore recycler" : Benefit(selected.Code));
            Set("researchEffect", mooncore ? "Lower shot cost. Slower energy recovery." : selected.Code == "halo_capacity" ? "More energy for shooting, defense and transfer. Upgrading does not refill energy." :
                selected.Code == "halo_stability" ? "Tighter spread and lower handling penalties." : selected.Code == "halo_charge" ? "Recover energy faster after the recovery delay." : "Increase the damage dealt by every halo weapon.");
            Set("researchComparison", mooncore ? "Rifle, compact rifle and shotgun. Uses the power slot; replaces a capacitor or rapid charge bridge. Shot costs round up." : "Knowledge is retained after death. Takes effect on your next deployment.");
            Set("researchSource", mooncore ? "Extract a fragment from MC00 and store it. Other components come from supply caches." : "Recover alien organs and tissue, then place them in storage.");
            Set("researchMaterialsTitle", unlocked && !fabricate ? "RESEARCH COST" : "MATERIAL COST");
            for (int i = 0; i < m_Materials.Length; i++)
            {
                bool show = i < recipe.Materials.Length && (!unlocked || fabricate);
                m_Materials[i].Root.EnableInClassList("research-hidden", !show);
                if (show) m_Materials[i].Present(recipe.Materials[i], graph == null ? -1 : recipe.Stored(graph, recipe.Materials[i]), recipe.Quantities[i]);
            }
            if (graph != null && (m_NumberCode != selected.Code || m_NumberBranches != graph.HaloResearchBranches || m_NumberLevel != graph.HaloResearchLevel))
            {
                Numbers(selected, graph); m_NumberCode = selected.Code;
                m_NumberBranches = graph.HaloResearchBranches; m_NumberLevel = graph.HaloResearchLevel;
            }
            Root.Q("researchNumbers").EnableInClassList("research-hidden", graph == null);
            string error = graph == null ? "unavailable" : null;
            if (fabricate && graph != null)
                error = ShopRules.Apply(graph, new ShopCommand { RequestId = Guid.Empty.ToString("D"), ExpectedVersion = graph.Version, Operation = ShopOperation.Fabricate, Code = recipe.Code, Quantity = 1 }, out _);
            bool affordable = graph != null && recipe.CanAfford(graph);
            bool next = mooncore && unlocked && !fabricate;
            m_CanCommit = ready && !busy && !pending && m_Order != null && (fabricate ? error == null : !unlocked && affordable);
            string state = loading ? "Loading storage…" : busy ? "Submitting…" : pending ? "Confirmation pending" : graph == null ? "Storage unavailable" : !ready && !unlocked ? "Unavailable" : fabricate ?
                error == "fabrication_locked" ? "Recipe locked" : error == "inventory_Full" ? "Storage full" : error != null ? "Materials needed" : "Ready to fabricate" :
                unlocked ? "Completed" : affordable ? "Ready to research" : "Materials needed";
            Set("researchState", state);
            Root.Q("researchState").EnableInClassList("is-ready", !busy && !pending && (m_CanCommit || next));
            Root.Q("researchState").EnableInClassList("is-complete", unlocked && !fabricate && !busy && !pending);
            Set("researchActionHint", next ? "Recipe saved. Fabricating each module requires its own materials." : fabricate ? "Consumes these materials for 1 module in storage. Install it before deploying; equipment can be lost on death." :
                mooncore ? "Consumes these materials once to unlock the recipe. The module is fabricated separately." : unlocked ? "This upgrade is already saved. No more materials are required." : "Consumes these materials once. The upgrade is saved permanently.");
            Set("researchFeedback", loading ? "Loading your storage and research records…" : busy ? "Waiting for confirmation. Materials are charged only once." : pending ? "Check the last order before submitting another." :
                graph == null ? "Load your profile to view stored materials." : !ready ? "Finish your raid or reconnect to use stored materials." : next ? "Recipe unlocked. You can now fabricate a module." : unlocked && !fabricate ? "This upgrade will apply on your next deployment." :
                error == "fabrication_locked" ? "Complete Mooncore stabilization research first." : error == "inventory_Full" ? "Free a storage cell to fabricate this module." : !affordable ? "Store the missing materials to continue." : fabricate ? "Creates 1 module. Installation is a separate step." : "Research is permanent.");
            MoonkovLocalization.Set(m_Confirm, next ? "GO TO FABRICATION" : fabricate ? "CONSUME & FABRICATE" : unlocked ? "Completed" : "CONSUME & RESEARCH");
            m_Confirm.SetEnabled((next && !busy && !pending) || m_CanCommit);
            int owned = graph?.Count("halo_mooncore_recycler") ?? 0;
            int installed = graph?.Items.Count(i => i.Code == "halo_mooncore_recycler" && HaloModules.IsWeapon(graph.Find(i.Parent)?.Code)) ?? 0;
            Root.Q("goWorkshop").EnableInClassList("research-hidden", !mooncore || owned == 0);
            Root.Q<Button>("goWorkshop").SetEnabled(m_Workshop != null && !busy && !pending);
            Root.Q("goStorage").EnableInClassList("research-hidden", m_Storage == null || (unlocked && !fabricate));
            Root.Q<Button>("goStorage").SetEnabled(!busy && !pending);
            Root.Q("researchSource").EnableInClassList("research-hidden", unlocked && !fabricate);
            Root.Q("researchMaterialsTitle").parent.EnableInClassList("research-hidden", unlocked && !fabricate);
            Root.Q("researchOwnership").EnableInClassList("research-hidden", !mooncore);
            MoonkovLocalization.Set(Root.Q<Label>("researchOwned"), "Owned {0}", owned);
            MoonkovLocalization.Set(Root.Q<Label>("researchInstalled"), "Installed {0}", installed);
        }
        private void Set(string name, string text) => MoonkovLocalization.Set(Root.Q<Label>(name), text);

        private static string Benefit(string code)
        {
            switch (code)
            {
                case "halo_biology": return "+60% damage";
                case "halo_capacity": return "+40% energy capacity";
                case "halo_charge": return "+33% energy recovery";
                default: return "−30% spread";
            }
        }
        private void Numbers(HaloResearchDefinition selected, InventoryGraph graph)
        {
            foreach (var stat in m_Stats) stat.Root.AddToClassList("research-hidden");
            if (selected == AlienBiology.MooncoreResearch)
            {
                if (m_Registry == null) m_Registry = WeaponManager.Instance != null ? WeaponManager.Instance.WeaponRegistry : Resources.FindObjectsOfTypeAll<WeaponRegistry>().FirstOrDefault();
                var mask = HaloModules.Mask(graph, new InventoryItem { Id = "research-comparison", Code = "rifle" });
                if (m_Registry != null)
                {
                    foreach (var pair in new[] { ("rifle", "Rifle shot cost", 0), ("shotgun", "Shotgun shot cost", 1) })
                    {
                        var before = m_Registry.GetWeaponData(DollSingerWeapons.WeaponId(pair.Item1), (ushort)mask);
                        var after = m_Registry.GetWeaponData(DollSingerWeapons.WeaponId(pair.Item1), (ushort)(mask | HaloModule.MooncoreRecycler));
                        m_Stats[pair.Item3].Present(pair.Item2, before.EnergyPerRound.ToString(), after.EnergyPerRound.ToString());
                    }
                }
                m_Stats[2].Present("Energy recovery / s", HaloEnergyRules.RecoveryRate(mask).ToString("0.#"), HaloEnergyRules.RecoveryRate(mask | HaloModule.MooncoreRecycler).ToString("0.#"));
            }
            else
            {
                string name = selected.Code == "halo_biology" ? "All halo damage" : selected.Code == "halo_capacity" ? "Shared energy capacity" : selected.Code == "halo_charge" ? "Energy recovery" : "Halo spread";
                string after = selected.Code == "halo_biology" ? "160%" : selected.Code == "halo_capacity" ? "140%" : selected.Code == "halo_charge" ? "133%" : "70%";
                m_Stats[0].Present(name, "100%", after);
                if (selected.Code == "halo_stability") m_Stats[1].Present("Handling penalties", "100%", "85%");
            }
        }
        private void Confirm()
        {
            if (m_Busy || m_Pending || m_Graph == null) return;
            bool fabricate = s_Selected == AlienBiology.MooncoreResearch.Code && s_Fabricating;
            if (!fabricate && s_Selected == AlienBiology.MooncoreResearch.Code && AlienBiology.MooncoreResearch.Unlocked(m_Graph)) { Stage(true); return; }
            if (!m_Ready || !m_CanCommit) return;
            m_CanCommit = false; m_Busy = true; m_Confirm.SetEnabled(false);
            m_Order(fabricate ? ShopOperation.Fabricate : ShopOperation.Research, fabricate ? AlienBiology.MooncoreFabrication.Code : s_Selected);
        }

        private sealed class Choice
        {
            internal readonly HaloResearchDefinition Definition;
            internal readonly VisualElement Root;
            internal readonly Button Button;
            internal readonly Label State;
            internal Choice(HaloResearchDefinition definition)
            {
                Definition = definition; Root = TerminalLayout.Clone("Components/ResearchChoice", "researchChoice");
                Root.name = definition.Code; Button = Root.Q<Button>("selectResearch"); State = Root.Q<Label>("choiceState");
                MoonkovLocalization.Set(Root.Q<Label>("choiceName"), definition.Code == "mooncore_stabilization" ? "Mooncore" : definition.Code == "halo_biology" ? "Firepower" :
                    definition.Code == "halo_capacity" ? "Capacity" : definition.Code == "halo_charge" ? "Energy recovery" : "Stability");
                Root.Q("choiceArt").Add(new StashItemArt(definition.Icon));
            }
        }
        private sealed class Material
        {
            internal readonly VisualElement Root = TerminalLayout.Clone("Components/ResearchMaterial", "researchMaterial");
            private string m_Code;
            internal void Present(string code, int have, int need)
            {
                if (m_Code != code)
                {
                    var art = Root.Q("materialArt"); art.Clear(); art.Add(new StashItemArt(code));
                    MoonkovLocalization.Set(Root.Q<Label>("materialName"), InventoryCatalog.Get(code).Name); m_Code = code;
                }
                Root.EnableInClassList("is-missing", have >= 0 && have < need);
                MoonkovLocalization.Set(Root.Q<Label>("materialCount"), "{0} / {1}", have < 0 ? "—" : have.ToString(), need);
                var shortage = Root.Q<Label>("materialShortage");
                shortage.EnableInClassList("research-hidden", have < 0 || have >= need);
                MoonkovLocalization.Set(shortage, "Need {0} more", Math.Max(0, need - have));
            }
        }
        private sealed class Stat
        {
            internal readonly VisualElement Root = new VisualElement();
            private readonly Label m_Name = new Label(), m_Before = new Label(), m_After = new Label();
            internal Stat()
            {
                Root.AddToClassList("research-stat-row");
                m_Name.AddToClassList("research-stat-name"); m_Before.AddToClassList("research-stat-before"); m_After.AddToClassList("research-stat-after");
                var arrow = new Label("→"); arrow.AddToClassList("research-stat-arrow");
                Root.Add(m_Name); Root.Add(m_Before); Root.Add(arrow); Root.Add(m_After);
            }
            internal void Present(string name, string before, string after)
            {
                Root.RemoveFromClassList("research-hidden");
                MoonkovLocalization.Set(m_Name, name); MoonkovLocalization.Set(m_Before, before); MoonkovLocalization.Set(m_After, after);
            }
        }
    }
}
