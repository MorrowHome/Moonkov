// 按光环武器 ID 查颜色和 HUD 图集位置。HUD 与护盾因此能用同一个光环身份，切到近战时仍会从装备槽找可用光环。
using UnityEngine;

namespace Unity.MP_FPS {
    // Baked from the formal weapon prefabs. HUD and defense share the same identity.
    public sealed class HaloAppearanceCatalog : ScriptableObject {
        [System.Serializable]
        public struct Entry {
            public uint Weapon;
            public Color Color;
            public Vector4 ArtworkRect;
        }

        public Texture2D Artwork;
        public Entry[] Entries = System.Array.Empty<Entry>();
        private static HaloAppearanceCatalog s_Current;

        public static HaloAppearanceCatalog Current =>
            s_Current ? s_Current : s_Current = Resources.Load<HaloAppearanceCatalog>("Moonkov/HaloAppearance");

        public Entry ForWeapon(uint weapon) {
            // 尽量用武器自己的图样与颜色。找不到时退回目录首项，空资源还能返回默认值。
            foreach (var entry in Entries)
                if (entry.Weapon == weapon)
                    return entry;
            return Entries.Length > 0 ? Entries[0] : default;
        }

        // A melee selection still has an equipped halo available for defense.
        // 当前在用光环时优先取手里的 ID。切到近战后先找已装备的主、副、手枪槽里的光环，维持同样的防御标识。
        public static uint CurrentWeapon(in PredictedPlayerGhost state) {
            return DollSingerWeapons.IsHalo(state.EquippedWeaponID) ? state.EquippedWeaponID :
                DollSingerWeapons.IsHalo(state.PrimaryWeaponID) ? state.PrimaryWeaponID :
                DollSingerWeapons.IsHalo(state.SecondaryWeaponID) ? state.SecondaryWeaponID :
                DollSingerWeapons.IsHalo(state.PistolWeaponID) ? state.PistolWeaponID : DollSingerWeapons.Halo;
        }
    }
}