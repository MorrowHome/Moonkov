// 电池堆叠里最多留一节没满的电池，其余都是满电。拆堆、并堆和供电用同一套数量与余量计算，电量会跟着被移动的那一节走。
#nullable disable
using System;

namespace Unity.MP_FPS.Inventory {
    // 一堆电池可以有若干节满电电池，以及最多一节未满的。
    // 单独记下那节余量，避免两节没满的电池合并成一节凭空满电的。
    public static class BatteryEnergy {
        public const int Capacity = 100;
        public static bool IsCell(InventoryItem item) { return item?.Code == "cells"; }
        // CellCharge 的负值是满电占位。整堆电量计算时把其余电池都按容量上限累加。
        public static int Charge(InventoryItem item) { return item.CellCharge < 0 ? Capacity : item.CellCharge; }

        public static int Stored(InventoryItem item) {
            return IsCell(item) ? (item.Quantity - 1) * Capacity + Charge(item) : 0;
        }

        public static bool Partial(InventoryItem item) { return IsCell(item) && Charge(item) < Capacity; }

        public static int PortionEnergy(InventoryItem item, int quantity) {
            return quantity == item.Quantity ? Stored(item) : quantity * Capacity;
        }

        // 同一堆只能有一节电量不满的电池。若两堆各有一节，搬几节满电的可以，连最后一节一起搬就需要保留两堆。
        public static bool CanMerge(InventoryItem source, InventoryItem target, int quantity) {
            return !IsCell(source) || !Partial(target) || !Partial(source) || quantity < source.Quantity;
        }

        // 拆堆优先拿满电电池，整堆移动才保留那节余量和原 ID。
        public static InventoryItem Take(InventoryItem source, int quantity) {
            if (source == null || quantity < 1 || quantity > source.Quantity)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            var moved = source.Clone();
            moved.Quantity = quantity;
            // 只拿一部分先从满电电池里拆，并给新堆唯一 ID。整堆移动才带走那节剩余电量与原 ID。
            if (quantity < source.Quantity) {
                moved.Id = Guid.NewGuid().ToString("D");
                if (IsCell(moved)) moved.CellCharge = -1;
            }
            source.Quantity -= quantity;
            return moved;
        }

        public static void Merge(InventoryItem source, InventoryItem target, int quantity) {
            var moved = Take(source, quantity);
            target.Quantity += quantity;
            // 合并后的不满电电池留在目标堆。CanMerge 已经保证那里能表示这一节。
            if (Partial(moved)) target.CellCharge = moved.CellCharge;
        }

        public static void Spend(InventoryItem item, int energy) {
            // 扣电后按剩余总电量反推需要几节电池。整除为全满，否则最后一节记录余量。
            int remaining = Stored(item) - energy;
            if (energy < 1 || remaining < 0) throw new ArgumentOutOfRangeException(nameof(energy));
            item.Quantity = (remaining + Capacity - 1) / Capacity;
            item.CellCharge = remaining == 0 || remaining % Capacity == 0 ? -1 : remaining % Capacity;
        }
    }
}