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
            window.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/Research"));
            MoonkovLocalization.Set(window.Q<Label>("windowTitle"), "HALO MODIFICATION");
            var scroll=new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow=1; scroll.style.flexShrink=1; scroll.style.minHeight=0; window.Add(scroll);
            Text(scroll, InventoryCatalog.Get(weapon.Code).Name, "inventory-section-title");
            Text(scroll, "Base workshop · one module per slot · installed modules travel with this halo.", "inventory-caption");
            var registry=WeaponManager.Instance != null ? WeaponManager.Instance.WeaponRegistry : Resources.FindObjectsOfTypeAll<WeaponRegistry>().FirstOrDefault();
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
                    var row=ModuleRow(group,installed.Code,true);
                    bool space=m_Graph.FindSpace(installed,"stash",out _,out _,out _);
                    MoonkovLocalization.Set(row.Q<Label>("moduleComparison"), space ? "Removing requires a free storage cell. The module remains a physical item." : "STORAGE FULL / FREE SPACE BEFORE REMOVING");
                    var remove=row.Q<Button>("moduleAction"); MoonkovLocalization.Set(remove,"REMOVE TO STORAGE");
                    remove.clicked += () => { m_Windows.Close(window); if(!AutoMove(installed,"stash")) MoonkovLocalization.Set(m_Message,"No free space in storage."); };
                    remove.SetEnabled(space && m_ReadOnlyReason == null);
                }
                else Text(group,"EMPTY", "inventory-caption");
                bool found=false;
                foreach(var candidate in m_Graph.Items.Where(i=>HaloModules.Compatible(weapon.Code,i.Code) &&
                    HaloModules.Slot(HaloModules.FromCode(i.Code))==slot.Id && !HaloModules.IsWeapon(m_Graph.Find(i.Parent)?.Code)))
                {
                    found=true; var module=candidate;
                    var row=ModuleRow(group,module.Code,false);
                    var preview=m_Graph.Clone();
                    var error=preview.TryApply(new InventoryCommand { ExpectedVersion=preview.Version,ItemId=module.Id,Parent=weapon.Id,Region=slot.Id });
                    var after=registry?.GetWeaponData(DollSingerWeapons.WeaponId(weapon.Code),(ushort)HaloModules.Mask(preview,preview.Find(weapon.Id)));
                    string comparison=data!=null && after!=null ? MoonkovLocalization.Format($"CURRENT: {data.EnergyPerRound}E / {data.ReloadTime:0.00}s / {data.MagazineSize} ROUNDS / {data.Damage:0.##} DAMAGE\nAFTER: {after.EnergyPerRound}E / {after.ReloadTime:0.00}s / {after.MagazineSize} ROUNDS / {after.Damage:0.##} DAMAGE") : "";
                    comparison += "\n" + MoonkovLocalization.Text(error!=InventoryError.None ? "Cannot install: replacement space or carried weight is insufficient." : installed!=null ? "SLOT CONFLICT / replaces the installed module; the old module returns to the incoming module's location." : "AVAILABLE SLOT / install this physical module before deployment.");
                    MoonkovLocalization.Set(row.Q<Label>("moduleComparison"),comparison);
                    var button=row.Q<Button>("moduleAction"); MoonkovLocalization.Set(button,installed == null ? "INSTALL MODULE" : "REPLACE MODULE");
                    button.clicked += () => { m_Windows.Close(window); Command(new InventoryCommand { ItemId=module.Id,Parent=weapon.Id,Region=slot.Id }); };
                    button.SetEnabled(m_ReadOnlyReason == null && error==InventoryError.None);
                }
                if(!found) Text(group,slot.Id=="Storage" ? "Mooncore recycler: research and fabricate at Suppliers → Halo research. Other compatible modules can be bought or recovered from caches." : "Buy compatible modules at the supply shop or recover them from caches.","inventory-caption");
            }
        }
        private static VisualElement ModuleRow(VisualElement parent,string code,bool installed)
        {
            var row=TerminalLayout.Clone("Components/ModuleOption","moduleOption"); parent.Add(row);
            row.Q("moduleArt").Add(new StashItemArt(code));
            MoonkovLocalization.Set(row.Q<Label>("moduleName"),InventoryCatalog.Get(code).Name);
            MoonkovLocalization.Set(row.Q<Label>("moduleDescription"),HaloModules.Description(code));
            MoonkovLocalization.Set(row.Q<Label>("moduleState"),installed ? "INSTALLED" : "OWNED / NOT INSTALLED");
            return row;
        }
    }
}
