namespace Unity.MP_FPS
{
    /// <summary>Shared server/prediction switching rules for the two DollSinger weapons.</summary>
    public static class DollSingerWeapons
    {
        public const uint Halo = 2;
        public const uint Revolver = 3;
        public static bool IsHalo(uint id) => id == Halo || id == Revolver;

        public static bool TryEquip(ref PredictedPlayerGhost state, in PlayerInput input, WeaponRegistry registry)
        {
            if (!IsHalo(state.EquippedWeaponID) || state.CurrentHealth <= 0f ||
                state.ControllerState.IsReloadingState) return false;
            uint requested = input.EquipRevolver ? Revolver : input.EquipHalo ? Halo : state.EquippedWeaponID;
            if (requested == state.EquippedWeaponID ||
                registry == null || registry.GetWeaponData(requested) == null) return false;
            if (state.EquippedWeaponID == Halo) state.StoredHaloAmmo = state.CurrentAmmo;
            else state.StoredRevolverAmmo = state.CurrentAmmo;
            state.EquippedWeaponID = requested;
            state.CurrentAmmo = requested == Halo ? state.StoredHaloAmmo : state.StoredRevolverAmmo;
            state.WeaponCooldown = 0f;
            return true;
        }
    }
}
