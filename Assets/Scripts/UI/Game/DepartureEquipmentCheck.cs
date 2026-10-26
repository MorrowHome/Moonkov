using System.Linq;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    public enum DepartureEquipmentIssue { Loading, Halo, Medicine, None }

    // An advisory check. It neither moves supplies nor decides deployment eligibility.
    public readonly struct DepartureEquipmentCheck
    {
        public readonly DepartureEquipmentIssue Issue;
        public readonly bool EmergencyAvailable;
        private DepartureEquipmentCheck(DepartureEquipmentIssue issue, bool emergency = false)
        { Issue = issue; EmergencyAvailable = emergency; }

        public static DepartureEquipmentCheck Read(InventoryGraph graph)
        {
            if (graph == null) return new DepartureEquipmentCheck(DepartureEquipmentIssue.Loading);
            bool halo = graph.Items.Any(i => i.Parent == InventoryCatalog.Equipment &&
                (InventoryCatalog.Get(i.Code)?.Kind == ItemKind.LongGun || InventoryCatalog.Get(i.Code)?.Kind == ItemKind.Pistol));
            if (!halo) return new DepartureEquipmentCheck(DepartureEquipmentIssue.Halo, ShopCatalog.CanClaim(graph));
            return new DepartureEquipmentCheck(graph.Items.Any(graph.MedicalAccessible)
                ? DepartureEquipmentIssue.None : DepartureEquipmentIssue.Medicine);
        }

        public string Copy => Issue == DepartureEquipmentIssue.Loading ? "Loading equipment..."
            : Issue == DepartureEquipmentIssue.Halo ? EmergencyAvailable
                ? "No halo weapon remains. Emergency supplies are available."
                : "No halo equipped. Open storage to equip one."
            : Issue == DepartureEquipmentIssue.Medicine ? "Place medical supplies in pockets or your chest rig."
            : "Halo equipped. Medicine accessible.";
        public string Action => EmergencyAvailable ? "EMERGENCY SUPPLY" : "OPEN STORAGE";
    }
}
