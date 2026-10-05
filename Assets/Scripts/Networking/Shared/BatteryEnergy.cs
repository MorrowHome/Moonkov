#nullable disable
using System;

namespace Unity.MP_FPS.Inventory
{
    // A stack holds full cells plus at most one partially discharged cell.
    // Keeping that cell distinct avoids turning two partial casings into one full cell.
    public static class BatteryEnergy
    {
        public const int Capacity = 100;
        public static bool IsCell(InventoryItem item) => item?.Code == "cells";
        public static int Charge(InventoryItem item) => item.CellCharge < 0 ? Capacity : item.CellCharge;
        public static int Stored(InventoryItem item) => IsCell(item) ? (item.Quantity - 1) * Capacity + Charge(item) : 0;
        public static bool Partial(InventoryItem item) => IsCell(item) && Charge(item) < Capacity;
        public static int PortionEnergy(InventoryItem item, int quantity) => quantity == item.Quantity ? Stored(item) : quantity * Capacity;
        public static bool CanMerge(InventoryItem source, InventoryItem target, int quantity) =>
            !IsCell(source) || !Partial(target) || !Partial(source) || quantity < source.Quantity;

        // Splits prefer full cells; moving the whole stack preserves its partial cell and identity.
        public static InventoryItem Take(InventoryItem source, int quantity)
        {
            if (source == null || quantity < 1 || quantity > source.Quantity) throw new ArgumentOutOfRangeException(nameof(quantity));
            var moved = source.Clone(); moved.Quantity = quantity;
            if (quantity < source.Quantity)
            { moved.Id = Guid.NewGuid().ToString("D"); if (IsCell(moved)) moved.CellCharge = -1; }
            source.Quantity -= quantity;
            return moved;
        }
        public static void Merge(InventoryItem source, InventoryItem target, int quantity)
        {
            var moved = Take(source, quantity);
            target.Quantity += quantity;
            if (Partial(moved)) target.CellCharge = moved.CellCharge;
        }
        public static void Spend(InventoryItem item, int energy)
        {
            int remaining = Stored(item) - energy;
            if (energy < 1 || remaining < 0) throw new ArgumentOutOfRangeException(nameof(energy));
            item.Quantity = (remaining + Capacity - 1) / Capacity;
            item.CellCharge = remaining == 0 || remaining % Capacity == 0 ? -1 : remaining % Capacity;
        }
    }
}
