using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Navigation and presentation. Economy actions remain disabled until a server API exists.
    public sealed class MoonkovTerminal : IDisposable
    {
        private readonly VisualElement m_Root, m_Gear, m_Content, m_Nav, m_Shade;
        private readonly Action m_Prepare;
        private readonly List<Button> m_Tabs = new List<Button>();
        private readonly string[] m_Pages = { "SHIP", "CHARACTER", "HALO BUILD", "SUPPLIERS", "OPERATIONS", "TASKS", "BASE", "COMMS", "SETTINGS" };
        private readonly TerminalWindows m_Windows;
        private MenuCharacterView m_Character;
        private int m_Page, m_Step;
        private bool m_Busy;
        private string m_Player;
        private int m_Dust, m_Alloy, m_Cells;
        private readonly Label m_Title, m_Subtitle;
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
            for (int i = 0; i < m_Pages.Length; i++)
            {
                int page = i;
                var tab = ActionButton(m_Nav, m_Pages[i], () => Navigate(page), "terminal-tab"); m_Tabs.Add(tab);
                if (i == 3 || i == 5 || i == 6 || i == 7) tab.style.display = DisplayStyle.None;
            }
            m_Content = new VisualElement(); m_Content.AddToClassList("terminal-content"); root.Insert(3, m_Content);
            m_Windows = new TerminalWindows(root, () => Navigate(0));
            ActionButton(m_Nav, "MORE", More, "terminal-tab");
            m_Shade = new MenuShade(); m_Shade.AddToClassList("terminal-scene-shade"); root.Insert(0, m_Shade);
            Navigate(0);
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
            m_Page = page; m_Step = 0; m_Windows?.CloseAll();
            m_Root.EnableInClassList("terminal-is-ship", page == 0);
            for (int i = 0; i < m_Tabs.Count; i++) m_Tabs[i].EnableInClassList("terminal-tab-selected", i == page);
            m_Gear.style.display = page == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Content.style.display = page == 1 ? DisplayStyle.None : DisplayStyle.Flex;
            m_Title.text = m_Pages[page];
            m_Subtitle.text = page == 1 ? "EQUIPMENT / PERSONAL STORAGE" : "ORBITAL SHIP / " + m_Pages[page];
            if (page != 1) Render();
            else
            {
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
        private void Render()
        {
            DisposeCharacter();
            m_Content.Clear();
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
        }

        private void Ship()
        {
            m_Character = new MenuCharacterView(); m_Content.Add(m_Character.Element);
            var menu = new VisualElement(); menu.AddToClassList("terminal-home-menu"); m_Content.Add(menu);
            Text(menu, "LUNAR EXPEDITION", "terminal-home-caption");
            Text(menu, "MOONKOV", "terminal-home-title");
            Text(menu, "ORBITAL SHIP  /  FIELD TERMINAL", "terminal-home-description");
            var deploy = ActionButton(menu, "OPERATION", Prepare, "terminal-home-action");
            deploy.AddToClassList("terminal-home-deploy"); deploy.SetEnabled(!m_Busy);
            ActionButton(menu, "CHARACTER", () => Navigate(1), "terminal-home-action");
            ActionButton(menu, "HALO", () => Navigate(2), "terminal-home-action");
            ActionButton(menu, "SETTINGS", () => Navigate(8), "terminal-home-action");
            Text(m_Content, "DOLLSINGER", "terminal-home-operator");
        }
        private void More()
        {
            var window = m_Windows.Open("SHIP SERVICES", null, true);
            foreach (int index in new[] { 3, 5, 6, 7 })
            {
                int page = index; ActionButton(window, m_Pages[index], () => Navigate(page));
            }
        }
        private void Console(VisualElement parent, string eyebrow, string title, string copy, int page)
        {
            var card = Panel(parent, eyebrow, "terminal-console"); Text(card, title, "terminal-heading"); Text(card, copy, "terminal-copy");
            ActionButton(card, "ACCESS TERMINAL", () => Navigate(page));
        }

        private void Halo()
        {
            var row = Row(m_Content);
            var diagram = Panel(row, "STANDARD HALO / COMPONENT SCHEMATIC", "terminal-wide");
            Text(diagram, "H-01   /   FIELD ISSUE", "terminal-title");
            var schematic = new HaloSchematic(); schematic.AddToClassList("terminal-halo-schematic"); diagram.Add(schematic);
            string[] nodes = { "FOCUSING LENS", "LEFT EMITTER", "HALO FRAME", "RIGHT EMITTER", "CORE", "AMPLIFIER", "CONTROL MODULE" };
            Vector2[] positions = { new Vector2(40, 5), new Vector2(2, 39), new Vector2(40, 30), new Vector2(78, 39), new Vector2(40, 64), new Vector2(2, 78), new Vector2(78, 78) };
            for (int i = 0; i < nodes.Length; i++)
            {
                string node = nodes[i]; var button = ActionButton(schematic, node, () => DescribeNode(node), "terminal-node");
                button.style.left = new Length(positions[i].x, LengthUnit.Percent); button.style.top = new Length(positions[i].y, LengthUnit.Percent);
            }
            Text(diagram, "SELECT A NODE TO INSPECT ITS ROLE", "terminal-eyebrow");
            var stats = Panel(row, "LOADOUT INFORMATION", "terminal-side");
            var weapon = WeaponManager.Instance != null && WeaponManager.Instance.WeaponRegistry != null ? WeaponManager.Instance.WeaponRegistry.GetWeaponData(2) : null;
            Stat(stats, "STATUS", "STANDARD ISSUE");
            Stat(stats, "DAMAGE", weapon != null ? weapon.Damage.ToString() : "—");
            Stat(stats, "ENERGY CAPACITY", weapon != null ? weapon.MagazineSize.ToString() : "—");
            Stat(stats, "RANGE", weapon != null ? weapon.HitscanRange.ToString("0") + " m" : "—");
            Text(stats, "Modular halo assembly is awaiting workshop access. Your current field weapon remains standard issue.", "terminal-copy");
            Disabled(stats, "SAVE HALO PRESET", "No modular loadout service is available.");
            Disabled(stats, "ASSEMBLE LOADOUT", "No modular loadout service is available.");
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
            Text(m_Content, "LUNAR SUPPLY NETWORK", "terminal-title");
            Text(m_Content, "SUPPLIERS  /  MARKET", "terminal-eyebrow");
            var row = Row(m_Content);
            string[] names = { "UNITED EARTH LOGISTICS", "LUNAR MEDICAL BUREAU", "THE SALVAGE ENGINEER" };
            string[] codes = { "UEL / 01", "LMB / 02", "SE / 03" };
            string[] copy = { "Structural materials, field equipment and cargo transport.", "Medical supplies, suit diagnostics and expedition recovery.", "Halo components, reclaimed electronics and rare artifacts." };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i; var card = Panel(row, codes[i], "terminal-trader");
                var art = new StashItemArt(i == 0 ? StashArtKind.Alloy : i == 1 ? StashArtKind.Outfit : StashArtKind.Halo); art.AddToClassList("terminal-trader-art"); card.Add(art);
                Text(card, names[i], "terminal-heading"); Text(card, copy[i], "terminal-copy");
                Stat(card, "CONTRACT", "NOT ESTABLISHED");
                ActionButton(card, "VIEW SUPPLIER", () => SupplierDetail(names[index]));
            }
            Text(m_Content, "No trading contracts or player offers are available yet.", "terminal-copy");
            Disabled(m_Content, "OPEN PLAYER MARKET", "The player market is not open.");
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
            for (int i = 0; i < steps.Length; i++)
            {
                int step = i; var button = ActionButton(stepper, (i + 1).ToString("00") + "  " + steps[i], () => { m_Step = step; Render(); });
                button.EnableInClassList("terminal-tab-selected", i == m_Step);
            }
            var row = Row(m_Content); var main = Panel(row, "EXPEDITION / " + steps[m_Step], "terminal-wide");
            var summary = Panel(row, "MISSION SUMMARY", "terminal-side");
            Text(summary, "LUNAR RECOVERY", "terminal-title"); Stat(summary, "LOCATION", "MOON / SURFACE SECTOR");
            Stat(summary, "CAPACITY", RaidRules.BagCapacity + " SUPPLIES"); Stat(summary, "EXTRACTION", "GREEN BEACON");
            Stat(summary, "TEAM", "SESSION PARTICIPANTS");
            Text(summary, "Your session host determines the map and mission timing. Connection setup follows the readiness check.", "terminal-copy");
            switch (m_Step)
            {
                case 0:
                    Text(main, "CHOOSE YOUR OPERATOR", "terminal-title");
                    Text(main, "Select the field kit you will deploy with.", "terminal-copy");
                    var choices = Row(main);
                    for (int i = 0; i < 3; i++)
                    {
                        int character = i; var card = Panel(choices, "0" + (i + 1), "terminal-console");
                        var art = new StashItemArt(i == 2 ? StashArtKind.Halo : StashArtKind.Outfit); art.AddToClassList("terminal-trader-art"); card.Add(art);
                        var button = ActionButton(card, new[] { "RIFLE", "SHOTGUN", "DOLLSINGER" }[i], () =>
                        {
                            if (GameSettings.Instance != null) GameSettings.Instance.PlayerCharacter = character;
                            Render();
                        });
                        button.EnableInClassList("terminal-tab-selected", GameSettings.Instance != null && GameSettings.Instance.PlayerCharacter == i);
                    }
                    break;
                case 1:
                    Text(main, "MOON / SURFACE SECTOR", "terminal-title");
                    var map = new LunarSchematic(); map.AddToClassList("terminal-operation-map"); main.Add(map);
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
                    carry.RegisterValueChangedCallback(evt =>
                    {
                        AccountClient.SelectCarryCells(evt.newValue);
                        carry.SetValueWithoutNotify(AccountClient.CarryCells);
                        cargo.text = $"Carry {AccountClient.CarryCells}/{RaidRules.BagCapacity} / Stash {m_Cells}";
                    });
                    main.Add(carry);
                    Text(main, "Selection does not reserve stock. Cells leave storage when the server accepts deployment. [R] consumes one cell to refill halo energy. No cell, no recharge.", "terminal-copy");
                    Stat(main, "01 / RECOVER", "[E] COLLECT NEARBY SUPPLIES");
                    Stat(main, "02 / MANAGE", "[TAB] CHECK YOUR RAID PACK");
                    Stat(main, "03 / EXTRACT", "STAY INSIDE THE GREEN BEACON");
                    Text(main, "Carried supplies are lost on death or timeout. Storage is credited after a successful server settlement. The session controls the extraction countdown.", "terminal-copy");
                    break;
                case 3:
                    Text(main, "RECOVERY CONTRACT", "terminal-title");
                    Text(main, "NO RECOVERY COVERAGE", "terminal-warning");
                    Text(main, "Drone insurance is not currently offered. You are deploying without a recovery contract. Lost cargo cannot be claimed after death.", "terminal-copy");
                    Disabled(main, "PURCHASE CONTRACT", "Recovery insurance is unavailable.");
                    break;
                case 4:
                    Text(main, "READY FOR DEPLOYMENT", "terminal-title");
                    Stat(main, "OPERATOR", GameSettings.Instance != null ? new[] { "RIFLE", "SHOTGUN", "DOLLSINGER" }[Mathf.Clamp(GameSettings.Instance.PlayerCharacter, 0, 2)] : "PREVIEW");
                    Stat(main, "CARGO", AccountClient.CarryCells + " ENERGY CELLS"); Stat(main, "RECOVERY", "NO CONTRACT");
                    Text(main, "Host a session or join an existing crew in the connection terminal. Selected cells are taken from storage; unused cells return only after extraction.", "terminal-copy");
                    ActionButton(main, "OPEN CONNECTION TERMINAL", m_Prepare, "terminal-primary").SetEnabled(!m_Busy);
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
            Text(list, "LUNAR RECOVERY", "terminal-title");
            Text(list, "YOUR FIRST RETURN", "terminal-heading");
            Text(list, "Recover supplies from the surface.", "terminal-copy");
            var detail = Panel(row, "MISSION BRIEF", "terminal-wide"); Text(detail, "EVERY RETURN COUNTS", "terminal-title");
            Text(detail, "Your ship needs recovered materials. Search the lunar caches, manage your limited cargo space and make it back to the extraction zone.", "terminal-copy");
            Stat(detail, "OBJECTIVE 01", "COLLECT ANY SURFACE SUPPLY"); Stat(detail, "OBJECTIVE 02", "REACH THE EXTRACTION BEACON");
            Stat(detail, "OBJECTIVE 03", "SURVIVE THE EXTRACTION COUNTDOWN");
            Text(detail, "REWARD / ALL SUCCESSFULLY EXTRACTED CARGO", "terminal-positive");
            ActionButton(detail, "PLAN THIS OPERATION", Prepare, "terminal-primary");
            Text(detail, "This is the expedition protocol. Separate quest rewards and progression are not available yet.", "terminal-copy");
        }
        private void Base()
        {
            Text(m_Content, "ORBITAL SHIP / MODULE MANAGEMENT", "terminal-title");
            var row = Row(m_Content);
            string[] modules = { "CARGO HOLD", "HALO WORKSHOP", "MEDICAL BAY", "REACTOR" };
            foreach (string module in modules)
            {
                var card = Panel(row, module, "terminal-console");
                Text(card, module == "CARGO HOLD" ? "ONLINE" : "OFFLINE", module == "CARGO HOLD" ? "terminal-positive" : "terminal-warning");
                Text(card, module == "CARGO HOLD" ? "Personal storage is available at the cargo terminal." : "Module upgrades and crafting require a ship construction contract.", "terminal-copy");
                if (module == "CARGO HOLD") ActionButton(card, "OPEN STORAGE", () => Navigate(1)); else Disabled(card, "UPGRADE / CRAFT", "Ship construction is unavailable.");
            }
            var stores = Panel(m_Content, "ONBOARD RESOURCES", "terminal-resources");
            Stat(stores, "MOON DUST", m_Dust.ToString("N0")); Stat(stores, "ALLOY", m_Alloy.ToString("N0")); Stat(stores, "ENERGY CELLS", m_Cells.ToString("N0"));
        }
        private void Comms()
        {
            var row = Row(m_Content); var contacts = Panel(row, "CONTACTS", "terminal-side");
            Text(contacts, "SHIP CHANNEL", "terminal-title"); Text(contacts, "No incoming transmissions.", "terminal-copy");
            var messages = Panel(row, "COMMUNICATIONS / 00 MESSAGES", "terminal-wide");
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
            hud.RegisterValueChangedCallback(e => PlayerPrefs.SetInt("Moonkov.AlwaysShowHUD", e.newValue ? 1 : 0)); settings.Add(hud);
            Text(settings, "The default HUD reveals critical states automatically. Hold [H] to check status or press [Tab] to open your raid pack.", "terminal-copy");
            var keys = Panel(row, "KEYBOARD / MOUSE", "terminal-side");
            Stat(keys, "RAID INVENTORY", "TAB"); Stat(keys, "STATUS CHECK", "H (HOLD)"); Stat(keys, "PICKUP", "E");
            Stat(keys, "ROTATE IN STASH", "R"); Stat(keys, "ITEM INSPECT", "DOUBLE CLICK / MIDDLE");
            Stat(keys, "ADVANCED ACTIONS", "RIGHT CLICK"); Stat(keys, "CLOSE TOP WINDOW", "ESC");
            Text(keys, "Container transfers and equipment changes become available with the inventory service.", "terminal-copy");
        }

        internal static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.AddToClassList("terminal-row"); parent.Add(row); return row;
        }
        internal static VisualElement Panel(VisualElement parent, string title, string css)
        {
            var panel = new VisualElement(); panel.AddToClassList("terminal-panel"); panel.AddToClassList(css);
            Text(panel, title, "terminal-eyebrow"); parent.Add(panel); return panel;
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
            m_Shade.RemoveFromHierarchy();
            m_Gear.style.display = DisplayStyle.Flex;
        }
        private void DisposeCharacter()
        {
            if (m_Character == null) return;
            m_Character.Element.RemoveFromHierarchy(); m_Character.Dispose(); m_Character = null;
        }
    }

    internal sealed class MenuShade : VisualElement
    {
        public MenuShade() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height; if (w < 1 || h < 1) return;
            var p = ctx.painter2D;
            p.fillGradient = FillGradient.MakeLinearGradient(new Color(1, 1, 1, .78f), new Color(1, 1, 1, 1f), Vector2.zero, new Vector2(w * .73f, 0), AddressMode.Clamp);
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h)); p.ClosePath(); p.Fill();
        }
    }

    internal sealed class LunarSchematic : VisualElement
    {
        public LunarSchematic() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height; if (w < 1 || h < 1) return;
            var p = ctx.painter2D;
            p.strokeColor = new Color(.24f, .27f, .26f, .55f); p.lineWidth = 1;
            for (int x = 0; x <= 16; x++) { p.BeginPath(); p.MoveTo(new Vector2(w * x / 16, 0)); p.LineTo(new Vector2(w * x / 16, h)); p.Stroke(); }
            for (int y = 0; y <= 10; y++) { p.BeginPath(); p.MoveTo(new Vector2(0, h * y / 10)); p.LineTo(new Vector2(w, h * y / 10)); p.Stroke(); }
            Vector2 center = new Vector2(w * .56f, h * .5f); float radius = Mathf.Min(w * .42f, h * .44f);
            p.fillColor = new Color(.14f, .16f, .16f); p.strokeColor = new Color(.49f, .5f, .45f); p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(center, radius, 0, 360); p.Fill(); p.Stroke();
            for (int i = 0; i < 27; i++)
            {
                float a = i * 2.39996f; float r = radius * Mathf.Sqrt((i + .5f) / 30f);
                var c = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                p.strokeColor = new Color(.35f, .38f, .35f, .6f); p.BeginPath(); p.Arc(c, radius * (.035f + (i % 4) * .02f), 0, 360); p.Stroke();
            }
            p.strokeColor = new Color(.71f, .68f, .55f); p.BeginPath(); p.Arc(center, radius * 1.12f, 200, 335); p.Stroke();
            var point = center + new Vector2(radius * .22f, -radius * .14f);
            p.fillColor = new Color(.77f, .73f, .6f); p.BeginPath(); p.Arc(point, 4, 0, 360); p.Fill();
            p.BeginPath(); p.MoveTo(point); p.LineTo(point + new Vector2(42, -24)); p.LineTo(point + new Vector2(96, -24)); p.Stroke();
        }
    }
    internal sealed class HaloSchematic : VisualElement
    {
        public HaloSchematic() { generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height; if (w < 1 || h < 1) return;
            var p = ctx.painter2D; var c = new Vector2(w * .5f, h * .5f); float r = Mathf.Min(w * .22f, h * .23f);
            p.strokeColor = new Color(.66f, .64f, .52f); p.lineWidth = 3;
            p.BeginPath(); p.Arc(c, r, 0, 360); p.Stroke(); p.lineWidth = 1;
            p.BeginPath(); p.Arc(c, r * .79f, 0, 360); p.Stroke();
            p.strokeColor = new Color(.32f, .36f, .33f);
            foreach (var point in new[] { new Vector2(w * .5f, h * .12f), new Vector2(w * .14f, h * .47f), new Vector2(w * .86f, h * .47f), new Vector2(w * .5f, h * .72f), new Vector2(w * .14f, h * .86f), new Vector2(w * .86f, h * .86f) })
            { p.BeginPath(); p.MoveTo(c); p.LineTo(point); p.Stroke(); }
            p.fillColor = new Color(.74f, .7f, .57f); p.BeginPath(); p.Arc(c, 7, 0, 360); p.Fill();
        }
    }
}
