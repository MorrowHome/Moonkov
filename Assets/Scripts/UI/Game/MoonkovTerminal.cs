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
        private readonly string[] m_Pages = { "SHIP", "LOADOUT / STORAGE", "HALO BUILD", "SUPPLIERS", "OPERATIONS", "TASKS", "BASE", "COMMS", "SETTINGS" };
        private readonly TerminalWindows m_Windows;
        private MenuCharacterView m_Character;
        private int m_Page;
        private bool m_Busy, m_Ready;
        private bool m_HomeDark;
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
        public event Action HaloModificationRequested;

        public MoonkovTerminal(VisualElement root, Action prepare)
        {
            m_Root = root; m_Prepare = prepare; root.AddToClassList("moonkov-terminal");
            root.AddToClassList("archive-ui");
            var pageStyle = Resources.Load<StyleSheet>("Moonkov/UI/ApprovedPages");
            if (pageStyle != null && !root.styleSheets.Contains(pageStyle)) root.styleSheets.Add(pageStyle);
            var homeStyle = Resources.Load<StyleSheet>("Moonkov/UI/ApprovedHome");
            if (homeStyle != null && !root.styleSheets.Contains(homeStyle)) root.styleSheets.Add(homeStyle);
            var loadoutStyle = Resources.Load<StyleSheet>("Moonkov/UI/ApprovedLoadout");
            if (loadoutStyle != null && !root.styleSheets.Contains(loadoutStyle)) root.styleSheets.Add(loadoutStyle);
            root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ProposalSystem"));
            m_HomeDark = PlayerPrefs.GetInt("Moonkov.HomeDark", 0) != 0;
            ApplyHomeTheme();
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
                MoonkovLocalization.Set(m_Clock, DateTime.UtcNow.ToString("HH:mm:ss") + " UTC");
                dot.EnableInClassList("terminal-nav-dot-dim", DateTime.UtcNow.Second % 2 == 0);
            }).Every(500);
            m_Content = root.Q<VisualElement>("terminalContent");
            m_Content.RegisterCallback<GeometryChangedEvent>(ContentGeometryChanged);
            m_Root.RegisterCallback<GeometryChangedEvent>(ContentGeometryChanged);
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
            m_Page = page; m_Windows?.CloseAll();
            m_Root.EnableInClassList("terminal-is-ship", page == 0);
            m_Root.EnableInClassList("terminal-is-loadout", page == 1);
            ApplyPageTheme();
            for (int i = 0; i < m_Tabs.Count; i++) m_Tabs[i].EnableInClassList("terminal-tab-selected", i == page);
            m_Gear.style.display = page == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Content.style.display = page == 1 ? DisplayStyle.None : DisplayStyle.Flex;
            m_Shade.Mode = page == 0 ? BackdropMode.Ship : BackdropMode.Page;
            MoonkovLocalization.Set(m_Title, m_Pages[page]);
            if (page == 1) MoonkovLocalization.Set(m_Subtitle, "EQUIPMENT / PERSONAL STORAGE");
            else MoonkovLocalization.Set(m_Subtitle, "ORBITAL SHIP / {0}", m_Pages[page]);
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
            ApplyPageTheme();
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
            // The wordmark is a fixed Latin brand, while functional text keeps the locale font.
            var wordmark = m_Content.Q<Label>(className: "terminal-home-title");
            wordmark.style.unityFontDefinition = StyleKeyword.Null;
            wordmark.style.letterSpacing = StyleKeyword.Null;
            var theme = m_Content.Q<Button>("homeTheme");
            theme.text = m_HomeDark ? "D / COMMAND" : "C / ARCHIVE";
            theme.clicked += () =>
            {
                m_HomeDark = !m_HomeDark;
                PlayerPrefs.SetInt("Moonkov.HomeDark", m_HomeDark ? 1 : 0);
                ApplyHomeTheme();
                theme.text = m_HomeDark ? "D / COMMAND" : "C / ARCHIVE";
            };
            m_Content.Q<Button>("homeObserve").clicked += m_Shade.OpenObserver;
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
            MoonkovLocalization.Set(state, m_Busy ? "LINK BUSY  /  STAND BY" : "READY FOR DEPLOYMENT");
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

        private void ApplyHomeTheme()
        {
            m_Root.AddToClassList("moon-approved-home");
            m_Root.AddToClassList("moon-proposal-plates");
            m_Root.EnableInClassList("moon-home-light", !m_HomeDark);
            m_Root.EnableInClassList("moon-home-dark", m_HomeDark);
        }

        private void ApplyPageTheme()
        {
            m_Root.EnableInClassList("archive-pages", m_Page != 0);
            // The approved choices mix light and dark screens; "both" permits either.
            bool light = m_Page == 2 || m_Page == 4;
            for (int i = 0; i < m_Pages.Length; i++) m_Root.EnableInClassList("archive-page-" + i, m_Page == i);
            m_Root.EnableInClassList("archive-light", light);
            m_Root.EnableInClassList("archive-halo-page", m_Page == 2);
            m_Root.EnableInClassList("archive-compact", m_Root.contentRect.width < 1400 || m_Root.contentRect.height < 780);
        }


        private void Halo()
        {
            TerminalLayout.Populate(m_Content, "Halo");
            m_Content.Q<Button>("modifyHalo").clicked += () => HaloModificationRequested?.Invoke();
            var schematic = m_Content.Q<VisualElement>(className: "terminal-halo-schematic");
            var nodes = schematic.Query<Button>(className: "terminal-node").ToList();
            string[] codes = { "halo_capacitor", "halo_charge_bridge", "halo_focus_lens", "halo_fan_array", "rifle", "halo_pulse_controller", "halo_hit_marker" };
            for (int i = 0; i < nodes.Count; i++)
            {
                int index = i;
                var button = nodes[i];
                MoonkovLocalization.Set(button.Q<Label>(className: "halo-node-title"), global::Unity.MP_FPS.Inventory.InventoryCatalog.Get(codes[i]).Name);
                var art = new StashItemArt(ContainerInventoryView.Art(codes[i]));
                art.style.position = Position.Absolute; art.style.left = art.style.right = art.style.top = art.style.bottom = 0;
                button.Q(className: "halo-node-art").Add(art);
                button.clicked += () => DescribeNode(index);
                button.RegisterCallback<FocusInEvent>(_ => DescribeNode(index));
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
            DescribeNode(4);
        }
        private void DescribeNode(int index)
        {
            string[] codes = { "halo_capacitor", "halo_charge_bridge", "halo_focus_lens", "halo_fan_array", "rifle", "halo_pulse_controller", "halo_hit_marker" };
            string code = codes[index];
            MoonkovLocalization.Set(m_Content.Q<Label>("haloComponentTitle"), global::Unity.MP_FPS.Inventory.InventoryCatalog.Get(code).Name);
            MoonkovLocalization.Set(m_Content.Q<Label>("haloComponentDescription"), index == 4
                ? "Choose an owned halo to install, replace or remove compatible modules in storage."
                : global::Unity.MP_FPS.Inventory.HaloModules.Description(code));
            var image = m_Content.Q("haloComponentArt"); image.Clear();
            var art = new StashItemArt(ContainerInventoryView.Art(code)); art.style.flexGrow = 1; image.Add(art);
            var nodes = m_Content.Query<Button>(className: "terminal-node").ToList();
            for (int i = 0; i < nodes.Count; i++) nodes[i].EnableInClassList("terminal-node-selected", i == index);
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
            m_Content.Q<Button>("reviewEquipment").clicked += () => Navigate(1);
            m_Content.Q<Button>("reviewHalo").clicked += () => Navigate(2);
            var carry = m_Content.Q<IntegerField>("operationCarryCells");
            carry.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
            carry.SetEnabled(!m_Busy);
            var cargo = m_Content.Q<Label>("carrySummary");
            var gauge = m_Content.Q<VisualElement>(className: "terminal-cargo-gauge-fill");
            void PresentCarry()
            {
                MoonkovLocalization.Set(cargo, "Carry {0}/{1} / Stash {2}", PlayerProfileClient.CarryCells, RaidRules.BagCapacity, m_Cells);
                gauge.style.width = Length.Percent(100f * PlayerProfileClient.CarryCells / Mathf.Max(1, RaidRules.BagCapacity));
            }
            carry.RegisterValueChangedCallback(evt =>
            {
                PlayerProfileClient.SelectCarryCells(evt.newValue);
                carry.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
                PresentCarry();
            });
            PresentCarry();
            var launch = m_Content.Q<Button>("launch");
            launch.clicked += m_Prepare;
            launch.SetEnabled(!m_Busy);
            m_Content.Q<Button>("back").clicked += () => Navigate(0);
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
            var language = m_Content.Q<DropdownField>("language");
            language.choices = new List<string> { "简体中文", "English" };
            language.SetValueWithoutNotify(UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code == "zh-Hans" ? "简体中文" : "English");
            language.RegisterValueChangedCallback(e =>
            {
                MoonkovLocalization.Select(e.newValue == "简体中文" ? "zh-Hans" : "en");
                Navigate(8);
            });
        }
        private void BindAudioSlider(string name, string channel, float value)
        {
            var slider = m_Content.Q<Slider>(name); slider.SetValueWithoutNotify(value);
            var readout = m_Content.Q<Label>(name + "Value");
            var tracker = slider.Q(className: "unity-base-slider__tracker");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("archive-slider-fill"); tracker?.Add(fill);
            void Present(float v) { MoonkovLocalization.Set(readout, "{0:0}%", v * 100); fill.style.width = Length.Percent(v * 100); }
            Present(value);
            slider.RegisterValueChangedCallback(e => { MoonkovAudio.SetVolume(channel, e.newValue); Present(e.newValue); });
        }
        private void NavGeometryChanged(GeometryChangedEvent evt) => MoveIndicator(false);
        private void ContentGeometryChanged(GeometryChangedEvent evt) { UpdateHomeMenuSize(); ApplyPageTheme(); }

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
            var label = new Label(); label.AddToClassList(css); parent.Add(label); MoonkovLocalization.Set(label, text); return label;
        }


        internal static Button ActionButton(VisualElement parent, string text, Action action, string css = "terminal-button")
        {
            var button = new Button(action); button.AddToClassList(css); parent.Add(button); MoonkovLocalization.Set(button, text); return button;
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
            m_Root.UnregisterCallback<GeometryChangedEvent>(ContentGeometryChanged);
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
