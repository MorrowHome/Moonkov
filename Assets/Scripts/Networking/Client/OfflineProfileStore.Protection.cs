using System.IO;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class OfflineProfileStore
    {
        // Existing readers preserve this receipt dictionary even if they cannot
        // serialize a newly added InventoryGraph field. Never re-grant a lost kit.
        private const string ProtectionReceipt = "migration/protection-kit-v1";
        private static bool ApplyProtectionMigration(SaveData save)
        {
            if(save.DeploymentId!=null)return false;
            if(save.Receipts.TryGetValue(ProtectionReceipt,out var receipt))
            {
                if(receipt!="1")throw new InvalidDataException("Invalid protection kit migration receipt.");
                if(save.Inventory.ProtectionKitVersion>=1)return false;
                save.Inventory=save.Inventory.Clone();save.Inventory.ProtectionKitVersion=1;save.Inventory.Version++;return true;
            }
            if(save.Inventory.ProtectionKitVersion>=1){save.Receipts.Add(ProtectionReceipt,"1");return true;}
            if(!ProtectionKitMigration.TryApply(save.Inventory,save.Recovery,out var graph,out var inbox))return false;
            save.Inventory=graph;save.Recovery=inbox;save.Receipts.Add(ProtectionReceipt,"1");return true;
        }
        private void TryMigrateProtection()
        {
            if(m_Save.DeploymentId!=null || m_Save.Inventory.ProtectionKitVersion>=1 && m_Save.Receipts.TryGetValue(ProtectionReceipt,out var receipt) && receipt=="1")return;
            var next=Copy();if(ApplyProtectionMigration(next))Commit(next);
        }
    }
}
