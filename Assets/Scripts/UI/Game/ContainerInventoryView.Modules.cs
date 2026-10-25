using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class ContainerInventoryView
    {
        // This is a presentation catalog, separate from HaloModules.Codes (the cache-loot pool).
        private static readonly string[] WorkshopCodes = { "halo_capacitor", "halo_charge_bridge", "halo_mooncore_recycler",
            "halo_focus_lens", "halo_fan_array", "halo_pulse_controller", "halo_hit_marker" };
        private VisualElement m_Workshop;
        private HaloWorkshopPreview m_WorkshopVisual;
        private bool m_WorkshopShowCurrent;
        private WeaponData m_WorkshopVisualData;
        private string m_WorkshopWeaponId, m_WorkshopModule, m_WorkshopMessage;
        private string m_WorkshopSlot = "Storage", m_WorkshopPreviewCode = "rifle";
        private List<string> m_WorkshopWeaponIds;
        public WeaponRegistry WorkshopWeaponRegistry { get; set; }
        public string WorkshopSupplyCode { get; private set; }
        public event Action<string> WorkshopRequested;
        public event Action<bool> WorkshopSupplyRequested;

        public void SelectWorkshopWeapon(string itemId)
        {
            m_WorkshopWeaponId = itemId;
            m_WorkshopModule = null;
            m_WorkshopMessage = null;
            m_WorkshopShowCurrent = false;
            RefreshWorkshop();
        }

        public void AttachWorkshop(VisualElement host)
        {
            if (!m_Stash || m_Disposed) return;
            ReleaseWorkshop();
            host.Clear();
            m_Workshop = TerminalLayout.Clone("Components/HaloWorkshop", "haloWorkshop");
            m_Workshop.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/HaloWorkshop"));
            host.Add(m_Workshop);
            m_WorkshopVisual = new HaloWorkshopPreview();
            m_WorkshopVisual.AvailabilityChanged += RefreshWorkshopPreviewControls;
            m_WorkshopVisual.Element.name = "workshopHaloPreview";
            m_WorkshopVisual.Element.AddToClassList("workshop-render-preview");
            m_Workshop.Q("workshopWeaponArt").Add(m_WorkshopVisual.Element);
            m_Workshop.Q<Button>("workshopPreviewCurrent").clicked += () => { m_WorkshopShowCurrent = true; RefreshWorkshop(); };
            m_Workshop.Q<Button>("workshopPreviewAfter").clicked += () => { m_WorkshopShowCurrent = false; RefreshWorkshop(); };
            m_Workshop.Q<Button>("workshopPreviewCharge").clicked += () => m_WorkshopVisual?.PlayCharge(m_WorkshopVisualData?.ReloadTime ?? 2f);
            m_Workshop.Q<Button>("workshopPreviewShot").clicked += () => m_WorkshopVisual?.PlayShot(m_WorkshopVisualData?.CooldownInMs ?? .1f);
            m_Workshop.RegisterCallback<GeometryChangedEvent>(evt =>
                m_Workshop?.EnableInClassList("workshop-compact", evt.newRect.height < 500 || evt.newRect.width < 1100));
            LocalizationSettings.SelectedLocaleChanged += WorkshopLocaleChanged;
            m_Workshop.Q<DropdownField>("workshopWeapon").RegisterValueChangedCallback(evt =>
            {
                var field = m_Workshop.Q<DropdownField>("workshopWeapon");
                int index = field.choices.IndexOf(evt.newValue);
                if (index < 0 || index >= m_WorkshopWeaponIds.Count) return;
                string id = m_WorkshopWeaponIds[index];
                if (id.StartsWith("catalog:", StringComparison.Ordinal))
                { m_WorkshopWeaponId = null; m_WorkshopPreviewCode = id.Substring(8); }
                else m_WorkshopWeaponId = id;
                m_WorkshopModule = m_WorkshopMessage = null; m_WorkshopShowCurrent = false;
                RefreshWorkshop();
            });
            m_Workshop.Q<Button>("workshopApply").clicked += ApplyWorkshopModule;
            m_Workshop.Q<Button>("workshopRemove").clicked += RemoveWorkshopModule;
            m_Workshop.Q<Button>("workshopAcquire").clicked += () => RequestWorkshopSupply(m_WorkshopModule == "halo_mooncore_recycler");
            m_Workshop.Q<Button>("workshopEmptySupply").clicked += () => RequestWorkshopSupply(false, true);
            RefreshWorkshop();
        }

        private void ReleaseWorkshop()
        {
            LocalizationSettings.SelectedLocaleChanged -= WorkshopLocaleChanged;
            if (m_WorkshopVisual != null)
            {
                m_WorkshopVisual.AvailabilityChanged -= RefreshWorkshopPreviewControls;
                m_WorkshopVisual.Dispose();
            }
            m_WorkshopVisual = null; m_WorkshopVisualData = null;
            m_Workshop?.RemoveFromHierarchy();
            m_Workshop = null;
        }
        private void WorkshopLocaleChanged(Locale locale) => RefreshWorkshop();

        public void OpenWorkshop()
        {
            if (!m_Stash || m_Busy) return;
            if (WorkshopRequested != null) { WorkshopRequested(null); return; }
            OpenWorkshopWindow();
        }

        private void OpenModification(InventoryItem weapon)
        {
            if (!m_Stash || m_Busy || weapon == null || !HaloModules.IsWeapon(weapon.Code)) return;
            SelectWorkshopWeapon(weapon.Id);
            if (WorkshopRequested != null) { WorkshopRequested(weapon.Id); return; }
            OpenWorkshopWindow();
        }

        private void OpenWorkshopWindow()
        {
            var window = m_Windows.Open("HALO MODIFICATION");
            window.AddToClassList("halo-workshop-window");
            window.style.width = Length.Percent(94); window.style.height = Length.Percent(88);
            window.style.left = Length.Percent(3); window.style.top = Length.Percent(4);
            var host = new VisualElement(); host.AddToClassList("halo-workshop-host"); window.Add(host);
            AttachWorkshop(host);
        }

        private InventoryItem WorkshopWeapon => m_Graph?.Find(m_WorkshopWeaponId);
        private InventoryItem WorkshopInstalled => WorkshopWeapon == null ? null : m_Graph.Children(WorkshopWeapon.Id, m_WorkshopSlot).FirstOrDefault();
        private IEnumerable<InventoryItem> WorkshopLoose(string code) => m_Graph == null ? Enumerable.Empty<InventoryItem>() :
            m_Graph.Items.Where(item => item.Code == code && !HaloModules.IsWeapon(m_Graph.Find(item.Parent)?.Code));

        private static string WorkshopSlotName(string slot) => slot == "Storage" ? "POWER SLOT" : slot == "Shaping" ? "FIRING SLOT" : "SUPPORT SLOT";
        private static string WorkshopPurpose(string code)
        {
            switch (code)
            {
                case "halo_capacitor": return "Fire longer before recharging";
                case "halo_charge_bridge": return "Get back to firing sooner";
                case "halo_mooncore_recycler": return "Make your battery last longer";
                case "halo_focus_lens": return "Keep shotgun pellets together";
                case "halo_fan_array": return "Cover a wider area with pellets";
                case "halo_pulse_controller": return "Fire controlled three-round bursts";
                default: return "Show your team where a hit landed";
            }
        }
        private static string WorkshopBenefit(string code)
        {
            switch (code)
            {
                case "halo_capacitor": return "Capacity +50%";
                case "halo_charge_bridge": return "Charge time -40%";
                case "halo_mooncore_recycler": return "Energy per round reduced by one third";
                case "halo_focus_lens": return "Pellet spread -55%";
                case "halo_fan_array": return "Wider spread with 12 pellets; total damage unchanged";
                case "halo_pulse_controller": return "Three rounds per trigger press";
                default: return "Last hit position marked for 3 seconds";
            }
        }
        private static string WorkshopTradeoff(string code)
        {
            switch (code)
            {
                case "halo_capacitor": return "Charge time +35%";
                case "halo_charge_bridge": return "Energy per round +50%";
                case "halo_mooncore_recycler": return "Charge time +20%";
                case "halo_focus_lens": return "Capacity -1 round";
                case "halo_fan_array": return "Less damage per pellet; spread +60%";
                case "halo_pulse_controller": return "Press again for the next burst; at least 0.42s between bursts";
                default: return "Energy per round +25%";
            }
        }

        private void RefreshWorkshop()
        {
            if (m_Workshop == null || m_Disposed) return;
            var weapons = m_Graph?.Items.Where(item => HaloModules.IsWeapon(item.Code)).ToArray() ?? Array.Empty<InventoryItem>();
            if (!weapons.Any(item => item.Id == m_WorkshopWeaponId))
                m_WorkshopWeaponId = weapons.FirstOrDefault(item => item.Parent == "equipment")?.Id ?? weapons.FirstOrDefault()?.Id;
            var weapon = WorkshopWeapon;
            string weaponCode = weapon?.Code ?? m_WorkshopPreviewCode;
            var field = m_Workshop.Q<DropdownField>("workshopWeapon");
            m_WorkshopWeaponIds = weapons.Select(item => item.Id).ToList();
            var names = weapons.Select(item => MoonkovLocalization.Format($"{InventoryCatalog.Get(item.Code).Name} / {WorkshopLocation(item)}")).ToArray();
            var choices = names.Select((name, index) => names.Count(other => other == name) > 1 ? name + " " + (index + 1) : name).ToList();
            if (weapons.Length == 0)
            {
                m_WorkshopWeaponIds.AddRange(new[] { "catalog:rifle", "catalog:shotgun" });
                choices.Add(MoonkovLocalization.Format($"{InventoryCatalog.Get("rifle").Name} / {"NOT OWNED"}"));
                choices.Add(MoonkovLocalization.Format($"{InventoryCatalog.Get("shotgun").Name} / {"NOT OWNED"}"));
            }
            field.choices = choices;
            field.SetValueWithoutNotify(choices[Math.Max(0, m_WorkshopWeaponIds.IndexOf(weapon?.Id ?? "catalog:" + weaponCode))]);
            field.SetEnabled(!m_Busy);
            var empty = m_Workshop.Q("workshopEmpty"); empty.style.display = weapon == null ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopEmptyMessage"), m_Graph == null ? "Loading your inventory…" : "No compatible halo owned. Browse rifle and shotgun modules here.");
            var weaponState = m_Workshop.Q<Label>("workshopWeaponState");
            MoonkovLocalization.Set(weaponState, weapon == null ? "CATALOG PREVIEW / NOT OWNED" : "");
            weaponState.style.display = weapon == null ? DisplayStyle.Flex : DisplayStyle.None;
            RenderWorkshopSlots(weapon);
            string[] codes = WorkshopCodes.Where(code => HaloModules.Compatible(weaponCode, code) && HaloModules.Slot(HaloModules.FromCode(code)) == m_WorkshopSlot).ToArray();
            if (!codes.Contains(m_WorkshopModule)) m_WorkshopModule = WorkshopInstalled?.Code ?? codes[0];
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopSlotTitle"), WorkshopSlotName(m_WorkshopSlot));
            var modules = m_Workshop.Q<ScrollView>("workshopModules"); modules.Clear();
            foreach (string code in codes)
            {
                string captured = code;
                var button = new Button(() => { m_WorkshopModule = captured; m_WorkshopMessage = null; m_WorkshopShowCurrent = false; RefreshWorkshop(); }) { name = "workshopModule" + code };
                button.AddToClassList("workshop-module");
                button.EnableInClassList("workshop-selected", code == m_WorkshopModule);
                button.EnableInClassList("workshop-installed", WorkshopInstalled?.Code == code);
                var icon = new VisualElement { pickingMode = PickingMode.Ignore }; icon.AddToClassList("workshop-module-art"); icon.Add(new StashItemArt(code)); button.Add(icon);
                var copy = new VisualElement { pickingMode = PickingMode.Ignore }; copy.AddToClassList("workshop-module-copy"); button.Add(copy);
                Text(copy, InventoryCatalog.Get(code).Name, "workshop-module-name");
                Text(copy, WorkshopPurpose(code), "workshop-module-purpose");
                Text(copy, WorkshopStock(code), "workshop-module-stock");
                modules.Add(button);
            }
            RenderWorkshopDetail(weapon, weaponCode);
        }

        private string WorkshopLocation(InventoryItem item) => item.Parent == "equipment" ? SlotLabel(item.Region) : m_Graph.RootOf(item.Id) == "stash" ? "PERSONAL STORAGE" : "CARRIED";
        private string WorkshopStock(string code)
        {
            if (WorkshopInstalled?.Code == code) return "INSTALLED";
            int count = WorkshopLoose(code).Count();
            return count > 0 ? MoonkovLocalization.Format($"OWNED {count}") : m_Graph != null && m_Graph.Items.Any(item => item.Code == code) ? "INSTALLED ON ANOTHER HALO" : "NOT OWNED";
        }
        private void RenderWorkshopSlots(InventoryItem weapon)
        {
            var slots = m_Workshop.Q("workshopSlots"); slots.Clear();
            foreach (var slot in HaloModules.Slots)
            {
                string id = slot.Id;
                var installed = weapon == null ? null : m_Graph.Children(weapon.Id, id).FirstOrDefault();
                var button = new Button(() => { m_WorkshopSlot = id; m_WorkshopModule = m_WorkshopMessage = null; m_WorkshopShowCurrent = false; RefreshWorkshop(); }) { name = "workshopSlot" + id };
                button.AddToClassList("workshop-slot"); button.EnableInClassList("workshop-selected", id == m_WorkshopSlot);
                var icon = new VisualElement { pickingMode = PickingMode.Ignore }; icon.AddToClassList("workshop-slot-art");
                if (installed != null) icon.Add(new StashItemArt(installed.Code));
                button.Add(icon); Text(button, WorkshopSlotName(id), "workshop-slot-label");
                Text(button, installed == null ? "EMPTY" : InventoryCatalog.Get(installed.Code).Name, "workshop-slot-value"); slots.Add(button);
            }
        }

        private void RenderWorkshopDetail(InventoryItem weapon, string weaponCode)
        {
            string code = m_WorkshopModule;
            var art = m_Workshop.Q("workshopModuleArt"); art.Clear(); art.Add(new StashItemArt(code));
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopModuleName"), InventoryCatalog.Get(code).Name);
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopPurpose"), WorkshopPurpose(code));
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopBenefit"), WorkshopBenefit(code));
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopTradeoff"), WorkshopTradeoff(code));
            MoonkovLocalization.Set(m_Workshop.Q<Label>("workshopOwnership"), WorkshopStock(code));
            var registry = WorkshopWeaponRegistry;
            if (registry == null)
                WorkshopWeaponRegistry = registry = WeaponManager.Instance != null ? WeaponManager.Instance.WeaponRegistry : Resources.FindObjectsOfTypeAll<WeaponRegistry>().FirstOrDefault();
            var mask = m_Graph == null ? HaloModule.None : HaloModules.Mask(m_Graph,
                weapon ?? new InventoryItem { Id = "workshop-preview", Code = weaponCode });
            var afterMask = mask;
            foreach (string other in WorkshopCodes.Where(other => HaloModules.Slot(HaloModules.FromCode(other)) == m_WorkshopSlot)) afterMask &= ~HaloModules.FromCode(other);
            afterMask |= HaloModules.FromCode(code);
            var before = registry?.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode), (ushort)mask);
            var after = registry?.GetWeaponData(DollSingerWeapons.WeaponId(weaponCode), (ushort)afterMask);
            m_WorkshopVisualData = m_WorkshopShowCurrent ? before : after;
            int rounds = weapon?.LoadedAmmo ?? -1;
            if (rounds < 0) rounds = before?.MagazineSize ?? 4;
            if (m_WorkshopVisualData != null)
                m_WorkshopVisual?.Present(weaponCode, (ushort)(m_WorkshopShowCurrent ? mask : afterMask), m_WorkshopVisualData.MagazineSize, rounds);
            RefreshWorkshopPreviewControls();
            m_Workshop.Q<Button>("workshopPreviewCurrent").EnableInClassList("workshop-selected", m_WorkshopShowCurrent);
            m_Workshop.Q<Button>("workshopPreviewAfter").EnableInClassList("workshop-selected", !m_WorkshopShowCurrent);
            var stats = m_Workshop.Q("workshopStats"); stats.Clear();
            if (before != null && after != null)
            {
                WorkshopStat(stats, "Energy", "ENERGY PER ROUND", before.EnergyPerRound, after.EnergyPerRound, "{0} E", -1);
                WorkshopStat(stats, "Charge", "CHARGE TIME", before.ReloadTime, after.ReloadTime, "{0:0.##} s", -1);
                WorkshopStat(stats, "Capacity", "CAPACITY", before.MagazineSize, after.MagazineSize, "{0}", 1);
                WorkshopStat(stats, "Damage", "DAMAGE PER ROUND (ALL PELLETS)", before.Damage * before.PelletCount, after.Damage * after.PelletCount, "{0:0.##}", 1);
                if (weaponCode == "shotgun")
                {
                    WorkshopStat(stats, "Pellets", "PELLETS", before.PelletCount, after.PelletCount, "{0}", 0);
                    WorkshopStat(stats, "Spread", "PELLET SPREAD", before.SpreadDegrees, after.SpreadDegrees, "{0:0.##}°", 0);
                }
                else
                {
                    var row = new VisualElement { name = "workshopStatFiring" }; row.AddToClassList("workshop-stat-row"); stats.Add(row);
                    Text(row, "FIRING MODE", "workshop-stat-label");
                    Text(row, (mask & HaloModule.PulseController) != 0 ? "3-ROUND BURST" : "AUTOMATIC", "workshop-stat-before");
                    Text(row, (afterMask & HaloModule.PulseController) != 0 ? "3-ROUND BURST" : "AUTOMATIC", "workshop-stat-after");
                }
            }
            else Text(stats, "Weapon data is unavailable.", "workshop-caption");
            bool installed = WorkshopInstalled?.Code == code;
            var candidate = WorkshopCandidate(out var error);
            var apply = m_Workshop.Q<Button>("workshopApply");
            MoonkovLocalization.Set(apply, m_Busy ? "SAVING…" : installed ? "INSTALLED" : WorkshopInstalled != null ? "REPLACE MODULE" : "INSTALL MODULE");
            apply.SetEnabled(!m_Busy && m_ReadOnlyReason == null && weapon != null && candidate != null && error == InventoryError.None && !installed);
            var remove = m_Workshop.Q<Button>("workshopRemove");
            remove.style.display = installed ? DisplayStyle.Flex : DisplayStyle.None;
            bool space = WorkshopInstalled != null && m_Graph.FindSpace(WorkshopInstalled, "stash", out _, out _, out _);
            remove.SetEnabled(!m_Busy && m_ReadOnlyReason == null && space);
            var acquire = m_Workshop.Q<Button>("workshopAcquire");
            acquire.style.display = candidate == null && !installed ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(acquire, code == "halo_mooncore_recycler" ? "RESEARCH / FABRICATE" : "GET MODULE");
            acquire.SetEnabled(!m_Busy && WorkshopSupplyRequested != null);
            m_Workshop.Q<Button>("workshopEmptySupply").SetEnabled(m_Graph != null && !m_Busy && WorkshopSupplyRequested != null);
            string status = m_WorkshopMessage;
            if (m_Busy) status = "Saving inventory…";
            else if (m_ReadOnlyReason != null) status = m_ReadOnlyReason;
            else if (weapon == null) status = m_Graph == null ? "Loading your inventory…" : "Acquire a compatible halo to install modules.";
            else if (candidate != null && error != InventoryError.None && !installed) status = "Cannot install: replacement space or carried weight is insufficient.";
            else if (WorkshopInstalled != null && !space && installed) status = "Free a storage cell before removing this module.";
            else if (status == null && !installed && candidate != null && WorkshopInstalled != null) status = "The replaced module returns to the incoming module's cell.";
            else if (status == null && candidate == null && !installed) status = WorkshopStock(code) == "INSTALLED ON ANOTHER HALO" ? "Remove this module from its other halo to use it here." : code == "halo_mooncore_recycler" ? "Research and fabricate this module at Suppliers." : "Buy this module at Suppliers or recover it from a supply cache.";
            var statusLabel = m_Workshop.Q<Label>("workshopStatus");
            statusLabel.EnableInClassList("workshop-status-saving", m_Busy);
            statusLabel.EnableInClassList("workshop-status-error", !m_Busy && (m_ReadOnlyReason != null || error != InventoryError.None || installed && !space || m_WorkshopMessage != null && m_WorkshopMessage != "Inventory updated."));
            MoonkovLocalization.Set(statusLabel, status ?? "");
        }

        private void RefreshWorkshopPreviewControls()
        {
            if (m_Workshop == null) return;
            bool available = m_WorkshopVisualData != null && m_WorkshopVisual?.Available == true;
            bool ready = available && m_WorkshopVisual.Target != null;
            m_Workshop.Q<Button>("workshopPreviewCurrent").SetEnabled(available);
            m_Workshop.Q<Button>("workshopPreviewAfter").SetEnabled(available);
            m_Workshop.Q<Button>("workshopPreviewCharge").SetEnabled(ready);
            m_Workshop.Q<Button>("workshopPreviewShot").SetEnabled(ready && m_WorkshopVisual.CanPlayShot);
            var state = m_Workshop.Q<Label>("workshopPreviewState");
            state.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
            MoonkovLocalization.Set(state, available ? "" : "Appearance preview unavailable.");
        }

        private static void WorkshopStat(VisualElement parent, string id, string label, float before, float after, string format, int preferredDirection)
        {
            var row = new VisualElement { name = "workshopStat" + id }; row.AddToClassList("workshop-stat-row"); parent.Add(row);
            Text(row, label, "workshop-stat-label");
            Text(row, string.Format(MoonkovLocalization.Text(format), before), "workshop-stat-before").name = "before";
            var value = Text(row, string.Format(MoonkovLocalization.Text(format), after), "workshop-stat-after"); value.name = "after";
            if (!Mathf.Approximately(before, after) && preferredDirection != 0)
                value.AddToClassList((after - before) * preferredDirection > 0 ? "workshop-stat-better" : "workshop-stat-worse");
        }

        private InventoryItem WorkshopCandidate(out InventoryError error)
        {
            error = InventoryError.None;
            var weapon = WorkshopWeapon;
            var candidates = WorkshopLoose(m_WorkshopModule).ToArray();
            if (weapon == null) return candidates.FirstOrDefault();
            foreach (var item in candidates)
            {
                error = m_Graph.Clone().TryApply(new InventoryCommand { ExpectedVersion = m_Graph.Version, ItemId = item.Id, Parent = weapon.Id, Region = m_WorkshopSlot });
                if (error == InventoryError.None) return item;
            }
            return candidates.FirstOrDefault();
        }
        private void ApplyWorkshopModule()
        {
            if (m_Busy || m_ReadOnlyReason != null || WorkshopWeapon == null || WorkshopInstalled?.Code == m_WorkshopModule) return;
            var candidate = WorkshopCandidate(out var error);
            if (candidate == null || error != InventoryError.None) return;
            m_WorkshopMessage = null;
            Command(new InventoryCommand { ItemId = candidate.Id, Parent = WorkshopWeapon.Id, Region = m_WorkshopSlot });
        }
        private void RemoveWorkshopModule()
        {
            if (m_Busy || m_ReadOnlyReason != null || WorkshopInstalled == null) return;
            var item = WorkshopInstalled;
            if (!m_Graph.FindSpace(item, "stash", out var region, out var x, out var y)) return;
            m_WorkshopMessage = null;
            Command(new InventoryCommand { ItemId = item.Id, Parent = "stash", Region = region, X = x, Y = y, Rotated = item.Rotated });
        }
        private void RequestWorkshopSupply(bool research, bool getWeapon = false)
        {
            if (m_Busy) return;
            WorkshopSupplyCode = getWeapon ? m_WorkshopPreviewCode : m_WorkshopModule;
            WorkshopSupplyRequested?.Invoke(research);
        }
    }
}
