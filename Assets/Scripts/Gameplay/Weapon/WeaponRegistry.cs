// 按武器编号查基础参数。附带配件掩码时生成运行时副本，把扩容、聚焦镜、研究和食物效果叠进去。
// 编号和掩码相同的角色复用同一份副本，基础资产不会跟着某个角色的改装改变。
using System.Collections.Generic;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    [CreateAssetMenu(fileName = "WeaponRegistry", menuName = "FPS Sample/Weapon Registry")]
    public class WeaponRegistry : ScriptableObject {
        public List<WeaponData> Weapons;
        private readonly Dictionary<ulong, WeaponData> m_ModuleVariants = new();

        public WeaponData GetWeaponData(uint weaponID, ushort modules) {
            var original = GetWeaponData(weaponID);
            // Attachments use the rifle/shotgun slots; biological research and
            // temporary organ buffs affect every halo. Cache runtime-only copies.
            // 只有存在有效模块的光环才需要副本。基础设置和普通武器可直接使用资产。
            if (original == null || modules == 0 || !DollSingerWeapons.IsHalo(weaponID)) return original;
            // 缓存键的高位存武器编号，低 16 位存配件和效果。不同武器或不同配件组合对应各自的运行时参数。
            ulong key = ((ulong)weaponID << 16) | modules;
            if (m_ModuleVariants.TryGetValue(key, out var cached)) return cached;
            // 改装作用于只在运行时存在的参数对象。两个玩家拿同一点数的武器，也不会把原配置资产改掉。
            var variant = Instantiate(original);
            variant.hideFlags = HideFlags.HideAndDontSave;
            variant.name = original.name + " (modules " + modules + ")";
            var mask = (HaloModule)modules;
            // 同一改装槽的扩容、加速等组件按互斥分支计算。散布镜片只给兼容的霰弹光环。
            if ((mask & HaloModule.Capacitor) != 0) {
                variant.MagazineSize = Mathf.CeilToInt(original.MagazineSize * 1.5f);
                variant.ReloadTime *= 1.35f;
            } else if ((mask & HaloModule.ChargeBridge) != 0) {
                variant.ReloadTime *= .6f;
                variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * 1.5f);
            } else if ((mask & HaloModule.MooncoreRecycler) != 0) {
                variant.ReloadTime *= 1.2f;
                variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * (2f / 3f));
            }
            if (weaponID == DollSingerWeapons.Shotgun) {
                if ((mask & HaloModule.FocusLens) != 0) {
                    variant.SpreadDegrees *= .45f;
                    variant.MagazineSize = Mathf.Max(1, variant.MagazineSize - 1);
                    variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * (4f / 3f));
                } else if ((mask & HaloModule.FanArray) != 0) {
                    variant.SpreadDegrees *= 1.6f;
                    variant.PelletCount = 12;
                    variant.Damage = original.Damage * original.PelletCount / 12f;
                }
            }
            if ((mask & HaloModule.HitMarker) != 0)
                variant.EnergyPerRound = Mathf.CeilToInt(variant.EnergyPerRound * 1.25f);
            if ((mask & HaloModule.BiologicalResearch) != 0) variant.Damage *= 1.6f;
            if ((mask & HaloModule.AlienSurge) != 0) variant.Damage *= 1.3f;
            if ((mask & HaloModule.BiologicalCapacity) != 0)
                variant.MagazineSize = Mathf.CeilToInt(variant.MagazineSize * 1.4f);
            if ((mask & HaloModule.BiologicalCharging) != 0) variant.ReloadTime *= .75f;
            if ((mask & HaloModule.BiologicalStability) != 0) variant.SpreadDegrees *= .7f;
            // 临时食物效果也编码在掩码里，和长期配件一起算当前数值。
            switch (AlienBiology.Effect(modules)) {
                case AlienFoodEffect.SilkFocus: variant.SpreadDegrees *= .6f; break;
                case AlienFoodEffect.ContractilePower: variant.Damage *= 1.4f; break;
                case AlienFoodEffect.VenomPower: variant.Damage *= 1.35f; break;
                case AlienFoodEffect.NeuralOverdrive:
                    variant.CooldownInMs *= .8f;
                    variant.SpreadDegrees *= .7f;
                    break;
            }
            m_ModuleVariants.Add(key, variant);
            return variant;
        }

        // 副本不作为资产写回。注册表卸载时统一销毁并清空缓存。
        private void OnDisable() {
            foreach (var weapon in m_ModuleVariants.Values)
                if (weapon) {
                    if (Application.isPlaying)
                        Destroy(weapon);
                    else
                        DestroyImmediate(weapon);
                }
            m_ModuleVariants.Clear();
        }

        public WeaponData GetWeaponData(uint weaponID) {
            if (weaponID < Weapons.Count) return Weapons[(int)weaponID];
            return null; // or a default weapon
        }
    }
}