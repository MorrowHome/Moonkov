using System.Collections.Generic;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(fileName = "WeaponRegistry", menuName = "FPS Sample/Weapon Registry")]
    public class WeaponRegistry : ScriptableObject
    {
        public List<WeaponData> Weapons;
        private readonly Dictionary<ulong, WeaponData> m_ModuleVariants = new Dictionary<ulong, WeaponData>();

        public WeaponData GetWeaponData(uint weaponID, ushort modules)
        {
            var original = GetWeaponData(weaponID);
            // Attachments use the rifle/shotgun slots; biological research and
            // temporary organ buffs affect every halo. Cache runtime-only copies.
            if (original == null || modules == 0 || !DollSingerWeapons.IsHalo(weaponID)) return original;
            ulong key = ((ulong)weaponID << 16) | modules;
            if (m_ModuleVariants.TryGetValue(key, out var cached)) return cached;
            var variant = Instantiate(original);
            variant.hideFlags = HideFlags.HideAndDontSave;
            variant.name = original.name + " (modules " + modules + ")";
            var mask = (HaloModule)modules;
            if ((mask & HaloModule.Capacitor) != 0)
            { variant.MagazineSize = Mathf.CeilToInt(original.MagazineSize * 1.5f); variant.ReloadTime *= 1.35f; }
            else if ((mask & HaloModule.ChargeBridge) != 0)
            { variant.ReloadTime *= .6f; variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * 1.5f); }
            else if ((mask & HaloModule.MooncoreRecycler) != 0)
            { variant.ReloadTime *= 1.2f; variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * (2f / 3f)); }
            if (weaponID == DollSingerWeapons.Shotgun)
            {
                if ((mask & HaloModule.FocusLens) != 0)
                { variant.SpreadDegrees *= .45f; variant.MagazineSize = Mathf.Max(1, variant.MagazineSize - 1); }
                else if ((mask & HaloModule.FanArray) != 0)
                { variant.SpreadDegrees *= 1.6f; variant.PelletCount = 12; variant.Damage = original.Damage * original.PelletCount / 12f; }
            }
            if ((mask & HaloModule.HitMarker) != 0) variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * 1.25f);
            if ((mask & HaloModule.BiologicalResearch) != 0) variant.Damage *= 1.6f;
            if ((mask & HaloModule.AlienSurge) != 0) variant.Damage *= 1.3f;
            if ((mask & HaloModule.BiologicalCapacity) != 0) variant.MagazineSize=Mathf.CeilToInt(variant.MagazineSize*1.4f);
            if ((mask & HaloModule.BiologicalCharging) != 0) variant.ReloadTime*=.75f;
            if ((mask & HaloModule.BiologicalStability) != 0) variant.SpreadDegrees*=.7f;
            switch(AlienBiology.Effect(modules))
            {
                case AlienFoodEffect.SilkFocus:variant.SpreadDegrees*=.6f;break;
                case AlienFoodEffect.ContractilePower:variant.Damage*=1.4f;break;
                case AlienFoodEffect.VenomPower:variant.Damage*=1.35f;break;
                case AlienFoodEffect.NeuralOverdrive:variant.CooldownInMs*=.8f;variant.SpreadDegrees*=.7f;break;
            }
            m_ModuleVariants.Add(key, variant);
            return variant;
        }

        private void OnDisable()
        {
            foreach (var weapon in m_ModuleVariants.Values)
                if (weapon) { if (Application.isPlaying) Destroy(weapon); else DestroyImmediate(weapon); }
            m_ModuleVariants.Clear();
        }

        public WeaponData GetWeaponData(uint weaponID)
        {
            if (weaponID < Weapons.Count)
            {
                return Weapons[(int)weaponID];
            }

            return null; // or a default weapon
        }
    }
}
