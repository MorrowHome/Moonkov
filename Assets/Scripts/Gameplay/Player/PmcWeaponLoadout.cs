using Unity.MP_FPS.Inventory;
using Random = Unity.Mathematics.Random;

namespace Unity.MP_FPS
{
    // Server-owned shuffled bag. Characters and personalities do not determine weapons.
    public sealed class PmcWeaponLoadout
    {
        // Explosive weapons need a separate safe-fire policy before AI can carry them.
        private static readonly string[] s_Weapons =
            { "rifle", "shotgun", "sniper", "cicada", "trine", "aegis", "vesper", "thorn", "rondo", "nocturne" };
        private readonly int[] m_Order = new int[s_Weapons.Length];
        private int m_Count, m_Next;

        public void Reset() { m_Count = m_Next = 0; }

        public InventoryGraph Create(WeaponRegistry registry, bool tutorial, bool clash, ref Random random)
        {
            var inventory = InventoryGraph.Create(stash: false);
            inventory.Equipped("Primary").LoadedAmmo = registry?.GetWeaponData(DollSingerWeapons.Halo)?.MagazineSize ?? 30;
            if (tutorial || clash) return inventory;

            string primary = ChoosePrimary(registry, ref random);
            string secondary = ChooseSecondary(registry, primary, ref random, complementary: true) ??
                ChooseSecondary(registry, primary, ref random, complementary: false);
            SetWeapon(inventory, "Primary", primary, registry);
            SetWeapon(inventory, "Secondary", secondary, registry);
            SetWeapon(inventory, "Pistol", Available(registry, "pistol") ? "pistol" : null, registry);
            return inventory;
        }

        private string ChoosePrimary(WeaponRegistry registry, ref Random random)
        {
            if (m_Next >= m_Count)
            {
                Reset();
                for (int i = 0; i < s_Weapons.Length; i++)
                    if (Available(registry, s_Weapons[i])) m_Order[m_Count++] = i;
                for (int i = m_Count - 1; i > 0; i--)
                {
                    int other = random.NextInt(i + 1);
                    (m_Order[i], m_Order[other]) = (m_Order[other], m_Order[i]);
                }
            }
            while (m_Next < m_Count)
            {
                string code = s_Weapons[m_Order[m_Next++]];
                if (Available(registry, code)) return code;
            }
            return null;
        }

        private static bool CloseRange(string code) => code == "shotgun" || code == "cicada" || code == "thorn";
        private static bool Available(WeaponRegistry registry, string code) =>
            registry?.GetWeaponData(DollSingerWeapons.WeaponId(code)) is { Behavior: ProjectileBehavior.DirectDamage };

        private static string ChooseSecondary(WeaponRegistry registry, string primary, ref Random random, bool complementary)
        {
            string chosen = null;
            int count = 0;
            foreach (string code in s_Weapons)
            {
                if (code == primary || !Available(registry, code) || complementary && CloseRange(code) == CloseRange(primary)) continue;
                if (random.NextInt(++count) == 0) chosen = code;
            }
            return chosen;
        }

        private static void SetWeapon(InventoryGraph inventory, string slot, string code, WeaponRegistry registry)
        {
            var item = inventory.Equipped(slot);
            if (code == null) { inventory.Items.Remove(item); return; }
            item.Code = code;
            item.LoadedAmmo = registry.GetWeaponData(DollSingerWeapons.WeaponId(code)).MagazineSize;
        }
    }
}
