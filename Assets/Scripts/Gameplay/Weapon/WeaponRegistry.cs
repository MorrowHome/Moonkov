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

        public WeaponData GetWeaponData(uint weaponID, byte modules)
        {
            var original = GetWeaponData(weaponID);
            // Only rifle and shotgun currently accept modules. Cache runtime-only
            // copies; never mutate a shared asset or allocate on every shot/tick.
            if (original == null || modules == 0 || (weaponID != DollSingerWeapons.Halo && weaponID != DollSingerWeapons.Shotgun)) return original;
            ulong key = ((ulong)weaponID << 8) | modules;
            if (m_ModuleVariants.TryGetValue(key, out var cached)) return cached;
            var variant = Instantiate(original);
            variant.hideFlags = HideFlags.HideAndDontSave;
            variant.name = original.name + " (modules " + modules + ")";
            var mask = (HaloModule)modules;
            if ((mask & HaloModule.Capacitor) != 0)
            { variant.MagazineSize = Mathf.CeilToInt(original.MagazineSize * 1.5f); variant.ReloadTime *= 1.35f; }
            else if ((mask & HaloModule.ChargeBridge) != 0)
            { variant.ReloadTime *= .6f; variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * 1.5f); }
            if (weaponID == DollSingerWeapons.Shotgun)
            {
                if ((mask & HaloModule.FocusLens) != 0)
                { variant.SpreadDegrees *= .45f; variant.MagazineSize = Mathf.Max(1, variant.MagazineSize - 1); }
                else if ((mask & HaloModule.FanArray) != 0)
                { variant.SpreadDegrees *= 1.6f; variant.PelletCount = 12; variant.Damage = original.Damage * original.PelletCount / 12f; }
            }
            if ((mask & HaloModule.HitMarker) != 0) variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * 1.25f);
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
