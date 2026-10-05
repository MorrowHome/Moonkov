using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Navigation and presentation; shop transactions go through the account inventory service.
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
        private readonly List<Action> m_NavActions = new List<Action>();
        private readonly IVisualElementScheduledItem m_ClockTimer;
        private IVisualElementScheduledItem m_IndicatorTimer;
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
            m_Nav = root.Q<VisualElement>("terminalNav");
            m_NavTabs = root.Q<VisualElement>("terminalNavTabs");
            for (int i = 0; i < m_Pages.Length; i++)
            {
                int page = i;
                var tab = root.Q<Button>("terminalTab" + i);
                Action action = () => Navigate(page);
                tab.clicked += action; m_NavActions.Add(action); m_Tabs.Add(tab);
            }
            m_Indicator = root.Q<VisualElement>(className: "terminal-nav-indicator");
            m_NavTabs.RegisterCallback<GeometryChangedEvent>(NavGeometryChanged);
            var dot = root.Q<VisualElement>(className: "terminal-nav-dot");
            m_Clock = root.Q<Label>(className: "terminal-nav-clock");
            m_ClockTimer = m_Nav.schedule.Execute(() =>
            {
                m_Clock.text = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
                dot.EnableInClassList("terminal-nav-dot-dim", DateTime.UtcNow.Second % 2 == 0);
            }).Every(500);
            m_Content = root.Q<VisualElement>("terminalContent");
            m_Content.RegisterCallback<GeometryChangedEvent>(ContentGeometryChanged);
            var spacer = root.Q<VisualElement>(className: "stash-footer-spacer");
            if (spacer != null) m_FooterTicker = Ticker(spacer, k_Ticker);
            m_Chrome = new TerminalChrome(); root.Add(m_Chrome);
            m_Shade = new LunarBackdrop(BackdropMode.Ship, root); m_Shade.AddToClassList("terminal-scene-shade"); root.Insert(0, m_Shade);
            m_Windows = new TerminalWindows(root, () => Navigate(0));
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
            m_IndicatorTimer?.Pause();
            m_IndicatorTimer = m_Nav.schedule.Execute(() => MoveIndicator(true));
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
            TerminalLayout.Populate(m_Content, "Ship");
            m_Character = new MenuCharacterView(element: m_Content.Q<Image>("characterView"));
            TerminalMotion.Reveal(m_Character.Element, .05f, -60, 0, 1.1f);
            var hero = m_Content.Q<VisualElement>(className: "terminal-home-hero");
            TerminalMotion.Cascade(hero, .25f, .09f, -40, 0);
            TerminalMotion.Scramble(hero.Q<Label>(className: "terminal-home-title"), .35f, .9f);
            var deploy = m_Content.Q<Button>("homeAction0"); deploy.clicked += Prepare; deploy.SetEnabled(!m_Busy);
            m_Content.Q<Button>("homeAction1").clicked += () => Navigate(1);
            m_Content.Q<Button>("homeAction2").clicked += () => Navigate(2);
            m_Content.Q<Button>("homeAction3").clicked += () => Navigate(8);
            int[] pages = { 3, 5, 6, 7 };
            for (int i = 0; i < pages.Length; i++)
            {
                int page = pages[i]; m_Content.Q<Button>("homeChip" + i).clicked += () => Navigate(page);
            }
            int[] resources = { m_Dust, m_Alloy, m_Cells };
            for (int i = 0; i < resources.Length; i++)
                TerminalMotion.CountUp(m_Content.Q<Label>("resourceValue" + i), resources[i], "{0:N0}", .7f + i * .1f, 1.4f);
            UpdateHomeMenuSize();
            var menu = m_Content.Q<VisualElement>(className: "terminal-home-menu");
            float delay = .35f;
            foreach (var child in menu.Children())
            {
                if (child.ClassListContains("terminal-row")) delay = TerminalMotion.Cascade(child, delay, .06f, 40, 0);
                else { TerminalMotion.Reveal(child, delay, 60, 0, .7f); delay += .08f; }
            }
            var card = m_Content.Q<VisualElement>(className: "terminal-home-operator-card");
            card.Q<Label>(className: "terminal-home-player").text = string.IsNullOrEmpty(m_Player) ? "UNREGISTERED CREW" : m_Player.ToUpperInvariant();
            var state = card.Q<Label>(className: "terminal-positive");
            state.text = m_Busy ? "LINK BUSY  /  STAND BY" : "READY FOR DEPLOYMENT";
            state.EnableInClassList("terminal-warning", m_Busy); state.EnableInClassList("terminal-positive", !m_Busy);
            TerminalMotion.Reveal(card, .55f, -30, 0, .7f);
            var ticker = m_Content.Q<VisualElement>(className: "terminal-home-ticker");
            AnimateTicker(ticker); TerminalMotion.Fade(ticker, .8f, .6f);
        }

        private void UpdateHomeMenuSize()
        {
            var menu = m_Content.Q<VisualElement>(className: "terminal-home-menu");
            menu?.EnableInClassList("terminal-home-menu-compact", m_Content.contentRect.height < 500 || m_Content.contentRect.width < 1120);
        }


        private void Halo()
        {
            TerminalLayout.Populate(m_Content, "Halo");
            var schematic = m_Content.Q<HaloSchematic>();
            var nodes = schematic.Query<Button>(className: "terminal-node").ToList();
            void UpdateNodes()
            {
                float w = schematic.contentRect.width, h = schematic.contentRect.height;
                if (w < 1 || h < 1) return;
                schematic.SetNodes(nodes.Select(n => new Vector2(n.layout.x * 100 / w, n.layout.y * 100 / h)));
            }
            schematic.RegisterCallback<GeometryChangedEvent>(_ => UpdateNodes());
            for (int i = 0; i < nodes.Count; i++)
            {
                var button = nodes[i]; string node = button.text;
                button.clicked += () => DescribeNode(node);
                button.RegisterCallback<GeometryChangedEvent>(_ => UpdateNodes());
                TerminalMotion.Fade(button, .45f + i * .08f, .4f);
            }
            var weapon = WeaponManager.Instance != null && WeaponManager.Instance.WeaponRegistry != null ? WeaponManager.Instance.WeaponRegistry.GetWeaponData(2) : null;
            var meters = m_Content.Query<ArcMeter>().ToList();
            float[] values = { weapon != null ? weapon.Damage / 100f : 0, weapon != null ? weapon.MagazineSize / 60f : 0, weapon != null ? weapon.HitscanRange / 300f : 0 };
            string[] display = { weapon != null ? weapon.Damage.ToString() : "—", weapon != null ? weapon.MagazineSize.ToString() : "—", weapon != null ? weapon.HitscanRange.ToString("0") : "—" };
            for (int i = 0; i < meters.Count; i++) { meters[i].Value = values[i]; meters[i].Q<Label>(className: "terminal-meter-value").text = display[i]; }
            m_Content.Q<Label>("statValue1").text = display[0];
            m_Content.Q<Label>("statValue2").text = display[1];
            m_Content.Q<Label>("statValue3").text = weapon != null ? display[2] + " m" : "—";
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
            TerminalLayout.Populate(m_Content, "Suppliers");
            string[] names = { "UNITED EARTH LOGISTICS", "LUNAR MEDICAL BUREAU", "THE SALVAGE ENGINEER" };
            for (int i = 0; i < names.Length; i++)
            {
                string supplier = names[i]; m_Content.Q<Button>("supplier" + i).clicked += () => SupplierDetail(supplier);
            }
            m_Content.Q<Button>("emergencySupply").clicked += () => OpenShop("LUNAR EMERGENCY SUPPLY", true);
        }
        private void SupplierDetail(string name)
        {
            OpenShop(name);
        }
        private void OpenShop(string name, bool recovery = false)
        {
            var window = m_Windows.Open(name);
            new MoonkovShopView(window, () => { m_Windows.Close(window); Navigate(1); }, recovery);
        }

        private void Operation()
        {
            TerminalLayout.Populate(m_Content, "Operation");
            string[] steps = { "OPERATOR", "LOCATION", "BRIEFING", "RECOVERY", "READY" };
            string[] layouts = { "OperationOperator", "OperationLocation", "OperationBriefing", "OperationRecovery", "OperationReady" };
            var stepper = m_Content.Q<VisualElement>("operationStepper");
            var buttons = stepper.Query<Button>().ToList();
            var fill = stepper.Q<VisualElement>(className: "terminal-stepper-fill");
            float from = m_ShownStep < 0 ? 0 : (m_ShownStep + .5f) / steps.Length, to = (m_Step + .5f) / steps.Length; m_ShownStep = m_Step;
            fill.style.width = Length.Percent(from * 100);
            TerminalMotion.Tween(fill, .7f, t => fill.style.width = Length.Percent(Mathf.Lerp(from, to, TerminalMotion.OutExpo(t)) * 100), .1f);
            for (int i = 0; i < buttons.Count; i++)
            {
                int step = i; buttons[i].clicked += () => { m_Step = step; Render(); };
                buttons[i].EnableInClassList("terminal-tab-selected", i == m_Step);
                buttons[i].EnableInClassList("terminal-step-done", i < m_Step);
            }
            var main = m_Content.Q<VisualElement>("operationMain");
            main.Q<Label>(className: "terminal-eyebrow").text = "EXPEDITION / " + steps[m_Step];
            TerminalLayout.Populate(main, layouts[m_Step]);
            m_Content.Q<Label>(className: "terminal-step-counter").text = (m_Step + 1).ToString("00") + " / " + steps.Length.ToString("00");
            var summary = m_Content.Q<VisualElement>(className: "terminal-side");
            summary.Q<Label>("statValue1").text = RaidRules.BagCapacity + " SUPPLIES";
            if (m_Step == 0)
            {
                m_Character = new MenuCharacterView(element: main.Q<Image>("operatorPortrait"));
                m_Character.Element.RemoveFromClassList("terminal-character-art");
            }
            else if (m_Step == 2)
            {
                AccountClient.SelectCarryCells(AccountClient.CarryCells);
                var carry = main.Q<IntegerField>("operationCarryCells");
                carry.SetValueWithoutNotify(AccountClient.CarryCells); carry.SetEnabled(!m_Busy);
                var cargo = main.Q<Label>("carrySummary");
                var gauge = main.Q<VisualElement>(className: "terminal-cargo-gauge-fill");
                void PresentCarry()
                {
                    cargo.text = $"Carry {AccountClient.CarryCells}/{RaidRules.BagCapacity} / Stash {m_Cells}";
                    gauge.style.width = Length.Percent(100f * AccountClient.CarryCells / Mathf.Max(1, RaidRules.BagCapacity));
                }
                carry.RegisterValueChangedCallback(evt =>
                {
                    AccountClient.SelectCarryCells(evt.newValue); carry.SetValueWithoutNotify(AccountClient.CarryCells); PresentCarry();
                });
                PresentCarry();
            }
            else if (m_Step == 4)
            {
                main.Q<Label>("statValue0").text = "DOLLSINGER";
                main.Q<Label>("statValue1").text = AccountClient.CarryCells + " ENERGY CELLS";
                var go = main.Q<Button>("launch"); go.clicked += m_Prepare; go.SetEnabled(!m_Busy);
            }
            m_Content.Q<Button>("back").clicked += () => { if (m_Step > 0) { m_Step--; Render(); } else Navigate(0); };
            var next = m_Content.Q<Button>("continue");
            next.style.display = m_Step < 4 ? DisplayStyle.Flex : DisplayStyle.None;
            next.SetEnabled(!m_Busy); next.clicked += () => { m_Step++; Render(); };
        }

        private void Tasks()
        {
            TerminalLayout.Populate(m_Content, "Tasks");
            TerminalMotion.Cascade(m_Content.Q<VisualElement>(className: "terminal-objectives"), .5f, .12f, 30, 0);
            m_Content.Q<Button>("planOperation").clicked += Prepare;
        }
        private void Base()
        {
            TerminalLayout.Populate(m_Content, "Base");
            var stores = m_Content.Q<VisualElement>(className: "terminal-resources");
            int[] values = { m_Dust, m_Alloy, m_Cells };
            for (int i = 0; i < values.Length; i++)
                TerminalMotion.CountUp(stores.Q<Label>("statValue" + i), values[i], "{0:N0}", .5f + i * .1f, 1.3f);
            m_Content.Q<Button>("openStorage").clicked += () => Navigate(1);
        }

        private void Comms() => TerminalLayout.Populate(m_Content, "Comms");
        private void Settings()
        {
            TerminalLayout.Populate(m_Content, "Settings");
            BindAudioSlider("audioMaster", "Master", MoonkovAudio.MasterVolume);
            BindAudioSlider("audioEffects", "Effects", MoonkovAudio.EffectsVolume);
            BindAudioSlider("audioInterface", "Interface", MoonkovAudio.InterfaceVolume);
            var hud = m_Content.Q<Toggle>("alwaysShowHUD");
            hud.SetValueWithoutNotify(PlayerPrefs.GetInt("Moonkov.AlwaysShowHUD", 0) != 0);
            hud.RegisterValueChangedCallback(e => PlayerPrefs.SetInt("Moonkov.AlwaysShowHUD", e.newValue ? 1 : 0));
        }
        private void BindAudioSlider(string name, string channel, float value)
        {
            var slider = m_Content.Q<Slider>(name); slider.SetValueWithoutNotify(value);
            slider.RegisterValueChangedCallback(e => MoonkovAudio.SetVolume(channel, e.newValue));
        }
        private void NavGeometryChanged(GeometryChangedEvent evt) => MoveIndicator(false);
        private void ContentGeometryChanged(GeometryChangedEvent evt) => UpdateHomeMenuSize();

        // Endless marquee; the copy is doubled so the loop has no seam.
        internal static VisualElement Ticker(VisualElement parent, string text)
        {
            var ticker = new VisualElement { pickingMode = PickingMode.Ignore }; ticker.AddToClassList("terminal-ticker"); parent.Add(ticker);
            var track = new VisualElement { pickingMode = PickingMode.Ignore }; track.AddToClassList("terminal-ticker-track"); ticker.Add(track);
            Text(track, text, "terminal-ticker-text"); Text(track, text, "terminal-ticker-text");
            AnimateTicker(ticker); return ticker;
        }
        private static void AnimateTicker(VisualElement ticker)
        {
            var track = ticker.Q<VisualElement>(className: "terminal-ticker-track");
            var first = track.Q<Label>(className: "terminal-ticker-text");
            double start = TerminalMotion.Now;
            ticker.schedule.Execute(() =>
            {
                if (!TerminalMotion.Animate(ticker)) return;
                float w = first.layout.width; if (float.IsNaN(w) || w < 1) return;
                track.style.translate = new Translate(-Mathf.Repeat((float)(TerminalMotion.Now - start) * 42, w), 0);
            }).Every(16);
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
            m_Windows.Dispose(); m_Content.Clear(); m_ClockTimer.Pause();
            m_IndicatorTimer?.Pause();
            m_NavTabs.UnregisterCallback<GeometryChangedEvent>(NavGeometryChanged);
            m_Content.UnregisterCallback<GeometryChangedEvent>(ContentGeometryChanged);
            for (int i = 0; i < m_Tabs.Count; i++) m_Tabs[i].clicked -= m_NavActions[i];
            m_Shade.Dispose(); m_Shade.RemoveFromHierarchy(); m_Chrome.RemoveFromHierarchy(); m_FooterTicker?.RemoveFromHierarchy();
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
