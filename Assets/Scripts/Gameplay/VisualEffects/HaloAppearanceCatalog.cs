using UnityEngine;

namespace Unity.MP_FPS
{
    // Baked from the formal weapon prefabs. HUD and defense share the same identity.
    public sealed class HaloAppearanceCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public uint Weapon;
            public Color Color;
            public Vector4 ArtworkRect;
        }

        public Texture2D Artwork;
        public Entry[] Entries = System.Array.Empty<Entry>();
        private static HaloAppearanceCatalog s_Current;
        public static HaloAppearanceCatalog Current => s_Current ? s_Current :
            s_Current = Resources.Load<HaloAppearanceCatalog>("Moonkov/HaloAppearance");

        public Entry ForWeapon(uint weapon)
        {
            foreach (var entry in Entries) if (entry.Weapon == weapon) return entry;
            return Entries.Length > 0 ? Entries[0] : default;
        }

        // A melee selection still has an equipped halo available for defense.
        public static uint CurrentWeapon(in PredictedPlayerGhost state) =>
            DollSingerWeapons.IsHalo(state.EquippedWeaponID) ? state.EquippedWeaponID :
            DollSingerWeapons.IsHalo(state.PrimaryWeaponID) ? state.PrimaryWeaponID :
            DollSingerWeapons.IsHalo(state.SecondaryWeaponID) ? state.SecondaryWeaponID :
            DollSingerWeapons.IsHalo(state.PistolWeaponID) ? state.PistolWeaponID : DollSingerWeapons.Halo;
    }
}
