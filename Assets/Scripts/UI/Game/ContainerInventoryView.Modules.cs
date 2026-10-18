using System.Linq;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class ContainerInventoryView
    {
        public void OpenWorkshop()
        {
            if (!m_Stash || m_Busy || m_Graph == null) return;
            var window = m_Windows.Open("HALO MODIFICATION", null, true);
            var weapons = m_Graph.Items.Where(item => HaloModules.IsWeapon(item.Code)).ToArray();
            foreach (var weapon in weapons)
            {
                var captured = weapon;
                MoonkovTerminal.ActionButton(window, InventoryCatalog.Get(weapon.Code).Name, () =>
                { m_Windows.Close(window); OpenModification(captured); });
            }
            if (weapons.Length == 0) Text(window, "No halo weapon is available. Open supply to acquire field equipment.", "inventory-caption");
        }

        private void OpenModification(InventoryItem weapon)
        {
            if (!m_Stash || m_Busy || m_Graph == null || !HaloModules.IsWeapon(weapon.Code)) return;
            var window=m_Windows.Open("HALO MODIFICATION", null, true);
            window.AddToClassList("archive-workshop");
            MoonkovLocalization.Set(window.Q<Label>("windowTitle"), "HALO MODIFICATION");
            var scroll=new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow=1; scroll.style.flexShrink=1; scroll.style.minHeight=0; window.Add(scroll);
            Text(scroll, InventoryCatalog.Get(weapon.Code).Name, "inventory-section-title");
            Text(scroll, "Base workshop · one module per slot · installed modules travel with this halo.", "inventory-caption");
            var registry=WeaponManager.Instance != null ? WeaponManager.Instance.WeaponRegistry : null;
            var data=registry?.GetWeaponData(DollSingerWeapons.WeaponId(weapon.Code), (ushort)HaloModules.Mask(m_Graph,weapon));
            if(data != null)
                Text(scroll, MoonkovLocalization.Format($"CAPACITY {data.MagazineSize} / CHARGE {data.ReloadTime:0.00}s / {data.EnergyPerRound}E PER ROUND / {data.PelletCount} PELLETS / SPREAD {data.SpreadDegrees:0.00}°"), "inventory-caption");
            Text(scroll, "Existing charged rounds are retained when reducing capacity; upgrades do not add ammunition.", "inventory-caption");
            foreach(var slot in HaloModules.Slots)
            {
                var slotPanel = TerminalLayout.Clone("Components/ModuleSlot", "moduleGroup"); scroll.Add(slotPanel);
                MoonkovLocalization.Set(slotPanel.Q<Label>("moduleSlotTitle"), slot.Id == "Storage" ? "STORAGE SLOT" : slot.Id == "Shaping" ? "SHAPING SLOT" : "UTILITY SLOT");
                var group = slotPanel.Q("moduleSlotContents");
                var installed=m_Graph.Children(weapon.Id,slot.Id).FirstOrDefault();
                if(installed != null)
                {
                    ModuleRow(group,installed.Code,true);
                    MoonkovTerminal.ActionButton(group,"REMOVE TO STORAGE",() =>
                    { m_Windows.Close(window); if(!AutoMove(installed,"stash")) MoonkovLocalization.Set(m_Message,"No free space in storage."); });
                }
                else Text(group,"EMPTY", "inventory-caption");
                bool found=false;
                foreach(var candidate in m_Graph.Items.Where(i=>HaloModules.Compatible(weapon.Code,i.Code) &&
                    HaloModules.Slot(HaloModules.FromCode(i.Code))==slot.Id && !HaloModules.IsWeapon(m_Graph.Find(i.Parent)?.Code)))
                {
                    found=true; var module=candidate;
                    ModuleRow(group,module.Code,false);
                    var button=MoonkovTerminal.ActionButton(group,installed == null ? "INSTALL MODULE" : "REPLACE MODULE",() =>
                    { m_Windows.Close(window); Command(new InventoryCommand { ItemId=module.Id,Parent=weapon.Id,Region=slot.Id }); });
                    button.SetEnabled(m_ReadOnlyReason == null);
                }
                if(!found) Text(group,"Buy compatible modules at the supply shop or recover them from caches.","inventory-caption");
            }
        }
        private static void ModuleRow(VisualElement parent,string code,bool installed)
        {
            var row=new VisualElement(); row.style.flexDirection=FlexDirection.Row; row.style.marginTop=8; parent.Add(row);
            var icon=new StashItemArt(Art(code)); icon.style.width=54; icon.style.height=54; icon.style.flexShrink=0; row.Add(icon);
            var copy=new VisualElement(); copy.style.flexGrow=1; row.Add(copy);
            Text(copy,InventoryCatalog.Get(code).Name,"inventory-region-title");
            var description=Text(copy,HaloModules.Description(code),"inventory-caption"); description.style.whiteSpace=WhiteSpace.Normal;
            if(installed) Text(copy,"INSTALLED","inventory-caption");
        }
    }
}
