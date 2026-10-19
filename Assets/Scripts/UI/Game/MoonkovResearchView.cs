using System;
using System.Linq;
using Unity.MP_FPS.Inventory;
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
        private readonly Action m_Workshop;
        private InventoryGraph m_Graph;
        private bool m_Ready, m_Busy, m_Pending;

        public MoonkovResearchView(Action<ShopOperation, string> order, Action workshop)
        {
            m_Order = order; m_Workshop = workshop;
            Root = TerminalLayout.Clone("Components/ResearchPanel", "researchPanel");
            Root.styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("Moonkov/UI/Research"));
            Root.Q<Button>("researchStage").clicked += () => Stage(false);
            Root.Q<Button>("fabricationStage").clicked += () => Stage(true);
            Root.Q<Button>("goFabrication").clicked += () => Stage(true);
            Root.Q<Button>("goWorkshop").clicked += () => m_Workshop?.Invoke();
            Root.Q<Button>("researchConfirm").clicked += Confirm;
        }
        private void Stage(bool fabricate) { s_Fabricating = fabricate; Present(m_Graph, m_Ready, m_Busy, m_Pending); }
        public void Present(InventoryGraph graph, bool ready, bool busy, bool pending)
        {
            m_Graph = graph; m_Ready = ready; m_Busy = busy; m_Pending = pending;
            if (graph == null) return;
            var choices = Root.Q<ScrollView>("researchChoices"); choices.Clear();
            MoonkovTerminal.Text(choices, "MOONCORE RESEARCH", "terminal-heading");
            foreach (var research in AlienBiology.Research)
            {
                if (research.Branch == 0) MoonkovTerminal.Text(choices, "BIOLOGICAL HALO RESEARCH", "terminal-heading");
                var choice = TerminalLayout.Clone("Components/ResearchChoice", "researchChoice"); choices.Add(choice);
                MoonkovLocalization.Set(choice.Q<Label>("choiceName"), research.Name);
                MoonkovLocalization.Set(choice.Q<Label>("choiceState"), research.Unlocked(graph) ? "RESEARCH COMPLETE" : research.CanAfford(graph) ? "READY TO RESEARCH" : "MATERIALS MISSING");
                choice.EnableInClassList("is-selected", s_Selected == research.Code);
                choice.Q<Button>("selectResearch").clicked += () => { s_Selected = research.Code; Present(m_Graph, m_Ready, m_Busy, m_Pending); };
            }
            var selected = AlienBiology.FindResearch(s_Selected) ?? AlienBiology.MooncoreResearch;
            bool mooncore = selected == AlienBiology.MooncoreResearch, unlocked = selected.Unlocked(graph);
            bool fabricate = mooncore && s_Fabricating;
            var recipe = fabricate ? AlienBiology.MooncoreFabrication : selected;
            Set("researchTitle", recipe.Name); Set("researchDescription", recipe.Description);
            var art = Root.Q("researchArt"); art.Clear(); art.Add(new StashItemArt(ContainerInventoryView.Art(recipe.Icon)));
            Root.Q("researchStages").EnableInClassList("research-hidden", !mooncore);
            Root.Q("researchStage").EnableInClassList("is-selected", !fabricate);
            Root.Q("fabricationStage").EnableInClassList("is-selected", fabricate);
            Set("researchEffect", mooncore ? HaloModules.Description("halo_mooncore_recycler") : selected.Description);
            Set("researchComparison", mooncore ? "STORAGE SLOT / Rifle, compact rifle and shotgun. Replaces capacitor or rapid charge bridge. Energy reduction is rounded up before the hit-marker surcharge; existing biological research still applies." : "Permanent knowledge is retained after death and applies on your next deployment.");
            Set("researchNumbers", mooncore ? Comparison(graph) : "Research does not grant free ammunition or a physical module.");
            Set("researchSource", mooncore ? "SOURCE: MC00 random Mooncore event → carry the fragment through a successful extraction → move it to storage. Circuit boards and copper coils come from supply caches. Only recovered, non-emergency materials count." : "Recover the required alien organs and tissue, then move them to personal storage.");
            var materials = Root.Q("researchMaterials"); materials.Clear();
            for (int i = 0; i < recipe.Materials.Length; i++)
            {
                string code = recipe.Materials[i]; int have = recipe.Stored(graph, code), need = recipe.Quantities[i];
                var row = TerminalLayout.Clone("Components/ResearchMaterial", "researchMaterial"); materials.Add(row);
                row.Q("materialArt").Add(new StashItemArt(ContainerInventoryView.Art(code)));
                MoonkovLocalization.Set(row.Q<Label>("materialName"), InventoryCatalog.Get(code).Name);
                MoonkovLocalization.Set(row.Q<Label>("materialCount"), have >= need ? "READY / {0} OWNED / {1} REQUIRED" : "MISSING {2} / {0} OWNED / {1} REQUIRED", have, need, Math.Max(0, need - have));
                row.EnableInClassList("is-missing", have < need);
            }
            string error = null;
            if (fabricate)
                error = ShopRules.Apply(graph, new ShopCommand { RequestId = Guid.Empty.ToString("D"), ExpectedVersion = graph.Version, Operation = ShopOperation.Fabricate, Code = recipe.Code, Quantity = 1 }, out _);
            string state = busy ? "SUBMITTING / WAIT FOR CONFIRMATION" : pending ? "CONFIRMATION UNKNOWN / CHECK LAST ORDER" : fabricate ?
                error == "fabrication_locked" ? "LOCKED / RESEARCH FIRST" : error == "inventory_Full" ? "STORAGE FULL / FREE SPACE AND RETRY" : error != null ? "MATERIALS MISSING" : "READY TO FABRICATE" :
                unlocked ? "RESEARCH COMPLETE / KNOWLEDGE SAVED" : selected.CanAfford(graph) ? "READY TO RESEARCH" : "NOT RESEARCHED / MATERIALS MISSING";
            if (!ready && !busy && !pending) state = "BASE TRANSACTION UNAVAILABLE / FINISH RAID OR RECONNECT";
            Set("researchState", state);
            Set("researchActionHint", fabricate ? "Consumes the materials shown above and creates 1 module in storage. No charge if storage cannot accept it. Install it in Halo modification before deployment." :
                mooncore ? "Consumes 1 recovered fragment and 2 circuit boards. Permanently unlocks fabrication; no module is granted. Fabricating your first module is a separate cost." : "Consumes the materials shown above once and permanently saves this research.");
            MoonkovLocalization.Set(Root.Q<Button>("researchConfirm"), fabricate ? "CONSUME MATERIALS / FABRICATE 1" : unlocked ? "RESEARCH COMPLETE" : "RESEARCH AND PERMANENTLY UNLOCK");
            Root.Q<Button>("researchConfirm").SetEnabled(ready && (fabricate ? error == null : !unlocked && selected.CanAfford(graph)));
            Root.Q("goFabrication").EnableInClassList("research-hidden", !mooncore || fabricate);
            Root.Q<Button>("goFabrication").SetEnabled(unlocked);
            Root.Q("goWorkshop").EnableInClassList("research-hidden", !mooncore);
            Root.Q<Button>("goWorkshop").SetEnabled(m_Workshop != null && !busy && !pending);
            int owned = graph.Count("halo_mooncore_recycler"), installed = graph.Items.Count(i => i.Code == "halo_mooncore_recycler" && HaloModules.IsWeapon(graph.Find(i.Parent)?.Code));
            MoonkovLocalization.Set(Root.Q<Label>("researchOwnership"), mooncore ? "OWNED {0} / INSTALLED {1}. Knowledge survives death; physical modules can be lost with your equipment. Owning a module does not equip it." : "Permanent knowledge survives death. No physical item is granted.", owned, installed);
        }
        private void Set(string name, string text) => MoonkovLocalization.Set(Root.Q<Label>(name), text);
        private static string Comparison(InventoryGraph graph)
        {
            var registry = WeaponManager.Instance != null ? WeaponManager.Instance.WeaponRegistry : UnityEngine.Resources.FindObjectsOfTypeAll<WeaponRegistry>().FirstOrDefault();
            if (registry == null) return "BEFORE → AFTER / ENERGY PER ROUND 100% → 2/3 (rounded up) / CHARGE TIME 100% → 120%";
            string Line(string code)
            {
                var weapon = new InventoryItem { Id = "research-comparison", Code = code };
                var mask = HaloModules.Mask(graph, weapon);
                var before = registry.GetWeaponData(DollSingerWeapons.WeaponId(code), (ushort)mask);
                var after = registry.GetWeaponData(DollSingerWeapons.WeaponId(code), (ushort)(mask | HaloModule.MooncoreRecycler));
                return MoonkovLocalization.Format($"{InventoryCatalog.Get(code).Name}: ENERGY {before.EnergyPerRound} → {after.EnergyPerRound} / CHARGE {before.ReloadTime:0.00}s → {after.ReloadTime:0.00}s");
            }
            return Line("rifle") + "\n" + Line("shotgun");
        }
        private void Confirm()
        {
            if (!m_Ready) return;
            bool fabricate = s_Selected == AlienBiology.MooncoreResearch.Code && s_Fabricating;
            m_Order?.Invoke(fabricate ? ShopOperation.Fabricate : ShopOperation.Research, fabricate ? AlienBiology.MooncoreFabrication.Code : s_Selected);
        }
    }
}
