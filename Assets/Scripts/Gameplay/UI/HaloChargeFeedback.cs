using Unity.Collections;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    public enum HaloChargeNotice { None, Waiting, Charging, Ready, Full, MoveCells, LowEnergy, Syncing }

    public readonly struct HaloChargeStatus
    {
        public readonly HaloChargeNotice Notice;
        public readonly float Remaining, Progress;
        public readonly int Ammo, Capacity, Energy, Cost;
        public HaloChargeStatus(HaloChargeNotice notice, int ammo, int capacity, float remaining = 0,
            float progress = 0, int energy = 0, int cost = 0)
        { Notice = notice; Ammo = ammo; Capacity = capacity; Remaining = remaining; Progress = progress; Energy = energy; Cost = cost; }
    }

    // Observes input and synchronized facts only; never reserves energy or predicts a refill.
    public sealed class HaloChargeFeedback
    {
        private MoonkovReloadAudio m_Transitions;
        private uint m_Weapon;
        private FixedString64Bytes m_Item;
        private bool m_EmptyFireHeld;
        private float m_AttemptAt = float.NegativeInfinity, m_ReadyUntil;
        private InventoryGraph m_EnergyGraph;
        private int m_EnergyVersion, m_ReadyEnergy, m_CarriedEnergy;
        private bool m_HasWeapon;

        public void Reset()
        {
            m_Transitions = null; m_Weapon = 0; m_Item = default; m_EnergyGraph = null;
            m_EmptyFireHeld = false; m_AttemptAt = float.NegativeInfinity; m_ReadyUntil = 0;
        }

        private void Context(in PredictedPlayerGhost player)
        {
            if (m_Weapon == player.EquippedWeaponID && m_Item.Equals(player.EquippedWeaponItem)) return;
            Reset(); m_Weapon = player.EquippedWeaponID; m_Item = player.EquippedWeaponItem;
        }

        public void ObserveInput(in PlayerInput input, in PredictedPlayerGhost player, float now)
        {
            Context(player);
            bool emptyFire = input.Shoot && input.Aim && player.CurrentAmmo <= 0;
            if ((input.Reload || emptyFire && !m_EmptyFireHeld) && !player.ControllerState.IsReloadingState &&
                player.CurrentHealth > 0 && player.WeaponEquipRemaining <= 0 && player.InventoryWeapons && DollSingerWeapons.IsHalo(player.EquippedWeaponID) &&
                now - m_AttemptAt >= .35f)
            { m_AttemptAt = now; m_ReadyUntil = 0; }
            m_EmptyFireHeld = emptyFire;
        }

        public HaloChargeStatus Update(in PredictedPlayerGhost player, WeaponData weapon, InventoryGraph inventory, bool visible, float now)
        {
            Context(player);
            if (!visible || player.CurrentHealth <= 0 || !player.InventoryWeapons || !DollSingerWeapons.IsHalo(player.EquippedWeaponID) || weapon == null)
            { Reset(); return default; }
            m_Transitions ??= new MoonkovReloadAudio();
            var transition = m_Transitions.Observe(player.LastReloadTick, player.EquippedWeaponID, player.CurrentAmmo,
                player.ControllerState.IsReloadingState, true);
            if ((transition & ReloadAudioEvent.Ready) != 0) { m_ReadyUntil = now + 1.5f; m_AttemptAt = float.NegativeInfinity; }
            int capacity = weapon.MagazineSize;
            if (player.ControllerState.IsReloadingState)
            {
                m_AttemptAt = float.NegativeInfinity;
                float duration = HaloReload.Total(player, weapon);
                return new HaloChargeStatus(HaloChargeNotice.Charging, player.ReloadTargetAmmo, capacity,
                    Mathf.Max(0, player.ReloadTimer), Mathf.Clamp01(1 - player.ReloadTimer / duration));
            }
            if (now < m_ReadyUntil) return new HaloChargeStatus(HaloChargeNotice.Ready, player.CurrentAmmo, capacity);
            if (now - m_AttemptAt >= 2.5f) return default;
            if (player.CurrentAmmo >= capacity) return new HaloChargeStatus(HaloChargeNotice.Full, player.CurrentAmmo, capacity);
            if (now - m_AttemptAt < .2f) return new HaloChargeStatus(HaloChargeNotice.Waiting, player.CurrentAmmo, capacity);
            if (inventory != null && (!ReferenceEquals(inventory, m_EnergyGraph) || inventory.Version != m_EnergyVersion))
            {
                m_EnergyGraph = inventory; m_EnergyVersion = inventory.Version;
                m_HasWeapon = inventory.Find(player.EquippedWeaponItem.ToString()) != null;
                m_ReadyEnergy = inventory.CellEnergy(accessibleOnly: true); m_CarriedEnergy = inventory.CellEnergy(carriedOnly: true);
            }
            if (inventory == null || !m_HasWeapon)
                return new HaloChargeStatus(HaloChargeNotice.Syncing, player.CurrentAmmo, capacity);
            int energy = m_ReadyEnergy, cost = Mathf.Max(1, weapon.EnergyPerRound);
            if (energy < cost)
                return new HaloChargeStatus(m_CarriedEnergy >= cost ? HaloChargeNotice.MoveCells : HaloChargeNotice.LowEnergy,
                    player.CurrentAmmo, capacity, energy: energy, cost: cost);
            return new HaloChargeStatus(HaloChargeNotice.Waiting, player.CurrentAmmo, capacity);
        }
    }
}
