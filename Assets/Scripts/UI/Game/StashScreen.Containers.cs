// 正式仓库的库存接线处。它从 PlayerProfileClient 读库存并接收改动通知，把格子操作发给同一客户端。战局未结束时，仓库显示为只读。
using System;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client {
    public sealed partial class StashScreen {
        private ContainerInventoryView m_Containers;
        private InventoryGraph m_PreviewInventory;
        private VisualElement m_RecoveryPanel;
        private Label m_RecoveryNote;
        private Button m_ClaimAll, m_InventoryRetry;
        private ListView m_RecoveryList;
        private List<InventoryItem> m_RecoveryRoots = new();
        private bool m_RecoveryBusy;
        private readonly CancellationTokenSource m_ContainerStop = new();

        private void InitializeContainers() {
            var body = m_Root.Q<VisualElement>(className: "stash-body");
            // 仓库模板自带一个汇总式旧界面。初始化正式容器页时隐藏旧节点，真实操作都从新的格子控件进来。
            foreach (var child in body.Children()) child.style.display = DisplayStyle.None;
            m_Containers = new ContainerInventoryView(body, true, SendContainerCommand);
            m_InventoryRetry = new Button(RetryInventory) { name = "stashInventoryRetry" };
            m_InventoryRetry.AddToClassList("terminal-button");
            MoonkovLocalization.Set(m_InventoryRetry, "RETRY SYNC");
            m_Containers.Element.Add(m_InventoryRetry);
            m_RecoveryPanel = new VisualElement { name = "stashRecovery" };
            m_RecoveryPanel.AddToClassList("inventory-container");
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.flexShrink = 0;
            m_RecoveryPanel.Add(header);
            m_RecoveryNote = new Label();
            m_RecoveryNote.style.flexGrow = 1;
            m_RecoveryNote.style.minWidth = 0;
            m_RecoveryNote.style.whiteSpace = WhiteSpace.Normal;
            m_RecoveryNote.AddToClassList("inventory-caption");
            header.Add(m_RecoveryNote);
            m_ClaimAll = new Button(() => ClaimRecovery(null)) { text = "CLAIM ALL THAT FITS" };
            m_ClaimAll.AddToClassList("terminal-button");
            m_ClaimAll.style.flexShrink = 0;
            header.Add(m_ClaimAll);
            m_RecoveryPanel.RegisterCallback<GeometryChangedEvent>(evt =>
                header.style.flexDirection = evt.newRect.width < 720 ? FlexDirection.Column : FlexDirection.Row);
            m_RecoveryList = new ListView { fixedItemHeight = 36, selectionType = SelectionType.None };
            m_RecoveryList.style.height = 144;
            m_RecoveryList.makeItem = () => {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                var label = new Label { name = "recoveryItem" };
                label.style.flexGrow = 1;
                label.style.minWidth = 0;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(label);
                var claim = new Button(() => ClaimRecovery(row.userData as string)) { text = "CLAIM TO STORAGE" };
                claim.AddToClassList("terminal-button");
                claim.style.flexShrink = 0;
                claim.style.height = 28;
                claim.style.marginTop = 2;
                claim.style.marginBottom = 2;
                row.Add(claim);
                MoonkovLocalization.Bind(claim);
                return row;
            };
            // 虚拟列表会反复使用同一行来画不同物品。重绑时更新行里的物品 ID，按钮点击后才会取正在显示的那一件。
            m_RecoveryList.bindItem = (row, index) => {
                var item = m_RecoveryRoots[index];
                row.userData = item.Id;
                row.tooltip = MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name);
                MoonkovLocalization.Set(row.Q<Label>("recoveryItem"), "{0} ×{1}{2}",
                    MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name), item.Quantity,
                    InventoryCatalog.Get(item.Code).Container ? MoonkovLocalization.Text(" (includes contents)") : "");
            };
            m_RecoveryPanel.Add(m_RecoveryList);
            m_Containers.Element.Insert(0, m_RecoveryPanel);
            MoonkovLocalization.Bind(m_RecoveryPanel);
            m_Terminal.LoadoutShowing += ShowContainers;
            m_Terminal.Navigating += m_Containers.Suspend;
            m_Terminal.HaloModificationRequested += OpenHaloWorkshop;
            m_Terminal.HaloWorkshopShowing += ShowHaloWorkshop;
            m_Containers.WorkshopRequested += OpenHaloWorkshopForItem;
            m_Containers.WorkshopSupplyRequested += OpenWorkshopSupply;
            // 正式仓库靠库存变动通知刷新。预览模式有隔离的样例物品树，只改自己的副本。
            if (m_Preview) {
                m_PreviewInventory = InventoryGraph.Create();
                m_PreviewInventory.AddSupply("dust", 128, "stash");
                m_PreviewInventory.AddSupply("alloy", 36, "stash");
                m_PreviewInventory.AddSupply("cells", 12, "stash");
                foreach (string code in new[]
                             { "helmet", "rifle", "compact", "pistol", "rig", "backpack", "small_pack" }) {
                    var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code };
                    if (m_PreviewInventory.FindSpace(item, "stash", out string region, out int x, out int y)) {
                        item.Parent = "stash";
                        item.Region = region;
                        item.X = x;
                        item.Y = y;
                        m_PreviewInventory.Items.Add(item);
                    }
                }
            } else { PlayerProfileClient.InventoryChanged += PresentContainers; }
            PresentContainers();
        }

        private void ShowContainers() {
            PresentContainers();
            m_Containers.Show();
        }

        private void OpenHaloWorkshop() { m_Terminal.Navigate(2); }

        private void OpenHaloWorkshopForItem(string itemId) {
            if (itemId != null) m_Containers.SelectWorkshopWeapon(itemId);
            OpenHaloWorkshop();
        }

        private void ShowHaloWorkshop(VisualElement host) {
            PresentContainers();
            m_Containers.AttachWorkshop(host);
        }

        private void OpenWorkshopSupply(bool research) {
            m_Terminal.OpenWorkshopSupply(research, m_Containers.WorkshopSupplyCode);
        }

        private void PresentContainers() {
            if (m_Containers == null) return;
            m_Terminal.PresentEquipment(m_Preview ? m_PreviewInventory : PlayerProfileClient.MenuInventory);
            PresentRecovery(m_Preview ? null : PlayerProfileClient.Recovery, PlayerProfileClient.RaidActive);
            // 已有出战仍在进行时，中央库存只能看。避免在菜单一起改动出战时已经拿走的装备。
            m_Containers.SetReadOnly(!m_Preview && PlayerProfileClient.RaidActive
                ? AccountClient.InventoryFailureMessage("raid_active")
                : null);
            // 普通快照刷新只改变画面。实际结束一次拖放的是发送回调里的本地确认或后续回执。
            m_Containers.Present(m_Preview ? m_PreviewInventory : PlayerProfileClient.MenuInventory,
                m_Preview ? "LOCAL PREVIEW / SAMPLE ITEMS" :
                PlayerProfileClient.MenuInventory == null ? "Loading account inventory…" : InventorySyncMessage(),
                false);
            m_InventoryRetry.style.display = !m_Preview && PlayerProfileClient.MenuInventoryNeedsRetry
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void PresentRecovery(RecoveryInbox inbox, bool raidActive) {
            bool pending = inbox != null && inbox.ItemCount() > 0;
            m_RecoveryPanel.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
            if (pending) {
                MoonkovLocalization.Set(m_RecoveryNote,
                    "RECOVERY / {0} items · Extracted items are saved. Make room or sell stored items, then claim them before deploying again.",
                    inbox.ItemCount());
                m_RecoveryRoots = inbox.Roots().ToList();
                m_RecoveryList.itemsSource = m_RecoveryRoots;
                m_RecoveryList.Rebuild();
            }
            // 领取暂存物资前要等仓库命令队列清空，否则界面上看到的空位可能还没被确认。
            m_RecoveryPanel.SetEnabled(!m_RecoveryBusy && !raidActive && PlayerProfileClient.MenuInventoryPending == 0);
        }

        private static string InventorySyncMessage() {
            return PlayerProfileClient.MenuInventoryNeedsRetry
                ?
                "Inventory confirmation is interrupted. Retry synchronization first."
                : PlayerProfileClient.MenuInventoryPending > 0
                    ? "Syncing inventory…"
                    : PlayerProfileClient.MenuInventoryError?.Message;
        }

        private async void SendContainerCommand(InventoryCommand command) {
            if (m_Preview) {
                var error = m_PreviewInventory.TryApply(command);
                m_Containers.Present(m_PreviewInventory,
                    error == InventoryError.None ? null : "Move blocked: " + error);
                return;
            }
            try {
                // 菜单可以先显示排队操作的本地结果，让拖放立即有反馈。网络回执后来校正库存，期间可看待同步状态。
                var confirmation = PlayerProfileClient.QueueMenuMoveAsync(command);
                // 先让本地操作获得反馈。后续回执只校正实际状态。
                m_Containers.Present(PlayerProfileClient.MenuInventory);
                PresentContainers();
                // 等待期间显示排队后的库存。回执到达后再刷新一次，把服务器确认或撤销的结果画出来。
                await confirmation;
                if (!m_ContainerStop.IsCancellationRequested) PresentContainers();
            } catch (OperationCanceledException) {
                if (!m_ContainerStop.IsCancellationRequested) PresentContainers();
            } catch (Exception ex) {
                if (!m_ContainerStop.IsCancellationRequested)
                    m_Containers.Present(PlayerProfileClient.MenuInventory, ex.Message);
            }
        }

        private async void RetryInventory() {
            if (m_Preview) return;
            try { await PlayerProfileClient.FlushMenuInventoryAsync(m_ContainerStop.Token); } catch
                (OperationCanceledException) { } catch (Exception ex) {
                if (!m_ContainerStop.IsCancellationRequested)
                    m_Containers.Present(PlayerProfileClient.MenuInventory, ex.Message);
            } finally {
                if (!m_ContainerStop.IsCancellationRequested) PresentContainers();
            }
        }

        private async void ClaimRecovery(string itemId) {
            if (m_RecoveryBusy || PlayerProfileClient.MenuInventoryPending > 0 || m_Preview ||
                PlayerProfileClient.RaidActive || PlayerProfileClient.Inventory == null ||
                PlayerProfileClient.Recovery == null)
                return;
            m_RecoveryBusy = true;
            m_RecoveryPanel.SetEnabled(false);
            // 领取撤离物资同时带上仓库和待领箱的版本。服务端以这两份状态检查还能装下多少。
            var request = new RecoveryClaim {
                RequestId = Guid.NewGuid().ToString("D"), ItemId = itemId,
                ExpectedVersion = PlayerProfileClient.Inventory.Version,
                ExpectedRecoveryVersion = PlayerProfileClient.Recovery.Version
            };
            try {
                await PlayerProfileClient.ClaimRecoveryAsync(request, m_ContainerStop.Token);
                if (!m_ContainerStop.IsCancellationRequested) {
                    PresentContainers();
                    m_Containers.Present(PlayerProfileClient.Inventory,
                        "Items that fit were claimed. Remaining items are still saved; make room and claim again.");
                }
            } catch (OperationCanceledException) { } catch (Exception ex) {
                if (!m_ContainerStop.IsCancellationRequested) {
                    PresentContainers();
                    m_Containers.Present(PlayerProfileClient.Inventory, ex.Message);
                }
            } finally {
                m_RecoveryBusy = false;
                if (!m_ContainerStop.IsCancellationRequested)
                    m_RecoveryPanel.SetEnabled(!PlayerProfileClient.RaidActive);
            }
        }

        private void DisposeContainers() {
            // 离开仓库时取消与该视图绑定的操作，并解除全局库存事件和终端事件。旧回执不会再去操作销毁的视图。
            m_ContainerStop.Cancel();
            m_ContainerStop.Dispose();
            PlayerProfileClient.InventoryChanged -= PresentContainers;
            m_Terminal.LoadoutShowing -= ShowContainers;
            m_Terminal.HaloModificationRequested -= OpenHaloWorkshop;
            m_Terminal.HaloWorkshopShowing -= ShowHaloWorkshop;
            m_Containers.WorkshopRequested -= OpenHaloWorkshopForItem;
            m_Containers.WorkshopSupplyRequested -= OpenWorkshopSupply;
            m_Terminal.Navigating -= m_Containers.Suspend;
            m_Containers.Dispose();
        }
    }
}