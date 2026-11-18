// 生成服务器人形敌人的初始装备。常规远征从可用武器中轮换主武器，副武器优先补不同距离的需求。
// 教学或悬赏走各自的初始库存配置。
using Unity.MP_FPS.Inventory;
using Random = Unity.Mathematics.Random;

namespace Unity.MP_FPS {
    // 主武器按服务端洗牌顺序取。角色外观和个性不决定枪型。
    public sealed class PmcWeaponLoadout {
        // AI 要用爆炸武器前，需要另有适合它的安全开火规则。
        private static readonly string[] s_Weapons =
            { "rifle", "shotgun", "sniper", "cicada", "trine", "aegis", "vesper", "thorn", "rondo", "nocturne" };

        private readonly int[] m_Order = new int[s_Weapons.Length];
        private int m_Count, m_Next;
        public void Reset() { m_Count = m_Next = 0; }

        public InventoryGraph Create(WeaponRegistry registry, bool tutorial, bool clash, ref Random random) {
            var inventory = InventoryGraph.Create(false);
            inventory.Equipped("Primary").LoadedAmmo =
                registry?.GetWeaponData(DollSingerWeapons.Halo)?.MagazineSize ?? 30;
            // 教学与悬赏保留简单的初始装备，不进入远征的随机搭配。
            if (tutorial || clash) return inventory;
            string primary = ChoosePrimary(registry, ref random);
            string secondary = ChooseSecondary(registry, primary, ref random, true) ??
                               ChooseSecondary(registry, primary, ref random, false);
            SetWeapon(inventory, "Primary", primary, registry);
            SetWeapon(inventory, "Secondary", secondary, registry);
            SetWeapon(inventory, "Pistol", Available(registry, "pistol") ? "pistol" : null, registry);
            return inventory;
        }

        // 主武器先把有效条目洗牌，逐项取出。比赛开始后的连续生成有机会覆盖多种射程。
        private string ChoosePrimary(WeaponRegistry registry, ref Random random) {
            if (m_Next >= m_Count) {
                Reset();
                for (int i = 0; i < s_Weapons.Length; i++)
                    if (Available(registry, s_Weapons[i]))
                        m_Order[m_Count++] = i;
                for (int i = m_Count - 1; i > 0; i--) {
                    int other = random.NextInt(i + 1);
                    (m_Order[i], m_Order[other]) = (m_Order[other], m_Order[i]);
                }
            }
            while (m_Next < m_Count) {
                string code = s_Weapons[m_Order[m_Next++]];
                if (Available(registry, code)) return code;
            }
            return null;
        }

        private static bool CloseRange(string code) { return code == "shotgun" || code == "cicada" || code == "thorn"; }

        // 只从当前注册表有合法配置且是直接伤害的武器中挑选。范围爆炸另需更严格的选用条件。
        private static bool Available(WeaponRegistry registry, string code) {
            return registry?.GetWeaponData(DollSingerWeapons.WeaponId(code)) is
                { Behavior: ProjectileBehavior.DirectDamage };
        }

        // 先试和主武器互补的类型，没可用项再放宽条件。随机换选使有效候选都有机会。
        private static string ChooseSecondary(
            WeaponRegistry registry,
            string primary,
            ref Random random,
            bool complementary) {
            string chosen = null;
            int count = 0;
            foreach (string code in s_Weapons) {
                if (code == primary || !Available(registry, code) ||
                    (complementary && CloseRange(code) == CloseRange(primary)))
                    continue;
                if (random.NextInt(++count) == 0) chosen = code;
            }
            return chosen;
        }

        // 找到有效装备后写物品代码和弹药。没合适类型就移除这个装备槽里的默认道具。
        private static void SetWeapon(InventoryGraph inventory, string slot, string code, WeaponRegistry registry) {
            var item = inventory.Equipped(slot);
            if (code == null) {
                inventory.Items.Remove(item);
                return;
            }
            item.Code = code;
            item.LoadedAmmo = registry.GetWeaponData(DollSingerWeapons.WeaponId(code)).MagazineSize;
        }
    }
}