#nullable disable
using System;

namespace Unity.MP_FPS.Inventory {
    public enum InventoryError {
        None,
        Stale,
        Missing,
        Invalid,
        Incompatible,
        Bounds,
        Collision,
        Cycle,
        Full,
        Overweight,
        Inaccessible
    }

    public enum InventoryOperation {
        Move,
        Split,
        Merge
    }

    [Serializable]
    public sealed class InventoryItem {
        public string Id, Code, Parent, Region;
        public int X, Y, Quantity = 1;
        public bool Rotated, FoundInRaid;

        // -1 表示新武器。实际弹药在移动、出战和结算中跟随物品。
        public int LoadedAmmo = -1;

        // 旧档缺此字段表示满电。一堆最多一节有余量，其他是满电。
        public int CellCharge = -1;
        public bool EmergencySupply;

        // -1 表示新物品或旧档缺值。第一次由权威状态绑定时初始化一次。
        // 实时余量跟随这件物品移动、丢弃、拾取、重连和结算。
        public float ShieldCharge = -1, ShieldEnergy = -1, SuitOxygen = -1;

        // 仅由权威设施产出时设置来源。搜刮、死亡和返回仓库都保留该标记。
        public string MissionWorldId, MissionOperationId;
        public int MissionContentVersion;
        public bool MissionRecovered;
        public InventoryItem Clone() { return (InventoryItem)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class InventoryCommand {
        public InventoryOperation Operation;
        public int ExpectedVersion;
        public string ItemId, Parent, Region, TargetId;
        public int X, Y, Quantity;
        public bool Rotated;
    }
}
