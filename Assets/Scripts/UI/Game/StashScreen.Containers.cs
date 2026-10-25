using System;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashScreen
    {
        private ContainerInventoryView m_Containers;
        private InventoryGraph m_PreviewInventory;
        private VisualElement m_RecoveryPanel;
        private Label m_RecoveryNote;
        private Button m_ClaimAll;
        private ListView m_RecoveryList;
        private List<InventoryItem> m_RecoveryRoots=new List<InventoryItem>();
        private bool m_RecoveryBusy;
        private readonly CancellationTokenSource m_ContainerStop = new CancellationTokenSource();
        private void InitializeContainers()
        {
            var body=m_Root.Q<VisualElement>(className:"stash-body");
            foreach (var child in body.Children()) child.style.display=DisplayStyle.None;
            m_Containers=new ContainerInventoryView(body, true, SendContainerCommand);
            m_RecoveryPanel=new VisualElement {name="stashRecovery"};m_RecoveryPanel.AddToClassList("inventory-container");
            var header=new VisualElement();header.style.flexDirection=FlexDirection.Row;header.style.flexShrink=0;m_RecoveryPanel.Add(header);
            m_RecoveryNote=new Label();m_RecoveryNote.style.flexGrow=1;m_RecoveryNote.style.minWidth=0;m_RecoveryNote.style.whiteSpace=WhiteSpace.Normal;
            m_RecoveryNote.AddToClassList("inventory-caption");header.Add(m_RecoveryNote);
            m_ClaimAll=new Button(()=>ClaimRecovery(null)) {text="CLAIM ALL THAT FITS"};m_ClaimAll.AddToClassList("terminal-button");m_ClaimAll.style.flexShrink=0;header.Add(m_ClaimAll);
            m_RecoveryPanel.RegisterCallback<GeometryChangedEvent>(evt=>header.style.flexDirection=evt.newRect.width<720 ? FlexDirection.Column : FlexDirection.Row);
            m_RecoveryList=new ListView {fixedItemHeight=36,selectionType=SelectionType.None};m_RecoveryList.style.height=144;
            m_RecoveryList.makeItem=()=>
            {
                var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;
                var label=new Label {name="recoveryItem"};label.style.flexGrow=1;label.style.minWidth=0;label.style.overflow=Overflow.Hidden;label.style.textOverflow=TextOverflow.Ellipsis;label.style.unityTextAlign=TextAnchor.MiddleLeft;row.Add(label);
                var claim=new Button(()=>ClaimRecovery(row.userData as string)) {text="CLAIM TO STORAGE"};claim.AddToClassList("terminal-button");claim.style.flexShrink=0;claim.style.height=28;claim.style.marginTop=2;claim.style.marginBottom=2;row.Add(claim);MoonkovLocalization.Bind(claim);return row;
            };
            m_RecoveryList.bindItem=(row,index)=>
            {
                var item=m_RecoveryRoots[index];row.userData=item.Id;
                row.tooltip=MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name);
                MoonkovLocalization.Set(row.Q<Label>("recoveryItem"),"{0} ×{1}{2}",MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name),item.Quantity,InventoryCatalog.Get(item.Code).Container ? MoonkovLocalization.Text(" (includes contents)") : "");
            };
            m_RecoveryPanel.Add(m_RecoveryList);m_Containers.Element.Insert(0,m_RecoveryPanel);MoonkovLocalization.Bind(m_RecoveryPanel);
            m_Terminal.LoadoutShowing += ShowContainers; m_Terminal.Navigating += m_Containers.Suspend;
            m_Terminal.HaloModificationRequested += OpenHaloWorkshop;
            m_Terminal.HaloWorkshopShowing += ShowHaloWorkshop;
            m_Containers.WorkshopRequested += OpenHaloWorkshopForItem;
            m_Containers.WorkshopSupplyRequested += OpenWorkshopSupply;
            if (m_Preview)
            {
                m_PreviewInventory=InventoryGraph.Create();
                m_PreviewInventory.AddSupply("dust",128,"stash"); m_PreviewInventory.AddSupply("alloy",36,"stash"); m_PreviewInventory.AddSupply("cells",12,"stash");
                foreach (string code in new[] { "helmet", "rifle", "compact", "pistol", "rig", "backpack", "small_pack" })
                {
                    var item=new InventoryItem { Id=Guid.NewGuid().ToString("D"),Code=code };
                    if (m_PreviewInventory.FindSpace(item,"stash",out var region,out var x,out var y)) { item.Parent="stash"; item.Region=region; item.X=x; item.Y=y; m_PreviewInventory.Items.Add(item); }
                }
            }
            else PlayerProfileClient.InventoryChanged += PresentContainers;
            PresentContainers();
        }
        private void ShowContainers() { PresentContainers(); m_Containers.Show(); }
        private void OpenHaloWorkshop() { m_Terminal.Navigate(2); }
        private void OpenHaloWorkshopForItem(string itemId)
        {
            if (itemId != null) m_Containers.SelectWorkshopWeapon(itemId);
            OpenHaloWorkshop();
        }
        private void ShowHaloWorkshop(VisualElement host)
        {
            PresentContainers();
            m_Containers.AttachWorkshop(host);
        }
        private void OpenWorkshopSupply(bool research) => m_Terminal.OpenWorkshopSupply(research, m_Containers.WorkshopSupplyCode);
        private void PresentContainers()
        {
            if (m_Containers == null) return;
            m_Terminal.PresentEquipment(m_Preview ? m_PreviewInventory : PlayerProfileClient.Inventory);
            PresentRecovery(m_Preview ? null : PlayerProfileClient.Recovery,PlayerProfileClient.RaidActive);
            m_Containers.SetReadOnly(!m_Preview && PlayerProfileClient.RaidActive ? AccountClient.InventoryFailureMessage("raid_active") : null);
            m_Containers.Present(m_Preview ? m_PreviewInventory : PlayerProfileClient.Inventory,
                m_Preview ? "LOCAL PREVIEW / SAMPLE ITEMS · Double click a backpack to open its contents" : PlayerProfileClient.Inventory==null ? "Loading account inventory…" : null,
                operationCompleted: false);
        }
        private void PresentRecovery(RecoveryInbox inbox,bool raidActive)
        {
            bool pending=inbox!=null && inbox.ItemCount()>0;
            m_RecoveryPanel.style.display=pending ? DisplayStyle.Flex : DisplayStyle.None;
            if(pending)
            {
                MoonkovLocalization.Set(m_RecoveryNote,"RECOVERY / {0} items · Extracted items are saved. Make room or sell stored items, then claim them before deploying again.",inbox.ItemCount());
                m_RecoveryRoots=inbox.Roots().ToList();m_RecoveryList.itemsSource=m_RecoveryRoots;m_RecoveryList.Rebuild();
            }
            m_RecoveryPanel.SetEnabled(!m_RecoveryBusy && !raidActive);
        }
        private async void SendContainerCommand(InventoryCommand command)
        {
            if (m_Preview) { var error=m_PreviewInventory.TryApply(command); m_Containers.Present(m_PreviewInventory,error==InventoryError.None ? "LOCAL PREVIEW / SAMPLE ITEMS" : "Move blocked: "+error); return; }
            try
            {
                await PlayerProfileClient.MoveInventoryAsync(command,m_ContainerStop.Token);
                if (!m_ContainerStop.IsCancellationRequested) m_Containers.Present(PlayerProfileClient.Inventory, "Inventory updated.");
            }
            catch (OperationCanceledException)
            {
                if (!m_ContainerStop.IsCancellationRequested) m_Containers.Present(PlayerProfileClient.Inventory, "Inventory request timed out. Refresh storage before retrying.");
            }
            catch (Exception ex) { if (!m_ContainerStop.IsCancellationRequested) m_Containers.Present(PlayerProfileClient.Inventory,ex.Message); }
        }
        private async void ClaimRecovery(string itemId)
        {
            if(m_RecoveryBusy || m_Preview || PlayerProfileClient.RaidActive || PlayerProfileClient.Inventory==null || PlayerProfileClient.Recovery==null)return;
            m_RecoveryBusy=true;m_RecoveryPanel.SetEnabled(false);
            var request=new RecoveryClaim {RequestId=Guid.NewGuid().ToString("D"),ItemId=itemId,
                ExpectedVersion=PlayerProfileClient.Inventory.Version,ExpectedRecoveryVersion=PlayerProfileClient.Recovery.Version};
            try
            {
                await PlayerProfileClient.ClaimRecoveryAsync(request,m_ContainerStop.Token);
                if(!m_ContainerStop.IsCancellationRequested) {PresentContainers();m_Containers.Present(PlayerProfileClient.Inventory,"Items that fit were claimed. Remaining items are still saved; make room and claim again.");}
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) {if(!m_ContainerStop.IsCancellationRequested) {PresentContainers();m_Containers.Present(PlayerProfileClient.Inventory,ex.Message);}}
            finally {m_RecoveryBusy=false;if(!m_ContainerStop.IsCancellationRequested)m_RecoveryPanel.SetEnabled(!PlayerProfileClient.RaidActive);}
        }
        private void DisposeContainers()
        {
            m_ContainerStop.Cancel(); m_ContainerStop.Dispose();
            PlayerProfileClient.InventoryChanged -= PresentContainers;
            m_Terminal.LoadoutShowing -= ShowContainers;
            m_Terminal.HaloModificationRequested -= OpenHaloWorkshop;
            m_Terminal.HaloWorkshopShowing -= ShowHaloWorkshop;
            m_Containers.WorkshopRequested -= OpenHaloWorkshopForItem;
            m_Containers.WorkshopSupplyRequested -= OpenWorkshopSupply;
            m_Terminal.Navigating -= m_Containers.Suspend; m_Containers.Dispose();
        }
    }
}
