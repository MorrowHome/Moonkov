using System;
using System.Threading;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashScreen
    {
        private ContainerInventoryView m_Containers;
        private InventoryGraph m_PreviewInventory;
        private readonly CancellationTokenSource m_ContainerStop = new CancellationTokenSource();
        private void InitializeContainers()
        {
            var body=m_Root.Q<VisualElement>(className:"stash-body");
            foreach (var child in body.Children()) child.style.display=DisplayStyle.None;
            m_Containers=new ContainerInventoryView(body, true, SendContainerCommand);
            m_Terminal.LoadoutShowing += ShowContainers; m_Terminal.Navigating += m_Containers.Suspend;
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
        private void PresentContainers()
        {
            if (m_Containers == null) return;
            m_Containers.SetReadOnly(!m_Preview && PlayerProfileClient.RaidActive ? AccountClient.InventoryFailureMessage("raid_active") : null);
            m_Containers.Present(m_Preview ? m_PreviewInventory : PlayerProfileClient.Inventory,
                m_Preview ? "LOCAL PREVIEW / SAMPLE ITEMS · Double click a backpack to open its contents" : PlayerProfileClient.Inventory==null ? "Loading account inventory…" : null,
                operationCompleted: false);
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
        private void DisposeContainers()
        {
            m_ContainerStop.Cancel(); m_ContainerStop.Dispose();
            PlayerProfileClient.InventoryChanged -= PresentContainers;
            m_Terminal.LoadoutShowing -= ShowContainers;
            m_Terminal.Navigating -= m_Containers.Suspend; m_Containers.Dispose();
        }
    }
}
