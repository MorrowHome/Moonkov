using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Navigation and presentation. Economy actions remain disabled until a server API exists.
    public sealed class MoonkovTerminal : IDisposable
    {
        private readonly VisualElement m_Root, m_Gear, m_Content, m_Nav, m_NavTabs, m_Indicator, m_Chrome, m_FooterTicker;
        private readonly LunarBackdrop m_Shade;
        private readonly Action m_Prepare;
        private readonly List<Button> m_Tabs = new List<Button>();
        private readonly string[] m_Pages = { "SHIP", "CHARACTER", "HALO BUILD", "SUPPLIERS", "OPERATIONS", "TASKS", "BASE", "COMMS", "SETTINGS" };
        private readonly TerminalWindows m_Windows;
        private MenuCharacterView m_Character;
        private int m_Page, m_Step, m_ShownStep = -1;
        private bool m_Busy, m_Ready;
        private string m_Player;
        private int m_Dust, m_Alloy, m_Cells;
        private readonly Label m_Title, m_Subtitle, m_Clock;
        private static float s_FrameDelay;
        private const string k_Ticker = "MOONKOV ORBITAL TERMINAL  //  SELENE RELAY 07 NOMINAL  //  SURFACE -173 C / NIGHT CYCLE  //  EXTRACTION BEACON: GREEN  //  CARGO IS LOST ON DEATH  //  RECOVER. SURVIVE. EXTRACT.  //  ";
        public TerminalWindows Windows => m_Windows;
        public MenuCharacterView Character => m_Character;
        public event Action Navigating;
        public event Action LoadoutShowing;

        public MoonkovTerminal(VisualElement root, Action prepare)
        {
            m_Root = root; m_Prepare = prepare; root.AddToClassList("moonkov-terminal");
            root.focusable = true;
            m_Title = root.Q<Label>(className: "stash-page-title");
            m_Subtitle = root.Q<VisualElement>(className: "stash-page-heading").Q<Label>(className: "stash-eyebrow");
            m_Gear = root.Q<VisualElement>(className: "stash-body");
            m_Nav = new VisualElement(); m_Nav.AddToClassList("terminal-nav"); root.Insert(1, m_Nav);
            m_NavTabs = new VisualElement(); m_NavTabs.AddToClassList("terminal-nav-tabs"); m_Nav.Add(m_NavTabs);
            for (int i = 0; i < m_Pages.Length; i++)
            {
                int page = i;
                var tab = ActionButton(m_NavTabs, m_Pages[i], () => Navigate(page), "terminal-tab"); m_Tabs.Add(tab);
                Text(tab, (i + 1).ToString("00"), "terminal-tab-index").pickingMode = PickingMode.Ignore;
            }
            m_Indicator = new VisualElement { pickingMode = PickingMode.Ignore }; m_Indicator.AddToClassList("terminal-nav-indicator"); m_NavTabs.Add(m_Indicator);
            m_NavTabs.RegisterCallback<GeometryChangedEvent>(_ => MoveIndicator(false));
            var status = new VisualElement(); status.AddToClassList("terminal-nav-status"); m_Nav.Add(status);
            var dot = new VisualElement(); dot.AddToClassList("terminal-nav-dot"); status.Add(dot);
            Text(status, "UPLINK STABLE", "terminal-nav-link");
            m_Clock = Text(status, "", "terminal-nav-clock");
            m_Nav.schedule.Execute(() =>
            {
                m_Clock.text = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
                dot.EnableInClassList("terminal-nav-dot-dim", DateTime.UtcNow.Second % 2 == 0);
            }).Every(500);
            m_Content = new VisualElement(); m_Content.AddToClassList("terminal-content"); root.Insert(3, m_Content);
            var spacer = root.Q<VisualElement>(className: "stash-footer-spacer");
            if (spacer != null) m_FooterTicker = Ticker(spacer, k_Ticker);
            m_Chrome = new TerminalChrome(); root.Add(m_Chrome);
            m_Windows = new TerminalWindows(root, () => Navigate(0));
            m_Shade = new LunarBackdrop(BackdropMode.Ship); m_Shade.AddToClassList("terminal-scene-shade"); root.Insert(0, m_Shade);
            Navigate(0); m_Ready = true;
        }
        public void Present(string player, int dust, int alloy, int cells, bool busy)
        {
            bool changed = player != m_Player || dust != m_Dust || alloy != m_Alloy || cells != m_Cells || busy != m_Busy;
            m_Player = player; m_Dust = dust; m_Alloy = alloy; m_Cells = cells; m_Busy = busy;
            if (changed && m_Page != 1) Render();
        }
        public void Navigate(int page)
        {
            Navigating?.Invoke(); DisposeCharacter();
            bool moved = page != m_Page || !m_Ready;
            m_Page = page; m_Step = 0; m_ShownStep = -1; m_Windows?.CloseAll();
            m_Root.EnableInClassList("terminal-is-ship", page == 0);
            for (int i = 0; i < m_Tabs.Count; i++) m_Tabs[i].EnableInClassList("terminal-tab-selected", i == page);
            m_Gear.style.display = page == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Content.style.display = page == 1 ? DisplayStyle.None : DisplayStyle.Flex;
            m_Shade.Mode = page == 0 ? BackdropMode.Ship : BackdropMode.Page;
            m_Title.text = m_Pages[page];
            m_Subtitle.text = page == 1 ? "EQUIPMENT / PERSONAL STORAGE" : "ORBITAL SHIP / " + m_Pages[page];
            if (moved)
            {
                TerminalMotion.Scramble(m_Title, .12f, .5f); TerminalMotion.Reveal(m_Subtitle, .18f, 24, 0, .5f);
                if (m_Ready) TerminalMotion.Wipe(m_Root, (page + 1).ToString("00") + "  //  " + m_Pages[page]);
            }
            m_Nav.schedule.Execute(() => MoveIndicator(true));
            if (page != 1) Render();
            else
            {
                TerminalMotion.Fade(m_Gear, .1f, .45f);
                if (LoadoutShowing != null) { LoadoutShowing(); return; }
                m_Character = new MenuCharacterView(idleOnly: true);
                m_Character.Element.RemoveFromClassList("terminal-character-art");
                m_Character.Element.AddToClassList("stash-character-render");
                var stage = m_Root.Q<VisualElement>("stashCharacterStage"); stage.Insert(0, m_Character.Element);
                var guides = stage.Q<StashEquipmentGuides>();
                if (guides != null) m_Character.FrameRendered += guides.MarkDirtyRepaint;
            }
        }
        public void Prepare() => Navigate(4);
        private void MoveIndicator(bool animate)
        {
            if (m_Page >= m_Tabs.Count) return;
            var target = m_Tabs[m_Page].layout; if (float.IsNaN(target.width) || target.width < 1) return;
            float x0 = m_Indicator.resolvedStyle.left, w0 = m_Indicator.resolvedStyle.width;
            if (!animate || float.IsNaN(x0) || float.IsNaN(w0) || w0 < 1)
            {
                m_Indicator.style.left = target.x; m_Indicator.style.width = target.width; return;
            }
            TerminalMotion.Tween(m_Indicator, .5f, t =>
            {
                float k = TerminalMotion.OutExpo(t), stretch = Mathf.Sin(t * Mathf.PI) * 40;
                m_Indicator.style.left = Mathf.Lerp(x0, target.x, k) - stretch * .5f; m_Indicator.style.width = Mathf.Lerp(w0, target.width, k) + stretch;
            }, 0, null, "indicator");
        }
        private void Render()
        {
            DisposeCharacter();
            m_Content.Clear(); s_FrameDelay = .08f;
            m_Content.EnableInClassList("terminal-content-page", m_Page != 0);
            if (m_Page != 0) Text(m_Content, (m_Page + 1).ToString("00"), "terminal-page-index").pickingMode = PickingMode.Ignore;
            switch (m_Page)
            {
                case 0: Ship(); break;
                case 2: Halo(); break;
                case 3: Suppliers(); break;
                case 4: Operation(); break;
                case 5: Tasks(); break;
                case 6: Base(); break;
                case 7: Comms(); break;
                case 8: Settings(); break;
            }
            if (m_Page == 0) return;
            float delay = .1f;
            foreach (var child in m_Content.Children())
            {
                if (child.ClassListContains("terminal-row")) delay = TerminalMotion.Cascade(child, delay, .08f, 0, 26);
                else { TerminalMotion.Reveal(child, delay, 0, 18); delay += .05f; }
            }
        }

        private void Ship()
        {
            m_Character = new MenuCharacterView(); m_Content.Add(m_Character.Element);
            TerminalMotion.Reveal(m_Character.Element, .05f, -60, 0, 1.1f);

            var hero = new VisualElement { pickingMode = PickingMode.Ignore }; hero.AddToClassList("terminal-home-hero"); m_Content.Add(hero);
            Text(hero, "ESCAPE FROM", "terminal-home-kicker");
            var mark = new VisualElement { pickingMode = PickingMode.Ignore }; mark.AddToClassList("terminal-home-mark"); hero.Add(mark);
            Text(mark, "MOONKOV", "terminal-home-title-ghost");
            var title = Text(mark, "MOONKOV", "terminal-home-title");
            var bar = new VisualElement(); bar.AddToClassList("terminal-home-bar"); hero.Add(bar);
            Text(bar, "LUNAR EXPEDITION", "terminal-home-caption");
            Text(bar, "VER 0.7  /  SECTOR 07", "terminal-home-version");
            Text(hero, "ORBITAL SHIP  /  FIELD TERMINAL", "terminal-home-description");
            TerminalMotion.Cascade(hero, .25f, .09f, -40, 0);
            TerminalMotion.Scramble(title, .35f, .9f);

            var menu = new VisualElement(); menu.AddToClassList("terminal-home-menu"); m_Content.Add(menu);
            var head = Row(menu); head.AddToClassList("terminal-home-menu-head");
            Text(head, "COMMAND DECK", "terminal-eyebrow"); Text(head, "SELECT  /  " + m_Pages.Length.ToString("00") + " SYSTEMS", "terminal-home-menu-count");
            var deploy = HomeAction(menu, "OPERATION", "05", "LUNAR SUPPLY RECOVERY  /  DEPLOY", Prepare);
            deploy.AddToClassList("terminal-home-deploy"); deploy.SetEnabled(!m_Busy);
            var stripes = new HazardStripes(TerminalPalette.A(TerminalPalette.Ink, .16f)); stripes.AddToClassList("terminal-home-deploy-stripes"); deploy.Insert(1, stripes);
            var grid = Row(menu); grid.AddToClassList("terminal-home-grid");
            HomeAction(grid, "CHARACTER", "02", "LOADOUT  /  STASH", () => Navigate(1));
            HomeAction(grid, "HALO", "03", "WEAPON SCHEMATIC", () => Navigate(2));
            HomeAction(grid, "SETTINGS", "09", "TERMINAL", () => Navigate(8));
            var services = Row(menu); services.AddToClassList("terminal-home-services");
            foreach (int index in new[] { 3, 5, 6, 7 })
            {
                int page = index; var chip = ActionButton(services, m_Pages[index], () => Navigate(page), "terminal-home-chip");
                Text(chip, (index + 1).ToString("00"), "terminal-home-chip-index").pickingMode = PickingMode.Ignore;
            }
            var resources = Row(menu); resources.AddToClassList("terminal-home-resources");
            Resource(resources, "MOON DUST", m_Dust, .7f); Resource(resources, "ALLOY", m_Alloy, .8f); Resource(resources, "ENERGY CELLS", m_Cells, .9f);
            float delay = .35f;
            foreach (var child in menu.Children())
            {
                if (child.ClassListContains("terminal-row")) delay = TerminalMotion.Cascade(child, delay, .06f, 40, 0);
                else { TerminalMotion.Reveal(child, delay, 60, 0, .7f); delay += .08f; }
            }

            var operatorCard = new VisualElement(); operatorCard.AddToClassList("terminal-home-operator-card"); m_Content.Add(operatorCard);
            TechFrame.Attach(operatorCard, .6f);
            Text(operatorCard, "OPERATOR  /  ACTIVE", "terminal-eyebrow");
            Text(operatorCard, "DOLLSINGER", "terminal-home-operator");
            Text(operatorCard, string.IsNullOrEmpty(m_Player) ? "UNREGISTERED CREW" : m_Player.ToUpperInvariant(), "terminal-home-player");
            Text(operatorCard, m_Busy ? "LINK BUSY  /  STAND BY" : "READY FOR DEPLOYMENT", m_Busy ? "terminal-warning" : "terminal-positive");
            TerminalMotion.Reveal(operatorCard, .55f, -30, 0, .7f);

            var ticker = Ticker(m_Content, k_Ticker); ticker.AddToClassList("terminal-home-ticker");
            TerminalMotion.Fade(ticker, .8f, .6f);
        }
        private Button HomeAction(VisualElement parent, string text, string index, string caption, Action action)
        {
            var button = ActionButton(parent, "", action, "terminal-home-action"); button.tooltip = text;
            TechFrame.Attach(button, .4f);
            Text(button, index, "terminal-home-action-index").pickingMode = PickingMode.Ignore;
            Text(button, text, "terminal-home-action-name").pickingMode = PickingMode.Ignore;
            Text(button, caption, "terminal-home-action-caption").pickingMode = PickingMode.Ignore;
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore }; arrow.AddToClassList("terminal-home-action-arrow"); button.Add(arrow);
            return button;
        }
        private static void Resource(VisualElement parent, string name, int value, float delay)
        {
            var cell = new VisualElement(); cell.AddToClassList("terminal-home-resource"); parent.Add(cell);
            Text(cell, name, "terminal-home-resource-name");
            TerminalMotion.CountUp(Text(cell, "0", "terminal-home-resource-value"), value, "{0:N0}", delay, 1.4f);
        }

        private void Halo()
        {
            var row = Row(m_Content);
            var diagram = Panel(row, "STANDARD HALO / COMPONENT SCHEMATIC", "terminal-wide");
            Text(diagram, "H-01   /   FIELD ISSUE", "terminal-title");
            var schematic = new HaloSchematic(); schematic.AddToClassList("terminal-halo-schematic"); diagram.Add(schematic);
            string[] nodes = { "FOCUSING LENS", "LEFT EMITTER", "HALO FRAME", "RIGHT EMITTER", "CORE", "AMPLIFIER", "CONTROL MODULE" };
            Vector2[] positions = { new Vector2(40, 5), new Vector2(2, 39), new Vector2(40, 30), new Vector2(78, 39), new Vector2(40, 64), new Vector2(2, 78), new Vector2(78, 78) };
            schematic.SetNodes(positions);
            for (int i = 0; i < nodes.Length; i++)
            {
                string node = nodes[i]; var button = ActionButton(schematic, node, () => DescribeNode(node), "terminal-node");
                button.style.left = new Length(positions[i].x, LengthUnit.Percent); button.style.top = new Length(positions[i].y, LengthUnit.Percent);
                Text(button, "N" + (i + 1), "terminal-node-index").pickingMode = PickingMode.Ignore;
                TerminalMotion.Fade(button, .45f + i * .08f, .4f);
            }
            Text(diagram, "SELECT A NODE TO INSPECT ITS ROLE", "terminal-eyebrow");
            var stats = Panel(row, "LOADOUT INFORMATION", "terminal-side");
            var weapon = WeaponManager.Instance != null && WeaponManager.Instance.WeaponRegistry != null ? WeaponManager.Instance.WeaponRegistry.GetWeaponData(2) : null;
            var meters = Row(stats); meters.AddToClassList("terminal-meters");
            Meter(meters, "DMG", weapon != null ? weapon.Damage / 100f : 0, weapon != null ? weapon.Damage.ToString() : "—", TerminalPalette.Yellow);
            Meter(meters, "CAP", weapon != null ? weapon.MagazineSize / 60f : 0, weapon != null ? weapon.MagazineSize.ToString() : "—", TerminalPalette.Cyan);
            Meter(meters, "RNG", weapon != null ? weapon.HitscanRange / 300f : 0, weapon != null ? weapon.HitscanRange.ToString("0") : "—", TerminalPalette.Text);
            Stat(stats, "STATUS", "STANDARD ISSUE");
            Stat(stats, "DAMAGE", weapon != null ? weapon.Damage.ToString() : "—");
            Stat(stats, "ENERGY CAPACITY", weapon != null ? weapon.MagazineSize.ToString() : "—");
            Stat(stats, "RANGE", weapon != null ? weapon.HitscanRange.ToString("0") + " m" : "—");
            Text(stats, "Modular halo assembly is awaiting workshop access. Your current field weapon remains standard issue.", "terminal-copy");
            Disabled(stats, "SAVE HALO PRESET", "No modular loadout service is available.");
            Disabled(stats, "ASSEMBLE LOADOUT", "No modular loadout service is available.");
        }
        private static void Meter(VisualElement parent, string name, float value, string display, Color color)
        {
            var meter = new ArcMeter(value, color); parent.Add(meter);
            Text(meter, display, "terminal-meter-value"); Text(meter, name, "terminal-meter-name");
        }
        private void DescribeNode(string node)
        {
            var window = m_Windows.Open(node);
            var art = new StashItemArt(StashArtKind.Halo); art.AddToClassList("terminal-inspect-art"); window.Add(art);
            Text(window, node, "terminal-title");
            Text(window, node == "CORE" ? "The power source at the center of the halo assembly." : "A component of the lunar halo weapon. Replaceable modules will become available through the workshop.", "terminal-copy");
            Stat(window, "CONFIGURATION", "STANDARD ISSUE"); Disabled(window, "REPLACE MODULE", "Workshop assembly is not available.");
        }

        private void Suppliers()
        {
            var head = Row(m_Content); head.AddToClassList("terminal-page-head");
            var heading = new VisualElement(); heading.AddToClassList("terminal-page-heading"); head.Add(heading);
            Text(heading, "SUPPLIERS  /  MARKET", "terminal-eyebrow");
            Text(heading, "LUNAR SUPPLY NETWORK", "terminal-title");
            Text(head, "03 FACTIONS  /  00 CONTRACTS", "terminal-page-meta");
            var row = Row(m_Content); row.AddToClassList("terminal-trader-row");
            string[] names = { "UNITED EARTH LOGISTICS", "LUNAR MEDICAL BUREAU", "THE SALVAGE ENGINEER" };
            string[] codes = { "UEL / 01", "LMB / 02", "SE / 03" };
            string[] roles = { "FREIGHT  /  STRUCTURE", "MEDICAL  /  RECOVERY", "SALVAGE  /  HALO PARTS" };
            string[] copy = { "Structural materials, field equipment and cargo transport.", "Medical supplies, suit diagnostics and expedition recovery.", "Halo components, reclaimed electronics and rare artifacts." };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i; var card = Panel(row, codes[i], "terminal-trader"); card.AddToClassList("terminal-trader-" + i);
                Text(card, (i + 1).ToString("00"), "terminal-trader-index").pickingMode = PickingMode.Ignore;
                var stage = new VisualElement(); stage.AddToClassList("terminal-trader-stage"); card.Add(stage);
                var stripes = new HazardStripes(TerminalPalette.A(TerminalPalette.Text, .05f), false); stripes.AddToClassList("terminal-trader-stripes"); stage.Add(stripes);
                var art = new StashItemArt(i == 0 ? StashArtKind.Alloy : i == 1 ? StashArtKind.Outfit : StashArtKind.Halo); art.AddToClassList("terminal-trader-art"); stage.Add(art);
                Text(card, roles[i], "terminal-trader-role");
                Text(card, names[i], "terminal-heading"); Text(card, copy[i], "terminal-copy");
                var standing = new VisualElement(); standing.AddToClassList("terminal-standing"); card.Add(standing);
                for (int s = 0; s < 6; s++) { var pip = new VisualElement(); pip.AddToClassList("terminal-standing-pip"); standing.Add(pip); }
                Stat(card, "CONTRACT", "NOT ESTABLISHED");
                ActionButton(card, "VIEW SUPPLIER", () => SupplierDetail(names[index]));
            }
            var foot = Row(m_Content); foot.AddToClassList("terminal-page-foot");
            Text(foot, "No trading contracts or player offers are available yet.", "terminal-copy");
            Disabled(foot, "OPEN PLAYER MARKET", "The player market is not open.");
        }
        private void SupplierDetail(string name)
        {
            var window = m_Windows.Open(name);
            var tabs = Row(window);
            foreach (var tab in new[] { "BUY", "SELL", "TASKS", "SERVICES" }) Disabled(tabs, tab, "Supplier contracts are not established.");
            Text(window, "AWAITING SUPPLY CONTRACT", "terminal-title");
            Text(window, "Buying, selling and reputation will be available once this supplier establishes a contract with your ship.", "terminal-copy");
        }

        private void Operation()
        {
            string[] steps = { "OPERATOR", "LOCATION", "BRIEFING", "RECOVERY", "READY" };
            var stepper = Row(m_Content); stepper.AddToClassList("terminal-stepper");
            var rail = new VisualElement { pickingMode = PickingMode.Ignore }; rail.AddToClassList("terminal-stepper-rail"); stepper.Add(rail);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("terminal-stepper-fill"); rail.Add(fill);
            float from = m_ShownStep < 0 ? 0 : (m_ShownStep + .5f) / steps.Length, to = (m_Step + .5f) / steps.Length; m_ShownStep = m_Step;
            fill.style.width = Length.Percent(from * 100);
            TerminalMotion.Tween(fill, .7f, t => fill.style.width = Length.Percent(Mathf.Lerp(from, to, TerminalMotion.OutExpo(t)) * 100), .1f);
            for (int i = 0; i < steps.Length; i++)
            {
                int step = i; var button = ActionButton(stepper, (i + 1).ToString("00") + "  " + steps[i], () => { m_Step = step; Render(); });
                button.EnableInClassList("terminal-tab-selected", i == m_Step); button.EnableInClassList("terminal-step-done", i < m_Step);
            }
            var row = Row(m_Content); var main = Panel(row, "EXPEDITION / " + steps[m_Step], "terminal-wide");
            var summary = Panel(row, "MISSION SUMMARY", "terminal-side");
            Text(summary, (m_Step + 1).ToString("00") + " / " + steps.Length.ToString("00"), "terminal-step-counter");
            Text(summary, "LUNAR RECOVERY", "terminal-title"); Stat(summary, "LOCATION", "MOON / SURFACE SECTOR");
            Stat(summary, "CAPACITY", RaidRules.BagCapacity + " SUPPLIES"); Stat(summary, "EXTRACTION", "GREEN BEACON");
            Stat(summary, "TEAM", "SESSION PARTICIPANTS");
            Text(summary, "Your session host determines the map and mission timing. Connection setup follows the readiness check.", "terminal-copy");
            switch (m_Step)
            {
                case 0:
                    Text(main, "CHOOSE YOUR OPERATOR", "terminal-title");
                    Text(main, "Select the field kit you will deploy with.", "terminal-copy");
                    var choices = Row(main); choices.AddToClassList("terminal-operator-row");
                    for (int i = 0; i < 3; i++)
                    {
                        int character = i; var card = Panel(choices, "0" + (i + 1), "terminal-console"); card.AddToClassList("terminal-operator-card");
                        var art = new StashItemArt(i == 2 ? StashArtKind.Halo : StashArtKind.Outfit); art.AddToClassList("terminal-trader-art"); card.Add(art);
                        bool selected = GameSettings.Instance != null && GameSettings.Instance.PlayerCharacter == i;
                        card.EnableInClassList("terminal-operator-selected", selected);
                        var button = ActionButton(card, new[] { "RIFLE", "SHOTGUN", "DOLLSINGER" }[i], () =>
                        {
                            if (GameSettings.Instance != null) GameSettings.Instance.PlayerCharacter = character;
                            Render();
                        });
                        button.EnableInClassList("terminal-tab-selected", selected);
                    }
                    break;
                case 1:
                    Text(main, "MOON / SURFACE SECTOR", "terminal-title");
                    var map = new LunarSchematic(); map.AddToClassList("terminal-operation-map"); main.Add(map);
                    Text(map, "SECTOR 07  /  MARE IMBRIUM", "terminal-map-tag").pickingMode = PickingMode.Ignore;
                    Text(main, "AVAILABLE OPERATION: LUNAR SUPPLY RECOVERY", "terminal-eyebrow");
                    Text(main, "Locate supply caches and navigate back to the extraction beacon. Additional regions are not charted for deployment.", "terminal-copy");
                    break;
                case 2:
                    Text(main, "RECOVER. SURVIVE. EXTRACT.", "terminal-title");
                    AccountClient.SelectCarryCells(AccountClient.CarryCells);
                    var carry = new IntegerField("ENERGY CELLS TO CARRY") { name = "operationCarryCells", isDelayed = true };
                    carry.AddToClassList("terminal-carry-field");
                    carry.SetValueWithoutNotify(AccountClient.CarryCells); carry.SetEnabled(!m_Busy);
                    var cargo = Text(main, $"Carry {AccountClient.CarryCells}/{RaidRules.BagCapacity} / Stash {m_Cells}", "terminal-copy");
                    var gauge = new VisualElement(); gauge.AddToClassList("terminal-cargo-gauge");
                    var gaugeFill = new VisualElement(); gaugeFill.AddToClassList("terminal-cargo-gauge-fill"); gauge.Add(gaugeFill);
                    void Gauge() => gaugeFill.style.width = Length.Percent(100f * AccountClient.CarryCells / Mathf.Max(1, RaidRules.BagCapacity));
                    carry.RegisterValueChangedCallback(evt =>
                    {
                        AccountClient.SelectCarryCells(evt.newValue);
                        carry.SetValueWithoutNotify(AccountClient.CarryCells);
                        cargo.text = $"Carry {AccountClient.CarryCells}/{RaidRules.BagCapacity} / Stash {m_Cells}"; Gauge();
                    });
                    main.Add(carry); main.Add(gauge); Gauge();
                    Text(main, "Selection does not reserve stock. Cells leave storage when the server accepts deployment. [R] consumes one cell to refill halo energy. No cell, no recharge.", "terminal-copy");
                    Stat(main, "01 / RECOVER", "[E] COLLECT NEARBY SUPPLIES");
                    Stat(main, "02 / MANAGE", "[TAB] CHECK YOUR RAID PACK");
                    Stat(main, "03 / EXTRACT", "STAY INSIDE THE GREEN BEACON");
                    Text(main, "Carried supplies are lost on death or timeout. Storage is credited after a successful server settlement. The session controls the extraction countdown.", "terminal-copy");
                    break;
                case 3:
                    Text(main, "RECOVERY CONTRACT", "terminal-title");
                    var hazard = new VisualElement(); hazard.AddToClassList("terminal-hazard-banner"); main.Add(hazard);
                    var stripes = new HazardStripes(TerminalPalette.A(TerminalPalette.Red, .35f)); stripes.AddToClassList("terminal-hazard-stripes"); hazard.Add(stripes);
                    Text(hazard, "NO RECOVERY COVERAGE", "terminal-warning");
                    Text(main, "Drone insurance is not currently offered. You are deploying without a recovery contract. Lost cargo cannot be claimed after death.", "terminal-copy");
                    Disabled(main, "PURCHASE CONTRACT", "Recovery insurance is unavailable.");
                    break;
                case 4:
                    Text(main, "READY FOR DEPLOYMENT", "terminal-title");
                    Stat(main, "OPERATOR", GameSettings.Instance != null ? new[] { "RIFLE", "SHOTGUN", "DOLLSINGER" }[Mathf.Clamp(GameSettings.Instance.PlayerCharacter, 0, 2)] : "PREVIEW");
                    Stat(main, "CARGO", AccountClient.CarryCells + " ENERGY CELLS"); Stat(main, "RECOVERY", "NO CONTRACT");
                    Text(main, "Host a session or join an existing crew in the connection terminal. Selected cells are taken from storage; unused cells return only after extraction.", "terminal-copy");
                    var go = ActionButton(main, "OPEN CONNECTION TERMINAL", m_Prepare, "terminal-primary"); go.SetEnabled(!m_Busy); go.AddToClassList("terminal-launch");
                    break;
            }
            var actions = Row(m_Content); actions.AddToClassList("terminal-page-actions");
            ActionButton(actions, "BACK", () => { if (m_Step > 0) { m_Step--; Render(); } else Navigate(0); });
            if (m_Step < 4) ActionButton(actions, "CONTINUE", () => { m_Step++; Render(); }, "terminal-primary").SetEnabled(!m_Busy);
        }

        private void Tasks()
        {
            var row = Row(m_Content); var list = Panel(row, "EXPEDITION OBJECTIVES", "terminal-side");
            Text(list, "ACTIVE / FIELD PROTOCOL", "terminal-eyebrow");
            var progress = Row(list); progress.AddToClassList("terminal-task-progress");
            Meter(progress, "DONE", 0, "0/3", TerminalPalette.Yellow);
            var progressCopy = new VisualElement(); progressCopy.AddToClassList("terminal-task-progress-copy"); progress.Add(progressCopy);
            Text(progressCopy, "LUNAR RECOVERY", "terminal-title");
            Text(progressCopy, "YOUR FIRST RETURN", "terminal-heading");
            Text(list, "Recover supplies from the surface.", "terminal-copy");
            var chain = new VisualElement(); chain.AddToClassList("terminal-task-chain"); list.Add(chain);
            Text(chain, "TASK CHAINS", "terminal-eyebrow");
            foreach (var locked in new[] { "SUPPLIER CONTRACTS", "SHIP EXPANSION", "DEEP SURVEY" }) Disabled(chain, locked, "Additional task chains are not available yet.");
            var detail = Panel(row, "MISSION BRIEF", "terminal-wide"); Text(detail, "EVERY RETURN COUNTS", "terminal-title");
            Text(detail, "Your ship needs recovered materials. Search the lunar caches, manage your limited cargo space and make it back to the extraction zone.", "terminal-copy");
            var objectives = new VisualElement(); objectives.AddToClassList("terminal-objectives"); detail.Add(objectives);
            Stat(objectives, "OBJECTIVE 01", "COLLECT ANY SURFACE SUPPLY"); Stat(objectives, "OBJECTIVE 02", "REACH THE EXTRACTION BEACON");
            Stat(objectives, "OBJECTIVE 03", "SURVIVE THE EXTRACTION COUNTDOWN");
            TerminalMotion.Cascade(objectives, .5f, .12f, 30, 0);
            Text(detail, "REWARD / ALL SUCCESSFULLY EXTRACTED CARGO", "terminal-positive");
            ActionButton(detail, "PLAN THIS OPERATION", Prepare, "terminal-primary");
            Text(detail, "This is the expedition protocol. Separate quest rewards and progression are not available yet.", "terminal-copy");
        }
        private void Base()
        {
            Text(m_Content, "ORBITAL SHIP / MODULE MANAGEMENT", "terminal-title");
            var top = Row(m_Content);
            var plan = Panel(top, "HULL  /  DECK PLAN", "terminal-wide"); plan.AddToClassList("terminal-blueprint-panel");
            var blueprint = new ShipBlueprint(); blueprint.AddToClassList("terminal-blueprint"); plan.Add(blueprint);
            var stores = Panel(top, "ONBOARD RESOURCES", "terminal-resources");
            CountStat(stores, "MOON DUST", m_Dust, .5f); CountStat(stores, "ALLOY", m_Alloy, .6f); CountStat(stores, "ENERGY CELLS", m_Cells, .7f);
            var power = Row(stores); power.AddToClassList("terminal-meters");
            Meter(power, "POWER", .25f, "25%", TerminalPalette.Green); Meter(power, "HULL", .92f, "92%", TerminalPalette.Cyan);
            var row = Row(m_Content); row.AddToClassList("terminal-module-row");
            string[] modules = { "CARGO HOLD", "HALO WORKSHOP", "MEDICAL BAY", "REACTOR" };
            for (int i = 0; i < modules.Length; i++)
            {
                string module = modules[i];
                var card = Panel(row, module, "terminal-console"); card.AddToClassList("terminal-module");
                card.EnableInClassList("terminal-module-online", module == "CARGO HOLD");
                Text(card, "B" + (i + 1), "terminal-module-index").pickingMode = PickingMode.Ignore;
                Text(card, module == "CARGO HOLD" ? "ONLINE" : "OFFLINE", module == "CARGO HOLD" ? "terminal-positive" : "terminal-warning");
                Text(card, module == "CARGO HOLD" ? "Personal storage is available at the cargo terminal." : "Module upgrades and crafting require a ship construction contract.", "terminal-copy");
                if (module == "CARGO HOLD") ActionButton(card, "OPEN STORAGE", () => Navigate(1)); else Disabled(card, "UPGRADE / CRAFT", "Ship construction is unavailable.");
            }
        }
        private static void CountStat(VisualElement parent, string name, int value, float delay)
        {
            Stat(parent, name, value.ToString("N0"));
            var label = parent[parent.childCount - 1].Q<Label>(className: "terminal-stat-value");
            TerminalMotion.CountUp(label, value, "{0:N0}", delay, 1.3f);
        }
        private void Comms()
        {
            var row = Row(m_Content); var contacts = Panel(row, "CONTACTS", "terminal-side");
            Text(contacts, "SHIP CHANNEL", "terminal-title"); Text(contacts, "No incoming transmissions.", "terminal-copy");
            foreach (var channel in new[] { "UEL / FREIGHT", "LMB / MEDICAL", "SE / SALVAGE", "CREW / SESSION" })
            {
                var item = Row(contacts); item.AddToClassList("terminal-contact");
                var led = new VisualElement(); led.AddToClassList("terminal-contact-led"); item.Add(led);
                Text(item, channel, "terminal-contact-name"); Text(item, "OFFLINE", "terminal-contact-state");
            }
            var messages = Panel(row, "COMMUNICATIONS / 00 MESSAGES", "terminal-wide");
            var wave = new SignalWave(); wave.AddToClassList("terminal-comms-wave"); messages.Add(wave);
            Text(wave, "CARRIER  /  148.250 MHZ", "terminal-wave-tag").pickingMode = PickingMode.Ignore;
            Text(messages, "CHANNEL QUIET", "terminal-hero-title");
            Text(messages, "Supplier rewards, recovery notices and crew messages will appear here when communications service is available.", "terminal-copy");
            Disabled(messages, "CLAIM ATTACHMENTS", "There are no message attachments.");
        }
        private void Settings()
        {
            var row = Row(m_Content); var settings = Panel(row, "PRESENTATION / AUDIO", "terminal-wide");
            Text(settings, "TERMINAL PREFERENCES", "terminal-title");
            var volume = new Slider("MASTER AUDIO", 0, 1) { value = AudioListener.volume };
            volume.AddToClassList("terminal-slider"); volume.RegisterValueChangedCallback(e => AudioListener.volume = e.newValue); settings.Add(volume);
            var hud = new Toggle("ALWAYS SHOW FIELD STATUS") { value = PlayerPrefs.GetInt("Moonkov.AlwaysShowHUD", 0) != 0 };
            hud.AddToClassList("terminal-toggle");
            hud.RegisterValueChangedCallback(e => PlayerPrefs.SetInt("Moonkov.AlwaysShowHUD", e.newValue ? 1 : 0)); settings.Add(hud);
            Text(settings, "The default HUD reveals critical states automatically. Hold [H] to check status or press [Tab] to open your raid pack.", "terminal-copy");
            var signal = new SignalWave(); signal.AddToClassList("terminal-settings-wave"); settings.Add(signal);
            var keys = Panel(row, "KEYBOARD / MOUSE", "terminal-side"); keys.AddToClassList("terminal-keys");
            Stat(keys, "RAID INVENTORY", "TAB"); Stat(keys, "STATUS CHECK", "H (HOLD)"); Stat(keys, "PICKUP", "E");
            Stat(keys, "ROTATE IN STASH", "R"); Stat(keys, "ITEM INSPECT", "DOUBLE CLICK / MIDDLE");
            Stat(keys, "ADVANCED ACTIONS", "RIGHT CLICK"); Stat(keys, "CLOSE TOP WINDOW", "ESC");
            Text(keys, "Container transfers and equipment changes become available with the inventory service.", "terminal-copy");
        }

        // Endless marquee; the copy is doubled so the loop has no seam.
        internal static VisualElement Ticker(VisualElement parent, string text)
        {
            var ticker = new VisualElement { pickingMode = PickingMode.Ignore }; ticker.AddToClassList("terminal-ticker"); parent.Add(ticker);
            var track = new VisualElement { pickingMode = PickingMode.Ignore }; track.AddToClassList("terminal-ticker-track"); ticker.Add(track);
            var first = Text(track, text, "terminal-ticker-text"); Text(track, text, "terminal-ticker-text");
            double start = TerminalMotion.Now;
            ticker.schedule.Execute(() =>
            {
                float w = first.layout.width; if (float.IsNaN(w) || w < 1) return;
                track.style.translate = new Translate(-Mathf.Repeat((float)(TerminalMotion.Now - start) * 42, w), 0);
            }).Every(16);
            return ticker;
        }
        internal static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.AddToClassList("terminal-row"); parent.Add(row); return row;
        }
        internal static VisualElement Panel(VisualElement parent, string title, string css)
        {
            var panel = new VisualElement(); panel.AddToClassList("terminal-panel"); panel.AddToClassList(css);
            Text(panel, title, "terminal-eyebrow"); TechFrame.Attach(panel, Mathf.Min(s_FrameDelay, .6f)); s_FrameDelay += .08f;
            parent.Add(panel); return panel;
        }
        internal static Label Text(VisualElement parent, string text, string css)
        {
            var label = new Label(text); label.AddToClassList(css); parent.Add(label); return label;
        }
        internal static Button ActionButton(VisualElement parent, string text, Action action, string css = "terminal-button")
        {
            var button = new Button(action) { text = text }; button.AddToClassList(css); parent.Add(button); return button;
        }
        internal static void Disabled(VisualElement parent, string text, string reason)
        {
            var button = ActionButton(parent, text, null); button.tooltip = reason; button.SetEnabled(false);
        }
        internal static void Stat(VisualElement parent, string name, string value)
        {
            var row = Row(parent); row.AddToClassList("terminal-stat"); Text(row, name, "terminal-stat-name"); Text(row, value, "terminal-stat-value");
        }
        public void Dispose()
        {
            DisposeCharacter();
            m_Windows.Dispose(); m_Content.RemoveFromHierarchy(); m_Nav.RemoveFromHierarchy();
            m_Shade.RemoveFromHierarchy(); m_Chrome.RemoveFromHierarchy(); m_FooterTicker?.RemoveFromHierarchy();
            m_Root.Query(className: "terminal-wipe").ForEach(e => e.RemoveFromHierarchy());
            m_Gear.style.display = DisplayStyle.Flex;
        }
        private void DisposeCharacter()
        {
            if (m_Character == null) return;
            m_Character.Element.RemoveFromHierarchy(); m_Character.Dispose(); m_Character = null;
        }
    }
}
