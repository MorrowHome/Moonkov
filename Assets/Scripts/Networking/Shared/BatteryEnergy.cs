// 电池堆叠里最多留一节没满的电池，其余都是满电。拆堆、并堆和供电用同一套数量与余量计算，电量会跟着被移动的那一节走。
#nullable disable
using System;

namespace Unity.MP_FPS.Inventory {
    // A stack holds full cells plus at most one partially discharged cell.
    // Keeping that cell distinct avoids turning two partial casings into one full cell.
    public static class BatteryEnergy {
        public const int Capacity = 100;
        public static bool IsCell(InventoryItem item) { return item?.Code == "cells"; }
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

        // Splits prefer full cells; moving the whole stack preserves its partial cell and identity.
        public static InventoryItem Take(InventoryItem source, int quantity) {
            if (source == null || quantity < 1 || quantity > source.Quantity)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            var moved = source.Clone();
            moved.Quantity = quantity;
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
            if (Partial(moved)) target.CellCharge = moved.CellCharge;
        }

        public static void Spend(InventoryItem item, int energy) {
            int remaining = Stored(item) - energy;
            if (energy < 1 || remaining < 0) throw new ArgumentOutOfRangeException(nameof(energy));
            item.Quantity = (remaining + Capacity - 1) / Capacity;
            item.CellCharge = remaining == 0 || remaining % Capacity == 0 ? -1 : remaining % Capacity;
        }
    }
}